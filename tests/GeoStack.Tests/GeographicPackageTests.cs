// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class GeographicPackageTests
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    private static string Fixture(string name) => Path.Combine(Root,".local/geographic-fixtures",name+".gsgeo");
    private static string Temporary() { var path=TestStorage.Create(Root);Directory.CreateDirectory(path);return path; }
    private static ReverseGeocoder Resolver(GeographicDataset dataset) => new(Path.Combine(Temporary(),"missing.geojson"), datasets:()=>[dataset]);
    [Fact] public void PackageUsesNativeRtreeAndExplicitHierarchy()
    {
        var dataset=GeographicPackage.Inspect(Fixture("topology")); Assert.Equal(5,dataset.Manifest.FeatureCount);
        var resolver=Resolver(dataset);var location=resolver.Resolve(2,2);
        Assert.Equal("synthetic-country",location.Country?.Id);Assert.Equal("synthetic-region",location.Region?.Id);Assert.Equal("synthetic-west",location.City?.Id);Assert.Equal("synthetic-district",location.District?.Id);
        Assert.Equal(new[]{"synthetic-country-en","synthetic-west-en","synthetic-district-en"},location.Hierarchy("en-US","CountryCityDistrict"));
        Assert.Equal(new[]{"synthetic-country-ru","synthetic-region-ru","synthetic-west-ru"},location.Hierarchy("ru-RU","CountryRegionCity"));
        Assert.True(resolver.LastCandidateCount<=4);Assert.InRange(resolver.CachedGeometryBytes,1,32*1024*1024);
    }
    [Theory][InlineData(5,5,false)][InlineData(2,2,true)][InlineData(3,5,false)]
    public void HoleAndHoleEdgeExcludeDistrict(double latitude,double longitude,bool district)
    {
        var location=Resolver(GeographicPackage.Inspect(Fixture("topology"))).Resolve(latitude,longitude);Assert.Equal(district,location.District!=null);Assert.NotNull(location.City);
        if(latitude==3)Assert.Contains("GeoBorderUncertain",location.Warnings);
    }
    [Theory][InlineData(5,9.99,"synthetic-west")][InlineData(5,10.01,"synthetic-east")][InlineData(5,10,null)]
    public void AdjacentCitiesAndExactBordersRemainConservative(double latitude,double longitude,string? city)
    {
        var location=Resolver(GeographicPackage.Inspect(Fixture("topology"))).Resolve(latitude,longitude);Assert.Equal(city,location.City?.Id);
        if(city==null){Assert.Contains("GeoBorderUncertain",location.Warnings);Assert.Contains("GeoRegionFallback",location.Warnings);}
    }
    [Fact] public void OverlappingCitiesFallBackWithoutChoosingSmallestOrNearest()
    {
        var location=Resolver(GeographicPackage.Inspect(Fixture("overlap"))).Resolve(5,8);Assert.Null(location.City);Assert.NotNull(location.Region);Assert.Null(location.District);Assert.Contains("GeoAmbiguousBoundary",location.Warnings);
    }
    [Fact] public void NoCityGeometryExposesRegionFallback()
    {
        var location=Resolver(GeographicPackage.Inspect(Fixture("topology"))).Resolve(25,25);Assert.Null(location.City);Assert.Contains("GeoRegionFallback",location.Warnings);Assert.Equal(new[]{"synthetic-country-en","synthetic-region-en"},location.Hierarchy("en-US","CountryCity"));
    }
    [Fact] public void CoordinateCacheInvalidatesWhenPackageVersionChangesInPlace()
    {
        var path=Path.Combine(Temporary(),"mutable.gsgeo");File.Copy(Fixture("topology"),path);
        var dataset=GeographicPackage.Inspect(path);var resolver=Resolver(dataset);var first=resolver.Resolve(2,2);Assert.NotNull(first.City);resolver.Resolve(2,2);Assert.Equal(1,resolver.CoordinateCacheHits);
        File.Copy(Fixture("updated"),path,true);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(3));
        var updated=resolver.Resolve(2,2);Assert.Null(updated.City);Assert.NotEqual(first.DatasetVersion,updated.DatasetVersion);
    }
    [Fact] public void CatalogPersistenceEnableDisableAndMissingReferencesDoNotDeleteSources()
    {
        var directory=Temporary();var dataset=GeographicPackage.Inspect(Fixture("topology"));var catalog=new GeographicCatalog(directory);catalog.Import(dataset);
        var restarted=new GeographicCatalog(directory);Assert.Equal(dataset.Sha256,Assert.Single(restarted.Datasets).Sha256);
        var resolver=new ReverseGeocoder(Path.Combine(directory,"missing.geojson"),datasets:()=>restarted.Datasets);Assert.NotNull(resolver.Resolve(2,2).City);
        restarted.Datasets[0]=dataset with {Enabled=false};Assert.Null(resolver.Resolve(2,2).City);
        restarted.Datasets[0]=dataset with {Path=Path.Combine(directory,"disconnected.gsgeo")};var fallback=resolver.Resolve(2,2);Assert.Null(fallback.City);Assert.Contains("GeoPackageUnavailable",fallback.Warnings);
        restarted.Datasets.Clear();restarted.Save();Assert.True(File.Exists(dataset.Path));Assert.Empty(new GeographicCatalog(directory).Datasets);
    }
    [Fact] public void CorruptPackageIsRejectedAndResolverSurvivesExternalCorruption()
    {
        Assert.Throws<InvalidDataException>(()=>GeographicPackage.Inspect(Fixture("corrupt")));
        var path=Path.Combine(Temporary(),"mutable.gsgeo");File.Copy(Fixture("topology"),path);var dataset=GeographicPackage.Inspect(path);var resolver=Resolver(dataset);Assert.NotNull(resolver.Resolve(2,2).City);
        File.WriteAllText(path,"corrupt");var result=resolver.Resolve(2,2);Assert.Null(result.City);Assert.Contains("GeoPackageUnavailable",result.Warnings);
    }
    [Theory][InlineData(179,true)][InlineData(-179,true)][InlineData(0,false)][InlineData(170,false)]
    public void AntimeridianPolygonDoesNotCoverGreenwich(double longitude,bool inside)
    {
        var geometry=new BoundaryGeometry("""{"type":"Polygon","coordinates":[[[170,-10],[-170,-10],[-170,10],[170,10],[170,-10]]]}""");
        Assert.Equal(inside,geometry.Contains(0,longitude)==BoundaryMatch.Inside);Assert.Equal(2,geometry.Bounds().Count());
        if(longitude==170)Assert.Equal(BoundaryMatch.Edge,geometry.Contains(0,longitude));
    }
    [Fact] public void MultiPolygonAndDatelineHoleUseTheSameContainmentRules()
    {
        var geometry=new BoundaryGeometry("""{"type":"MultiPolygon","coordinates":[[[[170,-10],[-170,-10],[-170,10],[170,10],[170,-10]],[[175,-5],[-175,-5],[-175,5],[175,5],[175,-5]]],[[[10,10],[20,10],[20,20],[10,20],[10,10]]]]}""");
        Assert.Equal(BoundaryMatch.Outside,geometry.Contains(0,179));Assert.Equal(BoundaryMatch.Inside,geometry.Contains(8,-179));Assert.Equal(BoundaryMatch.Inside,geometry.Contains(15,15));
    }
    [Fact] public void NativeIndexFindsBothSidesOfTheDateLineAndExcludesGreenwich()
    {
        var resolver=Resolver(GeographicPackage.Inspect(Fixture("dateline")));
        Assert.Equal("synthetic-dateline-city",resolver.Resolve(0,179).City?.Id);Assert.Equal("synthetic-dateline-city",resolver.Resolve(0,-179).City?.Id);
        Assert.Equal(resolver.Resolve(0,-180).City?.Id,resolver.Resolve(0,180).City?.Id);Assert.Null(resolver.Resolve(0,0).Country);
    }
    [Fact] public void NestedDistrictsRequireSourceAncestryRatherThanAreaOrdering()
    {
        var location=Resolver(GeographicPackage.Inspect(Fixture("nested"))).Resolve(2,2);Assert.Equal("synthetic-inner-district",location.District?.Id);Assert.DoesNotContain("GeoAmbiguousBoundary",location.Warnings);
    }
    [Fact] public void ReimportWithIdenticalVersionSizeAndTimestampStillInvalidatesCachedLocations()
    {
        var directory=Temporary();var a=Path.Combine(directory,"a.gsgeo");var b=Path.Combine(directory,"b.gsgeo");File.Copy(Fixture("topology"),a);File.Copy(Fixture("same-version-updated"),b);
        File.SetLastWriteTimeUtc(b,File.GetLastWriteTimeUtc(a));var packages=new List<GeographicDataset>{GeographicPackage.Inspect(a)};
        var resolver=new ReverseGeocoder(Path.Combine(directory,"missing.geojson"),datasets:()=>packages);Assert.NotNull(resolver.Resolve(2,2).City);
        var replacement=GeographicPackage.Inspect(b);Assert.Equal(packages[0].Size,replacement.Size);Assert.Equal(packages[0].ModifiedTicks,replacement.ModifiedTicks);Assert.Equal(packages[0].Manifest.Version,replacement.Manifest.Version);
        packages[0]=replacement;Assert.Null(resolver.Resolve(2,2).City);
    }
}
