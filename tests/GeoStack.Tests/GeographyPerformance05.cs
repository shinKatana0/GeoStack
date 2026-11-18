// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GeoStack.Core;
using Xunit;
namespace GeoStack.Tests;
public sealed class GeographyPerformance05
{
    [Fact(Explicit=true)] public void MeasureDistinctCoordinatesAndOptionalBoundaries()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;var countries=Path.Combine(root,"assets/geography/countries.geojson");var rows=new List<object>();
        var coarse=new ReverseGeocoder(countries);var timer=Stopwatch.StartNew();
        for(var i=0;i<10000;i++)coarse.Resolve(47.37+i*.00000001,8.53);
        rows.Add(new{Stage="10000DistinctCoarse",Milliseconds=timer.Elapsed.TotalMilliseconds,coarse.CoordinateCacheCount,coarse.CoordinateCacheHits});
        timer.Restart();for(var i=0;i<10000;i++)coarse.Resolve(47.37+i*.00000001,8.53);
        rows.Add(new{Stage="10000RepeatedCoarse",Milliseconds=timer.Elapsed.TotalMilliseconds,coarse.CoordinateCacheCount,coarse.CoordinateCacheHits});
        var datasets=Directory.GetFiles(Path.Combine(root,"dist/GeoStack-geographic-data-0.4"),"*.gsgeo").Select(GeographicPackage.Inspect).ToArray();
        var detailed=new ReverseGeocoder(countries,null,()=>datasets);var cities=new[]{(35.6812,139.7671),(34.9858,135.7588),(36.5781,136.6477),(31.2304,121.4737),(47.3782,8.5402),(52.2297,21.0122)};
        timer.Restart();using(var batch=detailed.BeginBatch())for(var i=0;i<1000;i++){TestContext.Current.CancellationToken.ThrowIfCancellationRequested();var p=cities[i%6];detailed.Resolve(p.Item1+(i/6)*.0000001,p.Item2);}
        rows.Add(new{Stage="1000DistinctWithFourRealBoundaryPackages",Milliseconds=timer.Elapsed.TotalMilliseconds,detailed.CoordinateCacheCount,detailed.CachedGeometryBytes,detailed.GeographicMeasurements});
        timer.Restart();using(var batch=detailed.BeginBatch())for(var i=1000;i<10000;i++){TestContext.Current.CancellationToken.ThrowIfCancellationRequested();var p=cities[i%6];detailed.Resolve(p.Item1+(i/6)*.0000001,p.Item2);}
        rows.Add(new{Stage="Next9000DistinctRealBoundaryLocations",Milliseconds=timer.Elapsed.TotalMilliseconds,detailed.CoordinateCacheCount,detailed.CachedGeometryBytes});
        timer.Restart();using(var batch=detailed.BeginBatch())for(var i=0;i<10000;i++){var p=cities[i%6];detailed.Resolve(p.Item1+(i/6)*.0000001,p.Item2);}
        rows.Add(new{Stage="10000RepeatedRealBoundaryLocations",Milliseconds=timer.Elapsed.TotalMilliseconds,detailed.CoordinateCacheHits,detailed.ActiveGeographicReaders});
        File.WriteAllText(Path.Combine(root,".local/geographic05-performance.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Assert.Equal(10000,detailed.CoordinateCacheCount);
    }
}
