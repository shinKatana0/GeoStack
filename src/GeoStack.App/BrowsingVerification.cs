// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    private async Task SettleBrowsing()
    {
        for(var i=0;i<300;i++) { await Task.Delay(100);if(!Atlas.IsAnimating && !Atlas.ActiveTiles.IsLoading) { await Task.Delay(200);UpdateIndicators();return; } }
        throw new InvalidOperationException("Browsing renderer did not settle");
    }
    internal async Task VerifyBrowsing(string projectRoot,bool restart)
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45)); await AtlasReady;
        var work=Path.Combine(projectRoot,".local/browsing-ux");var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        if(!restart)
        {
            foreach(var path in Directory.GetFiles(Path.Combine(projectRoot,"dist/GeoStack-geographic-data-0.4"),"*.gsgeo")) App.GeographicData.Import(await Task.Run(()=>GeographicPackage.Inspect(path)));
            var manager=new MapManagerWindow(this,Atlas);manager.Show();
            foreach(var path in Directory.GetFiles(Path.Combine(projectRoot,"offline-maps"),"*-city.mbtiles")) await manager.ImportPackageAsync(path);
            manager.Close();await ScanFolder(Path.Combine(work,"media"));await RebuildPlaces();
        }
        if(ViewModel.Items.Count!=9 || ViewModel.VisibleItems.Count!=9 || ViewModel.VisibleItems.Any(m=>m.Thumbnail==null)) throw new InvalidOperationException("Default library did not populate real thumbnails");
        var sourceHashes=await Task.WhenAll(ViewModel.Items.Select(m=>GeographicCopy.Hash(m.Path,default)));
        var photos=ViewModel.Items.ToDictionary(m=>m.Name);
        ApplySettings("en-US","Light");FeatureVerification.Snapshot(this,Path.Combine(evidence,"library-default.png"));
        var observations=new List<object>();
        foreach(var language in new[]{"en-US","ru-RU"}) foreach(var theme in new[]{"Light","Dark"})
        {
            ApplySettings(language,theme);await ShowSelection(ViewModel.Items,Strings.Get("AllMedia"),null);
            var warsaw=photos["Warsaw.jpg"];await NavigateToMedia(warsaw);await SettleBrowsing();
            if(Atlas.TileZoom!=15 || Atlas.ActiveTiles.DrawnTileCount==0 || Atlas.SelectedMediaId!=warsaw.Path) throw new InvalidOperationException("Warsaw navigation/selection failed");
            var japan=outside.Single(g=>g.Items.Any(i=>i.Name=="Kyoto.jpg"));
            if(japan.Items.Count!=4 || japan.Items.Any(i=>i.Name=="Warsaw.jpg")) throw new InvalidOperationException("Japan offscreen count/group incorrect");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"warsaw-{language}-{theme}.png"));
            await NavigateDestination(japan);await SettleBrowsing();
            if(japan.Items.Any(m=>!Atlas.Camera.Contains(Geo.Project(m.Latitude!.Value,m.Longitude!.Value).X,Geo.Project(m.Latitude!.Value,m.Longitude!.Value).Y))) throw new InvalidOperationException("Japan overview omitted selected locations");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"japan-overview-{language}-{theme}.png"));
            await ShowSelection(ViewModel.Items,Strings.Get("AllMedia"),photos["Kyoto.jpg"].Path);
            await NavigateToMedia(photos["Kyoto.jpg"]);await SettleBrowsing();
            var coordinate=Geo.Project(photos["Kyoto.jpg"].Latitude!.Value,photos["Kyoto.jpg"].Longitude!.Value);var screen=Atlas.Camera.Screen(coordinate.X,coordinate.Y);
            if(Math.Abs(screen.X-Atlas.ActualWidth/2)>2 || Atlas.SelectedMediaId!=photos["Kyoto.jpg"].Path || MediaGrid.SelectedItem is not MediaViewModel { Name:"Kyoto.jpg" }) throw new InvalidOperationException("Exact Kyoto media navigation failed");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"kyoto-{language}-{theme}.png"));
            if(language=="en-US" && theme=="Light") {
                OpenPreview_Click(this,new());await Task.Delay(800);
                var preview=Application.Current.Windows.OfType<Window>().Single(w=>w!=this && w.Title==Strings.Get("OpenPreview"));
                if(!Descendants(preview).OfType<System.Windows.Controls.Image>().Any(i=>i.Source!=null))throw new InvalidOperationException("Large preview unavailable");
                FeatureVerification.Snapshot(preview,Path.Combine(evidence,"large-preview.png"));preview.Close();
            }
            var beforeNoGps=Atlas.Camera;await NavigateToMedia(photos["Without-GPS.jpg"]);await SettleBrowsing();if(Atlas.Camera!=beforeNoGps)throw new InvalidOperationException("Missing GPS changed camera");
            await NavigateToMedia(photos["Tokyo.jpg"]);await SettleBrowsing();if(Geo.Cluster(ViewModel.Items,Atlas.Camera.World).Single(c=>c.Items.Any(m=>m.Name=="Tokyo.jpg")).Items[0].Name=="Tokyo.jpg")throw new InvalidOperationException("Non-first selection fixture is invalid");if(Atlas.SelectedMediaId!=photos["Tokyo.jpg"].Path)throw new InvalidOperationException("Exact non-first stack member lost");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"selected-stack-{language}-{theme}.png"));
            var beforeBack=Atlas.Camera;if(!await GoBackLocation())throw new InvalidOperationException("Navigation history empty");await SettleBrowsing();if(Atlas.Camera==beforeBack)throw new InvalidOperationException("Back did not restore previous camera");
            await NavigateToMedia(photos["Zurich.jpg"]);await SettleBrowsing();
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"zurich-{language}-{theme}.png"));
            Atlas.ZoomBy(1d/8);await SettleBrowsing();if(Atlas.AtlasPilot is {Enabled:true} context ? context.LastDrawnShapes==0 && Atlas.ActiveTiles.DrawnTileCount==0 : Atlas.ActiveTiles.RenderedMapIds.All(id=>!Atlas.Tiles.Catalog.Packages.Any(p=>p.Id==id && p.MaxZoom==12))) throw new InvalidOperationException("Wider low-detail context did not render");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,$"regional-context-{language}-{theme}.png"));
            var positions=OffscreenIndex.Place(outside,Atlas.Camera);foreach(var p in positions)if(p.X+148>Atlas.ActualWidth || p.Y+32>Atlas.ActualHeight)throw new InvalidOperationException("Indicator clipped");
            var originalWidth=Width;var originalHeight=Height;var originalMinWidth=MinWidth;var originalMinHeight=MinHeight;
            foreach(var scale in new[]{1.5,2d}) {
                MinWidth=0;MinHeight=0;Width=1366/scale;Height=720/scale;await SettleBrowsing();
                FeatureVerification.Snapshot(this,Path.Combine(evidence,$"compact-{scale}-{language}-{theme}.png"),scale);
                if(MediaGrid.ActualHeight<28 || !OpenPreviewButton.IsVisible)throw new InvalidOperationException("Compact library actions clipped");
                foreach(var chip in EdgeIndicators.Children.OfType<Button>()) if(Canvas.GetLeft(chip)+chip.ActualWidth>Atlas.ActualWidth+.5 || Canvas.GetTop(chip)+chip.ActualHeight>Atlas.ActualHeight+.5)throw new InvalidOperationException("Compact indicator outside map");
            }
            Width=originalWidth;Height=originalHeight;MinWidth=originalMinWidth;MinHeight=originalMinHeight;await SettleBrowsing();
            observations.Add(new { language,theme,DefaultThumbnails=ViewModel.VisibleItems.Count,JapanOffscreenCount=japan.Items.Count,KyotoExactSelection=true,MissingGpsPreservesCamera=true,NonFirstStackMedia=true,Back=true,ContextTiles=true,MarkerThumbnails=Atlas.LoadedMarkerThumbnails });
        }
        foreach(var name in new[]{"Kanazawa.jpg","Shanghai-video.mov"}) {
            await NavigateToMedia(photos[name]);await SettleBrowsing();
            if(Atlas.SelectedMediaId!=photos[name].Path || Atlas.ActiveTiles.DrawnTileCount==0)throw new InvalidOperationException("Additional city/video navigation failed");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,name+"-navigation.png"));
        }
        var benchmarkItems=Enumerable.Range(0,10000).Select(i=>new MediaItem("synthetic/"+i,30+i%100*.01,130+i/100*.01,"Photo")).ToArray();
        var watch=Stopwatch.StartNew();var index=new OffscreenIndex(benchmarkItems,(_,_)=>new(null,null,null,null));watch.Stop();var buildMs=watch.Elapsed.TotalMilliseconds;
        var timings=new List<double>();for(var i=0;i<50;i++){watch.Restart();var groups=index.Outside(new(.55+i*.000001,.35,8388608,950,700));OffscreenIndex.Place(groups,new(.55,.35,8388608,950,700));watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);}
        timings.Sort();
        var copySources=new[]{photos["Kyoto.jpg"],photos["Zurich.jpg"]};var destination=Path.Combine(App.DataDirectory,"copy-"+Guid.NewGuid().ToString("N"));
        var plan=await Task.Run(()=>GeographicCopy.Plan(copySources,App.LocationResolver,destination,"en-US","CountryCity",true,default));
        var result=await Task.Run(()=>GeographicCopy.Execute(plan,destination,Path.Combine(App.DataDirectory,"CopyJournal"),null,default));
        if(!plan.Select(p=>p.RelativeDirectory).SequenceEqual(new[]{Path.Combine("Japan","Kyoto"),Path.Combine("Switzerland","Zurich")}))throw new InvalidOperationException("Geographic folder regression");
        foreach(var row in result)if(row.Status!=CopyState.Completed || await GeographicCopy.Hash(row.Source,default)!=await GeographicCopy.Hash(row.Destination,default))throw new InvalidOperationException("Copy integrity regression");
        for(var i=0;i<ViewModel.Items.Count;i++)if(sourceHashes[i]!=await GeographicCopy.Hash(ViewModel.Items[i].Path,default))throw new InvalidOperationException("Source changed");
        ApplySettings("en-US","Light");await ShowSelection(ViewModel.Items,Strings.Get("AllMedia"),photos["Warsaw.jpg"].Path);await NavigateToMedia(photos["Warsaw.jpg"]);await SettleBrowsing();
        File.WriteAllText(Path.Combine(App.DataDirectory,restart?"browsing-restart.json":"browsing.json"),JsonSerializer.Serialize(new { Status="PASS",Restart=restart,Scope="Packaged command paths and actual WPF renders; physical double-click/hover/keyboard and disconnected-network NOT_TESTED", Observations=observations,InstalledMaps=Atlas.Tiles.Catalog.Packages.Count,SourceHashesPreserved=9,Copied=result.Count,DestinationHashesVerified=true,IndicatorBenchmark=new { Items=10000,Iterations=50,BuildMilliseconds=buildMs,MedianMilliseconds=timings[25],P95Milliseconds=timings[47],MaximumMilliseconds=timings[^1],Scope="Projected index and indicator computation, not whole-application 10k scan/thumbnail performance" } },new JsonSerializerOptions { WriteIndented=true }));
    }
}
