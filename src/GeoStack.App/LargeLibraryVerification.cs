// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GeoStack.Core;
namespace GeoStack.App;
public partial class MainWindow
{
    internal async Task VerifyLargeLibrary05()
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated synthetic profile required");
        await AtlasReady;await libraryInitialized.Task;var initialCount=ViewModel.Items.Count;var arguments=Environment.GetCommandLineArgs();var probeAt=Array.IndexOf(arguments,"--ui-probe");var probeMode=probeAt>=0?arguments[probeAt+1]:"legacy";using var profiling=probeMode is "legacy" or "detailed"?new AtlasProfiling(Dispatcher):null;var measurements=new List<object>();var delays=new List<double>();var phases=new List<(string Phase,double Delay)>();var phase="initial-page";var cancellation=new CancellationTokenSource();
        using var probe=probeMode=="legacy"?null:new UiResponsivenessProbe(this,()=>phase,probeMode);
        var sampler=Task.Run(async()=>{while(probeMode=="legacy" && !cancellation.IsCancellationRequested){await Task.Delay(16);var clock=Stopwatch.StartNew();var label=phase;await Dispatcher.InvokeAsync(()=>{delays.Add(clock.Elapsed.TotalMilliseconds);phases.Add((label,clock.Elapsed.TotalMilliseconds));},DispatcherPriority.Input);}});
        if(arguments.Contains("--ui-cold"))
        {
            var size=arguments.Contains("--ui-10000")?10000:1000;phase="cold-import";var cold=Stopwatch.StartNew();
            var import=ViewModel.ImportAsync([Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/library05-fixtures-v2",size.ToString()))]);
            var steps=0;
            while(!import.IsCompleted){Atlas.PanPixels(new Vector(steps++%2==0?8:-8,0));if(steps%20==0 && ViewModel.Items.Count>0)_=ViewModel.ShowPage((steps/20)%Math.Max(1,ViewModel.PageCount));await Task.Delay(50);}
            await import;initialCount=ViewModel.Items.Count;measurements.Add(new{Stage="ColdImportWithMapInteraction",Milliseconds=cold.Elapsed.TotalMilliseconds,Count=initialCount,ViewModel.LastScanMeasurements});
        }
        measurements.Add(new{Stage="RenderingConfiguration",Preference=System.Windows.Media.RenderOptions.ProcessRenderMode.ToString(),Tier=System.Windows.Media.RenderCapability.Tier>>16,ActualWidth,ActualHeight});
        var clock=Stopwatch.StartNew();await ViewModel.ShowItems(ViewModel.Items,Strings.Get("AllMedia"));measurements.Add(new{Stage="InitialLibraryPage",Milliseconds=clock.Elapsed.TotalMilliseconds,Count=ViewModel.Items.Count});
        foreach(var page in new[]{1,2,3,0}){phase="page-"+page;clock.Restart();await ViewModel.ShowPage(page);measurements.Add(new{Stage="ThumbnailPage"+page,Milliseconds=clock.Elapsed.TotalMilliseconds,MemoryThumbnails=ViewModel.Previews.MemoryEntries});if(probeMode=="capture"){phase="capture";FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,"ui-page-"+page+".png"));}}
        phase="sort";ViewModel.SortNewest=true;clock.Restart();await ViewModel.ShowItems(ViewModel.Items,Strings.Get("AllMedia"));measurements.Add(new{Stage="SortDateAndDecodePage",Milliseconds=clock.Elapsed.TotalMilliseconds});
        phase="no-gps";clock.Restart();await ViewModel.ShowItems(ViewModel.Items.Where(m=>!m.HasLocation),Strings.Get("NoGps"));measurements.Add(new{Stage="NoGpsFilterAndDecodePage",Milliseconds=clock.Elapsed.TotalMilliseconds});
        if(ViewModel.Items.Any(m=>!m.Path.Contains("\\.local\\library05-fixtures",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Approved large-library fixtures required");
        phase="cached-full-rescan";clock.Restart();await ViewModel.ImportAsync(ViewModel.Items.Select(m=>m.Path));measurements.Add(new{Stage="CachedFullRescan",Milliseconds=clock.Elapsed.TotalMilliseconds,ViewModel.LastScanMeasurements});
        phase="coverage-planning";clock.Restart();var planningItems=ViewModel.Items.ToArray();var planningMaps=Atlas.Tiles.Catalog.Packages.ToArray();
        var plan=await Task.Run(()=>LibraryMapPlanner.Build(planningItems,planningMaps,App.LocationResolver.Resolve,CancellationToken.None));measurements.Add(new{Stage="CoverageDiscoveryAndDownloadPlanning",Milliseconds=clock.Elapsed.TotalMilliseconds,Areas=plan.Areas.Count});
        phase="theme-resize";clock.Restart();var previousLanguage=App.Settings.Language;var previousTheme=App.Settings.Theme;var previousWidth=Width;
        foreach(var theme in new[]{"Light","Dark"})
        {
            phase="theme-"+theme;ApplySettings(previousLanguage,theme);await Dispatcher.Yield(DispatcherPriority.Background);
            phase="resize";Width=previousWidth-40;await Task.Delay(200);Width=previousWidth;await Task.Delay(200);
        }
        ApplySettings(previousLanguage,previousTheme);measurements.Add(new{Stage="ThemesAndResize",Milliseconds=clock.Elapsed.TotalMilliseconds});
        var original=ViewModel.Items.First(m=>Path.GetExtension(m.Path).Equals(".jpg",StringComparison.OrdinalIgnoreCase));
        string Hash(string file){using var input=File.OpenRead(file);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));}
        var sourceHash=Hash(original.Path);var extra=Path.Combine(App.DataDirectory,"incremental-"+Guid.NewGuid().ToString("N")+".jpg");File.Copy(original.Path,extra);File.SetAttributes(extra,FileAttributes.Normal);
        foreach(var operation in new[]{"AdditiveSingleFile","RepeatedSingleFile","ChangedPrivateCopy","MissingExternalSource"})
        {
            phase=operation;if(operation=="ChangedPrivateCopy")File.SetLastWriteTimeUtc(extra,File.GetLastWriteTimeUtc(extra).AddSeconds(2));
            clock.Restart();await ViewModel.ImportAsync([operation=="MissingExternalSource"?Path.Combine(App.DataDirectory,"absent-drive"):extra]);
            if(ViewModel.Items.Count!=initialCount+1)throw new InvalidOperationException("Incremental import changed existing records");
            measurements.Add(new{Stage=operation,Milliseconds=clock.Elapsed.TotalMilliseconds,Count=ViewModel.Items.Count,Metadata=ViewModel.LastScanMeasurements});
        }
        if(Hash(original.Path)!=sourceHash || Hash(extra)!=sourceHash)throw new InvalidOperationException("Incremental source bytes changed");
        phase="cluster";clock.Restart();var clusters=await Task.Run(()=>Geo.Cluster(ViewModel.Items,1<<20));measurements.Add(new{Stage="Clustering",Milliseconds=clock.Elapsed.TotalMilliseconds,Count=clusters.Count});
        var positions=Enumerable.Range(0,1000).Select(i=>new MediaItem("benchmark-"+i,47.37+(i%100)*.00005,8.53+(i/100)*.00005,"Photo")).ToArray();
        phase="boundary-lookup";clock.Restart();await Task.Run(()=>{foreach(var item in positions)App.LocationResolver.Resolve(item.Latitude,item.Longitude);});measurements.Add(new{Stage="OneThousandDistinctBoundaryQueries",Milliseconds=clock.Elapsed.TotalMilliseconds});
        phase="navigation";clock.Restart();await NavigateToMedia(ViewModel.Items.First(m=>m.HasLocation));await SettleBrowsing();measurements.Add(new{Stage="ExactMediaNavigationAndSettling",Milliseconds=clock.Elapsed.TotalMilliseconds});
        phase="idle";await Task.Delay(1000);cancellation.Cancel();await sampler;cancellation.Dispose();delays.Sort();using var process=Process.GetCurrentProcess();
        double Percent(double p)=>delays.Count==0?0:delays[(int)Math.Min(delays.Count-1,Math.Floor(delays.Count*p))];
        File.WriteAllText(Path.Combine(App.DataDirectory,"large-library05.json"),JsonSerializer.Serialize(new{Status="MEASURED",Count=initialCount,FinalCount=ViewModel.Items.Count,SourceChecksumPreserved=true,Stages=measurements,QueueSamples=delays.Count,P50=Percent(.5),P95=Percent(.95),P99=Percent(.99),Maximum=delays.LastOrDefault(),Phases=phases.GroupBy(p=>p.Phase).Select(g=>new{Phase=g.Key,Maximum=g.Max(x=>x.Delay)}),GcCounts=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)},Responsiveness=probe?.Report(),DispatcherStages=profiling?.Summary(),LastGcPauseMilliseconds=GC.GetGCMemoryInfo().PauseDurations.ToArray().Select(t=>t.TotalMilliseconds).ToArray(),process.PrivateMemorySize64,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false),ViewModel.Previews.MemoryEntries,ProcessCpuSeconds=process.TotalProcessorTime.TotalSeconds,Scope="Packaged synthetic indexed library, actual thumbnail decoding; dispatcher queue latency, not physical Explorer scrolling"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
