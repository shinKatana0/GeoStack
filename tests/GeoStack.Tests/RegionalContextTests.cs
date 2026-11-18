// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.Core;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class RegionalContextTests
{
    private static MapDownloadPlan Detail(double lat=50.087,double lon=14.421)=>LibraryMapPlanner.Build([new(Path.GetFullPath("regional-photo.jpg"),lat,lon,"Photo")],[],(_,_)=>new(null,null,null,null),default);
    [Theory][InlineData(false,"en-US")][InlineData(true,"en-US")][InlineData(false,"ru-RU")][InlineData(true,"ru-RU")]
    public void ContextFallbackKeepsThemeWhenDetailedPackageCannotFillViewport(bool dark,string language)
    {
        MapPackage Map(string id,string file,int max,double west,double south,double east,double north)=>new(id,id,file,0,"png",8,max,west,south,east,north,"","");
        var detail=Map("detail","detail-dark-"+language+".mbtiles",15,30.30,59.91,30.37,59.95);
        var light=Map("light","region-city.mbtiles",10,27,58,34,62);
        var matching=Map("matching",dark?"region-dark-"+language+".mbtiles":"region-light-"+language+".mbtiles",10,27,58,34,62);
        var center=Geo.Project(59.9343,30.3351);var selected=NativeAtlasPilot.ChooseRasterSurface([detail,light,matching],matching,new HashSet<string>{"detail"},dark,language,12,256*Math.Pow(2,12)*1.01,955.2,728,new(center.X,center.Y));
        Assert.NotNull(selected);Assert.Equal("matching",selected.Id);
    }
    [Fact] public void SaintPetersburgHasBoundedContextAndLocalDetailProposals()
    {
        var plan=RegionalContextPlanner.Extend(Detail(59.9343,30.3351),[],TestContext.Current.CancellationToken);
        var context=Assert.Single(plan.Areas,a=>a.IsContext);var city=Assert.Single(plan.Areas,a=>!a.IsContext);
        var map=new MapPackage(context.Id,"Context","fixture.mbtiles",0,"png",8,10,context.West,context.South,context.East,context.North,"","");
        var center=Geo.Project(59.9343,30.3351);
        Assert.True(RasterComposition.CoversViewport(map,256*Math.Pow(2,8)*1.01,955.2,728,new(center.X,center.Y)),"Regional package must cover the accepted zoom-8 viewport");
        Assert.True(context.Supported && city.Supported);Assert.False(context.Installed);Assert.InRange(context.Tiles.Count,1,512);
        Assert.True(context.West<29 && context.East>31 && context.North>60 && context.South<59.5);
        Assert.All(context.Tiles,t=>Assert.InRange(t.Zoom,8,10));Assert.All(city.Tiles,t=>Assert.InRange(t.Zoom,10,15));
    }
    [Fact] public void ContextIsWiderAndBoundedWithoutChangingDetail()
    {
        var detail=Detail();var plan=RegionalContextPlanner.Extend(detail,[],default);var local=plan.Areas.Single(a=>!a.IsContext);var context=plan.Areas.Single(a=>a.IsContext);
        Assert.Equal(detail.Areas[0],local);Assert.True(context.West<local.West && context.East>local.East && context.South<local.South && context.North>local.North);
        Assert.InRange(context.Tiles.Count,1,512);Assert.All(context.Tiles,t=>Assert.InRange(t.Zoom,8,10));Assert.False(context.Installed);Assert.True(context.MaximumInstalledBytes>0);
    }
    [Fact] public void ContextWrapsDateLineWithoutPlanetExtent()
    {
        var c=RegionalContextPlanner.Extend(Detail(0,179.99),[],default).Areas.Single(a=>a.IsContext);Assert.True(c.West>c.East);Assert.InRange(c.Tiles.Count,1,512);Assert.Contains(c.Tiles,t=>t.X==0);
    }
    [Fact] public void RegionalFootprintContainsTheAcceptedRendererViewport()
    {
        var area=RegionalContextPlanner.Extend(Detail(),[],default).Areas.Single(a=>a.IsContext);
        var map=new MapPackage(area.Id,"Regional context","fixture.mbtiles",0,"png",8,10,area.West,area.South,area.East,area.North,"","");var center=Geo.Project(50.12,14.50);
        Assert.True(RasterComposition.CoversViewport(map,256*Math.Pow(2,9)*1.01,955.2,728,new(center.X,center.Y)));
        var outside=Geo.Project(51,15);Assert.False(RasterComposition.CoversViewport(map,256*Math.Pow(2,8)*1.01,955.2,728,new(outside.X,outside.Y)));
    }
    [Fact] public void EmptyAndCancelledPlansDoNotDoWork()
    {
        Assert.Empty(RegionalContextPlanner.Extend(new([],1,0,0),[],default).Areas);
        Assert.Throws<OperationCanceledException>(()=>RegionalContextPlanner.Extend(Detail(),[],new(true)));
    }
    [Fact] public void TenThousandNearbyMediaShareOneContextProposal()
    {
        var media=Enumerable.Range(0,10000).Select(i=>new MediaItem(Path.GetFullPath("region-"+i+".jpg"),50.087+i%10*.00001,14.421,"Photo")).ToArray();
        var detail=LibraryMapPlanner.Build(media,[],(_,_)=>new(null,null,null,null),default);
        var plan=RegionalContextPlanner.Extend(detail,[],default);var context=Assert.Single(plan.Areas.Where(a=>a.IsContext));Assert.Equal(10000,context.MediaCount);Assert.True(context.Supported);
    }
    [Fact] public void PartialDetailCoverageCannotMoveTheRegionalProposal()
    {
        var detail=Detail();var first=RegionalContextPlanner.Extend(detail,[],default).Areas.Single(a=>a.IsContext);
        var changed=detail with{Areas=[detail.Areas[0] with{West=14.42,East=14.46}]};
        Assert.Equal(first.Id,RegionalContextPlanner.Extend(changed,[],default).Areas.Single(a=>a.IsContext).Id);
    }
    [Fact] public void ContextTileCoverageIsVerifiedAndReused()
    {
        var root=Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local","regional-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var plan=Detail();var area=RegionalContextPlanner.Extend(plan,[],default).Areas.Single(a=>a.IsContext);
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
        var file=Path.Combine(root,"context.mbtiles");using(var writer=new RasterPackageWriter(file,new Dictionary<string,string>{{"format","png"},{"minzoom","8"},{"maxzoom","10"},{"bounds","-180,-85,180,85"}})){foreach(var tile in area.Tiles)writer.Add(tile,png);writer.Complete();}
        var map=MapPackage.Inspect(file);Assert.True(RegionalContextPlanner.Extend(plan,[map],default).Areas.Single(a=>a.IsContext).Installed);
        Assert.False(RegionalContextPlanner.Extend(plan,[map],default,_=>false).Areas.Single(a=>a.IsContext).Installed);
        Assert.False(RegionalContextPlanner.Extend(plan,[map],default).Areas.Single(a=>!a.IsContext).Installed);
    }
}
