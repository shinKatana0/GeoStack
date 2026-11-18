// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;

namespace GeoStack.Core;

/// <summary>Owns a new temporary database on one acquisition worker; no existing package is overwritten.</summary>
public sealed class RasterPackageWriter : IDisposable
{
    private IntPtr db;
    public RasterPackageWriter(string path,IReadOnlyDictionary<string,string> metadata)
    {
        if(File.Exists(path))throw new IOException("Refusing to overwrite a map");
        if(sqlite3_open(Path.GetFullPath(path),out db)!=0){Dispose();throw new IOException("Cannot create map database");}
        try {
        Execute("CREATE TABLE metadata(name TEXT PRIMARY KEY,value TEXT);CREATE TABLE tiles(zoom_level INTEGER,tile_column INTEGER,tile_row INTEGER,tile_data BLOB,PRIMARY KEY(zoom_level,tile_column,tile_row));BEGIN IMMEDIATE;");
        foreach(var pair in metadata)Execute("INSERT INTO metadata VALUES('"+pair.Key.Replace("'","''")+"','"+pair.Value.Replace("'","''")+"');");
        } catch { Dispose();throw; }
    }
    public void Add(DownloadTile tile,byte[] png)
    {
        if(png.Length<8 || png.Length>8*1024*1024)throw new InvalidDataException("Invalid generated tile size");
        var sql=FormattableString.Invariant($"INSERT INTO tiles VALUES({tile.Zoom},{tile.X},{MapPackage.TmsRow(tile.Zoom,tile.Y)},?1)");
        if(sqlite3_prepare_v2(db,sql,-1,out var statement,IntPtr.Zero)!=0)throw new IOException("Map insertion failed");
        try{if(sqlite3_bind_blob(statement,1,png,png.Length,new IntPtr(-1))!=0 || sqlite3_step(statement)!=101)throw new IOException("Map tile write failed");}
        finally{sqlite3_finalize(statement);}
    }
    public void Complete()=>Execute("COMMIT;");
    private void Execute(string sql){var rc=sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out var error);if(error!=IntPtr.Zero)sqlite3_free(error);if(rc!=0)throw new IOException("Map database operation failed");}
    public void Dispose(){if(db!=IntPtr.Zero)sqlite3_close(db);db=IntPtr.Zero;}
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr state,out IntPtr error);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern void sqlite3_free(IntPtr error);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_bind_blob(IntPtr statement,int position,byte[] data,int length,IntPtr destructor);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_finalize(IntPtr statement);
}
