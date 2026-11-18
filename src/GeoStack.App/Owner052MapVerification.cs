// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyOwner052Maps(bool acquire)
    {
        if(!App.DataDirectory.Contains("\\.local\\owner052\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned profile required");
        await AtlasReady;await libraryInitialized.Task;
        var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        var items=new[]{new MediaItem(Path.Combine(App.DataDirectory,"synthetic-petersburg.jpg"),59.9343,30.3351,"Photo")};
        Atlas.Items=items;var pilot=Atlas.AtlasPilot??throw new InvalidOperationException("Hybrid atlas missing");
        if(pilot.GlobalAtlas!=null)throw new InvalidOperationException("Experimental world atlas must remain disabled");
        var plan=await Task.Run(()=>RegionalContextPlanner.Extend(LibraryMapPlanner.Build(items,Atlas.Tiles.Catalog.Packages,App.LocationResolver.Resolve,default,validateTile:MapProbe.CanDecode),Atlas.Tiles.Catalog.Packages,default,MapProbe.CanDecode));
        File.WriteAllText(Path.Combine(App.DataDirectory,"coverage-plan.json"),JsonSerializer.Serialize(plan,new JsonSerializerOptions{WriteIndented=true}));
        async Task<List<object>> Capture(string phase)
        {
            var rows=new List<object>();
            foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})foreach(var pose in new[]{("PetersburgRegion",59.9343,30.3351,8),("PetersburgContext",59.9343,30.3351,10),("PetersburgCity",59.9343,30.3351,12),("PetersburgDetail",59.9343,30.3351,15),("PetersburgOutsideDetail",60.1,30.7,10),("Warsaw",52.2297,21.0122,11),("Zurich",47.3782,8.5402,11),("Uninstalled",-33.8688,151.2093,10)})
            {
                ApplySettings(language,theme);Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);await SettleBrowsing();
                await Task.Delay(200);
                var drawn=pilot.RasterTiles.RenderedMapIds.Select(id=>pilot.RasterTiles.Catalog.Packages.FirstOrDefault(m=>m.Id==id)).Where(m=>m!=null).ToArray();
                if(phase=="after" && pose.Item1.StartsWith("Petersburg") && Atlas.Tiles.Catalog.Packages.Any(m=>m.Id.StartsWith("region-77f7100d7f93d6ea")) && (!pilot.LastRasterVisible || drawn.Length==0 || drawn.Any(m=>!NativeAtlasPilot.MatchesCityTheme(Path.GetFileName(m!.Path),theme=="Dark",language))))throw new InvalidOperationException("Themed Saint Petersburg surface was not rendered");
                rows.Add(new{RenderedFiles=drawn.Select(m=>Path.GetFileName(m!.Path)),Language=language,Theme=theme,Place=pose.Item1,Atlas.Camera,pilot.LastRasterVisible,pilot.LastDrawnShapes,Atlas.ActiveTiles.DrawnTileCount,Atlas.ActiveTiles.RenderedMapIds});
                FeatureVerification.Snapshot(this,Path.Combine(evidence,$"{phase}-{pose.Item1}-{language}-{theme}.png"));
            }
            return rows;
        }
        var before=await Capture("before");var installed=new List<MapPackage>();
        if(acquire)
        {
            // Explicit diagnostic acquisition requested by the owner; ordinary browsing never reaches this path.
            foreach(var area in plan.Areas.Where(a=>!a.Installed))
            {
                var map=await MapAcquisition.Prepare(area,Path.Combine(App.DataDirectory,"Maps","Library"),new Progress<AcquisitionProgress>(p=>ViewModel.Status=$"{p.Stage}: {p.Completed}/{p.Total}; {p.Bytes} bytes"),default);
                Atlas.Tiles.Catalog.Packages.Add(map);Atlas.Tiles.Catalog.Preferred=map.Id;Atlas.Tiles.Catalog.Save();installed.Add(map);
            }
        }
        var after=await Capture("after");
        var petersburg=after.Where(r=>r!=null).ToArray();
        var installedPlan=await Task.Run(()=>RegionalContextPlanner.Extend(LibraryMapPlanner.Build(items,Atlas.Tiles.Catalog.Packages,App.LocationResolver.Resolve,default,validateTile:MapProbe.CanDecode),Atlas.Tiles.Catalog.Packages,default,MapProbe.CanDecode));
        if(acquire && installedPlan.Areas.Any(a=>!a.Installed))throw new InvalidOperationException("Installed Saint Petersburg coverage still incomplete");
        object input;
        using(var probe=new UiResponsivenessProbe(this,()=>"Petersburg-pan","native-only"))
        {Atlas.SetComparisonCamera(59.9343,30.3351,10);await SettleBrowsing();Atlas.SetDragForVerification(true);for(var i=0;i<80;i++){Atlas.PanPixels(new(i<40?8:-8,0));await Task.Delay(16);}Atlas.SetDragForVerification(false);await SettleBrowsing();input=probe.Report();}
        using var process=Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(App.DataDirectory,"owner052-maps.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Central Saint Petersburg test pose; exact owner screenshot camera unavailable; WPF renders/programmatic pans, physical NOT_TESTED",Before=before,After=after,Installed=installed,Input=input,Resources=pilot.ResourceSnapshot(),process.PrivateMemorySize64},new JsonSerializerOptions{WriteIndented=true}));
    }
}
