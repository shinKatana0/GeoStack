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
    internal async Task VerifyRegionalRelease()
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated profile required");
        await AtlasReady;await libraryInitialized.Task;Width=1360;Height=880;await SettleBrowsing();
        var pilot=Atlas.AtlasPilot??throw new InvalidOperationException("Hybrid atlas missing");
        if(pilot.GlobalAtlas!=null || App.ExperimentalGlobalAtlasEnabled)throw new InvalidOperationException("Experimental world atlas must remain disconnected");
        Atlas.Tiles.Catalog.Preferred=Atlas.Tiles.Catalog.Packages.First(p=>p.Id.StartsWith("region-",StringComparison.Ordinal) && p.MaxZoom==10).Id;
        var phase="regional-navigation";var results=new List<object>();object inputReport;
        void Require(bool condition,string message)
        {
            if(condition)return;
            FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,"evidence","regional-failure.png"));
            File.WriteAllText(Path.Combine(App.DataDirectory,"regional-failure.json"),JsonSerializer.Serialize(new{Phase=phase,Message=message,Atlas.Camera,SourcePreferred=Atlas.Tiles.Catalog.Preferred,Preferred=Atlas.ActiveTiles.Catalog.Preferred,Atlas.ActiveTiles.VisibleNativeZoom,Atlas.ActiveTiles.VisibleFrameKey,Atlas.ActiveTiles.RenderedMapIds,pilot.LastRasterVisible,Resources=pilot.ResourceSnapshot()},new JsonSerializerOptions{WriteIndented=true}));
            throw new InvalidOperationException(message);
        }
        using(var probe=new UiResponsivenessProbe(this,()=>phase,"native-only"))
        {
            foreach(var theme in new[]{"Light","Dark"})foreach(var pose in new[]{("Prague",50.087,14.421,15),("Context",50.12,14.50,9),("Boundary",50.3,14.8,10),("Outside",51d,15d,8),("Warsaw",52.23,21.01,15),("Zurich",47.38,8.54,14)})
            {
                phase=theme+"/"+pose.Item1;ApplySettings("en-US",theme);Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);await SettleBrowsing();
                Atlas.SetDragForVerification(true);for(var i=0;i<64;i++){Atlas.PanPixels(new Vector(i<32?-10:10,i%2==0?2:-2));await Task.Delay(16);}Atlas.SetDragForVerification(false);await SettleBrowsing();
                bool Ready()=>pose.Item1 switch{
                    "Context"=>pilot.LastRasterVisible && Atlas.ActiveTiles.VisibleNativeZoom>=8 && Atlas.ActiveTiles.VisibleNativeZoom<=10 && Atlas.ActiveTiles.Catalog.Packages.Any(p=>Atlas.ActiveTiles.RenderedMapIds.Contains(p.Id) && Path.GetFileName(p.Path).StartsWith("region-",StringComparison.Ordinal) && p.MaxZoom==10),
                    "Prague"=>pilot.LastRasterVisible && Atlas.ActiveTiles.VisibleNativeZoom==15 && Atlas.ActiveTiles.Catalog.Packages.Any(p=>Atlas.ActiveTiles.RenderedMapIds.Contains(p.Id) && p.MaxZoom==15),
                    "Outside"=>!pilot.LastRasterVisible,_=>true};
                var publication=Stopwatch.StartNew();for(var i=0;i<120 && !Ready();i++)await Task.Delay(25);
                Require(Ready(),"Expected native regional/detail/fallback surface was not published");
                results.Add(new{Phase=phase,Atlas.Camera,pilot.LastRasterVisible,Atlas.ActiveTiles.DrawnTileCount,Atlas.ActiveTiles.VisibleNativeZoom,PublicationWaitMilliseconds=publication.Elapsed.TotalMilliseconds,Resources=pilot.ResourceSnapshot()});
            }
            inputReport=probe.Report();
        }
        var captures=new List<string>();
        foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})foreach(var pose in new[]{("Detail",50.087,14.421,15),("Context",50.12,14.50,9),("Outside",51d,15d,8)})
        {
            ApplySettings(language,theme);Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);await SettleBrowsing();
            var name=pose.Item1+"-"+language+"-"+theme+".png";FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,"evidence",name));captures.Add(name);
        }
        var items=ViewModel.Items.ToArray();var packages=Atlas.Tiles.Catalog.Packages.ToArray();
        var plan=await Task.Run(()=>RegionalContextPlanner.Extend(LibraryMapPlanner.Build(items,packages,App.LocationResolver.Resolve,default),packages,default,MapProbe.CanDecode));
        if(plan.Areas.Any(a=>!a.Installed))throw new InvalidOperationException("Installed context or detail was proposed again");
        using var process=Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(Path.Combine(App.DataDirectory,"regional-release.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Actual WPF regional/detail/fallback rendering and programmatic pans; physical gestures NOT_TESTED",Input=inputReport,Results=results,Captures=captures,GlobalAtlasLoaded=false,InstalledPlan=plan,process.PrivateMemorySize64,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false)},new JsonSerializerOptions{WriteIndented=true}));
    }
}
