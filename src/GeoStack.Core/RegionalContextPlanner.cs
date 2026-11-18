// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;

namespace GeoStack.Core;

/// <summary>Optional bounded regional context accompanying the existing local-detail plan. No network access.</summary>
public static class RegionalContextPlanner
{
    // Covers a 955-DIP viewport at zoom 8 near 60 degrees latitude; still subject to TileLimit.
    public const double ContextRadiusKm=160;
    public static MapDownloadPlan Extend(MapDownloadPlan detail,IReadOnlyList<MapPackage> packages,CancellationToken token,Func<byte[],bool>? validate=null)
    {
        var result=detail.Areas.ToList();var identities=new HashSet<string>();
        var readers=new BoundedLru<string,ReadOnlySqlite>(8);
        try
        {
            foreach(var area in detail.Areas)
            {
                token.ThrowIfCancellationRequested();
                if(!area.Supported)continue;
                var a=Geo.Project(area.South,area.West);var b=Geo.Project(area.North,area.East);
                var cx=area.ContextX??(a.X+Geo.WrappedDelta(b.X-a.X)/2);var cy=area.ContextY??((a.Y+b.Y)/2);
                var lat=Geo.Unproject(cx,cy).Latitude;var pad=ContextRadiusKm/(40075*Math.Max(.17,Math.Cos(lat*Math.PI/180)));
                var west=Geo.WrapLongitude((cx-pad)*360-180);var east=Geo.WrapLongitude((cx+pad)*360-180);
                var north=Geo.Unproject(cx,Math.Clamp(cy-pad,0,1)).Latitude;var south=Geo.Unproject(cx,Math.Clamp(cy+pad,0,1)).Latitude;
                var tiles=new List<DownloadTile>();
                for(var z=8;z<=10;z++)
                {
                    var n=1<<z;var left=Math.Clamp((int)Math.Floor((west+180)/360*n),0,n-1);var right=Math.Clamp((int)Math.Floor((east+180)/360*n),0,n-1);
                    var top=Math.Clamp((int)Math.Floor(Geo.Project(north,0).Y*n),0,n-1);var bottom=Math.Clamp((int)Math.Floor(Geo.Project(south,0).Y*n),0,n-1);
                    var xs=west<=east?Enumerable.Range(left,right-left+1):Enumerable.Range(left,n-left).Concat(Enumerable.Range(0,right+1));
                    foreach(var x in xs)for(var y=top;y<=bottom;y++)tiles.Add(new(z,x,y));
                }
                var identity=FormattableString.Invariant($"{west:F5},{south:F5},{east:F5},{north:F5}:8:10");
                var id="region-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
                if(!identities.Add(id))continue;
                bool Covered(DownloadTile tile)
                {
                    foreach(var map in packages.SelectMany(p=>new[]{p}.Concat(p.Variants??[])))
                    {
                        token.ThrowIfCancellationRequested();
                        if(map.MinZoom>tile.Zoom || map.MaxZoom<tile.Zoom || map.Path.StartsWith(@"\\",StringComparison.Ordinal))continue;
                        try
                        {
                            if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(map.Path))!).DriveType==DriveType.Network)continue;
                            if(!readers.TryGet(map.Path,out var db)){db=new ReadOnlySqlite(map.Path);readers.Set(map.Path,db)?.Dispose();}
                            var bytes=db.Blob(FormattableString.Invariant($"SELECT tile_data FROM tiles WHERE zoom_level={tile.Zoom} AND tile_column={tile.X} AND tile_row={MapPackage.TmsRow(tile.Zoom,tile.Y)} LIMIT 1"),token);
                            if(bytes is {Length:>8} && (bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}) || bytes.AsSpan().StartsWith(new byte[]{255,216,255})) && (validate==null || validate(bytes)))return true;
                        }
                        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException){ }
                    }
                    return false;
                }
                var installed=tiles.All(Covered);
                result.Add(new(id,area.Place,area.MediaCount,installed?0:area.MediaCount,west,south,east,north,installed,tiles.Count<=LibraryMapPlanner.TileLimit,area.Isolated,tiles){IsContext=true});
            }
        }
        finally{foreach(var db in readers.Values)db.Dispose();}
        return detail with{Areas=result};
    }
}
