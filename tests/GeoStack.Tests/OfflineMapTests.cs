// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class OfflineMapTests
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    private static string Fixture(string name) => Path.Combine(Root,".local/map-fixtures",name+".mbtiles");
    [Fact] public void RasterPackageReadsRealSqliteTilesAndConvertsTmsRows()
    {
        var map = MapPackage.Inspect(Fixture("raster")); Assert.Equal("png",map.Encoding); Assert.Equal(0,map.MinZoom); Assert.Equal(3,map.MaxZoom);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Root,".local/map-fixtures/tile.png")), map.ReadTile(1,0,0)); Assert.Null(map.ReadTile(1,0,1)); Assert.Null(map.ReadTile(4,0,0));
        Assert.Equal(7,MapPackage.TmsRow(3,0)); Assert.Equal(0,MapPackage.TmsRow(3,7));
    }
    [Theory][InlineData("vector")][InlineData("wrong-encoding")][InlineData("corrupt")]
    public void UnsupportedOrCorruptPackagesAreRejected(string fixture) => Assert.Throws<InvalidDataException>(()=>MapPackage.Inspect(Fixture(fixture)));
    [Fact] public void PackagesPersistAcrossRestartAndUnavailableReferencesFallBack()
    {
        var folder = TestStorage.Create(Root); var catalog = new MapCatalog(folder); var package = MapPackage.Inspect(Fixture("raster"));
        catalog.Packages.Add(package); catalog.Packages.Add(package with { Id="missing",Path=Fixture("absent") }); catalog.Preferred=package.Id; catalog.Save();
        var restarted = new MapCatalog(folder); Assert.Equal(2,restarted.Packages.Count); Assert.Single(restarted.Candidates(1,40,0)); Assert.Empty(restarted.Candidates(4,40,0));
        restarted.Packages[0]=package with { Enabled=false }; Assert.Empty(restarted.Candidates(1,40,0)); restarted.Preferred="world"; Assert.Empty(restarted.Candidates(0,0,0)); Assert.True(new BuiltInWorldMap().Covers(22,0,0));
    }
    [Fact] public void CoverageAndZoomExcludeOutsideTilesAndPreferSelectedPackage()
    {
        var catalog = new MapCatalog(TestStorage.Create(Root)); var map = MapPackage.Inspect(Fixture("raster")) with { West=130,East=145,South=30,North=45,MinZoom=1,MaxZoom=3 };
        catalog.Packages.Add(map); catalog.Packages.Add(map with { Id="second" }); catalog.Preferred="second";
        Assert.Equal("second",catalog.Candidates(2,35,139).First().Id); Assert.Empty(catalog.Candidates(2,48,2)); Assert.Empty(catalog.Candidates(0,35,139));
    }
}
