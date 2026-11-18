// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using GeoStack.App;
using GeoStack.Core;
using Xunit;
namespace GeoStack.Tests;
public sealed class RegionalLodCoverageTests
{
    [Fact] public void CoarseGeographySurvivesOutsideDetailedFootprintWithoutOverlap()
    {
        var bands=RegionalLodCoverage.Build(new Dictionary<int,Rect>{{8,new(0.4,0.3,.2,.2)},{10,new(.45,.35,.1,.1)}});
        Assert.True(bands[(10,8)].FillContains(new Point(.42,.32),1e-10,System.Windows.Media.ToleranceType.Absolute));
        Assert.False(bands[(10,8)].FillContains(new Point(.5,.4),1e-10,System.Windows.Media.ToleranceType.Absolute));
        Assert.True(bands[(10,10)].FillContains(new Point(.5,.4),1e-10,System.Windows.Media.ToleranceType.Absolute));
        Assert.True(bands[(8,8)].FillContains(new Point(.5,.4),1e-10,System.Windows.Media.ToleranceType.Absolute));
        Assert.All(bands.Values,g=>Assert.True(g.IsFrozen));
    }
    [Fact] public void UnsupportedOrInvalidCoverageIsRejected()
    {
        Assert.Throws<InvalidDataException>(()=>RegionalLodCoverage.Build(new Dictionary<int,Rect>{{99,new(.1,.1,.1,.1)}}));
        Assert.Throws<InvalidDataException>(()=>RegionalLodCoverage.Build(new Dictionary<int,Rect>{{8,new(-.1,.1,.2,.1)}}));
    }
    [Fact] public void ContextRecognitionDoesNotDependOnGeneratedFileNames()
    {
        var map=new MapPackage("legacy","Warsaw context","unused",1,"png",8,12,20.4,51.7,21.6,52.7,"test","test");
        Assert.True(RegionalLodCoverage.IsContext(map));Assert.False(RegionalLodCoverage.IsContext(map with{MinZoom=10,MaxZoom=15}));Assert.False(RegionalLodCoverage.IsContext(map with{West=-180,East=180}));
    }
    [Fact] public void CoverageManifestIsBoundToTheActualRetainedPackages()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"assets/geography/atlas-lod-coverage.json")));
        foreach(var entry in doc.RootElement.GetProperty("Regions").EnumerateObject())
        {
            var path=Path.Combine(root,"offline-maps/atlas-pilot-compact",entry.Name+".gsatlas");
            Assert.Equal(entry.Value.GetProperty("PackageSHA256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }
}
