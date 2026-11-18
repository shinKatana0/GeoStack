// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Security.Cryptography;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class MediaImportTests
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;
    private static string Temporary() { var p=Path.GetFullPath(TestStorage.Create(Root));Directory.CreateDirectory(p);return p; }
    private static string Fixture(string name) => Path.Combine(Root,".local/format-fixtures",name);
    private static ScanService Scanner => new(Path.Combine(Root,"tools/ExifTool/ExifTool.exe"));

    [Theory]
    [InlineData("Generated.jpg")][InlineData("HEIC-GPS.heic")][InlineData("Generated.MOV")][InlineData("Generated.MP4")]
    public async Task IndividualSupportedMediaImportsReadOnly(string name)
    {
        var path=Fixture(name);var before=SHA256.HashData(File.ReadAllBytes(path));
        using var index=new MediaIndex(Path.Combine(Temporary(),"index.db"));
        var result=await Scanner.ScanAsync(new[]{path},index,null,TestContext.Current.CancellationToken);
        Assert.Equal(1,result.Added);Assert.Single(index.ReadAll());Assert.Null(result.Items[0].Error);
        if(name=="HEIC-GPS.heic")Assert.True(result.Items[0].HasLocation);
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact] public async Task MixedNestedOverlappingImportsPreserveIndependentSourcesAndRestart()
    {
        var work=Temporary();var first=Path.Combine(work,"first");var second=Path.Combine(work,"second","nested");Directory.CreateDirectory(first);Directory.CreateDirectory(second);
        var a=Path.Combine(first,"same.jpg");var b=Path.Combine(second,"same.jpg");File.Copy(Fixture("Generated.jpg"),a);File.Copy(Fixture("Generated.jpg"),b);File.WriteAllText(Path.Combine(second,"notes.txt"),"not media");
        var database=Path.Combine(work,"index.db");
        using(var index=new MediaIndex(database))
        {
            await Scanner.ScanAsync(new[]{a},index,null,TestContext.Current.CancellationToken);
            var added=await Scanner.ScanAsync(new[]{first,Path.GetDirectoryName(second)!,second,b,a.ToUpperInvariant()},index,null,TestContext.Current.CancellationToken);
            Assert.Equal(1,added.Added);Assert.Equal(1,added.Reused);Assert.Equal(2,index.ReadAll().Count);Assert.True(added.Skipped>=1);
            var repeat=await Scanner.ScanAsync(new[]{first,second},index,null,TestContext.Current.CancellationToken);Assert.Equal(0,repeat.Added);Assert.Equal(2,repeat.Reused);
            File.SetLastWriteTimeUtc(a,File.GetLastWriteTimeUtc(a).AddSeconds(2));
            var changed=await Scanner.ScanAsync(new[]{a.ToUpperInvariant()},index,null,TestContext.Current.CancellationToken);Assert.Equal(a,Assert.Single(changed.Items).Path);Assert.Equal(0,changed.Reused);Assert.Equal(2,index.ReadAll().Count);
        }
        using var restart=new MediaIndex(database);Assert.Equal(2,restart.ReadAll().Count);
    }

    [Fact] public void DiscoverySkipsMissingUnsupportedAndLockedInputs()
    {
        var folder=Temporary();var locked=Path.Combine(folder,"locked.jpg");File.WriteAllText(locked,"synthetic");File.WriteAllText(Path.Combine(folder,"notes.txt"),"unsupported");
        using var hold=File.Open(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var result=MediaInputs.Discover(new[]{folder,Path.Combine(folder,"missing.mov")},TestContext.Current.CancellationToken);
        Assert.Empty(result.Files);Assert.True(result.Skipped>=3);Assert.NotEmpty(result.Errors);
    }

    [Fact] public async Task ReadOnlyUnicodeSourcesAndEmptyFoldersAreSafe()
    {
        var folder=Temporary();var path=Path.Combine(folder,"caf\u00e9-\u65e5\u672c.jpg");File.Copy(Fixture("Generated.jpg"),path);File.SetAttributes(path,FileAttributes.ReadOnly);
        using var index=new MediaIndex(Path.Combine(Temporary(),"index.db"));var result=await Scanner.ScanAsync(new[]{path},index,null,TestContext.Current.CancellationToken);Assert.Equal(path,Assert.Single(result.Items).Path);
        Assert.Empty(MediaInputs.Discover(new[]{Temporary()},TestContext.Current.CancellationToken).Files);
    }

    private sealed class CancelProgress(CancellationTokenSource stop):IProgress<ScanProgress>
    { public void Report(ScanProgress p){if(p.Indexed>=40)stop.Cancel();} }
    [Fact] public async Task CancellationPreservesCommittedBatchesAndExistingLibrary()
    {
        var folder=Temporary();for(var i=0;i<85;i++)File.Copy(Fixture("Generated.jpg"),Path.Combine(folder,i.ToString("D3")+".jpg"));
        using var index=new MediaIndex(Path.Combine(Temporary(),"index.db"));using var stop=new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Scanner.ScanAsync(new[]{folder},index,new CancelProgress(stop),stop.Token));
        Assert.Equal(40,index.ReadAll().Count);Assert.Equal(40,index.Fingerprints().Count);
        await Scanner.ScanAsync(new[]{folder},index,null,TestContext.Current.CancellationToken);Assert.Equal(85,index.ReadAll().Count);
    }

    [Fact] public async Task DirectoryJunctionLoopIsSkippedWithoutChangingItsTarget()
    {
        var folder=Temporary();File.Copy(Fixture("Generated.jpg"),Path.Combine(folder,"one.jpg"));var link=Path.Combine(folder,"loop");
        var create=await ToolProcess.Run("cmd.exe",new[]{"/c","mklink","/J",link,folder},TestContext.Current.CancellationToken);
        Assert.True(create.ExitCode==0,create.Output+create.Error);Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
        var result=MediaInputs.Discover(new[]{folder,link},TestContext.Current.CancellationToken);Assert.Single(result.Files);Assert.Equal(2,result.Skipped);
    }

    [Fact] public async Task ViewModelRejectsConcurrentImportsAndRefreshesLibraryOnce()
    {
        var folder=Temporary();for(var i=0;i<45;i++)File.Copy(Fixture("Generated.jpg"),Path.Combine(folder,i+".jpg"));
        var model=new GeoStack.App.MainViewModel(Temporary());var changes=0;model.LibraryChanged+=()=>changes++;
        var running=model.ImportAsync(new[]{folder});Assert.True(model.IsScanning);
        await model.ImportAsync(new[]{Fixture("Generated.MOV")});await running;
        Assert.Equal(45,model.Items.Count);Assert.False(model.IsScanning);Assert.Equal(1,changes);
    }

    [Fact] public async Task TenThousandCachedInputsDoNotLaunchMetadataExtraction()
    {
        var folder=Temporary();var records=new List<(MediaItem,long,long)>();
        for(var i=0;i<10000;i++)
        {
            var path=Path.Combine(folder,i.ToString("D5")+".jpg");File.WriteAllBytes(path,[]);var info=new FileInfo(path);
            records.Add((new MediaItem(path,null,null,"Photo"),info.Length,info.LastWriteTimeUtc.Ticks));
        }
        using var index=new MediaIndex(Path.Combine(Temporary(),"index.db"));index.StoreMany(records);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var result=await new ScanService("must-not-be-launched.exe").ScanAsync(new[]{folder},index,null,TestContext.Current.CancellationToken);
        timer.Stop();Assert.Equal(10000,result.Reused);Assert.Equal(0,result.Added);Assert.Empty(result.Errors);
        File.WriteAllText(Path.Combine(Root,".local/import-10000-benchmark.json"),System.Text.Json.JsonSerializer.Serialize(new{Status="PASS",Entries=10000,Milliseconds=timer.Elapsed.TotalMilliseconds,SourceContentBytes=0,Reused=result.Reused,Scope="Cached discovery/stat/index rescan with zero-byte synthetic files; not a fresh metadata, thumbnail or GUI throughput claim"}));
    }
}
