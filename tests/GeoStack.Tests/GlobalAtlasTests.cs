// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class GlobalAtlasTests
{
    private static string Fixture(string name)=>Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local/global-atlas-fixtures",name+".gsvector");
    [Fact] public void WorldwidePackageRequiresCompleteXyzAddressingAndRejectsExtremeRequests()
    {
        using var package=new GlobalAtlasPackage(Fixture("valid"),true);
        for(var z=0;z<=1;z++)for(var x=0;x<1<<z;x++)for(var y=0;y<1<<z;y++)Assert.NotNull(package.ReadTile(z,x,y));
        Assert.Null(package.ReadTile(2,0,0));Assert.Null(package.ReadTile(1,-1,0));Assert.Null(package.ReadTile(1,0,int.MaxValue));
    }
    [Theory][InlineData("missing-coordinate")][InlineData("missing-content")][InlineData("unsupported")][InlineData("corrupt")]
    public void IncompleteOrUnsupportedGlobalArchivesAreRejected(string name)=>Assert.Throws<InvalidDataException>(()=>new GlobalAtlasPackage(Fixture(name),true));
    [Fact] public void DisconnectedGlobalArchiveFailsWithoutChangingOtherPackages()=>Assert.ThrowsAny<IOException>(()=>new GlobalAtlasPackage(Fixture("absent")));
    [Theory][InlineData(8,1360,880)][InlineData(8,1920,1080)][InlineData(8,3840,2160)][InlineData(10,1920,1080)][InlineData(15,1920,1080)]
    public void LargeViewportsReduceSourceLodToKeepOverscanBounded(int requested,double width,double height)
    {
        var world=256*Math.Pow(2,requested);var z=GlobalVectorAtlas.SelectSourceZoom(world,width,height,8);
        Assert.InRange(z,0,8);var tilePixels=world/(1<<z);
        Assert.True((Math.Ceiling(width/tilePixels)+3)*(Math.Ceiling(height/tilePixels)+3)<=42);
    }
    [Theory][InlineData("roads","highway","motorway","road-major")][InlineData("roads","major_road","secondary","road-secondary")][InlineData("landuse","forest","","park")][InlineData("water","lake","","water")][InlineData("buildings","building","",null)][InlineData("roads","minor_road","residential",null)]
    public void IntermediateStyleUsesRealGeneralizedClassesWithoutInventingBuildingOrLocalStreetDetail(string layer,string kind,string detail,string? expected)
        =>Assert.Equal(expected,GlobalVectorAtlas.Classify(new(layer,3,4096,new Dictionary<string,string>{{"kind",kind},{"kind_detail",detail}},[])));
    [Fact] public void VectorContextPreventsThreeLevelRasterEnlargementAtCityBoundaries()
    {
        var detail=new MapPackage("detail","detail","unused",1,"png",10,15,20.9372,52.1747,21.0872,52.2847,"test","test");
        var context=detail with{Id="context",MinZoom=8,MaxZoom=12,West=20.4122,East=21.6122,South=51.7297,North=52.7297};
        var p=Geo.Project(52.2297,21.09);var world=256*Math.Pow(2,15)*1.01;
        Assert.NotEmpty(RasterComposition.Plans([detail,context],detail.Id,world,950,700,new Point(p.X,p.Y)));
        Assert.Empty(RasterComposition.Plans([detail,context],detail.Id,world,950,700,new Point(p.X,p.Y),nativeOnly:true));
        p=Geo.Project(52.2297,21.0122);
        var native=RasterComposition.Plans([detail,context],detail.Id,world,950,700,new Point(p.X,p.Y),nativeOnly:true);
        Assert.Single(native);Assert.Equal(15,native[0].Zoom);Assert.Equal(detail.Id,native[0].Package.Id);
    }
}
