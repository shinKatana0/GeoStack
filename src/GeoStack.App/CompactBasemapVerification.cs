// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    private FileStream? preview053Lock;
    internal async Task PrepareCompactBasemapPreview(bool verify,bool captures)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."));
        if(!App.DataDirectory.StartsWith(Path.Combine(root,".local","owner053")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned 0.5.3 profile required");
        preview053Lock=new FileStream(Path.Combine(App.DataDirectory,".preview053.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);Closed+=(_,_)=>preview053Lock.Dispose();
        await AtlasReady;await libraryInitialized.Task;
        var input=Path.Combine(root,".local/owner052/validation-media");
        if(!Directory.Exists(input) || Directory.GetFiles(input).Length!=13)throw new InvalidDataException("Approved 13-item validation kit unavailable; run the 0.5.2 validation launcher first");
        var hashes=new Dictionary<string,string>();foreach(var path in Directory.GetFiles(input))hashes[path]=await GeographicCopy.Hash(path,default);
        var references=new MapCatalog(Path.Combine(root,".local/owner052/validation"));
        foreach(var map in references.Packages.Where(m=>m.Available))if(!Atlas.Tiles.Catalog.Packages.Any(m=>m.Id==map.Id))Atlas.Tiles.Catalog.Packages.Add(map);
        foreach(var name in new[]{"shanghai-city.mbtiles","shanghai-context-city.mbtiles"})
        {var path=Path.Combine(root,"offline-maps",name);if(File.Exists(path) && !Atlas.Tiles.Catalog.Packages.Any(p=>p.Path==path))Atlas.Tiles.Catalog.Packages.Add(await Task.Run(()=>MapPackage.Inspect(path)));}
        Atlas.Tiles.Catalog.Preferred="world";Atlas.Tiles.Catalog.Save();
        await ScanFolder(input);await placesRefresh;
        Width=1360;Height=880;
        var pilot=Atlas.AtlasPilot??throw new InvalidDataException("Hybrid Atlas missing");
        if(pilot.GlobalAtlas!=null)throw new InvalidDataException("Experimental worldwide atlas must remain inactive");
        var rows=new List<object>();var phase="startup";using var process=Process.GetCurrentProcess();
        var startup=(DateTime.Now-process.StartTime).TotalMilliseconds;
        var peak=process.PrivateMemorySize64;
        async Task Ready()
        {
            var before=Atlas.CompletedRenderSequence;Atlas.InvalidateVisual();
            for(var i=0;i<400;i++){await Task.Delay(25);if(Atlas.CompletedRenderSequence>before && !Atlas.IsAnimating && !Atlas.ActiveTiles.IsLoading){await Task.Delay(60);return;}}
            throw new TimeoutException("Basemap verification frame did not settle");
        }
        var poses=new[]{("Shanghai",31.2304,121.4737),("SaintPetersburg",59.9343,30.3351),("WarsawOutside",52.6,21.6),("Tokyo",35.6762,139.6503),("ZurichBoundary",47.6,8.9),("NorthAmerica",39.5,-99.1),("CentralAsia",46d,68d),("Coast",35.4,140.2),("DateLine",-17.8,179.8)};
        object? responsiveness=null;object? stages=null;
        if(verify)
        {
            using var timing=new AtlasProfiling(Dispatcher);
            if(!captures)
            {
                using(var probe=new UiResponsivenessProbe(this,()=>phase,"native-only"))
                foreach(var theme in new[]{"Light","Dark"})
                {
                    ApplySettings("en-US",theme);
                    foreach(var pose in poses)foreach(var zoom in new[]{5,7,9,11,13,15})
                    {
                        phase=pose.Item1+"/"+theme+"/"+zoom;timing.Phase=phase;Atlas.SetComparisonCamera(pose.Item2,pose.Item3,zoom);await Ready();
                        var render=new List<double>();var sw=Stopwatch.StartNew();Atlas.SetDragForVerification(true);
                        for(var i=0;i<24;i++){Atlas.PanPixels(new Vector(i<12?-9:9,i%2==0?2:-2));await Task.Delay(16);render.Add(Atlas.LastRenderMilliseconds);process.Refresh();peak=Math.Max(peak,process.PrivateMemorySize64);}
                        Atlas.SetDragForVerification(false);await Ready();
                        rows.Add(new{Pose=pose.Item1,Theme=theme,Zoom=zoom,Atlas.Camera,Render=AtlasProfiling.Statistics(render),DurationMs=sw.Elapsed.TotalMilliseconds,pilot.LastDrawnShapes,pilot.LastNativeLod,pilot.LastRasterVisible});
                    }
                    responsiveness=probe.Report();
                }
                // The probe spans both themes; its final report includes every completed message.
            }
            else
            {
                var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
                foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
                {
                    ApplySettings(language,theme);
                    foreach(var pose in poses)foreach(var zoom in pose.Item1=="Shanghai"?new[]{5,7,9,11,13,15}:new[]{9})
                    {
                        Atlas.SetComparisonCamera(pose.Item2,pose.Item3,zoom);await Ready();
                        var sw=Stopwatch.StartNew();FeatureVerification.Snapshot(this,Path.Combine(evidence,$"{pose.Item1}-{zoom}-{language}-{theme}.png"));
                        rows.Add(new{Pose=pose.Item1,Theme=theme,Language=language,Zoom=zoom,Atlas.Camera,CaptureMs=sw.Elapsed.TotalMilliseconds,pilot.LastDrawnShapes,pilot.LastRasterVisible,MarkerPoints=Atlas.MediaPositionsForVerification()});
                        if(pose.Item1=="Shanghai" && zoom==11){Atlas.PanPixels(new Vector(-Atlas.ActualWidth*3,0));await Ready();FeatureVerification.Snapshot(this,Path.Combine(evidence,$"Shanghai-pan-{language}-{theme}.png"));}
                    }
                }
                if(Directory.GetFiles(evidence).Sum(p=>new FileInfo(p).Length)>32*1024*1024)throw new InvalidDataException("Evidence budget exceeded");
            }
            stages=timing.Summary();
        }
        Atlas.SetComparisonCamera(31.2304,121.4737,11);await Ready();await Task.Delay(500);process.Refresh();
        foreach(var pair in hashes)if(await GeographicCopy.Hash(pair.Key,default)!=pair.Value)throw new InvalidOperationException("Approved source changed");
        File.WriteAllText(Path.Combine(App.DataDirectory,"basemap-verification.json"),JsonSerializer.Serialize(new{Status="COMPLETED",Mode=App.LegacyGlobalFallbackForVerification?"Accepted coarse-world comparison model":"Compact always-on fallback",Captures=captures,Scope="Programmatic WPF camera/pan and native synthetic queue timings; physical pointer NOT_TESTED. Capture workloads are separate.",Version=RuntimeIdentity.ProductVersion,Executable=RuntimeIdentity.Executable,Profile=App.DataDirectory,PID=Environment.ProcessId,RuntimeIdentity.NativeCommandLine,StartupToPreparedMilliseconds=startup,pilot.InitializationMilliseconds,Resources=pilot.ResourceSnapshot(),PeakPrivateBytes=peak,SettledPrivateBytes=process.PrivateMemorySize64,WorkingSet=process.WorkingSet64,ManagedBytes=GC.GetTotalMemory(false),Threads=process.Threads.Count,Handles=process.HandleCount,SourceHashes=hashes,Rows=rows,Input=responsiveness,Stages=stages,NativeSyncFlush="NOT_MEASURED: no native ETW attach in this bounded run"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
