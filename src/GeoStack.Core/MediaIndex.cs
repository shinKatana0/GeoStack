// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GeoStack.Core;

// Windows ships SQLite as winsqlite3.dll. Callers serialize access to each connection.
public sealed class MediaIndex : IDisposable
{
    private IntPtr db;
    public MediaIndex(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MediaInputs.NormalizePath(path))!);
        if (sqlite3_open(MediaInputs.NormalizePath(path), out db) != 0) { var message = Error(); Dispose(); throw new IOException(message); }
        Execute("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS media(path TEXT PRIMARY KEY COLLATE NOCASE, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS fingerprints(path TEXT PRIMARY KEY COLLATE NOCASE, size INTEGER, modified INTEGER); CREATE TABLE IF NOT EXISTS exclusions(path TEXT PRIMARY KEY COLLATE NOCASE, token TEXT NOT NULL);");
    }
    private string Error() => Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "SQLite error";
    private static string Identity(string path)=>MediaInputs.NormalizePath(path).ToUpperInvariant();
    private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
    public void Upsert(MediaItem item)
    {
        var key = MediaInputs.NormalizePath(item.Path);
        Execute($"DELETE FROM media WHERE path={Quote(item.Path)} AND path<>{Quote(key)}; INSERT INTO media SELECT {Quote(key)},{Quote(JsonSerializer.Serialize(item))} WHERE NOT EXISTS(SELECT 1 FROM exclusions WHERE path={Quote(Identity(key))}) ON CONFLICT(path) DO UPDATE SET payload=excluded.payload;");
    }
    public void Store(MediaItem item, long size, long modified)
        => StoreMany([(item, size, modified)]);
    public void StoreMany(IEnumerable<(MediaItem Item, long Size, long Modified)> records)
    {
        Execute("BEGIN IMMEDIATE;");
        try { foreach (var (item,size,modified) in records) { Upsert(item); Execute($"INSERT OR REPLACE INTO fingerprints SELECT {Quote(MediaInputs.NormalizePath(item.Path))},{size},{modified} WHERE NOT EXISTS(SELECT 1 FROM exclusions WHERE path={Quote(Identity(item.Path))});"); } Execute("COMMIT;"); }
        catch { Execute("ROLLBACK;"); throw; }
    }
    public Dictionary<string, (long Size, long Modified)> Fingerprints()
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        Callback cb = (_, _, values, _) => {
            var path = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values))!;
            var size = long.Parse(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, IntPtr.Size))!, System.Globalization.CultureInfo.InvariantCulture);
            var modified = long.Parse(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, IntPtr.Size * 2))!, System.Globalization.CultureInfo.InvariantCulture);
            result[MediaInputs.NormalizePath(path)] = (size, modified); return 0;
        };
        var rc = sqlite3_exec(db, "SELECT path,size,modified FROM fingerprints", cb, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) sqlite3_free(error); GC.KeepAlive(cb);
        if (rc != 0) throw new IOException(Error()); return result;
    }
    public IReadOnlyList<MediaItem> ReadAll()
    {
        var list = new List<MediaItem>();
        Exception? failure = null;
        Callback cb = (_, count, values, names) => { try { list.Add(JsonSerializer.Deserialize<MediaItem>(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values))!) ?? throw new JsonException("Empty indexed item")); return 0; } catch (JsonException ex) { failure = ex; return 1; } };
        var rc = sqlite3_exec(db, "SELECT payload FROM media WHERE NOT EXISTS(SELECT 1 FROM exclusions WHERE exclusions.path=media.path) ORDER BY path", cb, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) sqlite3_free(error);
        GC.KeepAlive(cb);
        if (failure != null) throw new IOException("Invalid indexed metadata", failure);
        if (rc != 0) throw new IOException(Error());
        var excluded=Exclusions();
        return list.Where(m=>!excluded.Contains(MediaInputs.NormalizePath(m.Path))).GroupBy(m => MediaInputs.NormalizePath(m.Path), StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToArray();
    }
    public HashSet<string> Exclusions() => ExclusionTokens().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,string> ExclusionTokens()
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        Callback callback=(_,_,values,_)=>{result[MediaInputs.NormalizePath(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values))!)]=Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values,IntPtr.Size))!;return 0;};
        var rc=sqlite3_exec(db,"SELECT path,token FROM exclusions",callback,IntPtr.Zero,out var error);
        if(error!=IntPtr.Zero)sqlite3_free(error);GC.KeepAlive(callback);if(rc!=0)throw new IOException(Error());return result;
    }
    public void Exclude(string path) => ExcludeMany([path]);
    public void RelocateMany(IReadOnlyList<(MediaItem Original,string Destination)> moves)
    {
        Execute("BEGIN IMMEDIATE;");
        try
        {
            foreach(var (original,destination) in moves)
            {
                if(File.Exists(original.Path) || !File.Exists(destination))throw new IOException("Move is not complete; catalog paths preserved");
                var source=MediaInputs.NormalizePath(original.Path);var target=MediaInputs.NormalizePath(destination);
                Execute($"INSERT OR REPLACE INTO exclusions VALUES({Quote(Identity(source))},{Quote(Guid.NewGuid().ToString("N"))}); DELETE FROM media WHERE path={Quote(source)}; DELETE FROM fingerprints WHERE path={Quote(source)}; DELETE FROM exclusions WHERE path={Quote(Identity(target))};");
                var relocated=original with{Path=target};Upsert(relocated);
                var info=new FileInfo(target);Execute($"INSERT OR REPLACE INTO fingerprints VALUES({Quote(target)},{info.Length},{info.LastWriteTimeUtc.Ticks});");
            }
            Execute("COMMIT;");
        }
        catch{Execute("ROLLBACK;");throw;}
    }
    public void ExcludeMany(IEnumerable<string> paths)
    {
        var keys=paths.Select(MediaInputs.NormalizePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Execute("BEGIN IMMEDIATE;");
        try
        {
            var stored=ReadAll().Where(m=>keys.Contains(MediaInputs.NormalizePath(m.Path))).Select(m=>m.Path).Concat(keys).Distinct(StringComparer.Ordinal).ToArray();
            foreach(var path in keys)Execute($"INSERT OR REPLACE INTO exclusions VALUES({Quote(Identity(path))},{Quote(Guid.NewGuid().ToString("N"))});");
            foreach(var path in stored)Execute($"DELETE FROM media WHERE path={Quote(path)}; DELETE FROM fingerprints WHERE path={Quote(path)};");
            Execute("COMMIT;");
        }
        catch { Execute("ROLLBACK;"); throw; }
    }
    public void RestoreExplicitFiles(IEnumerable<string> paths,IReadOnlyDictionary<string,string>? expected=null)
    {
        expected ??= ExclusionTokens();
        Execute("BEGIN IMMEDIATE;");
        try{foreach(var path in paths){var key=Identity(path);if(expected.TryGetValue(key,out var token))Execute($"DELETE FROM exclusions WHERE path={Quote(Identity(key))} AND token={Quote(token)};");}Execute("COMMIT;");}
        catch{Execute("ROLLBACK;");throw;}
    }
    private void Execute(string sql)
    {
        var rc = sqlite3_exec(db, sql, null, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) sqlite3_free(error);
        if (rc != 0) throw new IOException(Error());
    }
    public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close(db); db = IntPtr.Zero; } }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Callback(IntPtr state, int count, IntPtr values, IntPtr names);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, Callback? callback, IntPtr state, out IntPtr error);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr ptr);
}
