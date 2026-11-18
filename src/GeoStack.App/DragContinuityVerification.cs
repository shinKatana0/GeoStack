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
    internal async Task VerifyDragContinuity(string root)
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45)); await AtlasReady;
        var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        var original=App.Settings;var settingsBytes=File.Exists(App.SettingsPath)?File.ReadAllBytes(App.SettingsPath):null;
        if(settingsBytes==null && (original.Theme!="System" || original.Language!=Preferences.DetectLanguage(App.StartupUICulture)))throw new InvalidOperationException("System defaults changed");
        var approved=ViewModel.Items.ToArray();if(approved.Length!=9 || approved.Any(m=>!m.Path.StartsWith(Path.GetFullPath(Path.Combine(root,".local/browsing-ux/media"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Approved isolated fixtures required");
        var hashes=await Task.WhenAll(approved.Select(m=>GeographicCopy.Hash(m.Path,default)));
        var reports=new List<object>();var watch=Stopwatch.StartNew();
        foreach(var city in new[]{(Id:"warsaw",Lat:52.2297,Lon:21.0122,Theme:"Light",Language:"ru-RU"),(Id:"kyoto",Lat:34.9858,Lon:135.7588,Theme:"Dark",Language:"en-US")})
        {
            App.Settings=original with {Language=city.Language,Theme=city.Theme};Strings.SetLanguage(city.Language);App.ApplyTheme();Localize();
            var map=Atlas.Tiles.Catalog.Packages.Single(m=>Path.GetFileName(m.Path)==city.Id+"-city.mbtiles");Atlas.Tiles.Catalog.Preferred=map.Id;Atlas.Tiles.Clear();Atlas.SetProbeCamera(city.Lat,city.Lon,15);await SettleBrowsing();
            var first=Atlas.Tiles.Presentation.Front ?? throw new InvalidOperationException("No initial raster");var startCamera=Atlas.Camera;var initialPositions=Atlas.MediaPositionsForVerification();
            var initialSourceChanges=Atlas.Tiles.Presentation.SourceChanges;var swaps=Atlas.Tiles.Presentation.Swaps;var samples=new List<object>();var moves=0;var retained=0;var blank=0;var maxAlignmentError=0d;
            Atlas.SetDragForVerification(true);
            async Task Move(Vector delta,string phase,bool requireSame=false)
            {
                var old=Atlas.Tiles.Presentation.Front;var before=Atlas.Camera;
                Atlas.PanPixels(delta);await Task.Delay(18);var after=Atlas.Camera;var front=Atlas.Tiles.Presentation.Front;moves++;
                if(front==null || Atlas.ActiveTiles.DrawnTileCount==0)blank++;
                if(ReferenceEquals(old,front))retained++;
                if(requireSame && !ReferenceEquals(first,front))throw new InvalidOperationException("Small drag replaced the front buffer");
                if(old!=null && ReferenceEquals(old,front))
                {
                    var a=old.Screen(before.World,before.Width,before.Height,new(before.X,before.Y));var b=old.Screen(after.World,after.Width,after.Height,new(after.X,after.Y));
                    maxAlignmentError=Math.Max(maxAlignmentError,Math.Abs(b.X-a.X-delta.X)+Math.Abs(b.Y-a.Y-delta.Y));
                }
                samples.Add(new{Phase=phase,Move=moves,after.X,after.Y,Key=front?.Plan.Key,Source=front?.Plan.Package.Name,VisibleTiles=Atlas.ActiveTiles.DrawnTileCount,Opacity=Atlas.Tiles.Presentation.FrontOpacity(after.World,after.Width,after.Height,new(after.X,after.Y))});
            }
            FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-00-start.png"));
            for(var i=0;i<12;i++)await Move(new(-4,0),"small",true);
            FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-01-small-drag.png"));
            for(var i=0;i<125;i++)
            {
                await Move(new(-24,0),"crossing-east");
                if(i is 44 or 54 or 64 or 94)FeatureVerification.Snapshot(this,Path.Combine(evidence,$"{city.Id}-edge-{i:D2}.png"));
            }
            for(var i=0;i<125;i++)await Move(new(24,0),"reverse-west");
            for(var i=0;i<35;i++)await Move(new(0,-16),"vertical");
            for(var i=0;i<35;i++)await Move(new(0,16),"vertical-reverse");
            for(var i=0;i<60;i++)await Move(new(i%2==0?3:-3,0),"rapid-small");
            Atlas.SetDragForVerification(false);await SettleBrowsing();
            var beforeJitter=Atlas.Tiles.Presentation.SourceChanges;
            var x=(map.East+180)/360-Atlas.Camera.Width/Atlas.Camera.World/2;
            Atlas.SetDragForVerification(true);
            for(var i=0;i<50;i++)
            {
                var p=Geo.Unproject(x+(i%2==0?2:-2)/Atlas.Camera.World,Geo.Project(city.Lat,city.Lon).Y);Atlas.SetProbeCamera(p.Latitude,p.Longitude,15);await Task.Delay(18);
            }
            var jitterChanges=Atlas.Tiles.Presentation.SourceChanges-beforeJitter;
            if(jitterChanges>1)throw new InvalidOperationException("Coverage mode flapped during two-pixel boundary jitter");
            for(var i=0;i<25;i++)Atlas.ZoomBy(1.4);
            await Task.Delay(200);Atlas.PanPixels(new(-1500,0));await Task.Delay(300);var zoomBefore=Atlas.Camera.World;Atlas.ZoomBy(1.4);await Task.Delay(300);
            if(Atlas.Camera.World<zoomBefore-.001)throw new InvalidOperationException("Zoom In reversed after entering lower-resolution coverage");
            await Task.Delay(400);FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-high-zoom-edge.png"));
            Atlas.SetDragForVerification(false);await SettleBrowsing();
            Atlas.SetDragForVerification(true);for(var i=0;i<8;i++)await Move(new(3,0),"pause-and-resume");Atlas.SetDragForVerification(false);await SettleBrowsing();
            Atlas.SetProbeCamera(city.Lat,city.Lon,15);await SettleBrowsing();
            var finalPositions=Atlas.MediaPositionsForVerification();if(initialPositions.Any(p=>!finalPositions.TryGetValue(p.Key,out var value)||(value-p.Value).Length>.01))throw new InvalidOperationException("Overlay coordinates drifted");
            if(blank!=0 || maxAlignmentError>.001 || retained<12 || Atlas.Tiles.Presentation.Swaps-swaps>moves/2)throw new InvalidOperationException($"Continuity failed: blank={blank}, alignment={maxAlignmentError}, retained={retained}");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-returned.png"));
            reports.Add(new{City=city.Id,city.Theme,city.Language,Moves=moves,RetainedFrames=retained,BlankFrames=blank,MaximumTransformError=maxAlignmentError,FrameSwaps=Atlas.Tiles.Presentation.Swaps-swaps,SourceChanges=Atlas.Tiles.Presentation.SourceChanges-initialSourceChanges,BoundaryJitterChanges=jitterChanges,MaximumTileZoom=Atlas.TileZoom,OverlayAlignment=true,Samples=samples});
        }
        for(var i=0;i<approved.Length;i++)if(hashes[i]!=await GeographicCopy.Hash(approved[i].Path,default))throw new InvalidOperationException("Source fixture changed");
        App.Settings=original;Strings.SetLanguage(original.Language);App.ApplyTheme();Localize();
        if(settingsBytes==null?File.Exists(App.SettingsPath):!File.ReadAllBytes(App.SettingsPath).SequenceEqual(settingsBytes))throw new InvalidOperationException("Preferences overwritten");
        File.WriteAllText(Path.Combine(App.DataDirectory,"drag-continuity.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Packaged WPF render/drag-state command paths; physical mouse NOT_TESTED",ElapsedSeconds=watch.Elapsed.TotalSeconds,SourceHashesPreserved=approved.Length,SystemDefaultsPreserved=true,Reports=reports},new JsonSerializerOptions{WriteIndented=true}));
    }
}
