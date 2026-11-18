// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.IO.Compression;
using System.Text;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class UrbanLiteTests
{
    [Fact] public void PrimaryRoadDensityStartsAboveMotorwaysAndTrunks()
    {Assert.True(UrbanRoadLayer.ClassVisibleAt(0,7));Assert.True(UrbanRoadLayer.ClassVisibleAt(1,7));Assert.False(UrbanRoadLayer.ClassVisibleAt(2,7));Assert.True(UrbanRoadLayer.ClassVisibleAt(2,8));}
    [Theory][InlineData(5,false)][InlineData(6.99,false)][InlineData(7,true)][InlineData(9,true)][InlineData(11,true)][InlineData(13,false)]
    public void GeneralizedRoadsRespectUsefulAtlasZoomRange(double zoom,bool visible)=>Assert.Equal(visible,UrbanRoadLayer.VisibleAt(zoom));

    [Fact] public void CuratedRoadPackageIsBoundedAndVersioned()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;var path=Path.Combine(root,"assets/geography/urban-roads.gslite");
        Assert.InRange(new FileInfo(path).Length,1,30*1024*1024);
        var roads=new UrbanRoadLayer(path);Assert.InRange(roads.GeometryCount,1,30000);Assert.InRange(roads.VertexCount,1,2000000);
    }
    [Fact] public void UnsupportedRoadSchemaIsRejectedBeforeGeometryAllocation()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;var dir=TestStorage.Create(root);var path=Path.Combine(dir,"invalid.gslite");
        using(var file=File.Create(path))using(var gzip=new GZipStream(file,CompressionMode.Compress))using(var writer=new BinaryWriter(gzip))
        {writer.Write(Encoding.ASCII.GetBytes("GSROAD01"));writer.Write(99);writer.Write(1);}
        Assert.Throws<InvalidDataException>(()=>new UrbanRoadLayer(path));
    }
}
