// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GeoStack.Core;

public interface IOfflineMapProvider
{
    string Id { get; }
    bool Covers(int zoom, double latitude, double longitude);
    byte[]? ReadTile(int zoom, int x, int y);
}

public sealed class BuiltInWorldMap : IOfflineMapProvider
{
    public string Id => "world";
    public bool Covers(int zoom, double latitude, double longitude) => true;
    public byte[]? ReadTile(int zoom, int x, int y) => null; // Drawn as local WPF geometry.
}

internal sealed class ReadOnlySqlite : IDisposable
{
    private IntPtr db;
    public ReadOnlySqlite(string path)
    {
        if (sqlite3_open_v2(Path.GetFullPath(path), out db, 1 | 0x10000, IntPtr.Zero) != 0) { Dispose(); throw new IOException("Map database cannot be opened"); }
        sqlite3_busy_timeout(db, 250);
    }
    public List<string[]> Rows(string sql,CancellationToken token=default)
    {
        var result = new List<string[]>();
        Query(sql, stmt => { var row = new string[sqlite3_column_count(stmt)]; for (var i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i)) ?? ""; result.Add(row); },token,token.CanBeCanceled);
        return result;
    }
    public byte[]? Blob(string sql,CancellationToken token=default)
    {
        byte[]? bytes = null;
        Query(sql.Replace("SELECT tile_data", "SELECT CASE WHEN length(tile_data) BETWEEN 1 AND 8388608 THEN tile_data ELSE NULL END"), stmt => { var length = sqlite3_column_bytes(stmt, 0); if (length <= 0 || length > 8 * 1024 * 1024) throw new InvalidDataException("Tile size is unsupported"); bytes = new byte[length]; Marshal.Copy(sqlite3_column_blob(stmt, 0), bytes, 0, length); },token,true); return bytes;
    }
    private void Query(string sql, Action<IntPtr> row,CancellationToken token=default,bool bounded=false)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out var statement, IntPtr.Zero) != 0) throw new InvalidDataException("Invalid MBTiles structure");
        var watch=System.Diagnostics.Stopwatch.StartNew();
        Progress? progress=bounded ? _=>token.IsCancellationRequested || watch.ElapsedMilliseconds>250 ? 1 : 0 : null;
        if(progress!=null)sqlite3_progress_handler(db,1000,progress,IntPtr.Zero);
        try { int rc; while((rc=sqlite3_step(statement))==100)row(statement);token.ThrowIfCancellationRequested();if(rc!=101)throw new IOException("Map query failed or exceeded its time budget"); }
        finally { if(progress!=null)sqlite3_progress_handler(db,0,null,IntPtr.Zero);sqlite3_finalize(statement);GC.KeepAlive(progress); }
    }
    public void Dispose() { if (db != IntPtr.Zero) sqlite3_close(db); db = IntPtr.Zero; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Progress(IntPtr data);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern void sqlite3_progress_handler(IntPtr db,int instructions,Progress? callback,IntPtr data);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
}

public sealed record MapPackage(string Id, string Name, string Path, long Size, string Encoding, int MinZoom, int MaxZoom, double West, double South, double East, double North, string Attribution, string Source, bool Enabled = true, IReadOnlyList<MapPackage>? Variants = null) : IOfflineMapProvider
{
    public static byte[] Sample(string path) { using var db = new ReadOnlySqlite(path); return db.Blob("SELECT tile_data FROM tiles LIMIT 1") ?? throw new InvalidDataException("MapEmpty"); }
    public long InstalledSize => Size + (Variants?.Sum(v => v.Size) ?? 0);
    public bool Available => File.Exists(Path);
    public bool Covers(int zoom,double latitude,double longitude)=>Available && CoversBounds(zoom,latitude,longitude);
    public bool CoversBounds(int zoom, double latitude, double longitude) => Enabled && zoom >= MinZoom && zoom <= MaxZoom && latitude >= South && latitude <= North && (West <= East ? longitude >= West && longitude <= East : longitude >= West || longitude <= East);
    public bool IntersectsTile(int zoom,int x,int y)=>Available && IntersectsTileBounds(zoom,x,y);
    public bool IntersectsTileBounds(int zoom, int x, int y)
    {
        if (zoom < 0 || zoom > 22 || x < 0 || y < 0 || x >= (1 << zoom) || y >= (1 << zoom)) return false;
        if (!Enabled || zoom < MinZoom || zoom > MaxZoom) return false;
        var n = (double)(1 << zoom); var a = Geo.Unproject(x / n, y / n); var b = Geo.Unproject((x + 1) / n, (y + 1) / n);
        return b.Latitude <= North && a.Latitude >= South && (West <= East ? b.Longitude >= West && a.Longitude <= East : b.Longitude >= West || a.Longitude <= East);
    }
    public static int TmsRow(int zoom,int xyzY) { if(zoom<0 || zoom>22 || xyzY<0 || xyzY>=(1<<zoom))throw new ArgumentOutOfRangeException(nameof(zoom));return (1<<zoom)-1-xyzY; }
    public byte[]? ReadTile(int zoom, int x, int y)
    {
        if (zoom < MinZoom || zoom > MaxZoom || x < 0 || x >= (1 << zoom) || y < 0 || y >= (1 << zoom)) return null;
        using var db = new ReadOnlySqlite(Path);
        return db.Blob(FormattableString.Invariant($"SELECT tile_data FROM tiles WHERE zoom_level={zoom} AND tile_column={x} AND tile_row={TmsRow(zoom, y)} LIMIT 1"));
    }
    public static MapPackage Inspect(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(path), ".mbtiles", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("MapUnsupportedFormat");
        using var db = new ReadOnlySqlite(path);
        var metadata = db.Rows("SELECT name,value FROM metadata LIMIT 1000").GroupBy(r => r[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
        var format = metadata.GetValueOrDefault("format", "").ToLowerInvariant();
        if (format is "pbf" or "mvt") throw new InvalidDataException("MapVectorUnsupported");
        if (format is not ("png" or "jpg" or "jpeg")) throw new InvalidDataException("MapEncodingUnsupported");
        var sample = db.Blob("SELECT tile_data FROM tiles LIMIT 1") ?? throw new InvalidDataException("MapEmpty");
        if (format == "png" ? !sample.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) : !sample.AsSpan().StartsWith(new byte[] { 255, 216, 255 })) throw new InvalidDataException("MapEncodingUnsupported");
        var zooms = db.Rows("SELECT MIN(zoom_level),MAX(zoom_level) FROM tiles")[0];
        if (!int.TryParse(zooms[0], out var min) || !int.TryParse(zooms[1], out var max) || min < 0 || max > 22 || max < min) throw new InvalidDataException("MapZoomInvalid");
        foreach (var (key, value) in new[] { ("minzoom", min), ("maxzoom", max) })
            if (metadata.TryGetValue(key, out var z) && (!int.TryParse(z, out var declared) || declared != value)) throw new InvalidDataException("MapZoomInvalid");
        var bounds = metadata.GetValueOrDefault("bounds", "-180,-85.05112878,180,85.05112878").Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (bounds.Length != 4 || bounds.Any(v => !double.IsFinite(v)) || bounds[0] < -180 || bounds[0] > 180 || bounds[2] < -180 || bounds[2] > 180 || bounds[1] < -90 || bounds[3] > 90 || bounds[1] >= bounds[3]) throw new InvalidDataException("MapBoundsInvalid");
        if (metadata.TryGetValue("scheme", out var scheme) && scheme != "tms") throw new InvalidDataException("MapSchemeUnsupported");
        return new(Guid.NewGuid().ToString("N"), metadata.GetValueOrDefault("name", System.IO.Path.GetFileNameWithoutExtension(path)), path, new FileInfo(path).Length, format, min, max, bounds[0], bounds[1], bounds[2], bounds[3], metadata.GetValueOrDefault("attribution", ""), metadata.GetValueOrDefault("description", metadata.GetValueOrDefault("source", "")));
    }
}

public sealed class MapCatalog
{
    private sealed record CatalogData(string Preferred, List<MapPackage> Packages);
    public List<MapPackage> Packages { get; private set; } = [];
    public string Preferred { get; set; } = "world";
    public long Revision { get; private set; }
    private readonly string path;
    public MapCatalog(string directory)
    {
        path = System.IO.Path.Combine(directory, "Maps/catalog.json");
        try { if (File.Exists(path)) { var data = JsonSerializer.Deserialize<CatalogData>(File.ReadAllText(path)); if (data?.Packages != null) { Packages = data.Packages.Where(p => p != null).ToList(); Preferred = data.Preferred ?? "world"; } } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new CatalogData(Preferred, Packages))); File.Move(temporary, path, true);
        Revision++;
    }
    public IEnumerable<MapPackage> Candidates(int zoom, double lat, double lon) => Preferred == "world" ? [] : Packages.Where(p => p.Covers(zoom, lat, lon)).OrderByDescending(p => p.Id == Preferred);
    public IEnumerable<MapPackage> CandidatesForTile(int zoom, int x, int y) => Preferred == "world" ? [] : Packages.Where(p => p.IntersectsTile(zoom, x, y)).OrderByDescending(p => p.Id == Preferred);
}
