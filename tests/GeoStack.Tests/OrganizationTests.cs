// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class OrganizationTests
{
    private static string Temporary() { var root = Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory); var path = TestStorage.Create(root); Directory.CreateDirectory(path); return path; }
    private static ReverseGeocoder Geocoder() => new(Path.Combine(AppContext.BaseDirectory, "geography/countries.geojson"));
    [Fact] public void AdministrativeContainmentUsesParentsAndRejectsAmbiguousEdges()
    {
        var directory=Temporary(); var path=Path.Combine(directory,"boundaries.geojson");
        object Feature(string id,string level,string name,string? parent,double west,double east) => new { type="Feature", properties=new { id,level,name,name_en=name,name_ru=name+"-ru",parent_id=parent }, geometry=new { type="Polygon",coordinates=new[]{new[]{new[]{west,0d},new[]{east,0d},new[]{east,10d},new[]{west,10d},new[]{west,0d}}} } };
        File.WriteAllText(path,JsonSerializer.Serialize(new { type="FeatureCollection",features=new[]{Feature("test-country","country","Synthetic Country",null,0,20),Feature("test-city-a","city","Synthetic West","test-country",0,10),Feature("test-city-b","city","Synthetic East","test-country",10,20),Feature("test-district","district","Synthetic District","test-city-a",1,3)} }));
        var geo=new ReverseGeocoder(Path.Combine(directory,"absent.geojson"),path);
        Assert.Equal("test-city-a",geo.Resolve(5,2).City?.Id); Assert.Equal("test-city-b",geo.Resolve(5,15).City?.Id);
        Assert.Equal("Synthetic District-ru",geo.Resolve(5,2).District?.Display("ru-RU")); Assert.Null(geo.Resolve(5,10).City);
        Assert.Equal(new[]{"Synthetic Country","Synthetic West","Synthetic District"},geo.Resolve(5,2).Hierarchy("en-US","CountryCityDistrict"));
    }
    [Fact] public async Task EditedUnicodeFolderIsRevalidatedBeforeExecution()
    {
        var directory=Temporary();var source=Path.Combine(directory,"a.jpg");await File.WriteAllTextAsync(source,"original",TestContext.Current.CancellationToken);var root=Temporary();
        var plan=await GeographicCopy.Plan([new(source,null,null,"Photo")],Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken);
        plan[0].RelativeDirectory="\u0420\u0443\u0447\u043d\u0430\u044f/\u041f\u0430\u043f\u043a\u0430";
        await GeographicCopy.Recheck(plan,root,TestContext.Current.CancellationToken);Assert.Contains("\u0420\u0443\u0447\u043d\u0430\u044f",plan[0].Destination);
        var results=await GeographicCopy.Execute(plan,root,Temporary(),null,TestContext.Current.CancellationToken);Assert.Equal(CopyState.Completed,results[0].Status);Assert.Equal("original",await File.ReadAllTextAsync(results[0].Destination,TestContext.Current.CancellationToken));
    }
    [Theory][InlineData(35.68,139.76,"Japan")][InlineData(47.37,8.54,"Switzerland")][InlineData(52.23,21.01,"Poland")]
    public void CountryContainmentDoesNotInventCities(double latitude, double longitude, string country)
    {
        var location = Geocoder().Resolve(latitude, longitude); Assert.Equal(country, location.Country?.Display("en-US")); Assert.Null(location.City); Assert.Null(location.District);
    }
    [Fact] public void MissingDatasetAndGpsResolveConservatively() { var geocoder = new ReverseGeocoder(Path.Combine(Temporary(), "absent.geojson")); Assert.Null(geocoder.Resolve(35,139).Country); Assert.Empty(geocoder.Resolve(null,null).Hierarchy("en-US", "CountryCity")); }
    [Theory][InlineData("CON", "_CON")][InlineData("nul.jpg", "_nul.jpg")][InlineData("bad:name?", "bad_name_")][InlineData("..", "_")][InlineData(" city. ", "city")]
    public void FolderComponentsAreWindowsSafe(string source, string expected) => Assert.Equal(expected, GeographicCopy.Component(source));
    [Theory][InlineData("../escape")][InlineData("Japan/../../escape")][InlineData("C:/escape")][InlineData("Japan/CON")]
    public void TraversalAndReservedFoldersAreRejected(string relative) => Assert.Throws<IOException>(() => GeographicCopy.Destination(Temporary(), relative, "a.jpg"));
    [Fact] public async Task MixedLocationsCopyIndividuallyWithChecksumsAndPersistentJournal()
    {
        var sources = Temporary(); var root = Temporary(); var journal = Temporary();
        var paths = new[] { Path.Combine(sources,"Tokyo.jpg"), Path.Combine(sources,"Zurich.mov"), Path.Combine(sources,"unknown.heic") };
        foreach (var path in paths) await File.WriteAllBytesAsync(path, Enumerable.Range(0,300000).Select(i => (byte)i).ToArray(), TestContext.Current.CancellationToken);
        var before = await Task.WhenAll(paths.Select(p => GeographicCopy.Hash(p, TestContext.Current.CancellationToken)));
        MediaItem[] items = [new(paths[0],35.68,139.76,"Photo"), new(paths[1],47.37,8.54,"Video"), new(paths[2],null,null,"Photo")];
        var plan = await GeographicCopy.Plan(items, Geocoder(), root, "en-US", "CountryCityDistrict", false, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "Japan", "Switzerland", "Unknown Location" }, plan.Select(p => p.RelativeDirectory));
        var results = await GeographicCopy.Execute(plan, root, journal, null, TestContext.Current.CancellationToken);
        Assert.All(results, r => Assert.Equal(CopyState.Completed, r.Status));
        for (var i = 0; i < paths.Length; i++) { Assert.Equal(before[i], await GeographicCopy.Hash(paths[i], TestContext.Current.CancellationToken)); Assert.Equal(before[i], await GeographicCopy.Hash(results[i].Destination, TestContext.Current.CancellationToken)); }
        Assert.Equal(3, GeographicCopy.ReadJournal(journal).Count(r => r.Status == CopyState.Completed));
        Assert.Empty(Directory.GetFiles(root,"*.partial",SearchOption.AllDirectories));
    }
    [Fact] public async Task IdenticalContentIsSkippedAndConflictsAreRenamed()
    {
        var source = Path.Combine(Temporary(), "same.jpg"); await File.WriteAllTextAsync(source,"original",TestContext.Current.CancellationToken); var root = Temporary(); var journal = Temporary();
        MediaItem[] items = [new(source,35.68,139.76,"Photo")];
        var first = await GeographicCopy.Plan(items,Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken); await GeographicCopy.Execute(first,root,journal,null,TestContext.Current.CancellationToken);
        var identical = await GeographicCopy.Plan(items,Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken); Assert.Equal(CopyState.Identical, identical[0].State); Assert.False(identical[0].Include);
        await File.WriteAllTextAsync(source,"different",TestContext.Current.CancellationToken); var conflict = await GeographicCopy.Plan(items,Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken); Assert.Equal(CopyState.Rename,conflict[0].State); Assert.EndsWith("same (1).jpg",conflict[0].Destination);
    }
    [Fact] public async Task FileAppearingAfterPreviewNeverOverwritesAndSourceChangeFails()
    {
        var source = Path.Combine(Temporary(),"a.jpg"); await File.WriteAllTextAsync(source,"original",TestContext.Current.CancellationToken); var root = Temporary(); var journal = Temporary();
        var plan = await GeographicCopy.Plan([new(source,35.68,139.76,"Photo")],Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(plan[0].Destination)!); var occupied = plan[0].Destination; await File.WriteAllTextAsync(occupied,"new file",TestContext.Current.CancellationToken);
        var results = await GeographicCopy.Execute(plan,root,journal,null,TestContext.Current.CancellationToken); Assert.Equal(CopyState.Completed,results[0].Status); Assert.Equal("new file",await File.ReadAllTextAsync(occupied,TestContext.Current.CancellationToken)); Assert.NotEqual(occupied,results[0].Destination);
        var changed = await GeographicCopy.Plan([new(source,35.68,139.76,"Photo")],Geocoder(),Temporary(),"en-US","CountryCity",false,TestContext.Current.CancellationToken); await File.WriteAllTextAsync(source,"changed",TestContext.Current.CancellationToken);
        var failure = await GeographicCopy.Execute(changed,Path.GetDirectoryName(Path.GetDirectoryName(changed[0].Destination))!,journal,null,TestContext.Current.CancellationToken); Assert.Equal(CopyState.Failed,failure[0].Status);
    }
    [Fact] public async Task CancellationAndPartialFailureNeverReportSuccess()
    {
        var root = Temporary(); var source = Path.Combine(Temporary(),"a.jpg"); await File.WriteAllTextAsync(source,"source",TestContext.Current.CancellationToken);
        var plan = await GeographicCopy.Plan([new(source,35.68,139.76,"Photo"),new(Path.Combine(Temporary(),"missing.mov"),35.68,139.76,"Video")],Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var results = await GeographicCopy.Execute(plan,root,Temporary(),null,cancelled.Token);
        Assert.Equal(CopyState.Cancelled,results[0].Status); Assert.Equal(CopyState.Missing,results[1].Status); Assert.False(File.Exists(plan[0].Destination));
    }
    [Fact] public async Task CorruptTrailingJournalEntryDoesNotHideCompletedRecords()
    {
        var folder = Temporary(); var path = Path.Combine(folder,"operation.jsonl"); var entry = new CopyJournalEntry(Guid.NewGuid(),DateTime.UtcNow,"source","destination",1,CopyState.Completed,"SHA",null);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(entry)+"\n{unfinished",TestContext.Current.CancellationToken); Assert.Equal(entry,Assert.Single(GeographicCopy.ReadJournal(folder)));
    }
    [Fact] public async Task LargeFileAndLockedSourceProduceAccuratePartialResults()
    {
        var directory=Temporary();var large=Path.Combine(directory,"large.mov");var locked=Path.Combine(directory,"locked.jpg");
        using(var file=File.Create(large)) file.SetLength(24L*1024*1024);
        await File.WriteAllTextAsync(locked,"locked",TestContext.Current.CancellationToken);
        var before=await GeographicCopy.Hash(large,TestContext.Current.CancellationToken);var root=Temporary();var journal=Temporary();
        var plan=await GeographicCopy.Plan([new(large,35.68,139.76,"Video"),new(locked,47.37,8.54,"Photo")],Geocoder(),root,"en-US","CountryCity",false,TestContext.Current.CancellationToken);
        using var held=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None);
        var results=await GeographicCopy.Execute(plan,root,journal,null,TestContext.Current.CancellationToken);
        Assert.Equal(CopyState.Completed,results[0].Status);Assert.Equal(CopyState.Failed,results[1].Status);Assert.NotNull(results[1].Failure);
        Assert.Equal(before,await GeographicCopy.Hash(large,TestContext.Current.CancellationToken));Assert.Equal(before,await GeographicCopy.Hash(results[0].Destination,TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(root,"*.partial",SearchOption.AllDirectories));Assert.Equal(2,GeographicCopy.ReadJournal(journal).Count(r=>r.Status is CopyState.Completed or CopyState.Failed));
    }
    [Fact] public async Task DestinationJunctionIsRefusedBeforeAnyFileIsCopied()
    {
        var root=Temporary();var target=Path.GetFullPath(Temporary());var link=Path.GetFullPath(Path.Combine(root,"junction"));
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var argument in new[]{"/c","mklink","/J",link,target}) start.ArgumentList.Add(argument);
        using var process=System.Diagnostics.Process.Start(start)!;var diagnostic=await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);await process.WaitForExitAsync(TestContext.Current.CancellationToken);Assert.True(process.ExitCode==0,diagnostic);
        Assert.True((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0);
        Assert.Throws<IOException>(()=>GeographicCopy.Destination(link,"Japan","a.jpg"));Assert.Empty(Directory.GetFiles(target));
    }
    [Fact] public void FolderPreferenceIsIndependentOfInterfaceLanguage()
    {
        var path = Path.Combine(Temporary(),"settings.json"); var settings = new Preferences("ru-RU","Dark"); settings.Save(path); Assert.Equal("en-US",Preferences.Load(path).FolderLanguage);
        (settings with { FolderLanguage="ru-RU", Language="en-US" }).Save(path); Assert.Equal("ru-RU",Preferences.Load(path).FolderLanguage);
    }
}
