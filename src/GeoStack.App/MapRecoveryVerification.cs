// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    private async Task WaitForMap(string id)
    {
        for (var i = 0; i < 150; i++)
        {
            Atlas.InvalidateVisual(); await Task.Delay(100);
            if (!Atlas.IsAnimating && !Atlas.ActiveTiles.IsLoading && Atlas.ActiveTiles.RenderedMapIds.Contains(id) && Atlas.ActiveTiles.DrawnTileCount > 0) return;
        }
        throw new InvalidOperationException("No geographic raster tiles displayed for " + id);
    }
    internal async Task VerifyMapRecovery(string directory, bool restart)
    {
        var output = App.DataDirectory; var evidence = Path.Combine(output, "evidence"); Directory.CreateDirectory(evidence);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "MANIFEST.json")));
        var packages = manifest.RootElement.GetProperty("Packages").EnumerateArray().ToArray();
        if (packages.Length != 6) throw new InvalidOperationException("Six city maps required");
        var manager = new MapManagerWindow(this, Atlas); manager.Show();
        if (restart && manager.InstalledCount != 6) throw new InvalidOperationException("Persisted map references missing");
        var report = new List<object>();
        if (!restart)
        {
            var media = Path.GetFullPath(Path.Combine(directory, "../acceptance-local/media"));
            await ScanFolder(media);
            var boundaryDirectory = Path.GetFullPath(Path.Combine(directory, "../dist/GeoStack-geographic-data-0.4"));
            foreach (var path in Directory.GetFiles(boundaryDirectory, "*.gsgeo")) App.GeographicData.Import(await Task.Run(() => GeographicPackage.Inspect(path)));
        }
        foreach (var entry in packages)
        {
            var slug = entry.GetProperty("Id").GetString()!;
            var path = Path.Combine(directory, slug + "-city.mbtiles");
            var sourceHash = await GeographicCopy.Hash(path, default);
            if (!sourceHash.Equals(entry.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Regional map checksum mismatch");
            MapPackage map;
            if (restart) { map = Atlas.Tiles.Catalog.Packages.Single(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); manager.SelectMap(map.Id); manager.ActivateSelected(); }
            else map = await manager.ImportPackageAsync(path); // Same validation/decode/catalog/activation method as the Import button.
            await Task.Delay(700); await WaitForMap(map.Id);
            if (manager.SelectedPackage?.Id != map.Id) throw new InvalidOperationException("Map selection lost during import/activation");
            FeatureVerification.Snapshot(this, Path.Combine(evidence, slug + (restart ? "-restart.png" : "-city.png")));
            var before = Atlas.MediaPositionsForVerification();
            manager.ToggleSelected(); await Task.Delay(200);
            if (Atlas.ActiveTiles.RenderedMapIds.Contains(map.Id)) throw new InvalidOperationException("Disabled map still shown");
            manager.ActivateSelected(false); await WaitForMap(map.Id);
            manager.ActivateWorld(false); await Task.Delay(200);
            if (Atlas.ActiveTiles.DrawnTileCount != 0) throw new InvalidOperationException("Built-in fallback did not replace raster");
            manager.SelectMap(map.Id); manager.ActivateSelected(false); await WaitForMap(map.Id);
            var after = Atlas.MediaPositionsForVerification();
            if (before.Count == 0 || before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out var value) || (value - p.Value).Length > .01)) throw new InvalidOperationException("Media alignment changed across map switches");
            Atlas.ZoomBy(Math.Pow(2,15-Atlas.TileZoom)); await Task.Delay(700); await WaitForMap(map.Id);
            if (Atlas.TileZoom != 15) throw new InvalidOperationException("Neighborhood zoom 15 is unreachable");
            Atlas.PanPixels(new Vector(-290, 80)); await WaitForMap(map.Id); await Task.Delay(500);
            FeatureVerification.Snapshot(this, Path.Combine(evidence, slug + (restart ? "-detail-restart.png" : "-detail.png")));
            if (!restart)
            {
                manager.SelectMap(map.Id); manager.RemoveSelected();
                if (!File.Exists(path) || sourceHash != await GeographicCopy.Hash(path, default)) throw new InvalidOperationException("External map changed during reference removal");
                map = await manager.ImportPackageAsync(path); await Task.Delay(600); await WaitForMap(map.Id);
            }
            report.Add(new { City = slug, map.Name, map.Path, map.Size, map.MinZoom, map.MaxZoom, Rendered = true, NeighborhoodZoom15 = true, ProgrammaticPanAcrossTiles = true, MediaPositionsPreserved = before.Count, SourceHash = sourceHash, ImportPath = restart ? "Persisted catalog, actual Activate command" : "Actual Map Manager import/validation/activation method; physical file picker NOT_TESTED" });
        }
        var zurich = Atlas.Tiles.Catalog.Packages.Single(p => Path.GetFileName(p.Path) == "zurich-city.mbtiles");
        manager.SelectMap(zurich.Id); manager.ActivateSelected(); await Task.Delay(700); await WaitForMap(zurich.Id); manager.Close();
        if (!restart)
        {
            await VerifyWindowLayouts(evidence);
            var sources = ViewModel.Items.Where(m => m.Name == "Kyoto.jpg" || m.Name == "Zurich.jpg").ToArray();
            if (sources.Length != 2) throw new InvalidOperationException("Controlled copy fixtures missing");
            var hashes = await Task.WhenAll(sources.Select(m => GeographicCopy.Hash(m.Path, default)));
            var destination = Path.Combine(output, "copy-regression-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(destination);
            var plan = await Task.Run(() => GeographicCopy.Plan(sources, App.LocationResolver, destination, "en-US", "CountryCity", true, default));
            if (!plan.Select(p => p.RelativeDirectory).Order().SequenceEqual(new[] { Path.Combine("Japan", "Kyoto"), Path.Combine("Switzerland", "Zurich") }.Order())) throw new InvalidOperationException("Mixed-city folder regression");
            var result = await Task.Run(() => GeographicCopy.Execute(plan, destination, Path.Combine(output, "CopyJournal"), null, default));
            for (var i = 0; i < sources.Length; i++) if (result[i].Status != CopyState.Completed || hashes[i] != await GeographicCopy.Hash(result[i].Destination, default) || hashes[i] != await GeographicCopy.Hash(sources[i].Path, default)) throw new InvalidOperationException("Copy checksum regression");
        }
        File.WriteAllText(Path.Combine(output, restart ? "map-recovery-restart.json" : "map-recovery.json"), JsonSerializer.Serialize(new { Status = "PASS", Restart = restart, Scope = "Packaged real raster map rendering and Map Manager command paths; physical input/DPI NOT_TESTED", CityMaps = report, CopyRegression = restart ? "Not repeated" : "Two real-city synthetic sources copied; destination/source SHA-256 verified", InstalledMaps = manager.InstalledCount, NetworkRuntime = "Local SQLite and PNG decoding only; no remote map provider", SourceVersion = "Protomaps 4.14.3, OSM 2026-03-31" }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task VerifyWindowLayouts(string output)
    {
        var original = App.Settings;
        foreach (var language in new[] { "en-US", "ru-RU" }) foreach (var theme in new[] { "Light", "Dark" })
        {
            ApplySettings(language, theme);
            var windows = new (string Purpose, Window Window)[] {
                ("Settings", new SettingsWindow(this)), ("MapManager", new MapManagerWindow(this, Atlas)),
                ("GeographicData", GeographicDataWindow.Create(this)), ("CopyToLocation", new CopyWindow(this, ViewModel.Items.ToArray()))
            };
            foreach (var item in windows)
            {
                item.Window.Show(); var size = WindowLayout.Minimum(item.Purpose);
                item.Window.Width = size.Width; item.Window.Height = size.Height;
                if (item.Window is CopyWindow copy) await copy.PrepareVerification(Path.Combine(App.DataDirectory, "layout-copy-preview"));
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var selectedCells = Descendants(item.Window).OfType<DataGridCell>().Where(c => c.IsSelected).ToArray();
                var selectedColor = ((System.Windows.Media.SolidColorBrush)App.Current.Resources["Line"]).Color;
                if (selectedCells.Any(c => c.Background is not System.Windows.Media.SolidColorBrush brush || brush.Color != selectedColor)) throw new InvalidOperationException("Selected table row theme contrast regression");
                if (item.Window.MinHeight <= 200 || item.Window.MinWidth < 400) throw new InvalidOperationException("Missing useful minimum window size");
                FeatureVerification.Snapshot(item.Window, Path.Combine(output, item.Purpose + "-minimum-" + language + "-" + theme + ".png"));
                item.Window.Close();
            }
            var savedWidth = Width; var savedHeight = Height; var minimum = WindowLayout.Minimum("Main");
            foreach (var viewport in new[] { (Width:910d, Height:480d, Scale:1.5, Name:"1366-150"), (Width:683d, Height:360d, Scale:2d, Name:"1366-200") })
            {
                MinWidth = Math.Min(minimum.Width, viewport.Width); MinHeight = viewport.Height; Width = viewport.Width; Height = viewport.Height;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                foreach (var button in new[] { ZoomInButton, ZoomOutButton, ResetButton, FitButton })
                { var position = button.TranslatePoint(new Point(), Atlas); if (position.X < 0 || position.Y < 0 || position.X + button.ActualWidth > Atlas.ActualWidth || position.Y + button.ActualHeight > Atlas.ActualHeight) throw new InvalidOperationException("Map navigation clipped in compact layout"); }
                if (MediaGrid.ActualHeight < 28 || !CopyButton.IsVisible || !CopyStackButton.IsVisible) throw new InvalidOperationException("Compact media selection/copy controls unusable");
                var preferred = Atlas.Tiles.Catalog.Preferred; if (preferred != "world") await WaitForMap(preferred);
                FeatureVerification.Snapshot(this, Path.Combine(output, "main-" + viewport.Name + "-effective-" + language + "-" + theme + ".png"), viewport.Scale);
            }
            MinWidth = minimum.Width; MinHeight = minimum.Height; Width = savedWidth; Height = savedHeight;
        }
        ApplySettings(original.Language, original.Theme);
    }
}
