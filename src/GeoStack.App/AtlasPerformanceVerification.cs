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
    internal async Task VerifyAtlasPerformance()
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var pilot=Atlas.AtlasPilot ?? throw new InvalidOperationException("Pilot required");
        using var profile=AtlasProfiling.Current ?? new AtlasProfiling(Dispatcher);
        var source=ViewModel.Items.ToArray();if(source.Length!=9 || source.Any(m=>!m.Path.Contains(Path.Combine(".local","browsing-ux","media"))))throw new InvalidOperationException("Approved fixtures required");
        var hashes=await Task.WhenAll(source.Select(m=>GeographicCopy.Hash(m.Path,default)));var original=App.Settings;
        var inputs=new List<(string Phase,double Ms)>();var inputGate=new object();var pending=0;using var stop=new CancellationTokenSource();
        var sampler=Task.Run(async()=>{while(!stop.IsCancellationRequested){if(Interlocked.CompareExchange(ref pending,1,0)==0){var stamp=Stopwatch.GetTimestamp();var phase=profile.Phase;_ = Dispatcher.BeginInvoke(DispatcherPriority.Input,()=>{lock(inputGate){if(inputs.Count<16384)inputs.Add((phase,Stopwatch.GetElapsedTime(stamp).TotalMilliseconds));}Interlocked.Exchange(ref pending,0);});}try{await Task.Delay(10,stop.Token);}catch(OperationCanceledException){break;}}});
        var results=new List<object>();using var process=Process.GetCurrentProcess();var peak=process.PrivateMemorySize64;var clock=Stopwatch.StartNew();
        async Task Settle(string phase)
        {
            profile.Phase=phase;var timer=Stopwatch.StartNew();var plannedAfter=Atlas.CompletedRenderSequence;Atlas.InvalidateVisual();long? readyAfter=null;
            for(var i=0;i<400;i++)
            {
                await Task.Delay(10);if(Atlas.CompletedRenderSequence<=plannedAfter)continue;
                if(Atlas.IsAnimating || (pilot.Enabled?pilot.IsLoading:Atlas.Tiles.IsLoading)){readyAfter=null;continue;}
                if(readyAfter==null){readyAfter=Atlas.CompletedRenderSequence;Atlas.InvalidateVisual();continue;}
                if(Atlas.CompletedRenderSequence>readyAfter){profile.Record("Settling."+phase,timer.Elapsed.TotalMilliseconds);return;}
            }
            throw new InvalidOperationException("Performance workload did not settle");
        }
        foreach(var enabled in new[]{false,true})
        {
            var prefix=enabled?"pilot":"production";pilot.Enabled=enabled;
            foreach(var city in new[]{(Id:"zurich",Lat:47.3782,Lon:8.5402),(Id:"warsaw",Lat:52.2297,Lon:21.0122)})
            foreach(var pose in new[]{(Name:"center",Offset:0d),(Name:"outskirts",Offset:.07),(Name:"beyond",Offset:.18)})
            {
                Atlas.Tiles.Catalog.Preferred=Atlas.Tiles.Catalog.Packages.Single(m=>Path.GetFileName(m.Path)==city.Id+"-city.mbtiles").Id;Atlas.Tiles.Clear();Atlas.SetComparisonCamera(city.Lat,city.Lon+pose.Offset,15);await Settle(prefix+"-cold");
                var phase=prefix+"-"+city.Id+"-"+pose.Name;profile.Phase=phase;var render=new List<double>();var allocated=GC.GetTotalAllocatedBytes(false);
                Atlas.SetDragForVerification(true);
                for(var i=0;i<80;i++){Atlas.PanPixels(new Vector(i<40?-12:12,0));await Task.Delay(16);render.Add(Atlas.LastRenderMilliseconds);process.Refresh();peak=Math.Max(peak,process.PrivateMemorySize64);}
                Atlas.SetDragForVerification(false);await Settle(prefix+"-warm");
                results.Add(new{Phase=phase,Camera=Atlas.Camera,Render=AtlasProfiling.Statistics(render),AllocatedBytes=GC.GetTotalAllocatedBytes(false)-allocated});
            }
            profile.Phase=prefix+"-rapid-and-zoom";Atlas.SetDragForVerification(true);
            for(var i=0;i<100;i++){Atlas.PanPixels(new Vector(i%2==0?32:-32,i%3==0?12:-6));if(i%10==0)Atlas.ZoomBy(i%20==0?.9:1/.9);await Task.Delay(12);}
            Atlas.SetDragForVerification(false);await Settle(prefix+"-zoom");
            profile.Phase=prefix+"-resize";var width=Width;for(var i=0;i<12;i++){Width=width-(i%2)*80;await Task.Delay(30);}Width=width;await Settle(prefix+"-resize");
            profile.Phase=prefix+"-theme";
            foreach(var theme in new[]{"Dark","Light","System"})
            {
                App.Settings=original with{Theme=theme};App.ApplyTheme();await Task.Delay(80);
                if(theme=="Dark")
                {
                    Atlas.SetComparisonCamera(47.3782,8.5402,15);await Settle(prefix+"-dark-cold");profile.Phase=prefix+"-dark-pan";Atlas.SetDragForVerification(true);
                    for(var i=0;i<80;i++){Atlas.PanPixels(new Vector(i<40?-12:12,0));await Task.Delay(16);}
                    Atlas.SetDragForVerification(false);await Settle(prefix+"-dark-warm");
                }
                profile.Phase=prefix+"-theme";
            }
            Atlas.SetComparisonCamera(52.2297,21.1922,15);await Settle(prefix+"-theme");
            profile.Phase=prefix+"-navigation";await NavigateToMedia(source.Single(m=>m.Name=="Zurich.jpg"));await Settle(prefix+"-navigation");
            var japan=outside.FirstOrDefault(g=>g.Items.Any(m=>m.Name=="Tokyo.jpg"));if(japan!=null){await NavigateDestination(japan);await Settle(prefix+"-navigation");}await GoBackLocation();await Settle(prefix+"-navigation");
            var previews=Atlas.Previews;Atlas.Previews=null;var projected=Enumerable.Range(0,10000).Select(i=>new MediaItem(Path.Combine(App.DataDirectory,"projected",i+".jpg"),52.2297+(i/100-50)*.0005,21.0122+(i%100-50)*.0007,"Photo")).ToArray();
            Atlas.Items=projected;Atlas.SetComparisonCamera(52.2297,21.0122,13);await Settle(prefix+"-10k-cold");profile.Phase=prefix+"-10k-projected";
            for(var i=0;i<50;i++){Atlas.PanPixels(new Vector(i%2==0?8:-8,0));await Task.Delay(16);}Atlas.Items=source;Atlas.Previews=previews;await Settle(prefix+"-10k-end");
        }
        profile.Phase="completion";stop.Cancel();await sampler;App.Settings=original;App.ApplyTheme();
        for(var i=0;i<source.Length;i++)if(hashes[i]!=await GeographicCopy.Hash(source[i].Path,default))throw new InvalidOperationException("Source changed");
        Dictionary<string,object> input;lock(inputGate)input=inputs.GroupBy(s=>s.Phase).ToDictionary(g=>g.Key,g=>AtlasProfiling.Statistics(g.Select(s=>s.Ms)));
        File.WriteAllText(Path.Combine(App.DataDirectory,"atlas-performance.json"),JsonSerializer.Serialize(new{Status="COMPLETED",Scope="No screenshots during measured workload. Synthetic Dispatcher Input queue latency, not physical hardware input; projected 10k has no file decoding/indexing",ElapsedSeconds=clock.Elapsed.TotalSeconds,PeakPrivateBytes=peak,Input=input,Stages=profile.Summary(),profile.RuntimeEvents,Results=results,Optimized=pilot.UseOptimizedRendering,GCCollections=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)},LastGCPauses=GC.GetGCMemoryInfo().PauseDurations.ToArray().Select(p=>p.TotalMilliseconds).ToArray(),pilot.LabelCacheHits,pilot.LabelCacheMisses,SourceHashesPreserved=source.Length},new JsonSerializerOptions{WriteIndented=true}));
    }
}
