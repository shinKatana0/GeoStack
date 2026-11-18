// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class LayoutAndMapRecoveryTests
{
    [Theory]
    [InlineData(1920,1080,1)] [InlineData(1920,1080,1.5)]
    [InlineData(1366,768,1)] [InlineData(1366,768,1.5)]
    [InlineData(1920,1080,2)] [InlineData(1366,768,2)]
    public void PurposeSpecificMinimumAndRestoredBoundsFitEveryWorkArea(double width,double height,double scale)
    {
        var work = new Rect(-width/scale, 0, width/scale, (height-48)/scale);
        foreach (var purpose in new[] { "Main", "Settings", "MapManager", "GeographicData", "CopyToLocation", "CopyHistory", "Message" })
        {
            var minimum = WindowLayout.Minimum(purpose);
            var smallest = WindowLayout.Clamp(work, new Rect(100000, -10000, 0, 0), minimum);
            Assert.True(work.Contains(smallest));
            Assert.Equal(Math.Min(minimum.Width,work.Width),smallest.Width);
            Assert.Equal(Math.Min(minimum.Height,work.Height),smallest.Height);
            Assert.True(smallest.Height>200);
            Assert.True(work.Contains(WindowLayout.Clamp(work,new Rect(-30000,40000,9000,9000),minimum)));
        }
    }
    [Fact] public void RegionalEdgeTileIsSelectedEvenWhenItsCenterIsOutsideCoverage()
    {
        var root = Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../",AppContext.BaseDirectory);
        var map = MapPackage.Inspect(Path.Combine(root,".local/map-fixtures/raster.mbtiles")) with { MinZoom=10,MaxZoom=15,West=139.6921,East=139.8421,South=35.6262,North=35.7362 };
        var p = Geo.Project(35.6812,139.7671);var x=(int)(p.X*1024);var y=(int)(p.Y*1024);var center=Geo.Unproject((x+.5)/1024,(y+.5)/1024);
        Assert.False(map.Covers(10,center.Latitude,center.Longitude)); Assert.True(map.IntersectsTile(10,x,y));
        var catalog = new MapCatalog(TestStorage.Create(root));catalog.Packages.Add(map);catalog.Preferred=map.Id;
        Assert.Single(catalog.CandidatesForTile(10,x,y));
        catalog.Preferred="world";Assert.Empty(catalog.CandidatesForTile(10,x,y));
        Assert.False(map.IntersectsTile(9,x,y));Assert.False(map.IntersectsTile(10,-1,y));
    }
}
