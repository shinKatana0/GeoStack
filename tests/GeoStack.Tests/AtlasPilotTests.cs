// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class AtlasPilotTests
{
    private static string Folder()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory);var folder=TestStorage.Create(root);Directory.CreateDirectory(folder);return folder;
    }
    private static void Write(string folder,string id,int schema,double[][][] paths)
    {
        var feature=new NativeAtlasPilot.Feature(0,"land",true,paths);var data=new NativeAtlasPilot.Package(schema,id,[-180,-85,180,85],[feature],[]);
        using var file=File.Create(Path.Combine(folder,id+".gsatlas"));using var gzip=new GZipStream(file,CompressionLevel.SmallestSize);JsonSerializer.Serialize(gzip,data);
    }
    [Fact] public async Task OptionalAtlasRejectsUnknownSchemaBeforeEnteringProductionRenderer()
    {
        var folder=Folder();Write(folder,"world",99,[]);await Assert.ThrowsAsync<InvalidDataException>(()=>NativeAtlasPilot.LoadAsync(folder,folder));
    }
    [Fact] public async Task OptionalAtlasRejectsOutOfRangeCoordinates()
    {
        var folder=Folder();Write(folder,"world",1,[[[2,.5],[.5,.5],[.5,.6],[2,.5]]]);await Assert.ThrowsAsync<InvalidDataException>(()=>NativeAtlasPilot.LoadAsync(folder,folder));
    }
}
