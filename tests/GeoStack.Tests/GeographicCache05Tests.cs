// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.Core;
using Xunit;
namespace GeoStack.Tests;
public sealed class GeographicCache05Tests
{
    [Fact] public void TenThousandCoordinatesReuseResultsWithoutWholesaleCacheClearing()
    {
        var resolver=new ReverseGeocoder(Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local/nonexistent-country-fixture"));
        var first=resolver.Resolve(2,3);
        for(var i=1;i<10000;i++)resolver.Resolve(2+i*.000001,3);
        Assert.Equal(10000,resolver.CoordinateCacheCount);
        for(var i=0;i<10000;i++)resolver.Resolve(2+i*.000001,3);
        Assert.Equal(10000,resolver.CoordinateCacheHits);Assert.Same(first,resolver.Resolve(2,3));
        for(var i=10000;i<17000;i++)resolver.Resolve(2+i*.000001,3);
        Assert.InRange(resolver.CoordinateCacheCount,10000,16384);Assert.True(resolver.CoordinateCacheCount>8192);
    }
    [Fact] public void NestedLookupBatchesReleaseNativeReadersAndPreserveFeatureMetadata()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;var folder=Path.Combine(root,".local","geo-batch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"data.gsgeo");File.Copy(Path.Combine(root,"dist/GeoStack-geographic-data-0.4/ch-selected.gsgeo"),file);var data=GeographicPackage.Inspect(file);
            var resolver=new ReverseGeocoder(Path.Combine(root,"assets/geography/countries.geojson"),null,()=>[data]);
            using(var outer=resolver.BeginBatch())
            {
                using(var inner=resolver.BeginBatch()){Assert.NotNull(resolver.Resolve(47.3782,8.5402).City);Assert.Equal(1,resolver.ActiveGeographicReaders);}
                Assert.Equal(1,resolver.ActiveGeographicReaders);Assert.NotNull(resolver.Resolve(47.3783,8.5402).City);
            }
            var reader=new GeographicPackageReader();var feature=reader.Query(data,47.3782,8.5402).Features.First(f=>f.Ancestors.Length>0);var expected=feature.Ancestors[0];feature.Ancestors[0]="changed-by-consumer";Assert.Equal(expected,reader.Query(data,47.3783,8.5402).Features.Single(f=>f.Name.Id==feature.Name.Id).Ancestors[0]);
            Assert.Equal(0,resolver.ActiveGeographicReaders);File.Move(file,file+".moved");
            Assert.Contains("GeoPackageUnavailable",resolver.Resolve(47.3782,8.5402).Warnings);
        }
        finally{Directory.Delete(folder,true);}
    }
}
