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
    private FileStream? urbanPreviewLock;
    internal async Task PrepareUrbanPilot(bool verify,bool captures)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."));
        var ownedRoot=Path.GetFullPath(Path.Combine(root,".local","owner054"))+Path.DirectorySeparatorChar;
        if(!App.DataDirectory.StartsWith(ownedRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated Urban Lite profile required");
        urbanPreviewLock=new FileStream(Path.Combine(App.DataDirectory,".urban-preview.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);Closed+=(_,_)=>urbanPreviewLock.Dispose();
        await AtlasReady;await libraryInitialized.Task;
        var input=Path.Combine(root,".local/owner052/validation-media");
        if(!Directory.Exists(input) || Directory.GetFiles(input).Length!=13)throw new InvalidDataException("Approved small media kit missing");
        var hashes=new Dictionary<string,string>();foreach(var file in Directory.GetFiles(input))hashes[file]=await GeographicCopy.Hash(file,default);
        var references=new MapCatalog(Path.Combine(root,".local/owner053/preview"));
        foreach(var map in references.Packages.Where(p=>p.Available))if(!Atlas.Tiles.Catalog.Packages.Any(p=>p.Path==map.Path))Atlas.Tiles.Catalog.Packages.Add(map);
        Atlas.Tiles.Catalog.Save();await ScanFolder(input);await placesRefresh;Width=1360;Height=880;
        var pilot=Atlas.AtlasPilot??throw new InvalidDataException("Hybrid Atlas unavailable");
        if(pilot.GlobalAtlas!=null || App.UrbanMode=="c" && pilot.UrbanRoads==null)throw new InvalidDataException("Incorrect atlas/road pilot initialization");
        var rows=new List<object>();var phase="startup";using var process=Process.GetCurrentProcess();process.Refresh();
        var startup=(DateTime.Now-process.StartTime).TotalMilliseconds;var peak=process.PrivateMemorySize64;
        async Task Ready()
        {
            var sequence=Atlas.CompletedRenderSequence;Atlas.InvalidateVisual();
            for(var i=0;i<400;i++){await Task.Delay(25);if(Atlas.CompletedRenderSequence>sequence && !Atlas.IsAnimating && !Atlas.ActiveTiles.IsLoading){await Task.Delay(60);return;}}
            throw new TimeoutException("Urban Lite frame did not settle");
        }
        void Camera(string name,double lat,double lon,int zoom)
        {
            Atlas.Tiles.Catalog.Preferred="world";
            if(App.UrbanMode=="d" || name=="SaintPetersburg")
            {
                var p=Geo.Project(lat,lon);var world=256*Math.Pow(2,zoom)*1.01;
                var local=Atlas.Tiles.Catalog.Packages.Where(m=>m.Enabled && zoom>=m.MinZoom && zoom<=m.MaxZoom+RasterTilePlan.MaximumOverzoom && RasterComposition.CoversViewport(m,world,Atlas.ActualWidth,Atlas.ActualHeight,new Point(p.X,p.Y)))
                    .OrderByDescending(m=>m.MaxZoom>=zoom).ThenBy(m=>m.MaxZoom).FirstOrDefault();
                if(local!=null)Atlas.Tiles.Catalog.Preferred=local.Id;
            }
            Atlas.SetComparisonCamera(lat,lon,zoom);
        }
        var poses=new[]{("Shanghai",31.2304,121.4737),("SaintPetersburg",59.9343,30.3351),("Tokyo",35.6762,139.6503),("Kyoto",34.9858,135.7588),("Warsaw",52.23,21.01),("Zurich",47.38,8.54),("Prague",50.08,14.42),("EuropeRural",48d,4d),("CentralAsia",46d,68d),("SouthAmerica",-16d,-49d),("Australia",-30.5,148.5),("Coast",35.4,140.2),("DateLine",-17.8,179.8)};
        object? inputTiming=null;object? stages=null;
        if(verify)
        {
            using var timing=new AtlasProfiling(Dispatcher);
            if(!captures)
            {
                using var probe=new UiResponsivenessProbe(this,()=>phase,"native-only");
                foreach(var theme in new[]{"Light","Dark"})
                {
                    ApplySettings("en-US",theme);
                    foreach(var pose in poses)foreach(var zoom in new[]{5,7,9,11,13})
                    {
                        phase=pose.Item1+"/"+theme+"/"+zoom;timing.Phase=phase;Camera(pose.Item1,pose.Item2,pose.Item3,zoom);await Ready();
                        var durations=new List<double>();Atlas.SetDragForVerification(true);
                        for(var i=0;i<16;i++){Atlas.PanPixels(new Vector(i<8?-9:9,0));await Task.Delay(16);durations.Add(Atlas.LastRenderMilliseconds);process.Refresh();peak=Math.Max(peak,process.PrivateMemorySize64);}
                        Atlas.SetDragForVerification(false);await Ready();
                        rows.Add(new{Place=pose.Item1,Theme=theme,Zoom=zoom,Atlas.Camera,Render=AtlasProfiling.Statistics(durations),Roads=pilot.UrbanRoads?.LastDrawn??0,pilot.LastRasterVisible,pilot.LastDrawnShapes});
                    }
                }
                inputTiming=probe.Report();
            }
            else
            {
                var directory=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(directory);
                foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
                {
                    ApplySettings(language,theme);
                    foreach(var pose in poses)foreach(var zoom in pose.Item1 is "Shanghai" or "SaintPetersburg"?new[]{7,9,11,13}:new[]{9})
                    {
                        Camera(pose.Item1,pose.Item2,pose.Item3,zoom);await Ready();
                        // Publication can follow an idle fetch queue; only require a frame where an installed full surface was selected.
                        if(pose.Item1=="SaintPetersburg" && Atlas.Tiles.Catalog.Preferred!="world")for(var i=0;i<100 && !pilot.LastRasterVisible;i++)await Task.Delay(50);
                        var file=$"{pose.Item1}-{zoom}-{language}-{theme}.png";FeatureVerification.Snapshot(this,Path.Combine(directory,file));
                        rows.Add(new{Place=pose.Item1,Theme=theme,Language=language,Zoom=zoom,File=file,Atlas.Camera,MarkerPoints=Atlas.MediaPositionsForVerification(),Roads=pilot.UrbanRoads?.LastDrawn??0,pilot.LastRasterVisible,Rendered=pilot.RasterTiles.RenderedMapIds.ToArray()});
                    }
                }
                if(Directory.GetFiles(directory).Sum(p=>new FileInfo(p).Length)>32*1024*1024)throw new InvalidDataException("Capture budget exceeded");
                if(App.UrbanMode=="d")
                {
                    var dialog=new MapDownloadsWindow(this,this);dialog.Show();
                    try
                    {
                        for(var i=0;i<200 && dialog.Rows.Count==0;i++)await Task.Delay(50);
                        if(dialog.Rows.Count==0)throw new InvalidOperationException("Library coverage planning produced no rows");
                        FeatureVerification.Snapshot(dialog,Path.Combine(directory,"regional-download-plan.png"));
                        File.WriteAllText(Path.Combine(App.DataDirectory,"regional-plan.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Routed preview only; no download confirmation or acquisition invoked",Areas=dialog.Rows.Select(r=>r.Area).ToArray(),NetworkAcquisitionInvoked=false},new JsonSerializerOptions{WriteIndented=true}));
                    }
                    finally{dialog.Close();}
                }
            }
            stages=timing.Summary();
        }
        Camera("Shanghai",31.2304,121.4737,11);await Ready();await Task.Delay(500);process.Refresh();
        foreach(var pair in hashes)if(await GeographicCopy.Hash(pair.Key,default)!=pair.Value)throw new InvalidOperationException("Approved original changed");
        File.WriteAllText(Path.Combine(App.DataDirectory,"urban-verification.json"),JsonSerializer.Serialize(new{Status="COMPLETED",Mode=App.UrbanMode,Captures=captures,Version=RuntimeIdentity.ProductVersion,Executable=RuntimeIdentity.Executable,Profile=App.DataDirectory,PID=Environment.ProcessId,RuntimeIdentity.NativeCommandLine,Assemblies=RuntimeIdentity.Assemblies(),StartupMilliseconds=startup,pilot.InitializationMilliseconds,RoadInitialization=pilot.UrbanRoads?.InitializationMilliseconds??0,Resources=pilot.ResourceSnapshot(),SampledPeakPrivate=peak,SettledPrivate=process.PrivateMemorySize64,Managed=GC.GetTotalMemory(false),WorkingSet=process.WorkingSet64,SourceHashes=hashes,Input=inputTiming,Stages=stages,Rows=rows,NativeSyncFlush="NOT_MEASURED",PhysicalInteraction="NOT_TESTED"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
