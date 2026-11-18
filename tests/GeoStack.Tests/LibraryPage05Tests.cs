// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using GeoStack.App;
using GeoStack.Core;
using Xunit;
namespace GeoStack.Tests;
public sealed class LibraryPage05Tests
{
    [Fact] public async Task UnchangedImportDoesNotRepublishTheLibraryOrRestartMetadataTools()
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local","unchanged-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"synthetic.jpg");File.WriteAllBytes(file,[1,2,3]);var info=new FileInfo(file);
            using(var index=new MediaIndex(Path.Combine(folder,"index.db")))index.Store(new MediaItem(file,null,null,"Photo"),info.Length,info.LastWriteTimeUtc.Ticks);
            var vm=new MainViewModel(folder);var previous=vm.Items;var notifications=0;vm.LibraryChanged+=()=>notifications++;
            await vm.ImportAsync([file]);Assert.False(vm.LastImportChanged);Assert.Equal(0,notifications);Assert.Same(previous,vm.Items);Assert.Equal(0,vm.LastScanMeasurements.MetadataProcesses);Assert.False(vm.IsScanning);Assert.Equal(1,vm.ScanFraction);
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task BackgroundRefreshCanPreservePageAndNoGpsFilter()
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local","page-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var vm=new MainViewModel(folder);var items=Enumerable.Range(0,1000).Select(i=>new MediaItem(Path.Combine(folder,i.ToString("D4")+".jpg"),i%2==0?30:null,i%2==0?40:null,"Photo")).ToArray();
            await vm.ShowItems(items,"Fixture library",4);Assert.Equal(4,vm.PageIndex);Assert.Equal(120,vm.VisibleItems.Count);Assert.Equal("0480.jpg",vm.VisibleItems[0].Name);
            var filtered=items.Where(m=>!m.HasLocation).Concat(Enumerable.Range(0,10).Select(i=>new MediaItem(Path.Combine(folder,"new"+i+".jpg"),null,null,"Photo"))).ToArray();
            await vm.ShowItems(filtered,"Fixture filter",vm.PageIndex);Assert.Equal(4,vm.PageIndex);Assert.Equal(30,vm.VisibleItems.Count);Assert.All(vm.VisibleItems,m=>Assert.False(m.Item.HasLocation));
        }
        finally{Directory.Delete(folder,true);}
    }
}
