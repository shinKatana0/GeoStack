// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.Core;

namespace GeoStack.App;

public sealed record RasterFramePlan(MapPackage Package,IReadOnlyList<TilePlacement> Tiles,string Key,int Zoom,int Left,int Top,int Columns,int Rows);
public sealed record RasterFrame(RasterFramePlan Plan,BitmapSource Image)
{
    public Rect WorldBounds=>new(Plan.Left/Math.Pow(2,Plan.Zoom),Plan.Top/Math.Pow(2,Plan.Zoom),Plan.Columns/Math.Pow(2,Plan.Zoom),Plan.Rows/Math.Pow(2,Plan.Zoom));
    public Rect Screen(double world,double width,double height,Point center)
    {
        var b=WorldBounds;var wrap=Math.Round(center.X-(b.X+b.Width/2));
        return new((b.X+wrap-center.X)*world+width/2,(b.Y-center.Y)*world+height/2,b.Width*world,b.Height*world);
    }
    public Rect CoverageScreen(double world,double width,double height,Point center)
    {
        var screen=Screen(world,width,height,center);var map=Plan.Package;
        if(map.East-map.West>=359.999)return screen;
        var west=(map.West+180)/360;var east=(map.East+180)/360;if(east<west)east+=1;
        var wrap=Math.Round(WorldBounds.X+WorldBounds.Width/2-(west+east)/2);west+=wrap;
        var north=Geo.Project(map.North,0).Y;var south=Geo.Project(map.South,0).Y;
        return Rect.Intersect(screen,new Rect(screen.X+(west-WorldBounds.X)*world,screen.Y+(north-WorldBounds.Y)*world,(east-(map.West+180)/360)*world,(south-north)*world));
    }
}

/// <summary>Choose one complete geographic surface; never mix independently arriving resolution patches.</summary>
public static class RasterComposition
{
    public const long PixelBudget=64L*1024*1024;
    public static bool CoversViewport(MapPackage map,double world,double width,double height,Point center)
    {
        if(world<=0 || !double.IsFinite(world))return false;
        var west=(map.West+180)/360;var east=(map.East+180)/360;if(east<west)east+=1;
        if(east-west>=1-1e-12 && map.South<=-85 && map.North>=85)return true;
        var wrap=Math.Round(center.X-(west+east)/2);west+=wrap;east+=wrap;
        return center.X-width/world/2>=west-1e-12 && center.X+width/world/2<=east+1e-12 && center.Y-height/world/2>=Geo.Project(map.North,0).Y-1e-12 && center.Y+height/world/2<=Geo.Project(map.South,0).Y+1e-12;
    }
    public static IReadOnlyList<RasterFramePlan> Plans(IEnumerable<MapPackage> packages,string preferred,double world,double width,double height,Point center,double padding=0,bool nativeOnly=false)
    {
        if(preferred=="world" || world<=0 || !double.IsFinite(world))return [];
        var requested=Math.Clamp((int)Math.Floor(Math.Log2(world/256)),0,22);
        var candidates=packages.Where(m=>m.Enabled && requested>=m.MinZoom && requested<=m.MaxZoom+(nativeOnly?0:RasterTilePlan.MaximumOverzoom) && CoversViewport(m,world,width,height,center))
            .OrderByDescending(m=>m.Id==preferred).ThenByDescending(m=>m.MaxZoom).ThenBy(m=>m.Id,StringComparer.Ordinal).Take(32).ToArray();
        var result=new List<RasterFramePlan>();var used=0;
        foreach(var map in candidates)
        {
            if(result.Count>0 && map.MaxZoom>=result[0].Package.MaxZoom)continue;
            var tiles=RasterTilePlan.Build([map],map.Id,world,width+padding*2,height+padding*2,center);if(tiles.Count==0 || used+tiles.Count>RasterTilePlan.Limit)continue;
            var z=tiles[0].Key.Z;var n=1<<z;
            int Column(TilePlacement t)=>t.WorldColumn;
            var left=tiles.Min(Column);var right=tiles.Max(Column);var top=tiles.Min(t=>t.Key.Y);var bottom=tiles.Max(t=>t.Key.Y);
            var columns=right-left+1;var rows=bottom-top+1;
            if((long)columns*rows*256*256*4>PixelBudget || tiles.Count!=columns*rows)continue;
            var key=$"{map.Id}:{z}:{left}:{top}:{columns}:{rows}";
            result.Add(new(map,tiles,key,z,left,top,columns,rows));used+=tiles.Count;if(result.Count==2)break;
        }
        return result;
    }
    public static RasterFrame Assemble(RasterFramePlan plan,IReadOnlyList<BitmapSource> images,CancellationToken token)
    {
        using var probe=AtlasProfiling.Measure("Raster.PrepareFrame.Background");
        if(images.Count!=plan.Tiles.Count)throw new InvalidDataException("Incomplete raster frame");
        var bitmap=new WriteableBitmap(plan.Columns*256,plan.Rows*256,96,96,PixelFormats.Pbgra32,null);var buffer=new byte[256*256*4];
        // RasterTilePlan orders tiles deterministically by coverage priority, so recover columns from the wrapped key.
        var n=1<<plan.Zoom;
        for(var i=0;i<plan.Tiles.Count;i++)
        {
            token.ThrowIfCancellationRequested();var tile=plan.Tiles[i];var column=tile.WorldColumn;
            BitmapSource source=images[i];if(source.Format!=PixelFormats.Pbgra32) { source=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);source.Freeze(); }
            source.CopyPixels(buffer,256*4,0);bitmap.WritePixels(new Int32Rect((column-plan.Left)*256,(tile.Key.Y-plan.Top)*256,256,256),buffer,256*4,0);
        }
        bitmap.Freeze();return new(plan,bitmap);
    }
    public static int NativeZoomAt(IEnumerable<MapPackage> maps,string preferred,double latitude,double longitude)=>preferred=="world"?12:maps.Where(m=>m.CoversBounds(m.MaxZoom,latitude,longitude)).Select(m=>m.MaxZoom).DefaultIfEmpty(12).Max();
}
