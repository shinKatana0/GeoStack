// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GeoStack.App;

public sealed partial class GlobalVectorAtlas
{
    private static readonly GuidelineSet CompactGuidelines=MakeCompactGuidelines();
    private static GuidelineSet MakeCompactGuidelines(){var value=new GuidelineSet([0,.25,.5,.75,1],[0,.25,.5,.75,1]);value.Freeze();return value;}
    internal static (int Shapes,int Vertices,NativeAtlasPilot.Place[] Places,bool Frozen) InspectCompact(byte[] payload)
    {var tile=PrepareCompact(payload,(8,227,100));return(tile.Shapes.Length,tile.Vertices,tile.Places,tile.Shapes.All(s=>s.Geometry.IsFrozen));}
    private static readonly string[] CompactKinds=["land","urban","park","water","river","boundary","rail","road-major","road-secondary"];
    private static Tile PrepareCompact(byte[] payload,(int Z,int X,int Y) key)
    {
        using var timer=AtlasProfiling.Measure("Global.CompactGeometry");
        using var stream=new MemoryStream(payload);using var reader=new BinaryReader(stream,Encoding.UTF8);
        if(!reader.ReadBytes(5).SequenceEqual(new byte[]{71,83,86,67,1}))throw new InvalidDataException("Unsupported compact geometry");
        var count=reader.ReadUInt16();if(count>256)throw new InvalidDataException("Too many compact fragments");
        var shapes=new List<Shape>();var vertices=0;
        for(var i=0;i<count;i++)
        {
            var kind=reader.ReadByte();var polygon=reader.ReadBoolean();var paths=reader.ReadUInt32();
            if(kind>=CompactKinds.Length || paths>50000)throw new InvalidDataException("Invalid compact fragment");
            var geometry=new StreamGeometry{FillRule=FillRule.Nonzero};
            using(var context=geometry.Open())for(var j=0;j<paths;j++)
            {
                var length=reader.ReadUInt32();if(length is <2 or >250000)throw new InvalidDataException("Invalid compact path length");vertices=checked(vertices+(int)length);
                if(vertices>250000)throw new InvalidDataException("Compact vertex budget exceeded");
                var points=new Point[length];for(var k=0;k<length;k++)
                {var x=reader.ReadUInt16();var y=reader.ReadUInt16();if(x>4096 || y>4096)throw new InvalidDataException("Invalid compact coordinate");points[k]=new(x/4096d,y/4096d);}
                context.BeginFigure(points[0],polygon,polygon);context.PolyLineTo(points[1..],true,false);
            }
            geometry.Freeze();shapes.Add(new(CompactKinds[kind],polygon,geometry,geometry.Bounds));
        }
        var labels=reader.ReadUInt16();if(labels>200)throw new InvalidDataException("Compact label budget exceeded");
        var places=new List<NativeAtlasPilot.Place>();var n=1<<key.Z;
        string Text(){var length=reader.ReadUInt16();if(length>4096)throw new InvalidDataException("Compact name budget exceeded");var bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(bytes);}
        for(var i=0;i<labels;i++)
        {
            var id=Text();var name=Text();var english=Text();var russian=Text();var x=reader.ReadSingle();var y=reader.ReadSingle();var min=reader.ReadSingle();var country=reader.ReadBoolean();
            if(!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(min) || x<0 || x>1 || y<0 || y>1 || min<0 || min>20)throw new InvalidDataException("Invalid compact place");
            var px=(key.X+x)/(double)n;var py=(key.Y+y)/(double)n;if(id.Length==0)id=FormattableString.Invariant($"{px:F6}/{py:F6}/{name}");
            places.Add(new(id,px,py,name,english,russian,min,country));
        }
        if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing compact payload");
        return new(shapes.OrderBy(s=>Array.IndexOf(CompactKinds,s.Kind)).ToArray(),places.OrderBy(p=>p.MinZoom).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray(),vertices);
    }
}
