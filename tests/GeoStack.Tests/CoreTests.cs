// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class CoreTests
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    private static string Temporary() { var dir = TestStorage.Create(Root); Directory.CreateDirectory(dir); return dir; }
    [Theory]
    [InlineData("a.JPG", "Photo")][InlineData("a.HEIC", "Photo")][InlineData("a.heif", "Photo")][InlineData("a.webp", "Photo")][InlineData("a.dng", "Photo")][InlineData("a.NEF", "Photo")][InlineData("a.cr3", "Photo")][InlineData("a.tiff", "Photo")][InlineData("a.PNG", "Photo")][InlineData("a.MOV", "Video")][InlineData("a.mp4", "Video")][InlineData("a.m4v", "Video")][InlineData("a.mkv", "Video")][InlineData("a.avi", "Video")][InlineData("a.webm", "Video")][InlineData("a.txt", null)]
    public void DetectsFormats(string path, string? expected) => Assert.Equal(expected, MediaTypes.Detect(path));
    [Fact] public void ParsesNumericGpsAndCaptureDate()
    {
        var m = Assert.Single(ExifParser.Parse("""[{"SourceFile":"C:/test.jpg","GPSLatitude":-33.86,"GPSLongitude":151.2,"DateTimeOriginal":"2026:01:02 03:04:05"}]"""));
        Assert.Equal(-33.86, m.Latitude); Assert.Equal(151.2, m.Longitude); Assert.True(m.HasLocation); Assert.NotNull(m.Captured);
    }
    [Theory][InlineData("{}")][InlineData("{\"GPSLatitude\":91,\"GPSLongitude\":20}")][InlineData("{\"GPSLatitude\":5,\"GPSLongitude\":181}")][InlineData("{\"GPSLatitude\":\"NaN\",\"GPSLongitude\":4}")][InlineData("{\"GPSLatitude\":0}")]
    public void RejectsMissingAndInvalidGps(string payload) => Assert.False(Assert.Single(ExifParser.Parse("[" + payload + "]")).HasLocation);
    [Fact] public void ZeroCoordinatesAreValid() => Assert.True(Geo.IsValid(0, 0));
    [Fact] public void MalformedExifIsReported() => Assert.ThrowsAny<System.Text.Json.JsonException>(() => ExifParser.Parse("broken"));
    [Theory][InlineData(0,0)][InlineData(55.75,37.62)][InlineData(-33.86,151.2)][InlineData(80,-179)]
    public void ProjectionRoundTrips(double lat, double lon) { var p = Geo.Project(lat, lon); var r = Geo.Unproject(p.X, p.Y); Assert.Equal(lat, r.Latitude, 8); Assert.Equal(lon, r.Longitude, 8); }
    [Fact] public void ProjectionClampsPoles() { Assert.True(double.IsFinite(Geo.Project(90, 180).Y)); Assert.Equal(.5, Geo.Project(0,0).X); }
    [Fact] public void ClustersChangeWithZoomAndExcludeMissingGps()
    {
        MediaItem[] items = [new("a.jpg", 48.85, 2.35, "Photo"), new("b.jpg", 48.86, 2.36, "Photo"), new("c.jpg", null, null, "Photo")];
        Assert.Single(Geo.Cluster(items, 1000)); Assert.Equal(2, Geo.Cluster(items, 10000000).Count);
    }
    [Fact] public void SqlitePersistsUpdatesAndUnicodePaths()
    {
        var path = Path.Combine(Temporary(), "index.db"); var item = new MediaItem("C:/synthetic/\u041C\u043E\u0441\u043A\u0432\u0430's photo.jpg", 55, 37, "Photo");
        using (var db = new MediaIndex(path)) { db.Upsert(item); db.Upsert(item with { Latitude = 56 }); }
        using (var reopen = new MediaIndex(path)) { var actual = Assert.Single(reopen.ReadAll()); Assert.Equal(56, actual.Latitude); Assert.Equal(item.Path, actual.Path); }
        Assert.Equal("SQLite format 3", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 15));
    }
    [Theory][InlineData("System")][InlineData("Light")][InlineData("Dark")]
    public void PreferencesPersist(string theme) { var path = Path.Combine(Temporary(), "settings.json"); new Preferences("ru-RU",theme).Save(path); Assert.Equal(new("ru-RU",theme), Preferences.Load(path)); }
    [Fact] public void CorruptPreferencesRecover() { var path = Path.Combine(Temporary(), "bad.json"); File.WriteAllText(path,"{"); Assert.Equal(new(), Preferences.Load(path)); }
    [Fact] public void LocalizationResourcesHaveSameNonemptyKeys()
    {
        Dictionary<string,string> Read(string name) => XDocument.Load(Path.Combine(Root,"src/GeoStack.App/Resources/"+name+".resx")).Root!.Elements("data").ToDictionary(x => x.Attribute("name")!.Value,x => x.Element("value")!.Value);
        var en = Read("Strings.en-US"); var ru = Read("Strings.ru-RU"); Assert.Equal(en.Keys.Order(),ru.Keys.Order()); Assert.All(ru.Values, x => Assert.False(string.IsNullOrWhiteSpace(x)));
        GeoStack.App.Strings.SetLanguage("ru-RU"); Assert.Equal("Открыть папку", GeoStack.App.Strings.Get("OpenFolder")); GeoStack.App.Strings.SetLanguage("en-US"); Assert.Equal("Open folder",GeoStack.App.Strings.Get("OpenFolder"));
    }
    [Fact] public async Task PrecancelledScanStopsWithoutWrites()
    {
        using var db = new MediaIndex(Path.Combine(Temporary(),"index.db")); using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ScanService("missing.exe").ScanAsync(Root,db,null,token.Token)); Assert.Empty(db.ReadAll());
    }
    [Fact] public async Task ActiveProcessCancellationKillsChild()
    {
        using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)); var timer = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ToolProcess.Run("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"], token.Token)); Assert.True(timer.Elapsed < TimeSpan.FromSeconds(8));
    }
    private sealed class ImmediateProgress(Action<ScanProgress> action) : IProgress<ScanProgress> { public void Report(ScanProgress value) => action(value); }
    [Fact] public async Task CancellationDuringDiscoveryStopsScan()
    {
        var directory=Temporary(); Directory.CreateDirectory(Path.Combine(directory,"nested")); File.WriteAllText(Path.Combine(directory,"a.jpg"),"synthetic");
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db")); using var token=new CancellationTokenSource();
        var progress=new ImmediateProgress(_=>token.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ScanService("missing.exe").ScanAsync(directory,db,progress,token.Token)); Assert.Empty(db.ReadAll());
    }
    [Fact] public async Task MissingFolderProducesScanError()
    {
        using var db = new MediaIndex(Path.Combine(Temporary(),"index.db")); var result = await new ScanService("missing.exe").ScanAsync(Path.Combine(Temporary(),"absent"),db,null,CancellationToken.None); Assert.NotEmpty(result.Errors); Assert.Empty(result.Items);
    }
    [Fact] public async Task MissingToolProducesIndexedError()
    {
        var root=Temporary(); File.WriteAllText(Path.Combine(root,"bad.jpg"),"synthetic invalid media"); using var db = new MediaIndex(Path.Combine(root,"index.db"));
        var result = await new ScanService(Path.Combine(root,"missing.exe")).ScanAsync(root,db,null,CancellationToken.None); Assert.NotEmpty(result.Errors); Assert.NotNull(Assert.Single(db.ReadAll()).Error);
    }
    [Fact] public async Task SyntheticJpegAndMovGpsExtractionIsReadOnly()
    {
        var fixtures=Path.Combine(Root,".local/fixtures"); Assert.True(Directory.Exists(fixtures),"Run scripts/create-fixtures.ps1 first");
        var before=Directory.GetFiles(fixtures,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        using var db=new MediaIndex(Path.Combine(Temporary(),"index.db"));
        var result=await new ScanService(Path.Combine(Root,"tools/ExifTool/ExifTool.exe")).ScanAsync(fixtures,db,null,CancellationToken.None);
        Assert.Contains(result.Items,m=>m.Name=="Paris-01.jpg" && m.HasLocation && Math.Abs(m.Latitude!.Value-48.8566)<.0001);
        Assert.Contains(result.Items,m=>m.Name=="Moscow.mov" && m.HasLocation && Math.Abs(m.Longitude!.Value-37.6173)<.0001);
        Assert.Contains(result.Items,m=>m.Name=="No-GPS.png" && !m.HasLocation);
        Assert.Contains(result.Items,m=>m.Name=="Corrupt.heic" && m.Error!=null);
        Assert.All(before,p=>Assert.Equal(p.Value,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))));
    }
}
