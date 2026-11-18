// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class LargeLibraryMeasurements
{
    [Theory(Explicit=true)][InlineData(1000)][InlineData(10000)]
    public async Task ProfileMixedLibrary(int count)
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;
        Assert.Equal("1",Environment.GetEnvironmentVariable("GEOSTACK_PROFILE_LARGE"));
        var folder=Path.Combine(root,".local/library05-fixtures-v2",count.ToString());
        Assert.True(Directory.Exists(folder));
        var rows=new List<object>();
        foreach(var size in new[]{40,160})
        {
            var data=Path.Combine(root,".local",$"profile05-{count}-{size}-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            using var index=new MediaIndex(Path.Combine(data,"index.db"));var service=new ScanService(TestMediaTools.ExifTool){BatchSize=size};
            var clock=Stopwatch.StartNew();var result=await service.ScanAsync(folder,index,null,TestContext.Current.CancellationToken);
            File.WriteAllText(Path.Combine(data,"errors.json"),JsonSerializer.Serialize(result.Errors));
            Assert.Equal(count,result.Discovered);Assert.Equal(count,result.Items.Count);Assert.Empty(result.Errors);var cold=clock.Elapsed.TotalMilliseconds;var measurements=service.Measurements;
            clock.Restart();var repeat=await service.ScanAsync(folder,index,null,TestContext.Current.CancellationToken);Assert.Equal(count,repeat.Reused);
            using var process=Process.GetCurrentProcess();rows.Add(new{Count=count,BatchSize=size,ColdMilliseconds=cold,ColdFilesPerSecond=count/(cold/1000),WarmMilliseconds=clock.Elapsed.TotalMilliseconds,ColdStages=measurements,DatabaseBytes=new FileInfo(Path.Combine(data,"index.db")).Length,process.PrivateMemorySize64,process.WorkingSet64,ManagedBytes=GC.GetTotalMemory(false),Scope="Real metadata extraction from repeated hard-linked approved mixed-format files; content/coordinates repeat, not independent archive diversity; no thumbnails in this Core phase"});
        }
        File.WriteAllText(Path.Combine(root,$".local/library05-core-{count}.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }
}
