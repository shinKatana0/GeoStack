// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class CatalogRemovalTests
{
    private static string Root=>Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;
    private static string Temporary(){var p=Path.GetFullPath(TestStorage.Create(Root));Directory.CreateDirectory(p);return p;}
    private static ScanService Scanner=>new(Path.Combine(Root,"tools/ExifTool/ExifTool.exe"));
    private sealed class InlineProgress(Action<ScanProgress> action):IProgress<ScanProgress>{public void Report(ScanProgress value)=>action(value);}

    [Theory]
    [InlineData("Generated.jpg")][InlineData("HEIC-GPS.heic")][InlineData("Generated.MOV")][InlineData("Generated.MP4")]
    public async Task RemovalPersistsThroughFolderScanAndDirectFileRestoresOnlyThatIdentity(string name)
    {
        var folder=Temporary();var file=Path.Combine(folder,name);File.Copy(Path.Combine(Root,".local/format-fixtures",name),file);
        var before=SHA256.HashData(File.ReadAllBytes(file));var database=Path.Combine(folder,"index.db");
        using(var index=new MediaIndex(database)){await Scanner.ScanAsync(folder,index,null,TestContext.Current.CancellationToken);index.Exclude(file);Assert.Empty(index.ReadAll());Assert.Empty(index.Fingerprints());}
        using(var index=new MediaIndex(database))
        {
            await new ScanService("must-not-start.exe").ScanAsync(folder,index,null,TestContext.Current.CancellationToken);
            Assert.Empty(index.ReadAll());Assert.Contains(file,index.Exclusions());
            await Scanner.ScanAsync(file,index,null,TestContext.Current.CancellationToken);Assert.Single(index.ReadAll());Assert.Empty(index.Exclusions());
        }
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(file)));
    }

    [Fact] public void ConcurrentLateStoreCannotResurrectRemovedMetadataOrFingerprints()
    {
        var folder=Temporary();var path=Path.Combine(folder,"photo.jpg");var database=Path.Combine(folder,"index.db");
        using var worker=new MediaIndex(database);using var writer=new MediaIndex(database);
        worker.Store(new(path,10,20,"Photo"),4,8);writer.Exclude(path);
        worker.StoreMany([(new(path,10,20,"Photo"),4,8)]);worker.Upsert(new(path,null,null,"Photo"));
        Assert.Empty(worker.ReadAll());Assert.Empty(worker.Fingerprints());Assert.Single(worker.Exclusions());
    }

    [Fact] public void NewerRemovalWinsOverAnEarlierExplicitRestoreSnapshot()
    {
        using var index=new MediaIndex(Path.Combine(Temporary(),"index.db"));var path=Path.Combine(Root,".local","identity.jpg");
        index.Exclude(path);var snapshot=index.ExclusionTokens();index.Exclude(path);
        index.RestoreExplicitFiles([path],snapshot);Assert.Contains(path,index.Exclusions());
        index.RestoreExplicitFiles([path]);Assert.Empty(index.Exclusions());
    }

    [Fact] public async Task RemovalDuringDiscoveryCannotBeUndoneByTheAlreadyRunningDirectImport()
    {
        var folder=Temporary();var path=Path.Combine(folder,"photo.jpg");File.Copy(Path.Combine(Root,".local/format-fixtures/Generated.jpg"),path);
        var database=Path.Combine(folder,"index.db");using var worker=new MediaIndex(database);using var writer=new MediaIndex(database);var removed=false;
        await Scanner.ScanAsync(path,worker,new InlineProgress(_=>{if(!removed){removed=true;writer.Exclude(path);}}),TestContext.Current.CancellationToken);
        Assert.True(removed);Assert.Empty(worker.ReadAll());Assert.Contains(path,worker.Exclusions());
    }

    [Fact] public void NormalizedWindowsPathsDoNotExcludeSameNamesInOtherDirectories()
    {
        var folder=Temporary();var first=Path.Combine(folder,"caf\u00e9","same.jpg");var second=Path.Combine(folder,"b","same.jpg");
        using var index=new MediaIndex(Path.Combine(folder,"index.db"));index.Upsert(new(first,null,null,"Photo"));index.Upsert(new(second,null,null,"Video"));
        index.Exclude(Path.Combine(folder,"caf\u00e9",".","same.jpg").ToUpperInvariant());Assert.Equal(second,Assert.Single(index.ReadAll()).Path);
        index.Upsert(new(first,1,2,"Photo"));Assert.Single(index.ReadAll());
        Assert.Equal(MediaInputs.NormalizePath(first),MediaInputs.NormalizePath(@"\\?\"+first));
    }

    [Fact] public async Task NoGpsIsNotEmptyAndRemovingLastPageReconcilesPagingAndLateThumbnails()
    {
        var folder=Temporary();using(var index=new MediaIndex(Path.Combine(folder,"index.db")))
            foreach(var i in Enumerable.Range(0,121))index.Upsert(new(Path.Combine(folder,i.ToString("D3")+".jpg"),null,null,i==120?"Video":"Photo"));
        var vm=new MainViewModel(folder);Assert.False(vm.IsEmpty);var loading=vm.ShowItems(vm.Items.Where(m=>!m.HasLocation),"Fixture without GPS",1);
        var removed=vm.Items.Last();await vm.RemoveFromLibraryAsync(removed);await loading;
        Assert.Equal(120,vm.Items.Count);Assert.Equal(1,vm.PageCount);Assert.Equal(0,vm.PageIndex);Assert.Equal(120,vm.VisibleItems.Count);
        Assert.DoesNotContain(vm.Selection,m=>m.Path==removed.Path);Assert.All(vm.VisibleItems,m=>Assert.False(m.Item.HasLocation));
        Assert.Equal(120,new MainViewModel(folder).Items.Count);
    }

    [Fact] public async Task RemovingFromStackUpdatesGeographicAndOffscreenCountsAndFinalEmptyState()
    {
        var folder=Temporary();using(var index=new MediaIndex(Path.Combine(folder,"index.db")))
        {index.Upsert(new(Path.Combine(folder,"a.jpg"),35,135,"Photo"));index.Upsert(new(Path.Combine(folder,"b.mov"),35,135,"Video"));}
        var vm=new MainViewModel(folder);await vm.ShowItems(vm.Items,"Fixture stack");await vm.RemoveFromLibraryAsync(vm.Items[0]);
        Assert.Single(Geo.Cluster(vm.Items,1000));Assert.Single(Geo.Cluster(vm.Items,1000)[0].Items);
        var offscreen=new OffscreenIndex(vm.Items,(_,_)=>new GeographicLocation(null,null,null,null));
        Assert.Equal(1,offscreen.Outside(new MapCamera(.05,.4,100000,800,600)).Sum(g=>g.Items.Count));
        await vm.RemoveFromLibraryAsync(vm.Items[0]);Assert.True(vm.IsEmpty);Assert.Empty(vm.VisibleItems);Assert.Empty(vm.Selection);Assert.Empty(new MainViewModel(folder).Items);
    }

    [Fact] public void EmptyHintAndContextActionsAreLocalizedAndDoNotObstructTheMap()
    {
        XNamespace n="http://schemas.microsoft.com/winfx/2006/xaml/presentation";XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var window=XDocument.Load(Path.Combine(Root,"src/GeoStack.App/MainWindow.xaml"));
        Assert.DoesNotContain(window.Descendants(),e=>(string?)e.Attribute(x+"Name") is "EmptyCard" or "EmptyOpenButton");
        var hint=window.Descendants(n+"Border").Single(e=>(string?)e.Attribute(x+"Name")=="HintOverlay");
        Assert.Equal("False",(string?)hint.Attribute("IsHitTestVisible"));Assert.Contains("IsEmpty",hint.Attribute("Visibility")!.Value);Assert.Equal("True",window.Root!.Attribute("AllowDrop")!.Value);
        foreach(var key in new[]{"CompactLibraryHint","RemoveFromLibrary","RemoveFromLibraryConfirm"})
        {var english=Strings.GetForLanguage(key,"en-US");var russian=Strings.GetForLanguage(key,"ru-RU");Assert.NotEqual(key,english);Assert.NotEqual(key,russian);Assert.NotEqual(english,russian);}
    }

    [Fact] public void ExistingSchemaAndCopyArtifactsRemainIntact()
    {
        var folder=Temporary();var source=Path.Combine(folder,"source.jpg");var sidecar=source+".xmp";var copy=Path.Combine(folder,"copied.jpg");var journal=Path.Combine(folder,"journal.json");
        foreach(var p in new[]{source,sidecar,copy,journal})File.WriteAllBytes(p,[1,2,3,4]);
        var hashes=new[]{source,sidecar,copy,journal}.ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));
        var database=Path.Combine(folder,"index.db");
        using(var old=new MediaIndex(database))
        {
            old.Store(new(source,1,2,"Photo"),4,8);
            // Construct the previous two-table schema without exposing SQL mutation as an application API.
            typeof(MediaIndex).GetMethod("Execute",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(old,["DROP TABLE exclusions;"]);
        }
        using(var upgraded=new MediaIndex(database)){Assert.Single(upgraded.ReadAll());Assert.Single(upgraded.Fingerprints());upgraded.Exclude(source);Assert.Empty(upgraded.ReadAll());}
        foreach(var p in hashes)Assert.Equal(p.Value,SHA256.HashData(File.ReadAllBytes(p.Key)));
    }

    [Fact] public async Task RemovalImmediatelyAfterBatchCommitFiltersTheResult()
    {
        var folder=Temporary();var first=Path.Combine(folder,"a.jpg");var second=Path.Combine(folder,"b.jpg");
        foreach(var file in new[]{first,second})File.Copy(Path.Combine(Root,".local/format-fixtures/Generated.jpg"),file);
        var database=Path.Combine(folder,"index.db");using var worker=new MediaIndex(database);using var writer=new MediaIndex(database);var removed=false;
        var result=await Scanner.ScanAsync(folder,worker,new InlineProgress(p=>{if(p.Committed!=null && !removed){removed=true;writer.Exclude(first);}}),TestContext.Current.CancellationToken);
        Assert.True(removed);Assert.Equal(second,Assert.Single(result.Items).Path);Assert.Single(worker.ReadAll());Assert.Single(worker.Fingerprints());
    }

    [Fact] public async Task DelayedRealThumbnailCannotRestoreRemovedSelection()
    {
        var folder=Temporary();var source=Path.GetFullPath(Path.Combine(Root,".local/format-fixtures/HEIC-GPS.heic"));
        var before=SHA256.HashData(File.ReadAllBytes(source));using(var index=new MediaIndex(Path.Combine(folder,"index.db")))index.Upsert(new(source,30,120,"Photo"));
        var vm=new MainViewModel(folder);
        var slots=(SemaphoreSlim)typeof(PreviewService).GetField("slots",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(vm.Previews)!;
        await slots.WaitAsync(TestContext.Current.CancellationToken);await slots.WaitAsync(TestContext.Current.CancellationToken);
        var loading=vm.ShowItems(vm.Items,"Fixture delayed thumbnail");var stale=Assert.Single(vm.VisibleItems);Assert.False(loading.IsCompleted);
        try{await vm.RemoveFromLibraryAsync(vm.Items[0]);Assert.True(vm.IsEmpty);Assert.Empty(vm.VisibleItems);}
        finally{slots.Release(2);}
        await loading;Assert.Null(stale.Thumbnail);Assert.Empty(vm.Selection);Assert.NotNull(await vm.Previews.GetAsync(source,true));
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(source)));
    }

    [Fact] public async Task InvalidInputDoesNotAbortAnOtherwiseValidFolderRescan()
    {
        var folder=Temporary();var source=Path.Combine(folder,"photo.jpg");File.Copy(Path.Combine(Root,".local/format-fixtures/Generated.jpg"),source);
        using var index=new MediaIndex(Path.Combine(folder,"index.db"));index.Exclude(source);
        var result=await new ScanService("must-not-start.exe").ScanAsync(["\0",folder],index,null,TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.Errors);Assert.Empty(index.ReadAll());Assert.Contains(source,index.Exclusions());
    }
}
