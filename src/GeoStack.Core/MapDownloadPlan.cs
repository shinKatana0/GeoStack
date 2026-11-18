// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GeoStack.Core;

public readonly record struct DownloadTile(int Zoom,int X,int Y);
public sealed record MapDownloadArea(string Id,GeographicName? Place,int MediaCount,int MissingCount,double West,double South,double East,double North,bool Installed,bool Supported,bool Isolated,IReadOnlyList<DownloadTile> Tiles)
{
    public bool IsContext { get; init; }
    public double? ContextX { get; init; }
    public double? ContextY { get; init; }
    public string CoordinateLabel=>FormattableString.Invariant($"{(South+North)/2:F3}, {Geo.WrapLongitude(West+(East>=West?East-West:360+East-West)/2):F3}");
    public long MaximumInstalledBytes=>(long)Tiles.Count*4*(512*512*4+8192);
    public string Coverage=>FormattableString.Invariant($"{West:F4}, {South:F4} - {East:F4}, {North:F4}");
}
public sealed record MapDownloadPlan(IReadOnlyList<MapDownloadArea> Areas,int WithoutGps,int UnavailablePackages,int IsolatedLocations);

/// <summary>Offline-only planner; grouping is independent of on-screen thumbnail clusters.</summary>
public static class LibraryMapPlanner
{
    public const int TileLimit=512;
    public static MapDownloadPlan Build(IReadOnlyList<MediaItem> items,IReadOnlyList<MapPackage> packages,Func<double?,double?,GeographicLocation> resolve,CancellationToken token,double marginKm=2,Func<byte[],bool>? validateTile=null)
    {
        if(marginKm<.5 || marginKm>5)throw new ArgumentOutOfRangeException(nameof(marginKm));
        var readers=new BoundedLru<string,ReadOnlySqlite>(8);var eligible=new List<MapPackage>();
        var seen=new HashSet<string>();var failedReaders=new HashSet<string>();var checkedTiles=new BoundedLru<(string,int,int),bool>(65536);var unavailable=0;
        try
        {
            foreach(var package in packages)
            {
                token.ThrowIfCancellationRequested();
                if(package.MaxZoom<15 || !seen.Add(package.Id))continue;
                try
                {
                    if(string.IsNullOrWhiteSpace(package.Path))throw new ArgumentException("Invalid map reference");
                    if(package.Path.StartsWith(@"\\",StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(Path.GetFullPath(package.Path))!).DriveType==DriveType.Network){unavailable++;continue;}
                    var inspected=MapPackage.Inspect(package.Path);if(inspected.MaxZoom<15)continue;
                    using var db=new ReadOnlySqlite(package.Path);
                    if(db.Rows("PRAGMA quick_check(1)",token)[0][0]!="ok")throw new InvalidDataException();
                    eligible.Add(inspected with{Id=package.Id,Enabled=true});
                }
                catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException){unavailable++;}
            }
            bool Covered(MediaItem item)
            {
                var p=Geo.Project(item.Latitude!.Value,item.Longitude!.Value);var x=(int)Math.Floor(p.X*32768);var y=(int)Math.Floor(p.Y*32768);x=((x%32768)+32768)%32768;y=Math.Clamp(y,0,32767);
                foreach(var package in eligible.Where(m=>!failedReaders.Contains(m.Id) && m.CoversBounds(15,item.Latitude.Value,item.Longitude.Value)))
                {
                    var key=(package.Id,x,y);
                    if(!checkedTiles.TryGet(key,out var available))
                    {
                        try {
                        if(!readers.TryGet(package.Id,out var db)){db=new ReadOnlySqlite(package.Path);readers.Set(package.Id,db)?.Dispose();}
                        var bytes=db.Blob(FormattableString.Invariant($"SELECT tile_data FROM tiles WHERE zoom_level=15 AND tile_column={x} AND tile_row={32767-y} LIMIT 1"),token);
                        available=bytes is {Length:>8} && (bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}) || bytes.AsSpan().StartsWith(new byte[]{255,216,255}));if(available && validateTile!=null)available=validateTile(bytes!);checkedTiles.Set(key,available);
                        } catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException){failedReaders.Add(package.Id);unavailable++;if(readers.TryGet(package.Id,out var failed)){readers.Remove(package.Id);failed.Dispose();}available=false;}
                    }
                    if(available)return true;
                }
                return false;
            }
            var groups=new List<List<MediaItem>>();var cells=new Dictionary<(int,int),List<int>>();
            foreach(var item in items.Where(m=>m.HasLocation).DistinctBy(m=>Path.GetFullPath(m.Path),StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();var p=Geo.Project(item.Latitude!.Value,item.Longitude!.Value);var cell=((int)Math.Floor(p.X*2048),(int)Math.Floor(p.Y*2048));int? selected=null;
                for(var dx=-1;dx<=1 && selected==null;dx++)for(var dy=-1;dy<=1 && selected==null;dy++)
                foreach(var index in cells.GetValueOrDefault((((cell.Item1+dx)%2048+2048)%2048,cell.Item2+dy))??[])
                {var anchor=groups[index][0];var q=Geo.Project(anchor.Latitude!.Value,anchor.Longitude!.Value);if(Math.Abs(Geo.WrappedDelta(q.X-p.X))<.0003 && Math.Abs(q.Y-p.Y)<.0003){selected=index;break;}}
                if(selected==null){selected=groups.Count;groups.Add([]);if(!cells.TryGetValue(cell,out var bucket))cells[cell]=bucket=[];bucket.Add(selected.Value);}groups[selected.Value].Add(item);
            }
            var output=new List<MapDownloadArea>();
            void Area(List<MediaItem> group)
            {
                token.ThrowIfCancellationRequested();var missing=group.Where(m=>!Covered(m)).ToArray();var bounds=Geo.Bounds(missing.Length>0?missing:group)!.Value;
                var location=Geo.Unproject(bounds.X,bounds.Y);var padding=marginKm/(40075*Math.Max(.17,Math.Cos(location.Latitude*Math.PI/180)));
                var west=Geo.WrapLongitude((bounds.X-bounds.Width/2-padding)*360-180);var east=Geo.WrapLongitude((bounds.X+bounds.Width/2+padding)*360-180);
                var north=Geo.Unproject(bounds.X,Math.Clamp(bounds.Y-bounds.Height/2-padding,0,1)).Latitude;var south=Geo.Unproject(bounds.X,Math.Clamp(bounds.Y+bounds.Height/2+padding,0,1)).Latitude;
                var tiles=Tiles(west,south,east,north,TileLimit+1);
                if(tiles.Count>TileLimit && group.Count>1)
                {
                    var ordered=group.OrderBy(m=>bounds.Width>=bounds.Height?Geo.WrappedDelta(Geo.Project(m.Latitude!.Value,m.Longitude!.Value).X-bounds.X):Geo.Project(m.Latitude!.Value,m.Longitude!.Value).Y).ToArray();
                    Area(ordered.Take(ordered.Length/2).ToList());Area(ordered.Skip(ordered.Length/2).ToList());return;
                }
                var first=group[0];var name=resolve(first.Latitude,first.Longitude);var supported=tiles.Count<=TileLimit && group.All(m=>Math.Abs(m.Latitude!.Value)<80);
                var identity=FormattableString.Invariant($"{west:F5},{south:F5},{east:F5},{north:F5}:10:15");var id="library-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
                var completeBounds=Geo.Bounds(group)!.Value;
                output.Add(new(id,name.City??name.Region??name.Country,group.Count,missing.Length,west,south,east,north,missing.Length==0,supported,items.Count>=20 && group.Count==1,tiles){ContextX=completeBounds.X,ContextY=completeBounds.Y});
            }
            foreach(var group in groups)Area(group);
            return new(output.OrderByDescending(a=>a.MediaCount).ToArray(),items.Count(m=>!m.HasLocation),unavailable,output.Count(a=>a.Isolated));
        }
        finally{foreach(var db in readers.Values)db.Dispose();}
    }
    public static IReadOnlyList<DownloadTile> Tiles(double west,double south,double east,double north,int limit=TileLimit+1)
    {
        var result=new List<DownloadTile>();
        for(var z=10;z<=15;z++)
        {
            var n=1<<z;var y0=Math.Clamp((int)Math.Floor(Geo.Project(north,0).Y*n),0,n-1);var y1=Math.Clamp((int)Math.Floor(Geo.Project(south,0).Y*n),0,n-1);var left=Math.Clamp((int)Math.Floor((west+180)/360*n),0,n-1);var right=Math.Clamp((int)Math.Floor((east+180)/360*n),0,n-1);
            var xs=west<=east?Enumerable.Range(left,right-left+1):Enumerable.Range(left,n-left).Concat(Enumerable.Range(0,right+1));
            foreach(var x in xs)for(var y=y0;y<=y1;y++){result.Add(new(z,x,y));if(result.Count>=limit)return result;}
        }
        return result;
    }
}
