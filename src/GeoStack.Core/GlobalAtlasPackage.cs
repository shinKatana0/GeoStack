// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace GeoStack.Core;

public sealed record GlobalAtlasManifest(int Schema,string Format,int MinZoom,int MaxZoom,double[] Bounds,string Source,string SourceETag,string DataDate,string Attribution,string Compression,string TileType,long TileCount);
public sealed record GlobalAtlasSelection(string Path,bool Enabled=true);

/// <summary>GeoStack's optional worldwide vector context format, separate from raster MBTiles.</summary>
public sealed class GlobalAtlasPackage : IDisposable
{
    private readonly ReadOnlySqlite database;
    public GlobalAtlasManifest Manifest {get;}
    public string Path {get;}
    public long Size=>new FileInfo(Path).Length;
    public GlobalAtlasPackage(string path,bool validate=false)
    {
        Path=System.IO.Path.GetFullPath(path);database=new(Path);
        try
        {
            var rows=database.Rows("SELECT value FROM metadata WHERE key='manifest' AND length(value)<65536");
            Manifest=rows.Count==1?JsonSerializer.Deserialize<GlobalAtlasManifest>(rows[0][0])??throw new InvalidDataException("Global atlas manifest missing"):throw new InvalidDataException("Global atlas manifest missing");
            if(Manifest.Schema is not (1 or 2) || Manifest.Format!="GeoStackGlobalVector" || Manifest.MinZoom!=0 || Manifest.MaxZoom is <0 or >8 || Manifest.Compression!="gzip" || Manifest.TileType!=(Manifest.Schema==1?"MVT":"GSVC") || Manifest.Bounds is not {Length:4} || Manifest.Bounds[0]!=-180 || Manifest.Bounds[2]!=180 || Math.Abs(Manifest.Bounds[1]+85.05112878)>1e-5 || Math.Abs(Manifest.Bounds[3]-85.05112878)>1e-5)throw new InvalidDataException("Unsupported global atlas format");
            long expected=0;for(var z=0;z<=Manifest.MaxZoom;z++)expected+=1L<<(2*z);
            var counts=database.Rows("SELECT z,count(*),min(x),min(y),max(x),max(y) FROM tiles GROUP BY z ORDER BY z");
            if(counts.Count!=Manifest.MaxZoom+1 || Manifest.TileCount!=expected)throw new InvalidDataException("Incomplete global atlas");
            for(var z=0;z<counts.Count;z++)
            {var c=counts[z].Select(long.Parse).ToArray();if(c[0]!=z || c[1]!=(1L<<(2*z)) || c[2]!=0 || c[3]!=0 || c[4]!=(1<<z)-1 || c[5]!=(1<<z)-1)throw new InvalidDataException("Incomplete global atlas coordinates");}
            if(database.Rows("SELECT count(*) FROM tiles LEFT JOIN contents ON contents.id=tiles.content WHERE contents.id IS NULL").Single()[0]!="0")throw new InvalidDataException("Missing global tile contents");
            if(validate && database.Rows("PRAGMA quick_check").Single()[0]!="ok")throw new InvalidDataException("Corrupt global atlas");
        }
        catch{database.Dispose();throw;}
    }
    public byte[]? ReadTile(int z,int x,int y,CancellationToken token=default)
    {
        if(z<0 || z>Manifest.MaxZoom || x<0 || y<0 || x>=1<<z || y>=1<<z)return null;
        return database.Blob($"SELECT data FROM contents WHERE id=(SELECT content FROM tiles WHERE z={z} AND x={x} AND y={y})",token);
    }
    public void Dispose()=>database.Dispose();
}
