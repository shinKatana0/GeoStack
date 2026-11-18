// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using GeoStack.Core;

namespace GeoStack.App;

internal sealed record GeographicExpectedPoint(string Id, double Latitude, double Longitude, string? CountryId, string? RegionId, string? CityId, string? DistrictId);

/// <summary>Explicit controlled integration mode. This is not physical mouse/confirmation acceptance.</summary>
internal static class GeographicVerification
{
    public static async Task Run(MainWindow window, string bundle, string mediaDirectory, string? raster)
    {
        var output = App.DataDirectory; var clock = Stopwatch.StartNew();
        var packages = await Task.Run(() => Directory.GetFiles(bundle, "*.gsgeo").Select(GeographicPackage.Inspect).ToArray());
        foreach (var dataset in packages) App.GeographicData.Import(dataset);
        var expected = JsonSerializer.Deserialize<GeographicExpectedPoint[]>(File.ReadAllText(Path.Combine(bundle, "expected-boundaries.json"))) ?? throw new InvalidDataException("Missing geographic expectations");
        var geocoder = App.LocationResolver; var cold = new List<double>(); var warm = new List<double>(); var locations = new List<object>();
        foreach (var point in expected)
        {
            var timer = Stopwatch.StartNew(); var location = await Task.Run(() => geocoder.Resolve(point.Latitude, point.Longitude)); cold.Add(timer.Elapsed.TotalMilliseconds);
            if (location.Country?.Id != point.CountryId || location.Region?.Id != point.RegionId || location.City?.Id != point.CityId || location.District?.Id != point.DistrictId) throw new InvalidOperationException("Source polygon expectation mismatch: " + point.Id);
            timer.Restart(); geocoder.Resolve(point.Latitude, point.Longitude); warm.Add(timer.Elapsed.TotalMilliseconds);
            locations.Add(new { point.Id, Country = location.Country?.Display("en-US"), Region = location.Region?.Display("en-US"), City = location.City?.Display("en-US"), District = location.District?.Display("en-US"), CountryId = location.Country?.Id, RegionId = location.Region?.Id, CityId = location.City?.Id, DistrictId = location.District?.Id, RussianCity = location.City?.Display("ru-RU"), location.Warnings });
        }
        var sourceHashes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(mediaDirectory, "*", SearchOption.AllDirectories)) sourceHashes[Path.GetFullPath(path)] = await GeographicCopy.Hash(path, default);
        await window.ScanFolder(mediaDirectory); var media = window.ViewModel.Items.ToArray();
        if (media.Length != sourceHashes.Count || media.Count(m => m.HasLocation) != media.Length - 1) throw new InvalidOperationException("Controlled fixture scan mismatch");
        var single = await window.SelectForGeographicVerification(media, 1); if (single.Length != 1) throw new InvalidOperationException("Single selection mismatch");
        var multiple = await window.SelectForGeographicVerification(media, 3); if (multiple.Length != 3) throw new InvalidOperationException("Multiple selection mismatch");
        var stack = Geo.Cluster(media, 100000).OrderByDescending(s => s.Items.Count).First();
        await window.ViewModel.ShowItems(stack.Items, Strings.Get("Library"));
        if (!window.ViewModel.Selection.SequenceEqual(stack.Items)) throw new InvalidOperationException("Stack source synchronization failed");
        var selected = await window.SelectForGeographicVerification(media, media.Length);
        var destination = Path.Combine(output, "verified-destination"); Directory.CreateDirectory(destination);
        var copy = new CopyWindow(window, selected); copy.Show(); await copy.PrepareVerification(destination);
        var plan = copy.PreviewRows.ToArray();
        if (plan.Length != media.Length || plan.Count(p => p.MissingGps) != 1) throw new InvalidOperationException("Preview source synchronization failed");
        FeatureVerification.Snapshot(copy, Path.Combine(output, "geographic-copy-preview.png")); copy.Close();
        var copyTimer = Stopwatch.StartNew();
        var results = await Task.Run(() => GeographicCopy.Execute(plan, destination, Path.Combine(output, "CopyJournal"), null, default)); copyTimer.Stop();
        if (results.Any(r => r.Status == CopyState.Failed) || results.Count(r => r.Status == CopyState.Completed) != media.Count(m => m.HasLocation)) throw new InvalidOperationException("Geographic copy integration failed");
        foreach (var result in results.Where(r => r.Status == CopyState.Completed)) if (sourceHashes[result.Source] != await GeographicCopy.Hash(result.Destination, default)) throw new InvalidOperationException("Destination checksum changed");
        foreach (var source in sourceHashes) if (source.Value != await GeographicCopy.Hash(source.Key, default)) throw new InvalidOperationException("Source checksum changed");
        var repeated = await Task.Run(() => GeographicCopy.Plan(selected, geocoder, destination, "en-US", "CountryCity", true, default));
        if (repeated.Any(p => p.Include)) throw new InvalidOperationException("Identical destination detection failed");
        var benchmark = Stopwatch.StartNew();
        await Task.Run(() => { for (var i = 0; i < 10000; i++) { var p = expected[i % expected.Length]; geocoder.Resolve(p.Latitude + (i / expected.Length) * .000001, p.Longitude); } }); benchmark.Stop();
        var manager = GeographicDataWindow.Create(window); manager.Show(); await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); FeatureVerification.Snapshot(manager, Path.Combine(output, "geographic-data-manager.png")); manager.Close();
        if (raster != null)
        {
            var map = MapPackage.Inspect(raster); var catalog = new MapCatalog(output); catalog.Packages.Add(map); catalog.Preferred = map.Id; catalog.Save();
            await window.VerifyFeatureRendering(media, map, output);
        }
        using var process = Process.GetCurrentProcess(); process.Refresh();
        File.WriteAllText(Path.Combine(output, "geographic-verification.json"), JsonSerializer.Serialize(new { Status = "PASS", Scope = "Automated selection and real preview plus verified copy engine; physical confirmation is NOT_TESTED", Packages = packages.Select(p => new { p.Manifest.Id, p.Manifest.Version, p.Manifest.Coverage, p.Manifest.FeatureCount, p.Size, p.Sha256 }), Locations = locations, Indexed = media.Length, ProgrammaticSingleSelection = single.Length, ProgrammaticMultipleSelection = multiple.Length, ProgrammaticStackSelection = stack.Items.Count, Copied = results.Count(r => r.Status == CopyState.Completed), SourceChecksumsPreserved = true, DestinationChecksumsVerified = true, IdenticalCopyOperations = repeated.Count(p => p.Include), ColdLookupMilliseconds = cold, WarmLookupMilliseconds = warm, Lookup10000Milliseconds = benchmark.Elapsed.TotalMilliseconds, geocoder.CachedGeometryBytes, geocoder.CoordinateCacheCount, geocoder.LastCandidateCount, CopyMilliseconds = copyTimer.Elapsed.TotalMilliseconds, CopiedBytes = results.Where(r => r.Status == CopyState.Completed).Sum(r => r.Size), process.PrivateMemorySize64, TotalMilliseconds = clock.Elapsed.TotalMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public partial class MainWindow
{
    internal async Task VerifyGeographicRestart(string bundle)
    {
        var expected = JsonSerializer.Deserialize<GeographicExpectedPoint[]>(File.ReadAllText(Path.Combine(bundle, "expected-boundaries.json")))!;
        var originalDatasets = App.GeographicData.Datasets.ToArray();
        if (originalDatasets.Length == 0) throw new InvalidOperationException("No persisted geographic datasets");
        foreach (var point in expected)
        {
            var location = await Task.Run(() => App.LocationResolver.Resolve(point.Latitude, point.Longitude));
            if (location.City?.Id != point.CityId || location.Country?.Id != point.CountryId) throw new InvalidOperationException("Geographic restart mismatch: " + point.Id);
        }
        var map = Atlas.Tiles.Catalog.Packages.FirstOrDefault() ?? throw new InvalidOperationException("No persisted raster map");
        var sourceHash = await GeographicCopy.Hash(map.Path, default);
        for (var retry = 0; retry < 50 && Atlas.ActiveTiles.DrawnTileCount == 0; retry++) { Atlas.InvalidateVisual(); await Task.Delay(100); }
        if (Atlas.ActiveTiles.DrawnTileCount == 0) throw new InvalidOperationException("Raster not displayed after process restart");
        var before = Atlas.MediaPositionsForVerification();
        if (before.Count == 0) throw new InvalidOperationException("No media markers available for restart alignment verification");
        Atlas.Tiles.Catalog.Packages[0] = map with { Enabled = false }; Atlas.Tiles.Clear(); Atlas.InvalidateVisual();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (Atlas.ActiveTiles.DrawnTileCount != 0) throw new InvalidOperationException("Disabled map still rendered");
        Atlas.Tiles.Catalog.Packages[0] = map; Atlas.Tiles.Catalog.Preferred = "world"; Atlas.Tiles.Clear(); Atlas.InvalidateVisual();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (Atlas.ActiveTiles.DrawnTileCount != 0) throw new InvalidOperationException("Built-in map preference ignored");
        Atlas.Tiles.Catalog.Preferred = map.Id; Atlas.Tiles.Clear(); Atlas.InvalidateVisual();
        for (var retry = 0; retry < 50 && Atlas.ActiveTiles.DrawnTileCount == 0; retry++) await Task.Delay(100);
        if (Atlas.ActiveTiles.DrawnTileCount == 0) throw new InvalidOperationException("Re-enabled raster was not displayed");
        var after = Atlas.MediaPositionsForVerification();
        if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var position) || position != pair.Value)) throw new InvalidOperationException("Media moved during basemap changes");
        Atlas.Tiles.Catalog.Packages.Clear(); Atlas.Tiles.Catalog.Save(); Atlas.Tiles.Clear(); Atlas.InvalidateVisual();
        if (!File.Exists(map.Path) || sourceHash != await GeographicCopy.Hash(map.Path, default)) throw new InvalidOperationException("External raster changed during reference removal");
        Atlas.Tiles.Catalog.Packages.Add(map); Atlas.Tiles.Catalog.Preferred = map.Id; Atlas.Tiles.Catalog.Save();
        var dataset = originalDatasets[0]; var packageHash = await GeographicCopy.Hash(dataset.Path, default);
        App.GeographicData.Datasets.Remove(dataset); App.GeographicData.Save();
        if (!File.Exists(dataset.Path) || packageHash != await GeographicCopy.Hash(dataset.Path, default)) throw new InvalidOperationException("External boundary package changed during removal");
        App.GeographicData.Import(dataset);
        File.WriteAllText(Path.Combine(App.DataDirectory,"geographic-restart-verification.json"), JsonSerializer.Serialize(new { Status="PASS", Scope="Separate process restart and programmatic management; physical GUI actions NOT_TESTED", GeographicPackages=originalDatasets.Length, RasterDisplayedAfterProcessRestart=true, EnabledDisabledAndBuiltInSwitching=true, MediaMarkersPreserved=before.Count, ExternalRasterPreserved=true, ExternalBoundaryPackagePreserved=true },new JsonSerializerOptions { WriteIndented=true }));
    }
    internal async Task<MediaItem[]> SelectForGeographicVerification(IReadOnlyList<MediaItem> media, int count)
    {
        await ViewModel.ShowItems(media, Strings.Get("AllMedia")); MediaGrid.SelectedItems.Clear();
        foreach (var item in MediaGrid.Items.Cast<MediaViewModel>().Take(count)) MediaGrid.SelectedItems.Add(item);
        return CurrentCopySelection();
    }
}
