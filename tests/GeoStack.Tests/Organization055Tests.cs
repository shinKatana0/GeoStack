// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;
public sealed class Organization055Tests
{
    private static string Root=>Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory);
    private static GeographicDataset[] Packages()=>Directory.GetFiles(Path.Combine(Root,"tests/GeoStack.Tests/Fixtures/Geography"),"*.gsgeo").Concat(Directory.GetFiles(Path.Combine(Root,"tests/GeoStack.Tests/Fixtures/Geography055"),"*.gsgeo")).Select(GeographicPackage.Inspect).ToArray();
    [Theory]
    [InlineData(36.5781,136.6477,"Japan","Kanazawa")]
    [InlineData(34.9858,135.7588,"Japan","Kyoto")]
    [InlineData(35.6812,139.7671,"Japan","Tokyo")]
    [InlineData(59.9343,30.3351,"Russia","Saint Petersburg")]
    [InlineData(31.2304,121.4737,"China","Shanghai")]
    [InlineData(47.3782,8.5402,"Switzerland","Zurich")]
    [InlineData(52.2297,21.0122,"Poland","Warsaw")]
    public async Task RealContainmentBuildsCountryCityWithoutFilenameInference(double lat,double lon,string country,string locality)
    {
        var root=TestStorage.Create(Root);var file=Path.Combine(root,"unrelated-name.jpg");await File.WriteAllTextAsync(file,"Disposable geographic proposal fixture",TestContext.Current.CancellationToken);
        var packages=Packages();var resolver=new ReverseGeocoder(Path.Combine(Root,"assets/geography/countries.geojson"),datasets:()=>packages);
        var row=Assert.Single(await GeographicCopy.Plan([new(file,lat,lon,"Photo")],resolver,TestStorage.Create(Root),"en-US","CountryCity",false,TestContext.Current.CancellationToken));
        Assert.Equal(Path.Combine(country,locality),row.RelativeDirectory);Assert.NotEmpty(row.GeographicIds);
        if(locality=="Tokyo")Assert.Contains("GeoRegionFallback",row.GeographicWarnings);
        if(locality=="Saint Petersburg")Assert.Contains("GeoDatasetOutdated",row.GeographicWarnings);
        else Assert.DoesNotContain("GeoDatasetOutdated",row.GeographicWarnings);
    }
    [Fact] public void PackageActivationInvalidatesCountryOnlyCache()
    {
        var catalog=new GeographicCatalog(TestStorage.Create(Root));var resolver=new ReverseGeocoder(Path.Combine(Root,"assets/geography/countries.geojson"),datasets:()=>catalog.Datasets);
        Assert.Null(resolver.Resolve(36.5781,136.6477).City);var package=Packages().Single(d=>d.Manifest.Id.Contains("jp-selected"));catalog.Import(package);Assert.Equal("Kanazawa",resolver.Resolve(36.5781,136.6477).City?.Display("en-US"));
        catalog.Datasets[0]=package with{Enabled=false};catalog.Save();Assert.Null(resolver.Resolve(36.5781,136.6477).City);
    }
    [Fact] public void MissingCityRemainsExplicitAndHistoricFederalCityHasRealPolygon()
    {
        var packages=Packages();var resolver=new ReverseGeocoder(Path.Combine(Root,"assets/geography/countries.geojson"),datasets:()=>packages);
        var missing=resolver.Resolve(61,70);Assert.Null(missing.City);Assert.Contains("GeoCountryFallback",missing.Warnings);
        var location=resolver.Resolve(59.9343,30.3351);Assert.Equal("geoboundaries:RUS-ADM1-28173009:RU-SPE",location.City?.Id);Assert.Equal("Russia",location.Country?.Display("en-US"));
    }
}
