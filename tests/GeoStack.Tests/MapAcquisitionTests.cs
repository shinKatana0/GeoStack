// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.App;
using GeoStack.Core;
using Xunit;
namespace GeoStack.Tests;
public sealed class MapAcquisitionTests
{
    private sealed class Source(byte[]? tile) : IRegionalMapSource
    {
        public long DownloadedBytes=>0;public string Identity=>"synthetic";public string Freshness=>"2026-01-01";
        public Task Initialize(CancellationToken token){token.ThrowIfCancellationRequested();return Task.CompletedTask;}
        public Task<byte[]?> Tile(int z,int x,int y,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(tile);}
        public void Dispose() { }
    }
    private sealed class Progress(Action<AcquisitionProgress> action) : IProgress<AcquisitionProgress>{public void Report(AcquisitionProgress value)=>action(value);}
    private static MapDownloadArea Area()=>new("test-area",null,1,1,14.39,50.06,14.45,50.11,false,true,false,[new(10,552,346),new(15,17697,11100)]);
    private static string Folder(){var p=Path.Combine(Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local"),"GeoStack-acquisition-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    [Fact] public async Task AtomicPreparedPackageHasAllFourStylesAndNoScriptingRuntime()
    {
        var folder=Folder();try
        {
            var map=await MapAcquisition.Prepare(Area(),folder,new Progress(_=>{}),TestContext.Current.CancellationToken,()=>new Source([]));
            Assert.Contains(Area().CoordinateLabel,map.Name);Assert.Equal(3,map.Variants!.Count);Assert.All(new[]{map}.Concat(map.Variants),m=>{Assert.True(MapProbe.CanDecode(m.ReadTile(15,17697,11100)!));Assert.NotEmpty(m.Source);});
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(map.Path)!,"manifest.json")));Assert.Empty(Directory.GetDirectories(folder,".pending-*"));
        }finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task CancellationCleansOnlyItsOwnTemporaryDirectory()
    {
        var folder=Folder();try
        {
            File.WriteAllText(Path.Combine(folder,"external-map.txt"),"preserve");using var cancellation=new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>MapAcquisition.Prepare(Area(),folder,new Progress(_=>cancellation.Cancel()),cancellation.Token,()=>new Source([])));
            Assert.Empty(Directory.GetDirectories(folder));Assert.Equal("preserve",File.ReadAllText(Path.Combine(folder,"external-map.txt")));
        }finally{Directory.Delete(folder,true);}
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task MissingOrCorruptSourceNeverBecomesInstalled(bool corrupt)
    {
        var folder=Folder();try
        {
            var failure=await Assert.ThrowsAsync<InvalidDataException>(()=>MapAcquisition.Prepare(Area(),folder,new Progress(_=>{}),TestContext.Current.CancellationToken,()=>new Source(corrupt?[26,255]:null)));
            try{Assert.Empty(Directory.GetFileSystemEntries(folder));Assert.Empty(new MapCatalog(folder).Packages);}
            catch
            {
                var output=TestContext.Current.TestOutputHelper;output?.WriteLine("Worker cleanup: "+failure.Data["PendingCleanup"]);
                var entries=Directory.GetFileSystemEntries(folder);output?.WriteLine("Entries after failed cleanup assertion: "+entries.Length);
                foreach(var entry in entries)
                {
                    output?.WriteLine("Remainder: "+entry+"; Exists="+Directory.Exists(entry));
                    try{output?.WriteLine("Attributes: "+File.GetAttributes(entry)+"; children: "+string.Join(",",Directory.GetFileSystemEntries(entry)));}catch(IOException ex){output?.WriteLine("Read failure "+ex.GetType().Name);}
                }
                throw;
            }
        }finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task UnwritableStorageFailsBeforeOpeningProvider()
    {
        var folder=Folder();try
        {
            var file=Path.Combine(folder,"not-directory");File.WriteAllText(file,"preserve");var calls=0;
            await Assert.ThrowsAsync<IOException>(()=>MapAcquisition.Prepare(Area(),file,new Progress(_=>{}),TestContext.Current.CancellationToken,()=>{calls++;return new Source([]);}));Assert.Equal(0,calls);
        }finally{Directory.Delete(folder,true);}
    }
}
