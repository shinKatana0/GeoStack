// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class GlobalCompactTests
{
    private static byte[] Payload(Action<BinaryWriter> body)
    {using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write(new byte[]{71,83,86,67,1});body(writer);return stream.ToArray();}
    [Fact] public void EmptyOceanFragmentIsValidAndContainsNoInventedGeography()
    {var value=GlobalVectorAtlas.InspectCompact(Payload(w=>{w.Write((ushort)0);w.Write((ushort)0);}));Assert.Equal(0,value.Shapes);Assert.Equal(0,value.Vertices);Assert.Empty(value.Places);}
    [Fact] public void CompactGeometryIsFrozenAndCoordinatesRemainInTheSourceTile()
    {
        var value=GlobalVectorAtlas.InspectCompact(Payload(w=>{w.Write((ushort)1);w.Write((byte)7);w.Write(false);w.Write((uint)1);w.Write((uint)2);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)4096);w.Write((ushort)4096);w.Write((ushort)0);}));
        Assert.Equal(1,value.Shapes);Assert.Equal(2,value.Vertices);Assert.True(value.Frozen);
    }
    [Fact] public void SourceEnglishAndRussianPlaceNamesSurviveCompactStorage()
    {
        var value=GlobalVectorAtlas.InspectCompact(Payload(w=>{
            w.Write((ushort)0);w.Write((ushort)1);foreach(var text in new[]{"Q1490","Tokyo","Tokyo","\u0422\u043e\u043a\u0438\u043e"}){var bytes=Encoding.UTF8.GetBytes(text);w.Write((ushort)bytes.Length);w.Write(bytes);}w.Write(.5f);w.Write(.5f);w.Write(2f);w.Write(false);
        }));var place=Assert.Single(value.Places);Assert.Equal("Tokyo",place.English);Assert.Equal("\u0422\u043e\u043a\u0438\u043e",place.Russian);Assert.Equal((227.5)/256,place.X,8);Assert.Equal((100.5)/256,place.Y,8);
    }
    [Theory][InlineData(uint.MaxValue)][InlineData(250001u)][InlineData(0u)]
    public void UntrustedPathLengthsAreRejectedBeforeAllocation(uint count)
        =>Assert.Throws<InvalidDataException>(()=>GlobalVectorAtlas.InspectCompact(Payload(w=>{w.Write((ushort)1);w.Write((byte)7);w.Write(false);w.Write((uint)1);w.Write(count);})));
    [Fact] public void TruncatedPayloadCannotBePublishedAsValidGeometry()
        =>Assert.Throws<EndOfStreamException>(()=>GlobalVectorAtlas.InspectCompact(Payload(w=>w.Write((ushort)1))));
}
