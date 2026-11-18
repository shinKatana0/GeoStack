// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace GeoStack.Core;

public sealed record GeographicLocation(GeographicName? Country, GeographicName? Region, GeographicName? City, GeographicName? District)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string DatasetVersion { get; init; } = "";
    public string[] Hierarchy(string language, string hierarchy) => (hierarchy switch
    {
        "CountryRegionCity" => new[] { Country, Region, City },
        "CountryCityDistrict" => new[] { Country, City ?? Region, District },
        _ => new[] { Country, City ?? Region }
    }).Where(n => n != null).DistinctBy(n => n!.Id).Select(n => n!.Display(language)).ToArray();
}

/// <summary>Indexed polygon containment. Place points never establish administrative assignments.</summary>
public sealed class ReverseGeocoder
{
    private sealed record Boundary(GeographicName Name, string Level, string? Parent, BoundaryGeometry Geometry);
    private readonly List<Boundary> boundaries = [];
    private readonly Dictionary<(int X, int Y), HashSet<int>> index = [];
    private readonly BoundedLru<(double, double), GeographicLocation> cache = new(16384);
    private readonly GeographicPackageReader reader = new();
    private readonly Func<IReadOnlyList<GeographicDataset>>? datasets;
    private readonly Dictionary<string, (string Stamp, GeographicDataset? Dataset)> validated = [];
    private readonly object gate = new();
    private string version = "";
    public int CoordinateCacheHits { get; private set; }
    public int CoordinateCacheCount => cache.Count;
    public int LastCandidateCount { get; private set; }
    public int ActiveGeographicReaders=>reader.ReaderCount;
    public object GeographicMeasurements=>new{reader.OpenMilliseconds,reader.SqlMilliseconds,reader.MetadataLoadMilliseconds,reader.ContainmentMilliseconds};
    public long CachedGeometryBytes => reader.CachedGeometryBytes;
    public ReverseGeocoder(string countriesPath, string? administrativePath = null, Func<IReadOnlyList<GeographicDataset>>? datasets = null)
    {
        this.datasets = datasets;
        try
        {
            if (File.Exists(countriesPath)) Load(countriesPath, true);
            if (administrativePath != null && File.Exists(administrativePath)) Load(administrativePath, false);
            if (boundaries.Select(b => b.Name.Id).Distinct().Count() != boundaries.Count) throw new InvalidDataException("Duplicate boundary identifiers");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException) { throw new InvalidDataException("Invalid administrative boundary data", ex); }
    }
    private static (int X, int Y) Cell(double latitude, double longitude) => (Math.Clamp((int)Math.Floor((longitude + 180) / 5), 0, 71), Math.Clamp((int)Math.Floor((latitude + 90) / 5), 0, 35));
    private void Load(string path, bool countries)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = f.GetProperty("properties"); var level = countries ? "country" : p.GetProperty("level").GetString()!;
            if (level is not ("country" or "region" or "city" or "district")) continue;
            var geometry = new BoundaryGeometry(f.GetProperty("geometry").GetRawText()); var id = boundaries.Count;
            boundaries.Add(new(GeographicName.Read(p, countries ? "NAME" : "name", countries ? "NE_ID" : "id"), level, p.TryGetProperty("parent_id", out var parent) ? parent.GetString() : null, geometry));
            foreach (var b in geometry.Bounds())
            {
                var lower = Cell(b.South, b.West); var upper = Cell(b.North, b.East);
                for (var x = lower.X; x <= upper.X; x++) for (var y = lower.Y; y <= upper.Y; y++)
                { if (!index.TryGetValue((x, y), out var ids)) index[(x, y)] = ids = []; ids.Add(id); }
            }
        }
    }
    private int batchDepth;
    /// <summary>Reuse at most eight read-only databases within a background lookup batch; the last scope releases them.</summary>
    public IDisposable BeginBatch()
    {
        lock(gate){batchDepth++;reader.RetainReaders=true;return new Batch(this);}
    }
    private sealed class Batch(ReverseGeocoder owner) : IDisposable
    {
        private ReverseGeocoder? current=owner;
        public void Dispose()
        {
            var value=Interlocked.Exchange(ref current,null);if(value==null)return;
            lock(value.gate){if(--value.batchDepth==0){value.reader.RetainReaders=false;value.reader.ReleaseReaders();}}
        }
    }
    public GeographicLocation Resolve(double? latitude, double? longitude)
    {
        lock (gate) return ResolveCore(latitude, longitude);
    }
    private GeographicLocation ResolveCore(double? latitude, double? longitude)
    {
        if (!Geo.IsValid(latitude, longitude)) return new(null, null, null, null) { Warnings = ["GeoMissingGps"] };
        var installed = (datasets?.Invoke() ?? []).Where(d => d.Enabled).Take(32).ToArray();
        var current = string.Join('|', installed.Select(GeographicPackage.Fingerprint));
        if (current != version) { cache.Clear(); reader.Clear(); version = current; validated.Clear(); }
        var key = (latitude!.Value, Geo.WrapLongitude(longitude!.Value));
        if (cache.TryGet(key, out var cached)) { CoordinateCacheHits++; return cached; }
        var warnings = new HashSet<string>(); var edge = false; LastCandidateCount = 0;
        var local = new List<Boundary>();
        foreach (var id in index.GetValueOrDefault(Cell(key.Item1, key.Item2)) ?? [])
        {
            LastCandidateCount++; var boundary = boundaries[id]; var match = boundary.Geometry.Contains(key.Item1, key.Item2);
            if (match == BoundaryMatch.Inside) local.Add(boundary); else if (match == BoundaryMatch.Edge) edge = true;
        }
        var detailed = new List<GeographicFeature>();
        foreach (var entry in installed.OrderByDescending(d => d.Manifest.DataDate, StringComparer.Ordinal))
        {
            try
            {
                if (!entry.Available) { warnings.Add("GeoPackageUnavailable"); continue; }
                var stamp = GeographicPackage.Fingerprint(entry);
                GeographicDataset? dataset;
                if (!validated.TryGetValue(entry.Path, out var checkedEntry) || checkedEntry.Stamp != stamp)
                {
                    try
                    {
                        dataset = GeographicPackage.Inspect(entry.Path);
                        if (dataset.Manifest.Id != entry.Manifest.Id) throw new InvalidDataException("GeoInvalidPackage");
                        if (dataset.Size == entry.Size && dataset.ModifiedTicks == entry.ModifiedTicks && dataset.Sha256 != entry.Sha256) throw new InvalidDataException("GeoInvalidPackage");
                        validated[entry.Path] = (stamp, dataset);
                    }
                    catch { validated[entry.Path] = (stamp, null); throw; }
                }
                else dataset = checkedEntry.Dataset;
                if (dataset == null) { warnings.Add("GeoPackageUnavailable"); continue; }
                var hits = reader.Query(dataset, key.Item1, key.Item2); LastCandidateCount += reader.LastCandidateCount;
                detailed.AddRange(hits.Features); edge |= hits.Edge;
                if (dataset.Outdated && (hits.Features.Count>0 || hits.Edge)) warnings.Add("GeoDatasetOutdated");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or ArgumentException)
            { warnings.Add("GeoPackageUnavailable"); }
        }
        GeographicFeature? Pick(string level, Func<GeographicFeature, bool> predicate)
        {
            var candidates = detailed.Where(f => f.Level == level && predicate(f)).DistinctBy(f => f.Name.Id).ToArray();
            if (level == "district" && candidates.Length > 1)
            {
                var deepest = candidates.Where(f => candidates.All(other => other.Name.Id == f.Name.Id || f.Ancestors.Contains(other.Name.Id))).ToArray();
                if (deepest.Length == 1) return deepest[0];
            }
            if (candidates.Length > 1) warnings.Add("GeoAmbiguousBoundary");
            return candidates.Length == 1 ? candidates[0] : null;
        }
        var country = Pick("country", _ => true);
        GeographicName? countryName, regionName, cityName, districtName;
        if (country != null)
        {
            var region = Pick("region", f => f.CountryId == country.Name.Id);
            var city = Pick("city", f => f.CountryId == country.Name.Id && (region == null || f.Ancestors.Contains(region.Name.Id)));
            var district = city == null ? region == null ? null : Pick("district", f => f.CountryId == country.Name.Id && f.ParentId == region.Name.Id) : Pick("district", f => f.CountryId == country.Name.Id && (f.ParentId == city.Name.Id || f.Ancestors.Contains(city.Name.Id)));
            countryName = country.Name; regionName = region?.Name; cityName = city?.Name; districtName = district?.Name;
            foreach (var notice in new[] { country, region, city, district }.Where(f => f != null).SelectMany(f => f!.Warnings)) warnings.Add(notice);
        }
        else if (!detailed.Any(f => f.Level == "country"))
        {
            Boundary? Legacy(string level, params string?[] parents)
            {
                var found = local.Where(b => b.Level == level && (level == "country" || b.Parent != null && parents.Contains(b.Parent))).ToArray();
                if (found.Length > 1) warnings.Add("GeoAmbiguousBoundary");
                return found.Length == 1 ? found[0] : null;
            }
            var fallbackCountry = Legacy("country"); var region = Legacy("region", fallbackCountry?.Name.Id); var city = Legacy("city", region?.Name.Id, fallbackCountry?.Name.Id); var district = Legacy("district", city?.Name.Id);
            countryName = fallbackCountry?.Name; regionName = region?.Name; cityName = city?.Name; districtName = district?.Name;
            if (countryName != null) warnings.Add("GeoCoarseCountry");
        }
        else { countryName = regionName = cityName = districtName = null; }
        if (cityName == null) warnings.Add(regionName != null ? "GeoRegionFallback" : countryName != null ? "GeoCountryFallback" : "GeoUnknownLocation");
        if (edge) warnings.Add("GeoBorderUncertain");
        var result = new GeographicLocation(countryName, regionName, cityName, districtName) { Warnings = warnings.Order().ToArray(), DatasetVersion = version };
        cache.Set(key,result); return result;
    }
}
