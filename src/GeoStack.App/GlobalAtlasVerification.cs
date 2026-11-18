// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyGlobalAtlas(string package,bool restart)
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated synthetic profile required");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."));
        var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        var initialization=Stopwatch.StartNew();await AtlasReady;await libraryInitialized.Task;
        var pilot=Atlas.AtlasPilot??throw new InvalidOperationException("Native atlas required");
        var startupMilliseconds=initialization.Elapsed.TotalMilliseconds;
        if(!restart)await ScanFolder(Path.Combine(root,".local/browsing-ux/media"));
        await placesRefresh;await SettleBrowsing();
        var media=ViewModel.Items.ToArray();if(media.Length!=9 || media.Any(m=>!m.Path.Contains("\\.local\\browsing-ux\\media\\",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Approved nine-media fixture required");
        var hashes=new Dictionary<string,string>();foreach(var item in media)hashes[item.Path]=await GeographicCopy.Hash(item.Path,default);
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        Width=1360;Height=880;ShowSidebar();await ShowSelection(media,Strings.Get("AllMedia"),null);
        var cases=new[]{("Warsaw",52.2297,21.0122,8),("Tokyo",35.6762,139.6503,8),("Tokyo-Regional",35.6762,139.6503,7),("Tokyo-Context",35.6762,139.6503,11),("Zurich",47.3782,8.5402,10),("Kyoto",35.0116,135.7681,10),("Prague",50.0755,14.4378,10),("Rural-France",46.3,2.2,9),("Patagonia",-46.5,-72d,9),("Nairobi",-1.2864,36.8172,10),("Tokyo-Coast",35.4,140.2,9),("Date-Line",-17.8,179.8,8),("Ocean",0d,-150d,8),("Warsaw-Edge",52.2297,21.22,12),("Warsaw-Detail",52.2297,21.0122,15),("Warsaw-Boundary",52.2297,21.085,15)};
        async Task<double> Settle(bool global)
        {
            var timer=Stopwatch.StartNew();Atlas.InvalidateVisual();
            for(var i=0;i<600;i++)
            {
                await Task.Delay(20);var camera=Atlas.Camera;
                if(Atlas.IsAnimating || Atlas.ActiveTiles.IsLoading)continue;
                if(global && !pilot.GlobalAtlas!.CanCover(camera.World,camera.Width,camera.Height,new(camera.X,camera.Y)))continue;
                Atlas.InvalidateVisual();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);return timer.Elapsed.TotalMilliseconds;
            }
            throw new InvalidOperationException("Global context did not settle: "+JsonSerializer.Serialize(pilot.ResourceSnapshot()));
        }
        var installed=Stopwatch.StartNew();if(!restart)await pilot.InstallGlobalAtlas(package);
        Require(pilot.GlobalAtlas!=null && Path.GetFullPath(pilot.GlobalAtlas.Path)==Path.GetFullPath(package),"Global reference did not persist");
        var installMilliseconds=installed.Elapsed.TotalMilliseconds;var rows=new List<object>();var referenceViews=new Dictionary<string,(MapCamera Camera,IReadOnlyDictionary<string,Point> Positions)>();
        if(!restart)
        foreach(var theme in new[]{"Light","Dark"})foreach(var enabled in new[]{false,true})
        {
            pilot.GlobalAtlas!.Enabled=enabled;ApplySettings("en-US",theme);
            foreach(var pose in cases)
            {
                Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);var settle=await Settle(enabled);
                if(enabled)Require(pilot.GlobalAtlas.DrawnTiles>0 && pilot.GlobalAtlas.FailedTiles==0,"Real global tiles unavailable");
                var key=pose.Item1+theme;var positions=Atlas.MediaPositionsForVerification();
                if(!enabled)referenceViews[key]=(Atlas.Camera,positions);
                else{var reference=referenceViews[key];Require(reference.Camera==Atlas.Camera && positions.All(p=>reference.Positions[p.Key]==p.Value),"Geographic marker alignment changed between layers");}
                var name=$"{pose.Item1}-{theme}-{(enabled?"after":"before")}";
                FeatureVerification.Snapshot(this,Path.Combine(evidence,name+".png"));
                rows.Add(new{Name=name,Atlas.Camera,MarkerAlignmentVerified=enabled,SettlingMilliseconds=settle,RenderMilliseconds=Atlas.LastRenderMilliseconds,Resources=pilot.ResourceSnapshot()});
            }
        }
        pilot.GlobalAtlas!.Enabled=true;
        var performance=new List<object>();var performanceTargetsMet=true;using var process=Process.GetCurrentProcess();
        foreach(var enabled in restart?new[]{true}:new[]{false,true})
        {
            pilot.GlobalAtlas.Enabled=enabled;ApplySettings("en-US","Light");Atlas.SetComparisonCamera(52.2297,21.0122,10);await Settle(enabled);
            var delays=new List<(string Phase,double Ms)>();var phase="setup";var renders=new List<double>();var settling=new List<double>();var pending=0;var sync=new object();using var stop=new CancellationTokenSource();
            var clock=Stopwatch.StartNew();var cpu=process.TotalProcessorTime;process.Refresh();var beforePrivate=process.PrivateMemorySize64;var peak=beforePrivate;
            var sampler=Task.Run(async()=>{while(!stop.IsCancellationRequested){if(Interlocked.CompareExchange(ref pending,1,0)==0){var tick=Stopwatch.GetTimestamp();var samplePhase=phase;_=Dispatcher.BeginInvoke(DispatcherPriority.Input,()=>{lock(sync){if(delays.Count<16384)delays.Add((samplePhase,Stopwatch.GetElapsedTime(tick).TotalMilliseconds));}Interlocked.Exchange(ref pending,0);});}try{await Task.Delay(10,stop.Token);}catch(OperationCanceledException){break;}}});
            try
            {
                foreach(var pose in cases.Where(p=>p.Item1 is not ("Tokyo-Regional" or "Tokyo-Context")).Take(10))
                {
                    phase=pose.Item1+"-navigate";Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);settling.Add(await Settle(enabled));Atlas.SetDragForVerification(true);phase=pose.Item1+"-pan";
                    for(var i=0;i<40;i++){Atlas.PanPixels(new Vector(i<20?-12:12,i%2==0?3:-3));await Task.Delay(16);renders.Add(Atlas.LastRenderMilliseconds);}
                    Atlas.SetDragForVerification(false);settling.Add(await Settle(enabled));process.Refresh();peak=Math.Max(peak,process.PrivateMemorySize64);
                }
            }
            finally{Atlas.SetDragForVerification(false);stop.Cancel();await sampler;}
            process.Refresh();(string Phase,double Ms)[] input;lock(sync)input=delays.ToArray();
            if(enabled && input.Any(s=>s.Ms>150))performanceTargetsMet=false;
            performance.Add(new{Global=enabled,Scope="Capture-free synthetic Input-priority queue timing and programmatic camera movement; not physical mouse acceptance",Input=AtlasProfiling.Statistics(input.Select(s=>s.Ms)),ByPhase=input.GroupBy(s=>s.Phase).ToDictionary(g=>g.Key,g=>AtlasProfiling.Statistics(g.Select(s=>s.Ms))),Render=AtlasProfiling.Statistics(renders),Settling=AtlasProfiling.Statistics(settling),ElapsedSeconds=clock.Elapsed.TotalSeconds,CpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds,BeforePrivateBytes=beforePrivate,AfterPrivateBytes=process.PrivateMemorySize64,PeakPrivateBytes=peak,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false),Resources=pilot.ResourceSnapshot()});
        }
        pilot.GlobalAtlas.Enabled=true;
        foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
        {
            ApplySettings(language,theme);Atlas.SetComparisonCamera(35.6762,139.6503,10);await Settle(true);
            MediaGrid.SelectedItems.Clear();RefreshCopyActions();Require(!CopyButton.IsEnabled,"Unselected copy action enabled");
            foreach(var item in ViewModel.VisibleItems.Take(2))MediaGrid.SelectedItems.Add(item);RefreshCopyActions();Require(CopyButton.IsEnabled && copyActionLabel!.Text.EndsWith("(2)"),"Primary copy selection count mismatch");
            Atlas.ZoomBy(1e6);await Settle(true);RefreshZoomActions();Require(!ZoomInButton.IsEnabled,"Zoom-in enabled at actual camera limit");var max=Atlas.Camera;Atlas.ZoomBy(1.5);await Settle(true);Require(Atlas.Camera==max,"Redundant zoom-in changed camera");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"controls-{language}-{theme}.png"));
            Exception? managerFailure=null;
            _=Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{try{var dialog=OwnedWindows.Cast<Window>().Single(w=>w.Title==Strings.Get("GlobalAtlas"));dialog.UpdateLayout();FeatureVerification.Snapshot(dialog,Path.Combine(evidence,$"global-manager-{language}-{theme}.png"));dialog.Close();}catch(Exception error){managerFailure=error;foreach(Window dialog in OwnedWindows)dialog.Close();}});
            OpenGlobalAtlasManager(this);if(managerFailure!=null)throw managerFailure;
            Atlas.ZoomBy(1e-6);await SettleBrowsing();RefreshZoomActions();Require(!ZoomOutButton.IsEnabled,"Zoom-out enabled at actual camera limit");var min=Atlas.Camera;Atlas.ZoomBy(.5);await SettleBrowsing();Require(Atlas.Camera==min,"Redundant zoom-out changed camera");
        }
        ApplySettings("en-US","Light");await NavigateToMedia(media.Single(m=>m.Name=="Kyoto.jpg"));await Settle(true);
        Require(Atlas.SelectedMediaId==media.Single(m=>m.Name=="Kyoto.jpg").Path,"Exact media selection lost");
        foreach(var hash in hashes)Require(await GeographicCopy.Hash(hash.Key,default)==hash.Value,"Approved media source changed");
        File.WriteAllText(Path.Combine(App.DataDirectory,restart?"global-restart.json":"global-atlas.json"),JsonSerializer.Serialize(new{Status=performanceTargetsMet?"PASS":"BLOCKED_UI_PERFORMANCE",FunctionalChecks="PASS",PerformanceTargetsMet=performanceTargetsMet,Scope="Packaged native WPF rendered frames, actual global MVT data, programmatic camera and button states; physical mouse and file-picker NOT_TESTED",Restart=restart,InitializationMilliseconds=startupMilliseconds,InstallMilliseconds=installMilliseconds,Package=package,SourceHashes=hashes,Performance=performance,Captures=rows,FinalResources=pilot.ResourceSnapshot()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
