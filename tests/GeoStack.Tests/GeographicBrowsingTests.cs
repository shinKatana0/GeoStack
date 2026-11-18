// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class GeographicBrowsingTests
{
    private static readonly GeographicName Japan=new("JP","Japan","Japan",null), Poland=new("PL","Poland","Poland",null);
    private static MediaItem Media(string id,double? lat,double? lon,string kind="Photo")=>new(id,lat,lon,kind);
    private static GeographicLocation Resolve(double? lat,double? lon)=>new(lon>100?Japan:Poland,null,new("city:"+lat,"Source city","Source city",null),null);
    private static MapCamera At(double lat,double lon,double world=8388608,double width=950,double height=700) { var p=Geo.Project(lat,lon);return new(p.X,p.Y,world,width,height); }
    [Fact] public void WarsawViewGroupsJapanWithoutCountingVisibleOrMissingGpsMedia()
    {
        var items=new[]{Media("Warsaw",52.2297,21.0122),Media("Tokyo",35.6812,139.7671),Media("Kyoto",34.9858,135.7588),Media("No GPS",null,null),Media("Kyoto video",34.9858,135.7588,"Video")};
        var index=new OffscreenIndex(items,Resolve);var outside=index.Outside(At(52.2297,21.0122));
        var group=Assert.Single(outside);Assert.Equal("JP",group.Name!.Id);Assert.Equal(3,group.Items.Count);Assert.Contains(group.Items,m=>m.Kind=="Video");
        Assert.Empty(index.Outside(new(.5,.5,850,1000,1000)));
        Assert.Equal(3,index.Outside(At(35.6812,139.7671)).Sum(g=>g.Items.Count));
    }
    [Fact] public void AntimeridianVisibilityAndDirectionUseShortestWrappedProjection()
    {
        var media=Media("across",0,-179.99);var index=new OffscreenIndex([media],Resolve);
        Assert.Empty(index.Outside(At(0,179.99,1000)));
        var camera=At(0,179.9,2000000);var outside=index.Outside(camera);var chip=Assert.Single(OffscreenIndex.Place(outside,camera));Assert.Equal("\u2192",chip.Arrow);
        Assert.Equal(chip.Arrow,Assert.Single(OffscreenIndex.Place(index.Outside(At(0,179.9001,2000000)),At(0,179.9001,2000000))).Arrow);
    }
    [Fact] public void NearbyJapaneseCitiesSeparateAndBoundsContainAllTheirMedia()
    {
        var media=new[]{Media("Tokyo",35.68,139.76),Media("Kyoto",34.98,135.75),Media("Kanazawa",36.57,136.64)};var index=new OffscreenIndex(media,Resolve);
        var outside=index.Outside(At(35.68,139.76));Assert.Equal(2,outside.Count);Assert.Equal(2,outside.Sum(g=>g.Items.Count));
        var bounds=Geo.Bounds(media)!.Value;foreach(var item in media) { var p=Geo.Project(item.Latitude!.Value,item.Longitude!.Value);Assert.True(Math.Abs(Geo.WrappedDelta(p.X-bounds.X))<=bounds.Width/2+.000001); }
    }
    [Theory] [InlineData(950,700)] [InlineData(530,480)] [InlineData(350,250)]
    public void EdgeChipsStayInsideViewportAndDoNotOverlap(double width,double height)
    {
        var camera=At(52,21,width:width,height:height);var groups=Enumerable.Range(0,12).Select(i=>new OffscreenGroup("g"+i,null,[Media("m"+i,35,139)],.9+i*.001,.4)).ToArray();
        var placed=OffscreenIndex.Place(groups,camera);Assert.NotEmpty(placed);
        foreach(var p in placed) { Assert.InRange(p.X,0,width-148);Assert.InRange(p.Y,108,height-32);foreach(var other in placed.Where(o=>o.Group.Id!=p.Group.Id)) Assert.True(Math.Abs(p.X-other.X)>=152 || Math.Abs(p.Y-other.Y)>=36); }
    }
    [Fact] public void UpdatedIndexExcludesOldLibraryAndDuplicateIdentities()
    {
        var item=Media("same",35,139);var index=new OffscreenIndex([item,item],Resolve);Assert.Single(index.Places);Assert.Empty(new OffscreenIndex([],Resolve).Outside(At(0,0)));
    }
    [Fact] public void HistoryIsBoundedAndManualCameraUpdatesDoNotAppendEntries()
    {
        var history=new LocationHistory<int>();Assert.True(history.Push(0));Assert.False(history.Push(0));Assert.Equal(1,history.Count);for(var i=0;i<30;i++)history.Push(i);Assert.Equal(20,history.Count);Assert.True(history.TryBack(out var value));Assert.Equal(29,value);for(var i=0;i<19;i++)Assert.True(history.TryBack(out _));Assert.False(history.TryBack(out _));
    }
    [Fact] public void TenThousandProjectedMediaMaintainExactCountWithoutResolverCallsOnPan()
    {
        var calls=0;var media=Enumerable.Range(0,10000).Select(i=>Media("synthetic/"+i,30+i%100*.01,130+i/100*.01)).ToArray();
        var index=new OffscreenIndex(media,(a,b)=>{ calls++;return Resolve(a,b); });var original=calls;
        for(var i=0;i<10;i++)Assert.Equal(10000,index.Outside(At(52,21+i*.01)).Sum(g=>g.Items.Count));Assert.Equal(original,calls);
    }
}
