// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using GeoStack.Core;

namespace GeoStack.App;

public readonly record struct RasterTileKey(string Map,int Z,int X,int Y);
public sealed record TilePlacement(MapPackage Package,RasterTileKey Key,Rect Bounds,Rect Clip,double Priority) { public int WorldColumn { get; init; } }

public static class RasterTilePlan
{
    public const int Limit=128;
    public const int MaximumOverzoom=3;
    public static IReadOnlyList<TilePlacement> Build(IEnumerable<MapPackage> packages,string preferred,double world,double width,double height,Point center,Action? coverageCheck=null)
    {
        if(preferred=="world" || !double.IsFinite(world) || world<Math.Max(width,height)/4 || width<=0 || height<=0 || !double.IsFinite(center.X) || !double.IsFinite(center.Y))return [];
        var requested=Math.Clamp((int)Math.Floor(Math.Log2(world/256)),0,22);var result=new List<TilePlacement>();
        foreach(var map in packages.Where(p=>p.Enabled && requested>=p.MinZoom && requested<=p.MaxZoom+MaximumOverzoom).OrderByDescending(p=>p.Id==preferred).ThenByDescending(p=>p.MaxZoom).Take(32))
        {
            var z=Math.Min(requested,map.MaxZoom);var n=1<<z;var pixels=world/n;
            var west=(map.West+180)/360;var east=(map.East+180)/360;if(east<west)east+=1;
            var north=Geo.Project(map.North,0).Y;var south=Geo.Project(map.South,0).Y;
            var x0=(int)Math.Floor((center.X-width/world/2)*n);var x1=(int)Math.Floor((center.X+width/world/2)*n);
            var y0=Math.Max(0,(int)Math.Floor((center.Y-height/world/2)*n));var y1=Math.Min(n-1,(int)Math.Floor((center.Y+height/world/2)*n));
            if(x1-x0>64 || y1-y0>64)continue;
            for(var x=x0;x<=x1;x++)for(var y=y0;y<=y1;y++)
            {
                var tx=(x%n+n)%n;coverageCheck?.Invoke();if(!map.IntersectsTileBounds(z,tx,y))continue;
                var tile=new Rect(x/(double)n,y/(double)n,1d/n,1d/n);
                var wrap=Math.Round(tile.X+tile.Width/2-(west+east)/2);
                var coverage=new Rect(west+wrap,north,east-west,south-north);var clip=Rect.Intersect(tile,coverage);
                if(clip.IsEmpty || clip.Width<=0 || clip.Height<=0)continue;
                Rect Screen(Rect r)=>new((r.X-center.X)*world+width/2,(r.Y-center.Y)*world+height/2,r.Width*world,r.Height*world);
                var bounds=Screen(tile);var clipped=Screen(clip);clipped.Intersect(new Rect(0,0,width,height));if(clipped.IsEmpty)continue;
                var priority=(map.Id==preferred?1e12:0)+map.MaxZoom*1e9-Math.Pow(bounds.X+bounds.Width/2-width/2,2)-Math.Pow(bounds.Y+bounds.Height/2-height/2,2);
                result.Add(new(map,new(map.Id,z,tx,y),bounds,clipped,priority) {WorldColumn=x});
            }
        }
        return result.OrderByDescending(p=>p.Priority).Take(Limit).OrderBy(p=>p.Package.Id==preferred).ThenBy(p=>p.Package.MaxZoom).ToArray();
    }
}
