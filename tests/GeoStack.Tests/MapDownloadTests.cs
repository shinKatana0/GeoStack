// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using GeoStack.Core;
using GeoStack.App;
using Xunit;
namespace GeoStack.Tests;
public sealed class MapDownloadTests
{
    private static GeographicLocation Unknown(double? a,double? b)=>new(null,null,null,null);
    private static MediaItem Item(string id,double? lat,double? lon)=>new(Path.GetFullPath(id+".jpg"),lat,lon,"Photo");
    [Fact] public void TenThousandLocationsProduceBoundedOfflineAreasWithoutDoubleCounts()
    {
        var cities=new[]{(35.68,139.76),(35.01,135.76),(36.57,136.65),(47.38,8.54),(52.23,21.01)};
        var items=Enumerable.Range(0,10000).Select(i=>Item("planning"+i,cities[i%5].Item1+(i/5%10)*.00001,cities[i%5].Item2)).ToArray();
        var clock=System.Diagnostics.Stopwatch.StartNew();var plan=LibraryMapPlanner.Build(items,[],Unknown,CancellationToken.None);
        Assert.Equal(10000,plan.Areas.Sum(a=>a.MediaCount));Assert.Equal(5,plan.Areas.Count);Assert.All(plan.Areas,a=>{Assert.True(a.Supported);Assert.InRange(a.Tiles.Count,1,512);});
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local/download05-plan-measurement.json"),System.Text.Json.JsonSerializer.Serialize(new{Count=items.Length,Areas=plan.Areas.Count,Milliseconds=clock.Elapsed.TotalMilliseconds,Scope="Offline grouping and no installed packages; no network"}));
    }
    [Fact] public void DateLineLocationsShareOneSmallArea()
    {var plan=LibraryMapPlanner.Build([Item("west",0,179.99),Item("east",0,-179.99)],[],Unknown,CancellationToken.None);Assert.Single(plan.Areas);Assert.True(plan.Areas[0].West>plan.Areas[0].East);Assert.True(plan.Areas[0].Tiles.Count<100);}
    [Fact] public void MissingGpsAndIsolatedOutliersRemainExplicit()
    {var items=Enumerable.Range(0,20).Select(i=>Item("same"+i,47.38,8.54)).Append(Item("outlier",-30,150)).Append(Item("none",null,null)).ToArray();var plan=LibraryMapPlanner.Build(items,[],Unknown,CancellationToken.None);Assert.Equal(1,plan.WithoutGps);Assert.Equal(1,plan.IsolatedLocations);Assert.Equal(21,plan.Areas.Sum(a=>a.MediaCount));}
    [Fact] public void InvalidAndNetworkPackagesDoNotAbortOfflinePlanning()
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local","bad-map-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{var path=Path.Combine(folder,"bad.mbtiles");File.WriteAllText(path,"not SQLite");var invalid=new MapPackage("broken","Broken",path,10,"png",10,15,-180,-85,180,85,"","");var plan=LibraryMapPlanner.Build([Item("valid-location",50.087,14.421)],[invalid,invalid with{Id="network",Path=@"\\unavailable\map.mbtiles"}],Unknown,CancellationToken.None);Assert.Equal(2,plan.UnavailablePackages);Assert.False(plan.Areas.Single().Installed);}
        finally{Directory.Delete(folder,true);}
    }
    [Fact] public void PlanningHonorsCancellation()=>Assert.Throws<OperationCanceledException>(()=>LibraryMapPlanner.Build([Item("cancel",0,0)],[],Unknown,new CancellationToken(true)));
    [Theory][InlineData(1,0,0,1UL)][InlineData(1,0,1,2UL)][InlineData(1,1,1,3UL)][InlineData(1,1,0,4UL)][InlineData(0,0,0,0UL)]
    public void HilbertAddressMatchesPmtilesSpecification(int z,int x,int y,ulong expected)=>Assert.Equal(expected,PmtilesSource.TileId(z,x,y));
    [Fact] public void ExtremeTileCoordinatesAreRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>PmtilesSource.TileId(23,0,0));Assert.Throws<ArgumentOutOfRangeException>(()=>PmtilesSource.TileId(15,-1,0));Assert.Throws<ArgumentOutOfRangeException>(()=>PmtilesSource.TileId(15,32768,0));}
    private sealed class SourceHandler : HttpMessageHandler
    {
        public int Requests;public bool IgnoreRange,ChangeIdentity,Unavailable;
        private readonly byte[] archive=new byte[137];
        public SourceHandler(){"PMTiles"u8.CopyTo(archive);archive[7]=3;archive[97]=archive[98]=archive[99]=1;archive[101]=15;void N(int p,ulong n)=>BinaryPrimitives.WriteUInt64LittleEndian(archive.AsSpan(p,8),n);N(8,129);N(16,5);N(24,127);N(32,2);N(56,134);"{}"u8.CopyTo(archive.AsSpan(127));new byte[]{1,0,1,3,1,4,5,6}.CopyTo(archive,129);}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {token.ThrowIfCancellationRequested();Requests++;if(Unavailable)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));var range=request.Headers.Range!.Ranges.Single();var from=(int)range.From!;var to=(int)range.To!;var response=new HttpResponseMessage(IgnoreRange?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new ByteArrayContent(archive[from..(to+1)])};response.Content.Headers.ContentRange=new(from,to,archive.Length);response.Headers.ETag=new(ChangeIdentity && Requests>1?"\"other\"":"\"stable\"");return Task.FromResult(response);}
    }
    [Fact] public async Task ProviderDoesNotConnectUntilExplicitInitializeAndCachesDirectories()
    {var handler=new SourceHandler();using var source=new PmtilesSource(handler);Assert.Equal(0,handler.Requests);await source.Initialize(default);Assert.Equal(new byte[]{4,5,6},await source.Tile(0,0,0,default));var requests=handler.Requests;Assert.Null(await source.Tile(1,0,0,default));Assert.Equal(requests,handler.Requests);Assert.InRange(source.DownloadedBytes,1,137);}
    [Fact] public async Task FullPlanetResponsesAreRejected(){using var source=new PmtilesSource(new SourceHandler{IgnoreRange=true});await Assert.ThrowsAsync<InvalidDataException>(()=>source.Initialize(default));}
    [Fact] public async Task ChangedArchiveIdentityIsRejected(){using var source=new PmtilesSource(new SourceHandler{ChangeIdentity=true});await Assert.ThrowsAsync<InvalidDataException>(()=>source.Initialize(default));}
    [Fact] public async Task ProviderRetriesAreBounded(){var h=new SourceHandler{Unavailable=true};using var source=new PmtilesSource(h);await Assert.ThrowsAsync<HttpRequestException>(()=>source.Initialize(default));Assert.Equal(3,h.Requests);}
    [Fact] public async Task ProviderCancellationDoesNotRetry(){var h=new SourceHandler();using var source=new PmtilesSource(h);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>source.Initialize(new(true)));Assert.Equal(0,h.Requests);}
    [Fact] public void CorruptVectorTileIsRejected(){Assert.Throws<InvalidDataException>(()=>VectorTile.Decode([26,255]));Assert.Empty(VectorTile.Decode([]));}
    [Theory][InlineData("map-light-ru-RU.mbtiles",false,"ru-RU",true)][InlineData("map-light-ru-RU.mbtiles",false,"en-US",false)][InlineData("map-dark-en-US.mbtiles",true,"en-US",true)]
    public void DownloadStylesAreSelectedByThemeAndLanguage(string file,bool dark,string language,bool expected)=>Assert.Equal(expected,NativeAtlasPilot.MatchesCityTheme(file,dark,language));
    [Fact] public void GeneratedPackageIsTmsAndCatalogVariantsPersist()
    {
        var directory=Path.Combine(Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local"),"GeoStack-download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var file=Path.Combine(directory,"map.mbtiles");var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
            using(var writer=new RasterPackageWriter(file,new Dictionary<string,string>{{"name","Fixture"},{"format","png"},{"minzoom","15"},{"maxzoom","15"},{"bounds","-180,-85,180,85"}})){writer.Add(new(15,100,200),png);writer.Complete();}
            var map=MapPackage.Inspect(file);Assert.Equal(png,map.ReadTile(15,100,200));Assert.Null(map.ReadTile(15,100,201));var coordinate=Geo.Unproject(100.5/32768,200.5/32768);var point=Item("covered",coordinate.Latitude,coordinate.Longitude);
            Assert.True(LibraryMapPlanner.Build([point],[map],Unknown,CancellationToken.None).Areas.Single().Installed);
            Assert.True(LibraryMapPlanner.Build([point],[map with{Enabled=false}],Unknown,CancellationToken.None).Areas.Single().Installed);
            Assert.False(LibraryMapPlanner.Build([point],[map],Unknown,CancellationToken.None,validateTile:_=>false).Areas.Single().Installed);
            var catalog=new MapCatalog(directory);catalog.Packages.Add(map with{Variants=[map with{Id="dark"}]});catalog.Save();Assert.Single(new MapCatalog(directory).Packages[0].Variants!);Assert.Contains("dark",NativeAtlasPilot.FamilyIds(new MapCatalog(directory).Packages[0]));Assert.Equal(map.Size*2,new MapCatalog(directory).Packages[0].InstalledSize);
            Assert.Throws<IOException>(()=>new RasterPackageWriter(file,new Dictionary<string,string>()));
        }
        finally{Directory.Delete(directory,true);}
    }
}
