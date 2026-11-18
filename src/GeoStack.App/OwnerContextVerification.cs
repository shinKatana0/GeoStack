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
    internal async Task VerifyOwner051Context()
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated profile required");
        await AtlasReady;await libraryInitialized.Task;Width=1360;Height=880;
        var pilot=Atlas.AtlasPilot??throw new InvalidOperationException("Hybrid atlas required");
        if(pilot.GlobalAtlas!=null)throw new InvalidOperationException("Experimental global atlas must stay disconnected");
        var city=Atlas.Tiles.Catalog.Packages.Single(p=>Path.GetFileName(p.Path)=="warsaw-city.mbtiles");
        Atlas.Tiles.Catalog.Preferred=city.Id;Atlas.Tiles.Catalog.Save();
        var fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/geostack05-owner-acceptance/media"));await ScanFolder(fixture);await placesRefresh;
        var output=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(output);var rows=new List<object>();
        foreach(var theme in new[]{"Light","Dark"})foreach(var legacy in new[]{true,false})
        {
            pilot.LegacyCoverageForVerification=legacy;ApplySettings("en-US",theme);
            foreach(var pose in new[]{("Warsaw",52.2297,21.0122,12),("Detail",52.2297,21.0122,15),("Edge",52.2297,21.11,13),("CoarseMargin",52.8,20.1,10),("Outside",54.0,24.0,9),("Zurich",47.3782,8.5402,12)})
            {
                Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);await SettleBrowsing();
                var before=Atlas.MediaPositionsForVerification();var durations=new List<double>();
                using(var probe=new UiResponsivenessProbe(this,()=>"regional-pan","native-only"))
                {
                    Atlas.SetDragForVerification(true);for(var i=0;i<32;i++){Atlas.PanPixels(new Vector(i<16?-8:8,0));await Task.Delay(16);durations.Add(Atlas.LastRenderMilliseconds);}Atlas.SetDragForVerification(false);await SettleBrowsing();
                    var after=Atlas.MediaPositionsForVerification();if(before.Any(p=>(after[p.Key]-p.Value).Length>.01))throw new InvalidOperationException("Media marker drift");
                    var active=Atlas.ActiveTiles;rows.Add(new{Theme=theme,LegacyPolicy=legacy,Pose=pose.Item1,Atlas.Camera,pilot.LastRasterVisible,pilot.LastDrawnShapes,active.VisibleNativeZoom,active.RenderedMapIds,Render=AtlasProfiling.Statistics(durations),Input=probe.Report()});
                }
                FeatureVerification.Snapshot(this,Path.Combine(output,$"{pose.Item1}-{theme}-{(legacy?"before":"after")}.png"));
            }
        }
        pilot.LegacyCoverageForVerification=false;
        if(!rows.Any())throw new InvalidOperationException("No rendered comparisons");
        using var process=Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(App.DataDirectory,"owner051-context.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Matched WPF renders with the old policy diagnostically restored in the same binary; no claim of physical interaction or unchanged historical binary A/B",GlobalAtlasLoaded=false,NetworkBytes=0,Results=rows,process.PrivateMemorySize64,process.WorkingSet64,Resources=pilot.ResourceSnapshot()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
