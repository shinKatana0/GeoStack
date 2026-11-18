// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class RealGeographicTests
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    private static string Bundle => Path.Combine(Root,"tests/GeoStack.Tests/Fixtures/Geography");
    private static ReverseGeocoder Resolver() { var packages=Directory.GetFiles(Bundle,"*.gsgeo").Select(GeographicPackage.Inspect).ToArray();return new(Path.Combine(AppContext.BaseDirectory,"geography/countries.geojson"),datasets:()=>packages); }
    private static string Temporary() {var directory=TestStorage.Create(Root);Directory.CreateDirectory(directory);return directory;}
    [Theory]
    [InlineData("tokyo-station",null)]
    [InlineData("kyoto-station","Kyoto")]
    [InlineData("kanazawa-station","Kanazawa")]
    [InlineData("shanghai-peoples-square","Shanghai")]
    [InlineData("zurich-hauptbahnhof","Zurich")]
    [InlineData("warsaw-central-station","Warsaw")]
    public void RealOriginalPolygonsAndIndependentShapelyEvidenceAgreeWithNativeLookup(string target,string? cityName)
    {
        using var proof=JsonDocument.Parse(File.ReadAllText(Path.Combine(Bundle,"source-containment-evidence.json")));
        var point=proof.RootElement.EnumerateArray().Single(p=>p.GetProperty("expected").GetProperty("Id").GetString()==target);
        var expected=point.GetProperty("expected");var latitude=expected.GetProperty("Latitude").GetDouble();var longitude=expected.GetProperty("Longitude").GetDouble();var location=Resolver().Resolve(latitude,longitude);
        Assert.Equal(expected.GetProperty("CountryId").GetString(),location.Country?.Id);Assert.Equal(expected.GetProperty("RegionId").GetString(),location.Region?.Id);Assert.Equal(expected.GetProperty("CityId").GetString(),location.City?.Id);Assert.Equal(expected.GetProperty("DistrictId").GetString(),location.District?.Id);Assert.Equal(cityName,location.City?.Display("en-US"));
        if(cityName==null){Assert.Equal("Tokyo",location.Region?.Display("en-US"));Assert.Equal("Chiyoda",location.District?.Display("en-US"));Assert.Contains("GeoRegionFallback",location.Warnings);}
        var ids=point.GetProperty("sourceContainment").EnumerateArray().SelectMany(p=>p.GetProperty("originalSourceAreaIdsContainingPoint").EnumerateArray().Select(i=>i.GetString()!)).ToHashSet();Assert.NotEmpty(ids);
        using var file=File.OpenRead(Path.Combine(Bundle,"original-area-fixtures.geojsonseq.gz"));using var gzip=new GZipStream(file,CompressionMode.Decompress);using var input=new StreamReader(gzip);var matched=new HashSet<string>();
        while(input.ReadLine() is { } line)
        {
            using var original=JsonDocument.Parse(line);var id=original.RootElement.GetProperty("properties").GetProperty("id").GetString()!;
            if(!ids.Contains(id))continue;
            var geometry=new BoundaryGeometry(original.RootElement.GetProperty("geometry").GetRawText());Assert.Equal(BoundaryMatch.Inside,geometry.Contains(latitude,longitude));matched.Add(id);
        }
        Assert.Equal(ids.Order(),matched.Order());
    }
    [Fact] public void RealDataFilesMatchTheirProvenanceAndLicenseManifest()
    {
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(Bundle,"bundle-manifest.json")));
        foreach(var package in manifest.RootElement.GetProperty("packages").EnumerateArray())
        {
            var filename=Path.GetFileName(package.GetProperty("path").GetString()!);using var file=File.OpenRead(Path.Combine(Bundle,filename));Assert.Equal(package.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
            var metadata=GeographicPackage.Inspect(Path.Combine(Bundle,filename)).Manifest;Assert.Contains("ODbL",metadata.License);Assert.NotEmpty(metadata.Attribution);Assert.Contains("2026-10-21.0",metadata.Version);
        }
        using var geometry=File.OpenRead(Path.Combine(Bundle,"original-area-fixtures.geojsonseq.gz"));Assert.Equal(manifest.RootElement.GetProperty("originalAreaFixtureSha256").GetString(),Convert.ToHexString(SHA256.HashData(geometry)).ToLowerInvariant());
    }
    [Fact] public async Task RealMixedCityPlanningCopiesVerifiedBytesAndKeepsFolderLanguageIndependent()
    {
        var source=Temporary();var destination=Temporary();var journal=Temporary();var geocoder=Resolver();
        var a=Path.Combine(source,"a.jpg");var b=Path.Combine(source,"b.mov");await File.WriteAllTextAsync(a,"Controlled synthetic bytes A",TestContext.Current.CancellationToken);await File.WriteAllTextAsync(b,"Controlled synthetic bytes B",TestContext.Current.CancellationToken);
        MediaItem[] items=[new(a,34.9858,135.7588,"Photo"),new(b,47.3782,8.5402,"Video")];
        var plan=await GeographicCopy.Plan(items,geocoder,destination,"en-US","CountryCity",true,TestContext.Current.CancellationToken,displayLanguage:"ru-RU");
        Assert.Equal(Path.Combine("Japan","Kyoto"),plan[0].RelativeDirectory);Assert.Equal(Path.Combine("Switzerland","Zurich"),plan[1].RelativeDirectory);Assert.NotEmpty(plan[0].GeographicIds);Assert.Contains("\u041a\u0438\u043e\u0442\u043e",plan[0].Location);
        var original=await Task.WhenAll(items.Select(i=>GeographicCopy.Hash(i.Path,TestContext.Current.CancellationToken)));
        var results=await GeographicCopy.Execute(plan,destination,journal,null,TestContext.Current.CancellationToken);Assert.All(results,r=>Assert.Equal(CopyState.Completed,r.Status));
        for(var i=0;i<items.Length;i++){Assert.Equal(original[i],await GeographicCopy.Hash(items[i].Path,TestContext.Current.CancellationToken));Assert.Equal(original[i],await GeographicCopy.Hash(results[i].Destination,TestContext.Current.CancellationToken));}
        var russian=await GeographicCopy.Plan(items,geocoder,Temporary(),"ru-RU","CountryCityDistrict",true,TestContext.Current.CancellationToken);Assert.Contains("\u042f\u043f\u043e\u043d\u0438\u044f",russian[0].RelativeDirectory);Assert.NotEqual(plan[0].RelativeDirectory,russian[0].RelativeDirectory);
    }
}
