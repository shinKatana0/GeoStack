// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

/// <summary>Frozen coverage bands keep coarser real geography outside higher-detail source footprints.</summary>
public static class RegionalLodCoverage
{
    public static Dictionary<(int Target,int Level),Geometry> Build(IReadOnlyDictionary<int,Rect> bounds)
    {
        var result=new Dictionary<(int,int),Geometry>();
        if(bounds.Count>5 || bounds.Any(p=>p.Key is not (0 or 8 or 10 or 12 or 15) || !double.IsFinite(p.Value.X+p.Value.Y+p.Value.Width+p.Value.Height) || p.Value.IsEmpty || p.Value.Width<=0 || p.Value.Height<=0 || p.Value.X<0 || p.Value.Y<0 || p.Value.Right>1 || p.Value.Bottom>1))throw new InvalidDataException("Invalid regional LOD coverage");
        foreach(var target in bounds.Keys)foreach(var level in bounds.Keys.Where(l=>l<=target))
        {
            Geometry mask=new RectangleGeometry(bounds[level]);
            foreach(var higher in bounds.Where(p=>p.Key>level && p.Key<=target))mask=Geometry.Combine(mask,new RectangleGeometry(higher.Value),GeometryCombineMode.Exclude,null,1e-10,ToleranceType.Absolute);
            mask.Freeze();result[(target,level)]=mask;
        }
        return result;
    }
    internal static Dictionary<(int Target,int Level),Geometry> Load(string id,string hash)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"geography/atlas-lod-coverage.json");
        if(!File.Exists(path))return [];
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
        if(root.GetProperty("Schema").GetInt32()!=1 || !root.GetProperty("Regions").TryGetProperty(id,out var region) || !region.GetProperty("PackageSHA256").GetString()!.Equals(hash,StringComparison.OrdinalIgnoreCase))return [];
        var bounds=region.GetProperty("Bounds").EnumerateObject().ToDictionary(p=>int.Parse(p.Name,System.Globalization.CultureInfo.InvariantCulture),p=>{var a=p.Value.EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(a.Length!=4)throw new InvalidDataException("Invalid LOD bounds");return new Rect(new Point(a[0],a[1]),new Point(a[2],a[3]));});
        return Build(bounds);
    }
    public static bool IsContext(MapPackage map)=>map.MinZoom<=8 && map.MaxZoom is >=8 and <=12 && (map.East>=map.West?map.East-map.West:360+map.East-map.West)<=30;
}
