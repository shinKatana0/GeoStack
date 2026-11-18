// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Windows.Media.Imaging;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GeoStack.Tests;

public sealed class HardeningTests
{
    [Fact] public async Task PackagedStartupRecordsMonotonicPhasesThroughFirstRender()
    {
        var executable = Environment.GetEnvironmentVariable("GEOSTACK_EXE") ?? Path.Combine(Root,"dist/GeoStack-portable-win-x64/GeoStack.exe");
        var directory = Temporary();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[]{"--smoke-test", "--data", directory}) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.Equal(0, process.ExitCode);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"startup-phases.json")));
        var phases = document.RootElement.EnumerateArray().ToArray();
        foreach (var name in new[]{"ManagedEntry","AtlasDataLoaded","IndexOpened","IndexRead","WindowLoaded","ContentRendered","ApplicationIdle"}) Assert.Contains(phases,p=>p.GetProperty("Phase").GetString()==name);
        var times = phases.Select(p=>p.GetProperty("ManagedElapsedMilliseconds").GetDouble()).ToArray();
        Assert.Equal(times.Order().ToArray(), times);
        Assert.DoesNotContain(directory, document.RootElement.GetRawText());
    }
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    private static string Temporary() { var path = TestStorage.Create(Root); Directory.CreateDirectory(path); return path; }
    [Theory]
    [InlineData("+37.7749-122.4194+0015.0/",37.7749,-122.4194)]
    [InlineData("-33.8600+151.2000/",-33.86,151.2)]
    [InlineData("+00.0000+000.0000/",0,0)]
    public void Iso6709ParsesSignedCoordinatesAndAltitude(string value,double lat,double lon) { Assert.True(ExifParser.TryIso6709(value,out var a,out var b)); Assert.Equal(lat,a,6); Assert.Equal(lon,b,6); }
    [Theory][InlineData("+91.0000+000.0000/")][InlineData("NaN")][InlineData("+12.0-200.0/")][InlineData("")]
    public void Iso6709RejectsInvalidLocations(string value) => Assert.False(ExifParser.TryIso6709(value,out _,out _));
    [Fact] public void ExifCoordinatesFallbackSupportsQuickTimeIso6709() { var m=Assert.Single(ExifParser.Parse("""[{"SourceFile":"a.mov","GPSCoordinates":"+37.7749-122.4194/"}]""")); Assert.True(m.HasLocation); Assert.Equal(-122.4194,m.Longitude); }
    [Fact] public void DateLineNeighborsClusterWithoutGreenwichCentroid()
    {
        MediaItem[] items=[new("east.jpg",0,179.99,"Photo"),new("west.jpg",0,-179.99,"Photo")];
        var cluster=Assert.Single(Geo.Cluster(items,10000)); Assert.True(Math.Abs(cluster.Longitude)>179); var bounds=Geo.Bounds(items)!.Value; Assert.True(bounds.Width<.001); Assert.True(Math.Abs(bounds.X-.5)>.49);
    }
    [Fact] public void ClusteringIsStableAcrossInputOrder()
    {
        var items=Enumerable.Range(0,500).Select(i=>new MediaItem(i.ToString("D5")+".jpg",48.85+i*.0001,2.35+i*.0001,"Photo")).ToArray();
        string[] Describe(IEnumerable<MediaItem> input)=>Geo.Cluster(input,50000).Select(c=>string.Join(",",c.Items.Select(i=>i.Path))).ToArray();
        Assert.Equal(Describe(items),Describe(items.Reverse()));
    }
    [Theory][InlineData("ru-RU","ru-RU")][InlineData("ru-KZ","ru-RU")][InlineData("fr-FR","en-US")][InlineData("en-US","en-US")]
    public void FirstLaunchLanguageHasEnglishFallback(string culture,string expected)=>Assert.Equal(expected,Preferences.DetectLanguage(culture));
    [Fact] public async Task UnchangedRescanReusesMetadataAndChangedFilesInvalidateCache()
    {
        var folder=Temporary(); var source=Path.Combine(folder,"photo.jpg"); File.Copy(Path.Combine(Root,".local/fixtures/Paris-01.jpg"),source);
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db")); var scanner=new ScanService(TestMediaTools.ExifTool);
        var first=await scanner.ScanAsync(folder,db,null,CancellationToken.None); Assert.Equal(0,first.Reused);
        var second=await scanner.ScanAsync(folder,db,null,CancellationToken.None); Assert.Equal(1,second.Reused); Assert.Equal(first.Items[0],second.Items[0]);
        File.SetLastWriteTimeUtc(source,File.GetLastWriteTimeUtc(source).AddSeconds(2)); var third=await scanner.ScanAsync(folder,db,null,CancellationToken.None); Assert.Equal(0,third.Reused);
    }
    [Fact] public async Task ValidHeicMetadataAndPortableDecoding()
    {
        var folder=Path.Combine(Root,".local/format-fixtures"); Assert.True(File.Exists(Path.Combine(folder,"HEIC-GPS.heic")),"Run scripts/create-format-fixtures.ps1");
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db")); var result=await new ScanService(TestMediaTools.ExifTool).ScanAsync(folder,db,null,CancellationToken.None);
        var heic=Assert.Single(result.Items,m=>m.Name=="HEIC-GPS.heic"); Assert.Equal(51.4778,heic.Latitude!.Value,4); Assert.Equal(-.0015,heic.Longitude!.Value,4);
        Assert.False(Assert.Single(result.Items,m=>m.Name=="HEIF-noGPS.heif").HasLocation);
        var cache=Temporary(); var preview=new PreviewService(cache); var image=await preview.GetAsync(heic.Path); Assert.NotNull(image); Assert.True(image.Width>=256);
        var thumb=await preview.GetAsync(heic.Path,true); Assert.NotNull(thumb); Assert.Equal(256,thumb.Width);
        Assert.Null(await preview.GetAsync(Path.Combine(folder,"Malformed.heic"))); Assert.Empty(Directory.GetFiles(Path.Combine(cache,"cache"),"*.tmp.jpg",SearchOption.TopDirectoryOnly));
    }
    [Theory][InlineData("MOV")][InlineData("MP4")][InlineData("M4V")][InlineData("MKV")][InlineData("AVI")][InlineData("WebM")][InlineData("webp")][InlineData("tiff")]
    public async Task ValidGeneratedContainersDecode(string extension)
    {
        var source=Path.Combine(Root,".local/format-fixtures/Generated."+extension); Assert.True(File.Exists(source));
        Assert.NotNull(await new PreviewService(Temporary()).GetAsync(source,true));
    }
    [Fact] public void BrandingIcoContainsAllSizesAndTransparency()
    {
        var bytes=File.ReadAllBytes(Path.Combine(Root,"assets/branding/GeoStack.ico")); Assert.Equal(7,BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        int[] expected=[16,24,32,48,64,128,256];
        for(var i=0;i<7;i++)
        {
            var entry=6+i*16; Assert.Equal(expected[i],bytes[entry]==0?256:bytes[entry]);
            var offset=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry+12)); Assert.Equal(new byte[]{137,80,78,71},bytes[offset..(offset+4)]);
            Assert.Equal(6,bytes[offset+25]);
        }
    }
    [Fact] public void ApplicationExecutableEmbedsSevenIconResources()
    {
        var file=Environment.GetEnvironmentVariable("GEOSTACK_EXE") ?? Path.Combine(Root,"src/GeoStack.App/bin/Release/net10.0-windows/GeoStack.exe");
        using var stream=File.OpenRead(file); using var pe=new PEReader(stream);
        var directory=pe.GetSectionData(pe.PEHeaders.PEHeader!.ResourceTableDirectory.RelativeVirtualAddress).GetContent().ToArray();
        uint U32(int p)=>BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(p));
        int Count(int p)=>BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(p+12))+BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(p+14));
        var group=-1;
        for(var i=0;i<Count(0);i++) if(U32(16+i*8)==14) group=(int)(U32(20+i*8)&0x7fffffff);
        Assert.True(group>=0); var name=(int)(U32(group+20)&0x7fffffff); var data=(int)(U32(name+20)&0x7fffffff);
        var payload=pe.GetSectionData((int)U32(data)).GetContent(0,(int)U32(data+4)).ToArray(); Assert.Equal(7,BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)));
    }
    [Fact] public async Task PreviewMemoryIsBoundedAndSourceChangesInvalidateDiskCache()
    {
        var directory=Temporary(); var data=Temporary(); var service=new PreviewService(data);
        for(var i=0;i<105;i++)
        {
            var file=Path.Combine(directory,i+".jpg"); File.Copy(Path.Combine(Root,".local/fixtures/Paris-01.jpg"),file);
            Assert.NotNull(await service.GetAsync(file,true));
        }
        Assert.InRange(service.MemoryEntries,1,PreviewService.MemoryEntryLimit);
        var count=Directory.GetFiles(Path.Combine(data,"cache"),"*.jpg").Length;
        var first=Path.Combine(directory,"0.jpg"); File.SetLastWriteTimeUtc(first,File.GetLastWriteTimeUtc(first).AddSeconds(2));
        Assert.NotNull(await service.GetAsync(first,true)); Assert.Equal(count+1,Directory.GetFiles(Path.Combine(data,"cache"),"*.jpg").Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(data,"cache"),"*.tmp.jpg"));
    }
    [Fact] public void FailedIndexBatchRollsBackMetadataAndFingerprints()
    {
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db"));
        IEnumerable<(MediaItem,long,long)> Broken() { yield return (new("C:/synthetic/first.jpg",0,0,"Photo"),1,1); throw new IOException("Synthetic interrupted batch"); }
        Assert.Throws<IOException>(()=>db.StoreMany(Broken())); Assert.Empty(db.ReadAll()); Assert.Empty(db.Fingerprints());
    }
    [Fact] public async Task PackagedLocationStructuresReadExpectedSigns()
    {
        var tool=TestMediaTools.ExifTool; var folder=Path.Combine(Root,".local/format-fixtures");
        var response=await ToolProcess.Run(tool,["-config","","-json","-n","-GPSLatitude","-GPSLongitude",Path.Combine(folder,"Generated.MOV"),Path.Combine(folder,"Generated.MP4")],CancellationToken.None);
        var items=ExifParser.Parse(response.Output); Assert.Equal(37.7749,items[0].Latitude!.Value,4); Assert.Equal(-122.4194,items[0].Longitude!.Value,4); Assert.Equal(-33.86,items[1].Latitude!.Value,4); Assert.Equal(151.2,items[1].Longitude!.Value,4);
    }
    [Fact] public void SmallBrandingExportsKeepTransparentMargins()
    {
        foreach(var size in new[]{16,32,256})
        {
            using var stream=File.OpenRead(Path.Combine(Root,$"assets/branding/GeoStack-{size}.png")); var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad); var frame=decoder.Frames[0];
            Assert.Equal(size,frame.PixelWidth); var converted=new System.Windows.Media.Imaging.FormatConvertedBitmap(frame,System.Windows.Media.PixelFormats.Bgra32,null,0); var pixels=new byte[size*size*4]; converted.CopyPixels(pixels,size*4,0);
            Assert.Equal(0,pixels[3]); Assert.True(Enumerable.Range(0,size*size).Count(i=>pixels[i*4+3]>0)>size*size/4);
        }
    }
    [Theory][InlineData("Ricoh-GR-III.DNG")][InlineData("Nikon-Z6.NEF")]
    public async Task LicensedCameraRawEmbeddedPreviewsDecode(string filename)
    {
        var source=Path.Combine(Root,".local/format-fixtures",filename); Assert.True(File.Exists(source));
        var service=new PreviewService(Temporary()); Assert.NotNull(await service.GetAsync(source,true)); Assert.NotNull(await service.GetAsync(source));
    }
    [Fact] public void WindowsPathAliasesDoNotCreateDuplicateIndexRows()
    {
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db"));
        db.Upsert(new("C:/synthetic/photo.jpg",1,2,"Photo")); db.Upsert(new(@"C:\synthetic\photo.jpg",3,4,"Photo"));
        Assert.Equal(3,Assert.Single(db.ReadAll()).Latitude);
    }
    [Fact] public async Task LargeSidebarSelectionIsPagedWithoutDroppingItems()
    {
        var model=new MainViewModel(Temporary()); var items=Enumerable.Range(0,10001).Select(i=>new MediaItem($"C:/synthetic/missing-{i:D5}.jpg",null,null,"Photo")).ToArray();
        await model.ShowItems(items,"Library"); Assert.Equal(120,model.VisibleItems.Count); Assert.Equal(84,model.PageCount);
        await model.ShowPage(83); Assert.Equal(41,model.VisibleItems.Count); Assert.Equal(items[9960].Path,model.VisibleItems[0].Item.Path); Assert.Equal(10001,model.Selection.Count);
    }
    [Fact] public void LiteralUiResourceKeysArePresentInEnglishSource()
    {
        var keys=System.Xml.Linq.XDocument.Load(Path.Combine(Root,"src/GeoStack.App/Resources/Strings.en-US.resx")).Root!.Elements("data").Select(e=>e.Attribute("name")!.Value).ToHashSet();
        foreach(var file in Directory.GetFiles(Path.Combine(Root,"src/GeoStack.App"),"*.cs"))
            foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file),"Strings\\.Get\\(\"([^\"]+)\"")) Assert.Contains(match.Groups[1].Value,keys);
    }
    [Fact] public async Task JpegExifOrientationProducesUprightBoundedThumbnail()
    {
        var source=Path.Combine(Root,".local/format-fixtures/Orientation-6.jpg");
        var result=Assert.IsAssignableFrom<BitmapSource>(await new PreviewService(Temporary()).GetAsync(source,true));
        Assert.True(result.PixelHeight>result.PixelWidth); Assert.InRange(result.PixelHeight,1,256);
    }
    [Fact] public async Task PrecancelledToolDoesNotStartAnExecutable()
    {
        using var token=new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>ToolProcess.Run("nonexistent-geostack-tool.exe",[],token.Token));
    }
    [Fact] public void OversizedCoordinateTextIsRejected() => Assert.False(ExifParser.TryIso6709("+"+new string('1',10000),out _,out _));
    [Fact] public void PortablePackageContainsRuntimeMediaToolsAndBranding()
    {
        var folder=Path.GetDirectoryName(Environment.GetEnvironmentVariable("GEOSTACK_EXE")) ?? Path.Combine(Root,"dist/GeoStack-portable-win-x64");
        foreach(var file in new[]{"GeoStack.exe","coreclr.dll","PresentationFramework.dll","geography/countries.geojson","geography/cities.geojson","tools/ExifTool/ExifTool.exe","tools/FFmpeg/ffmpeg.exe","branding/GeoStack.ico","licenses/THIRD-PARTY-NOTICES.TXT"}) Assert.True(File.Exists(Path.Combine(folder,file)),file);
        if (Environment.GetEnvironmentVariable("GEOSTACK_EXE") != null) Assert.False(Directory.Exists(Path.Combine(folder,"data")), "A new package must not contain a previous index, settings or cache");
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"dependencies.json")));
        var expected = new[]{"tools/ExifTool/ExifTool.exe","tools/FFmpeg/ffmpeg.exe","geography/countries.geojson","geography/cities.geojson"};
        var entries = manifest.RootElement.EnumerateArray().ToArray();
        Assert.Equal(expected.Order(), entries.Select(e=>e.GetProperty("File").GetString()!.Replace('\\','/')).Order());
        foreach (var entry in entries)
        {
            using var stream = File.OpenRead(Path.Combine(folder,entry.GetProperty("File").GetString()!));
            Assert.Equal(entry.GetProperty("Hash").GetString(),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)));
        }
    }
    [Fact] public async Task CyrillicFoldersAndFilenamesScanAndPreviewWithoutAnsiLoss()
    {
        var folder=Path.Combine(Temporary(),"\u0430\u043b\u044c\u0431\u043e\u043c"); Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(Root,".local/fixtures/Paris-01.jpg"),Path.Combine(folder,"\u0444\u043e\u0442\u043e.jpg"));
        File.Copy(Path.Combine(Root,".local/format-fixtures/HEIC-GPS.heic"),Path.Combine(folder,"\u043c\u0435\u0441\u0442\u043e.heic"));
        File.Copy(Path.Combine(Root,".local/format-fixtures/Nikon-Z6.NEF"),Path.Combine(folder,"\u043a\u0430\u043c\u0435\u0440\u0430.nef"));
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db")); var result=await new ScanService(TestMediaTools.ExifTool).ScanAsync(folder,db,null,CancellationToken.None);
        Assert.Equal(3,result.Items.Count); Assert.Empty(result.Errors); Assert.Equal(2,result.Items.Count(m=>m.HasLocation));
        var previews=new PreviewService(Temporary()); foreach(var item in result.Items) Assert.NotNull(await previews.GetAsync(item.Path,true));
    }
}
