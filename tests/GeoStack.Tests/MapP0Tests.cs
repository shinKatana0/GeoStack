// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class MapP0Tests
{
    private static string Root=>Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory);
    private static string Temporary(){var path=TestStorage.Create(Root);Directory.CreateDirectory(path);return path;}
    private static MapPackage Map()=>MapPackage.Inspect(Path.Combine(Root,".local/map-fixtures/raster.mbtiles"));
    [Fact] public void RecentWorkingSetSurvivesEvictionAfter192OldEntries()
    {
        var cache=new BoundedLru<string,string>(192);for(var i=0;i<192;i++)cache.Set("old"+i,"old"+i);
        for(var i=0;i<48;i++)cache.Set("current"+i,"current"+i);
        for(var i=0;i<48;i++){Assert.True(cache.TryGet("current"+i,out _));Assert.False(cache.Contains("old"+i));}Assert.Equal(192,cache.Count);
        cache.TryGet("old48",out _);cache.Set("next","next");Assert.True(cache.Contains("old48"));Assert.False(cache.Contains("old49"));
    }
    [Fact] public void CoveragePlanningIsPureAndClipsPartialEdgeTilesWithoutFilesystemAccess()
    {
        var map=Map() with { Path=Path.Combine(Temporary(),"disconnected.mbtiles"),West=20.9,East=21.1,South=52.1,North=52.4,MinZoom=10,MaxZoom=15 };
        Assert.False(map.Available);var p=Geo.Project(52.23,21.09);var plan=RasterTilePlan.Build([map],map.Id,8388608,950,700,new(p.X,p.Y));Assert.NotEmpty(plan);
        foreach(var tile in plan){Assert.True(tile.Bounds.Contains(tile.Clip));Assert.True(new Rect(0,0,950,700).Contains(tile.Clip));Assert.InRange(tile.Key.X,0,(1<<tile.Key.Z)-1);}
        Assert.Empty(RasterTilePlan.Build([map],map.Id,8388608,950,700,new(.6,.2)));
    }
    [Fact] public void ParentFallbackHasAFiniteOverzoomLimitAndWorldSelectionDisablesRaster()
    {
        var map=Map();Assert.NotEmpty(RasterTilePlan.Build([map],map.Id,256*Math.Pow(2,6),800,600,new(.5,.5)));
        Assert.Empty(RasterTilePlan.Build([map],map.Id,256*Math.Pow(2,7),800,600,new(.5,.5)));
        Assert.Empty(RasterTilePlan.Build([map],"world",2048,800,600,new(.5,.5)));
        Assert.Empty(RasterTilePlan.Build([map],map.Id,double.NaN,800,600,new(.5,.5)));
    }
    [Fact] public void AntimeridianPlacementsUseWrappedKeysAndCurrentScreenCoordinates()
    {
        var map=Map() with { West=170,East=-170,South=-30,North=30 };
        var plan=RasterTilePlan.Build([map],map.Id,2048,700,500,new(.999,.5));Assert.NotEmpty(plan);Assert.Contains(plan,p=>p.Key.X==0);Assert.Contains(plan,p=>p.Key.X==7);
        var moved=RasterTilePlan.Build([map],map.Id,2048,700,500,new(.999-10d/2048,.5));
        foreach(var tile in plan){var next=moved.FirstOrDefault(t=>t.Key==tile.Key);if(next!=null)Assert.InRange(next.Bounds.X-tile.Bounds.X,9.99,10.01);}
    }
    [Theory] [InlineData(-1,0)] [InlineData(23,0)] [InlineData(10,-1)] [InlineData(10,1024)]
    public void InvalidTileRowsAreRejectedBeforeShiftOrLookup(int zoom,int row)=>Assert.Throws<ArgumentOutOfRangeException>(()=>MapPackage.TmsRow(zoom,row));
    [Fact] public void ReaderCanReuseAConnectionAndHonorsCancellation()
    {
        using var reader=new RasterTileReader(Map());var bytes=reader.Read(0,0,0,TestContext.Current.CancellationToken);Assert.NotNull(bytes);for(var i=0;i<25;i++)Assert.Equal(bytes,reader.Read(0,0,0,TestContext.Current.CancellationToken));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>reader.Read(0,0,0,cancel.Token));
    }
    [Fact] public async Task IndependentReadersCanQueryOneArchiveConcurrently()
    {
        var map=Map();var result=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>{using var reader=new RasterTileReader(map);return reader.Read(0,0,0,TestContext.Current.CancellationToken);},TestContext.Current.CancellationToken)));
        Assert.All(result,r=>Assert.Equal(result[0],r));
    }
    [Fact] public async Task TileQueryBudgetInterruptsAnUnindexedExpensiveView()
    {
        var map=Map() with {Path=Path.Combine(Root,".local/map-fixtures/slow-view.mbtiles"),MinZoom=22,MaxZoom=22};
        var watch=System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<IOException>(()=>Task.Run(()=>{using var reader=new RasterTileReader(map);reader.Read(22,2097152,2097152,TestContext.Current.CancellationToken);},TestContext.Current.CancellationToken));
        Assert.True(watch.Elapsed<TimeSpan.FromSeconds(5),"SQL work did not honor its bounded progress budget");
    }
    private static Task Sta(Func<Task> action)
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.SetResult();}catch(Exception ex){completion.SetException(ex);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    private static void Frame(OfflineTileLayer layer,Point center,Action redraw,double world=2048)
    {
        var size=world==256?200:512;var visual=new DrawingVisual();using var dc=visual.RenderOpen();layer.Draw(dc,new(center.X-size/2/world,center.Y-size/2/world,size/world,size/world),world,size,size,center,redraw);
    }
    private static async Task Drain(OfflineTileLayer layer){for(var i=0;i<1000 && layer.IsLoading;i++)await Task.Delay(5);Assert.False(layer.IsLoading);await Task.Delay(50);}
    [Fact] public Task MissingTilesAreNegativeCachedWithoutCompletionRedrawLoops()=>Sta(async()=>{
        await using var layer=new OfflineTileLayer(Temporary());var map=Map();layer.Catalog.Packages.Add(map);layer.Catalog.Preferred=map.Id;layer.Diagnostics.Enabled=true;var redraws=0;
        Frame(layer,new(.5,.5),()=>redraws++);await Drain(layer);var requests=layer.Diagnostics.Requests;Assert.True(requests>0);Assert.Equal(0,redraws);Assert.True(layer.MissingCacheCount>0);
        for(var i=0;i<100;i++)Frame(layer,new(.5,.5),()=>redraws++);await Drain(layer);Assert.Equal(requests,layer.Diagnostics.Requests);Assert.Equal(0,redraws);Assert.InRange(layer.Diagnostics.MaxActive,0,1);
    });
    [Fact] public Task ClearCancelsObsoleteWorkAndDoesNotRedrawWorldFallback()=>Sta(async()=>{
        var layer=new OfflineTileLayer(Temporary());var map=Map();layer.Catalog.Packages.Add(map);layer.Catalog.Preferred=map.Id;layer.Diagnostics.Enabled=true;var redraws=0;
        Frame(layer,new(.5,.5),()=>redraws++,256);layer.Catalog.Preferred="world";layer.Clear();Frame(layer,new(.5,.5),()=>redraws++);await Drain(layer);Assert.Equal(0,layer.DrawnTileCount);Assert.Equal(0,redraws);Assert.Equal(0,layer.PendingCount);
        await layer.DisposeAsync();Assert.Equal(layer.Diagnostics.ConnectionsOpened,layer.Diagnostics.ConnectionsClosed);
    });
    [Fact] public Task RapidMapClearsDisposeReadersWithoutLeavingWork()=>Sta(async()=>{
        var layer=new OfflineTileLayer(Temporary());var map=Map();layer.Catalog.Packages.Add(map);layer.Catalog.Preferred=map.Id;layer.Diagnostics.Enabled=true;
        Frame(layer,new(.5,.5),()=>{},256);await Drain(layer);
        for(var i=0;i<500;i++)layer.Clear();
        Frame(layer,new(.5,.5),()=>{},256);await Drain(layer);await layer.DisposeAsync();Assert.Equal(0,layer.PendingCount);Assert.Equal(layer.Diagnostics.ConnectionsOpened,layer.Diagnostics.ConnectionsClosed);
    });
    [Fact] public Task CorruptTileDecodingFailsOnceAndViewportChangesRemainBounded()=>Sta(async()=>{
        await using var layer=new OfflineTileLayer(Temporary());var map=Map() with {Path=Path.Combine(Root,".local/map-fixtures/wrong-encoding.mbtiles")};layer.Catalog.Packages.Add(map);layer.Catalog.Preferred=map.Id;layer.Diagnostics.Enabled=true;
        for(var i=0;i<300;i++){Frame(layer,new((i%90)/100d,.5),()=>{},256);if(i%10==0)await Task.Delay(1);}
        Frame(layer,new(.5,.5),()=>{},256);await Drain(layer);var requests=layer.Diagnostics.Requests;Frame(layer,new(.5,.5),()=>{},256);await Drain(layer);Assert.Equal(requests,layer.Diagnostics.Requests);Assert.Equal(0,layer.DecodedTileCount);Assert.InRange(layer.Diagnostics.MaxPending,0,128);Assert.InRange(layer.Diagnostics.MaxActive,0,1);
    });
}
