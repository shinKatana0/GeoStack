// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GeoStack.Core;

public sealed record GeographicManifest(int SchemaVersion, string Id, string Name, string Version, string Source, string SourceUrl, string License, string LicenseUrl, string Attribution, string Coverage, string[] Levels, string DataDate, int FeatureCount, string Semantics, double BoundaryToleranceDegrees = 1e-8);
public sealed record GeographicDataset(string Path, GeographicManifest Manifest, long Size, long ModifiedTicks, string Sha256, bool Enabled = true, string Validation = "GeoValidated")
{
    public bool Available => File.Exists(Path);
    public bool Outdated => DateTime.TryParse(Manifest.DataDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) && date < DateTime.UtcNow.AddYears(-3);
}
public sealed record GeographicFeature(GeographicName Name, string Level, string? ParentId, string CountryId, string[] Ancestors, string Semantics, string[] Warnings);
public sealed record GeographicHits(IReadOnlyList<GeographicFeature> Features, bool Edge);

public static class GeographicPackage
{
    public const int ApplicationId = 0x47534731; // GSG1
    public static GeographicDataset Inspect(string path)
    {
        try { return InspectCore(path); }
        catch (Exception ex) when (ex is not FileLoadException && (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException))
        { throw new InvalidDataException("GeoInvalidPackage", ex); }
    }
    private static GeographicDataset InspectCore(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!path.EndsWith(".gsgeo", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GeoUnsupportedPackage");
        using var db = new ReadOnlySqlite(path);
        if (db.Rows("PRAGMA application_id")[0][0] != ApplicationId.ToString(CultureInfo.InvariantCulture) || db.Rows("PRAGMA user_version")[0][0] != "1") throw new InvalidDataException("GeoUnsupportedPackage");
        var rows = db.Rows("SELECT value FROM metadata WHERE key='manifest' AND length(value)<=65536");
        if (rows.Count != 1) throw new InvalidDataException("GeoInvalidPackage");
        var manifest = JsonSerializer.Deserialize<GeographicManifest>(rows[0][0]) ?? throw new InvalidDataException("GeoInvalidPackage");
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Source) || string.IsNullOrWhiteSpace(manifest.SourceUrl) || string.IsNullOrWhiteSpace(manifest.License) || string.IsNullOrWhiteSpace(manifest.LicenseUrl) || string.IsNullOrWhiteSpace(manifest.Attribution) || string.IsNullOrWhiteSpace(manifest.Coverage) || manifest.Levels == null || manifest.Levels.Length == 0 || manifest.Levels.Any(l => l is not ("country" or "region" or "city" or "district")) || manifest.FeatureCount is < 1 or > 2000000) throw new InvalidDataException("GeoInvalidPackage");
        if (db.Rows("PRAGMA quick_check").Any(r => r[0] != "ok") || db.Rows("SELECT rtreecheck('spatial_index')")[0][0] != "ok") throw new InvalidDataException("GeoInvalidPackage");
        if (db.Rows("SELECT count(*) FROM features")[0][0] != manifest.FeatureCount.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("GeoInvalidPackage");
        if (db.Rows("SELECT f.fid FROM features f LEFT JOIN bounds b ON b.feature_id=f.fid WHERE b.feature_id IS NULL LIMIT 1").Count != 0 || db.Rows("SELECT b.part_id FROM bounds b LEFT JOIN spatial_index s ON b.part_id=s.part_id WHERE s.part_id IS NULL LIMIT 1").Count != 0) throw new InvalidDataException("GeoInvalidPackage");
        if (!double.IsFinite(manifest.BoundaryToleranceDegrees) || manifest.BoundaryToleranceDegrees < 0 || manifest.BoundaryToleranceDegrees > .01) throw new InvalidDataException("GeoInvalidPackage");
        db.Rows("SELECT fid,id,level,parent_id,country_id,source_name,name_en,name_ru,ancestors,semantics,geometry,warnings FROM features LIMIT 0");
        var file = new FileInfo(path); using var input = File.OpenRead(path);
        return new(path, manifest, file.Length, file.LastWriteTimeUtc.Ticks, Convert.ToHexString(SHA256.HashData(input)));
    }
    public static string Fingerprint(GeographicDataset dataset)
    {
        try { var info = new FileInfo(dataset.Path); return info.Exists ? $"{dataset.Manifest.Id}:{dataset.Manifest.Version}:{dataset.Sha256}:{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{dataset.Enabled}" : dataset.Manifest.Id + ":missing"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return dataset.Manifest.Id + ":unavailable"; }
    }
}

/// <summary>Geometry is loaded only after an R-tree hit and retained under a 32 MiB estimate.</summary>
public sealed class GeographicPackageReader
{
    private sealed record CachedFeature(BoundaryGeometry Geometry,GeographicFeature Feature,long EstimatedBytes);
    private readonly Dictionary<(string File,string Stamp,string Id),CachedFeature> geometries=[];
    private long cachedBytes;
    public int LastCandidateCount {get;private set;}
    public long CachedGeometryBytes=>cachedBytes;
    private readonly BoundedLru<(string File,string Stamp),ReadOnlySqlite> readers=new(8);
    internal bool RetainReaders {get;set;}
    public int ReaderCount=>readers.Count;
    internal void ReleaseReaders(){foreach(var reader in readers.Values)reader.Dispose();readers.Clear();}
    public double OpenMilliseconds {get;private set;}
    public double SqlMilliseconds {get;private set;}
    public double MetadataLoadMilliseconds {get;private set;}
    public double ContainmentMilliseconds {get;private set;}
    public GeographicHits Query(GeographicDataset dataset,double latitude,double longitude)
    {
        var timing=System.Diagnostics.Stopwatch.GetTimestamp();var stamp=GeographicPackage.Fingerprint(dataset);ReadOnlySqlite db;
        if(RetainReaders){var key=(dataset.Path,stamp);if(!readers.TryGet(key,out db!)){db=new ReadOnlySqlite(dataset.Path);readers.Set(key,db)?.Dispose();}}
        else db=new ReadOnlySqlite(dataset.Path);
        try
        {
            OpenMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(timing).TotalMilliseconds;timing=System.Diagnostics.Stopwatch.GetTimestamp();
            var tolerance=dataset.Manifest.BoundaryToleranceDegrees;
            // Query only spatial identities. Immutable names and geometry are read once per versioned feature.
            var query=FormattableString.Invariant($"SELECT DISTINCT b.feature_id FROM spatial_index s JOIN bounds b ON b.part_id=s.part_id WHERE s.min_x<={longitude+tolerance} AND s.max_x>={longitude-tolerance} AND s.min_y<={latitude+tolerance} AND s.max_y>={latitude-tolerance} LIMIT 257");
            var candidates=db.Rows(query);SqlMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(timing).TotalMilliseconds;LastCandidateCount=candidates.Count;
            if(candidates.Count>256)throw new InvalidDataException("GeoTooManyOverlaps");
            var results=new List<GeographicFeature>();var edge=false;
            foreach(var row in candidates)
            {
                var key=(dataset.Path,stamp,row[0]);
                if(!geometries.TryGetValue(key,out var entry))
                {
                    timing=System.Diagnostics.Stopwatch.GetTimestamp();
                    if(!long.TryParse(row[0],out var fid))throw new InvalidDataException("GeoInvalidPackage");
                    var data=db.Rows(FormattableString.Invariant($"SELECT fid,id,level,parent_id,country_id,source_name,name_en,name_ru,ancestors,semantics,geometry,warnings FROM features WHERE fid={fid} AND length(geometry) BETWEEN 1 AND 8388608"));
                    if(data.Count!=1)throw new InvalidDataException("GeoInvalidGeometry");var r=data[0];
                    if(r.Where((_,i)=>i!=10).Sum(v=>(long)v.Length)>32768)throw new InvalidDataException("GeoInvalidPackage");
                    var geometry=new BoundaryGeometry(r[10]);var bytes=geometry.EstimatedBytes+r.Where((_,i)=>i!=10).Sum(v=>v.Length*2L)+256;
                    if(bytes>32*1024*1024)throw new InvalidDataException("GeoInvalidGeometry");
                    var feature=new GeographicFeature(new(r[1],r[5],string.IsNullOrWhiteSpace(r[6])?null:r[6],string.IsNullOrWhiteSpace(r[7])?null:r[7]),r[2],string.IsNullOrWhiteSpace(r[3])?null:r[3],r[4],JsonSerializer.Deserialize<string[]>(r[8])??[],r[9],JsonSerializer.Deserialize<string[]>(r[11])??[]);
                    while(cachedBytes+bytes>32*1024*1024 || geometries.Count>=256){var first=geometries.First();cachedBytes-=first.Value.EstimatedBytes;geometries.Remove(first.Key);}
                    entry=new(geometry,feature,bytes);geometries.Add(key,entry);cachedBytes+=bytes;
                    MetadataLoadMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(timing).TotalMilliseconds;
                }
                timing=System.Diagnostics.Stopwatch.GetTimestamp();var match=entry.Geometry.Contains(latitude,longitude,tolerance);ContainmentMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(timing).TotalMilliseconds;
                if(match==BoundaryMatch.Edge)edge=true;if(match!=BoundaryMatch.Inside)continue;
                results.Add(entry.Feature with{Ancestors=(string[])entry.Feature.Ancestors.Clone(),Warnings=(string[])entry.Feature.Warnings.Clone()});
            }
            return new(results,edge);
        }
        finally{if(!RetainReaders)db.Dispose();}
    }
    public void Clear() { ReleaseReaders();geometries.Clear(); cachedBytes = 0; }
}

public sealed class GeographicCatalog
{
    private readonly string path;
    public List<GeographicDataset> Datasets { get; private set; } = [];
    public string LoadStatus { get; private set; } = "GeoValidated";
    public GeographicCatalog(string dataDirectory)
    {
        path = System.IO.Path.Combine(dataDirectory, "GeographicData/catalog.json"); Reload();
    }
    public void Reload()
    {
        try { Datasets = File.Exists(path) ? JsonSerializer.Deserialize<List<GeographicDataset>>(File.ReadAllText(path))?.Where(d => d?.Manifest?.Levels != null && !string.IsNullOrWhiteSpace(d.Path) && !string.IsNullOrWhiteSpace(d.Manifest.Id) && !string.IsNullOrWhiteSpace(d.Manifest.Version)).Take(32).ToList() ?? [] : []; LoadStatus = "GeoValidated"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Datasets = []; LoadStatus = "GeoCatalogUnavailable"; }
    }
    public void Import(GeographicDataset dataset)
    {
        var existing = Datasets.FindIndex(d => d.Manifest.Id == dataset.Manifest.Id);
        if (existing >= 0) Datasets[existing] = dataset with { Enabled = Datasets[existing].Enabled }; else Datasets.Add(dataset);
        Save();
    }
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Datasets)); File.Move(temporary, path, true);
    }
}
