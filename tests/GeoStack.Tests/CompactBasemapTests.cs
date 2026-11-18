// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class CompactBasemapTests
{
    [Theory][InlineData(0,0)][InlineData(5,0)][InlineData(7,8)][InlineData(9,8)][InlineData(11,12)][InlineData(13,12)][InlineData(15,12)][InlineData(22,12)]
    public void GlobalContextRetainsItsBestLodBeyondNativeDetail(double zoom,int expected)=>Assert.Equal(expected,NativeAtlasPilot.BasemapLod(zoom));

    [Fact] public void ShippedWorldGeometryHasVersionedBoundedMultiscaleData()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;
        var path=Path.Combine(root,"assets/geography/world.gsbase");
        Assert.InRange(new FileInfo(path).Length,1,30*1024*1024);
        using var file=File.OpenRead(path);using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new BinaryReader(gzip);
        Assert.Equal("GSBASE02",System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8)));Assert.Equal(2,reader.ReadInt32());
        Assert.InRange(reader.ReadInt32(),1,100000);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path,".manifest.json")));
        Assert.True(manifest.RootElement.GetProperty("LODs").GetProperty("12").GetProperty("Features").GetProperty("urban").GetInt32()>0);
        Assert.DoesNotContain("road",manifest.RootElement.GetProperty("LODs").ToString(),StringComparison.OrdinalIgnoreCase);
    }
}
