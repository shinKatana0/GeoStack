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
    internal async Task VerifyAtlasPilot()
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var pilot=Atlas.AtlasPilot ?? throw new InvalidOperationException("No atlas pilot");
        var output=Path.Combine(App.DataDirectory,"atlas-evidence");Directory.CreateDirectory(output);
        var original=App.Settings;var results=new List<object>();var originals=ViewModel.Items.ToArray();
        if(originals.Length!=9 || originals.Any(m=>!m.Path.Contains(Path.Combine(".local","browsing-ux","media"))))throw new InvalidOperationException("Approved fixtures required");
        var hashes=await Task.WhenAll(originals.Select(m=>GeographicCopy.Hash(m.Path,default)));
        using var process=Process.GetCurrentProcess();long peak=process.PrivateMemorySize64;
        var phase="setup";var stageGaps=new Dictionary<string,double>();var gap=0d;var last=Stopwatch.GetTimestamp();var heartbeat=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};
        heartbeat.Tick+=(_,_)=>{var now=Stopwatch.GetTimestamp();var elapsed=Stopwatch.GetElapsedTime(last,now).TotalMilliseconds;gap=Math.Max(gap,elapsed);stageGaps[phase]=Math.Max(stageGaps.GetValueOrDefault(phase),elapsed);last=now;process.Refresh();peak=Math.Max(peak,process.PrivateMemorySize64);};heartbeat.Start();
        async Task Settle(){for(var i=0;i<100;i++){await Task.Delay(100);if(!Atlas.IsAnimating && !(pilot.Enabled?pilot.IsLoading:Atlas.Tiles.IsLoading)){await Task.Delay(250);return;}}throw new InvalidOperationException("Atlas comparison did not settle");}
        foreach(var theme in new[]{"Light","Dark"})
        {
            App.Settings=original with{Theme=theme};App.ApplyTheme();
            foreach(var city in new[]{(Id:"zurich",Lat:47.3782,Lon:8.5402),(Id:"warsaw",Lat:52.2297,Lon:21.0122)})
            foreach(var pose in new[]{(Name:"region",Offset:.15,Z:10),(Name:"transition-11",Offset:0d,Z:11),(Name:"transition-13",Offset:0d,Z:13),(Name:"transition-14",Offset:0d,Z:14),(Name:"center",Offset:0d,Z:15),(Name:"outskirts",Offset:.07,Z:15),(Name:"beyond",Offset:.18,Z:14)})
            {
                var map=Atlas.Tiles.Catalog.Packages.Single(p=>Path.GetFileName(p.Path)==city.Id+"-city.mbtiles");Atlas.Tiles.Catalog.Preferred=map.Id;Atlas.Tiles.Clear();
                foreach(var enabled in new[]{false,true})
                {
                    pilot.Enabled=enabled;Atlas.SetComparisonCamera(city.Lat,city.Lon+pose.Offset,pose.Z);await Settle();
                    var name=$"{city.Id}-{pose.Name}-{theme}-{(enabled?"pilot":"production")}";phase="capture";FeatureVerification.Snapshot(this,Path.Combine(output,name+".png"));await Task.Delay(50);
                    var renderSamples=new List<double>();var total=Stopwatch.StartNew();
                    phase=enabled?"pilot-pan":"production-pan";
                    for(var i=0;i<40;i++){Atlas.PanPixels(new Vector(i%2==0?4:-4,0));await Task.Delay(16);renderSamples.Add(Atlas.LastRenderMilliseconds);}
                    phase="settling";await Settle();process.Refresh();results.Add(new{City=city.Id,Pose=pose.Name,Theme=theme,Pilot=enabled,Camera=Atlas.Camera,NativeLod=enabled?pilot.LastNativeLod:0,Shapes=enabled?pilot.LastDrawnShapes:0,Raster=enabled?pilot.LastRasterVisible:Atlas.Tiles.DrawnTileCount>0,RenderMedianMs=renderSamples.Count==0?0:renderSamples.Order().ElementAt(renderSamples.Count/2),RenderMaximumMs=renderSamples.DefaultIfEmpty().Max(),PanSequenceMilliseconds=total.Elapsed.TotalMilliseconds,PrivateBytes=process.PrivateMemorySize64});
                }
            }
        }
        pilot.Enabled=true;App.Settings=original;App.ApplyTheme();
        // Crossing sequences at identical camera states preserve the shared media projection.
        foreach(var city in new[]{(Id:"zurich",Lat:47.3782,Lon:8.5402),(Id:"warsaw",Lat:52.2297,Lon:21.0122)})
        {
            Atlas.SetComparisonCamera(city.Lat,city.Lon,15);await Settle();var before=Atlas.MediaPositionsForVerification();Atlas.SetDragForVerification(true);phase="boundary-pan";
            for(var i=0;i<100;i++){Atlas.PanPixels(new(-24,0));await Task.Delay(16);if(i is 30 or 55 or 80){phase="capture";FeatureVerification.Snapshot(this,Path.Combine(output,$"{city.Id}-pan-{i:D2}.png"));await Task.Delay(50);phase="boundary-pan";}}
            for(var i=0;i<100;i++){Atlas.PanPixels(new(24,0));await Task.Delay(16);}Atlas.SetDragForVerification(false);await Settle();
            var after=Atlas.MediaPositionsForVerification();if(before.Any(p=>(after[p.Key]-p.Value).Length>.01))throw new InvalidOperationException("Pilot marker drift");
        }
        foreach(var z in new[]{3,6}){Atlas.SetComparisonCamera(35,10,z);phase="settling";await Settle();phase="capture";FeatureVerification.Snapshot(this,Path.Combine(output,$"world-{z}.png"));await Task.Delay(50);}
        foreach(var language in new[]{"en-US","ru-RU"})
        {
            App.Settings=original with{Language=language,Theme="Dark"};Strings.SetLanguage(language);App.ApplyTheme();Localize();ViewModel.RefreshLanguage();
            foreach(var city in new[]{"Zurich","Warsaw"})
            {
                var selected=originals.Single(m=>m.Name==city+".jpg");await NavigateToMedia(selected);await Settle();
                if(Atlas.SelectedMediaId!=selected.Path || !pilot.LastRasterVisible)throw new InvalidOperationException("Detailed dark map or exact navigation selection missing");
                phase="capture";FeatureVerification.Snapshot(this,Path.Combine(output,$"{city.ToLowerInvariant()}-Dark-{language}-selected-stack.png"));await Task.Delay(50);
            }
        }
        App.Settings=original;Strings.SetLanguage(original.Language);App.ApplyTheme();Localize();ViewModel.RefreshLanguage();
        for(var i=0;i<originals.Length;i++)if(hashes[i]!=await GeographicCopy.Hash(originals[i].Path,default))throw new InvalidOperationException("Pilot source changed");
        heartbeat.Stop();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(200);process.Refresh();
        File.WriteAllText(Path.Combine(App.DataDirectory,"atlas-pilot.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Actual packaged WPF comparisons; physical input NOT_TESTED; A/B retains both implementations in one process",pilot.InitializationMilliseconds,pilot.FeatureCount,pilot.VertexCount,PeakPrivateBytes=peak,PostCollectionPrivateBytes=process.PrivateMemorySize64,PostCollectionManagedBytes=GC.GetTotalMemory(false),MaximumDispatcherGapMs=gap,DispatcherStageMaxima=stageGaps,SourceHashesPreserved=originals.Length,MarkerAlignment=true,Results=results},new JsonSerializerOptions{WriteIndented=true}));
    }
}
