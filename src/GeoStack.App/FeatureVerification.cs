// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.Core;

namespace GeoStack.App;

/// <summary>Explicit packaged integration checks; synthetic inputs only, no personal-media discovery.</summary>
internal static class FeatureVerification
{
    public static async Task Run(MainWindow window, string fixture)
    {
        var output = App.DataDirectory;
        var package = MapPackage.Inspect(Path.Combine(fixture, "raster.mbtiles"));
        using (var probe = new MapProbe(package.Path)) probe.Verify();
        var catalog = new MapCatalog(output); catalog.Packages.Add(package); catalog.Preferred = package.Id; catalog.Save();
        var restarted = new MapCatalog(output);
        if (restarted.Packages.Count != 1 || restarted.Preferred != package.Id) throw new InvalidOperationException("Map catalog restart failed");
        var sourceRoot = Path.Combine(output, "synthetic-source"); Directory.CreateDirectory(sourceRoot);
        var sourceA = Path.Combine(sourceRoot, "sample-a.png"); var sourceB = Path.Combine(sourceRoot, "sample-b.mov");
        File.Copy(Path.Combine(fixture, "tile.png"), sourceA, false); File.Copy(Path.Combine(fixture, "clip.mov"), sourceB, false);
        MediaItem[] selection = [new(sourceA,35.68,139.76,"Photo"),new(sourceB,47.37,8.54,"Video")];
        var hashes = await Task.WhenAll(selection.Select(i => GeographicCopy.Hash(i.Path, default)));
        var root = Path.Combine(output,"synthetic-destination");
        var geocoder = new ReverseGeocoder(Path.Combine(AppContext.BaseDirectory,"geography/countries.geojson"));
        var plan = await Task.Run(()=>GeographicCopy.Plan(selection,geocoder,root,"en-US","CountryCity",false,default));
        if (!plan.Select(p=>p.RelativeDirectory).SequenceEqual(new[]{"Japan","Switzerland"})) throw new InvalidOperationException("Per-file country fallback failed");
        var results = await Task.Run(()=>GeographicCopy.Execute(plan,root,Path.Combine(output,"CopyJournal"),null,default));
        if (results.Any(r=>r.Status!=CopyState.Completed)) throw new InvalidOperationException("Copy failed");
        for (var i=0;i<selection.Length;i++) if (hashes[i]!=await GeographicCopy.Hash(selection[i].Path,default) || hashes[i]!=await GeographicCopy.Hash(results[i].Destination,default)) throw new InvalidOperationException("Copy checksum mismatch");
        await window.VerifyFeatureRendering(selection, package, output);
        File.WriteAllText(Path.Combine(output,"feature-verification.json"),JsonSerializer.Serialize(new { Status="PASS", Scope="Automated packaged integration; physical dialog gestures not verified", RasterDecoded=true, RasterDrawn=window.RenderedTileCount>0, CatalogRestart=true, Copied=results.Count, SourceChecksumsPreserved=true, DestinationChecksumsVerified=true, CountryFallbacks=plan.Select(p=>p.RelativeDirectory), CityBoundaries="NOT_BUNDLED", Journal=GeographicCopy.ReadJournal(Path.Combine(output,"CopyJournal")).Count, Snapshots="Rendered WPF controls; inspect images before claiming visual acceptance" },new JsonSerializerOptions { WriteIndented=true }));
    }
    internal static void Snapshot(FrameworkElement element, string path, double scale=1)
    {
        element.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*scale),(int)Math.Ceiling(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}

public partial class MainWindow
{
    internal int RenderedTileCount => Atlas.ActiveTiles.DrawnTileCount;
    internal async Task VerifyFeatureRendering(MediaItem[] selection, MapPackage package, string directory)
    {
        Atlas.Tiles.Catalog.Packages.Clear(); Atlas.Tiles.Catalog.Packages.Add(package); Atlas.Tiles.Catalog.Preferred=package.Id; Atlas.Tiles.Clear(); Atlas.Items=selection;
        var original=App.Settings;
        foreach (var language in new[]{"en-US","ru-RU"}) foreach (var theme in new[]{"Light","Dark"})
        {
            App.Settings=original with { Language=language,Theme=theme }; Strings.SetLanguage(language); App.ApplyTheme(); Localize(); Atlas.InvalidateVisual();
            ViewModel.RefreshLanguage(); ViewModel.RefreshTitle(sidebarPlace == null ? Strings.Get(sidebarKey) : Atlas.Describe(sidebarPlace.Latitude, sidebarPlace.Longitude));
            foreach (var media in ViewModel.VisibleItems) media.RefreshLanguage();
            await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            for (var retry=0;retry<30 && RenderedTileCount==0;retry++) await Task.Delay(100);
            FeatureVerification.Snapshot(this,Path.Combine(directory,$"main-{language}-{theme}.png"));
            // Explicitly open real tooltip popups for rendered verification, independent of mouse hover.
            foreach (var button in new[]{ZoomInButton,ZoomOutButton,ResetButton,FitButton})
            {
                if (button.ToolTip is not string text || string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Empty map tooltip");
                var tooltip=new ToolTip { Content=text,PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Left,IsOpen=true };
                await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                FeatureVerification.Snapshot(tooltip,Path.Combine(directory,$"tooltip-{button.Name}-{language}-{theme}.png")); tooltip.IsOpen=false;
            }
        }
        if (RenderedTileCount==0) throw new InvalidOperationException("No raster tiles drawn");
        var copy=new CopyWindow(this,selection);copy.Show();await copy.PrepareVerification(Path.Combine(directory,"preview-destination"));await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);FeatureVerification.Snapshot(copy,Path.Combine(directory,"copy-dialog.png"));copy.Close();
        App.Settings=original;Strings.SetLanguage(original.Language);App.ApplyTheme();Localize();
    }
}
