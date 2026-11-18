// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GeoStack.App;

public sealed partial class NativeAtlasPilot
{
    private static void LoadCompactWorld(string path,Dataset dataset,ref int vertices)
    {
        using var file=File.OpenRead(path);
        if(file.Length>30*1024*1024)throw new InvalidDataException("Compact basemap compressed budget exceeded");
        using var gzip=new GZipStream(file,CompressionMode.Decompress);
        using var reader=new BinaryReader(gzip,Encoding.UTF8);
        if(Encoding.ASCII.GetString(reader.ReadBytes(8))!="GSBASE02" || reader.ReadInt32()!=2)throw new InvalidDataException("Unsupported compact basemap");
        var count=reader.ReadInt32();if(count<1 || count>100000)throw new InvalidDataException("Invalid compact feature count");
        string[] kinds=["land","urban","water","river","boundary","region"];
        for(var f=0;f<count;f++)
        {
            var lod=reader.ReadByte();var kind=reader.ReadByte();var polygon=reader.ReadBoolean();
            if(lod is not (0 or 8 or 12) || kind>=kinds.Length)throw new InvalidDataException("Invalid compact feature type");
            var x0=reader.ReadSingle();var y0=reader.ReadSingle();var x1=reader.ReadSingle();var y1=reader.ReadSingle();
            if(!float.IsFinite(x0+y0+x1+y1) || x0<0 || y0<0 || x1>1 || y1>1 || x1<x0 || y1<y0)throw new InvalidDataException("Invalid compact bounds");
            var paths=reader.ReadInt32();if(paths<1 || paths>1024)throw new InvalidDataException("Invalid compact path count");
            var geometry=new StreamGeometry{FillRule=FillRule.EvenOdd};
            using(var context=geometry.Open())for(var p=0;p<paths;p++)
            {
                var length=reader.ReadInt32();if(length<2 || length>10000 || (vertices+=length)>4000000)throw new InvalidDataException("Compact vertex budget exceeded");
                Point ReadPoint(){var x=reader.ReadUInt32();var y=reader.ReadUInt32();if(x>1000000000 || y>1000000000)throw new InvalidDataException("Invalid compact point");return new(x/1e9,y/1e9);}
                context.BeginFigure(ReadPoint(),polygon,polygon);var points=new Point[length-1];for(var i=0;i<points.Length;i++)points[i]=ReadPoint();context.PolyLineTo(points,true,false);
            }
            geometry.Freeze();
            var b=new Rect(new Point(x0,y0),new Point(x1,y1));b.Inflate(1e-7,1e-7);
            var shape=new Shape(lod,kinds[kind],polygon,geometry,b);dataset.Shapes.Add(shape);
            var ix0=(int)Math.Floor(b.Left*256);var ix1=(int)Math.Floor(b.Right*256);var iy0=(int)Math.Floor(b.Top*256);var iy1=(int)Math.Floor(b.Bottom*256);
            if((long)(ix1-ix0+1)*(iy1-iy0+1)>64){dataset.Wide.Add(shape);continue;}
            for(var x=ix0;x<=ix1;x++)for(var y=iy0;y<=iy1;y++)
            {if(!dataset.Index.TryGetValue((x,y),out var cell))dataset.Index[(x,y)]=cell=[];cell.Add(shape);}
        }
        if(reader.BaseStream.ReadByte()!=-1)throw new InvalidDataException("Trailing compact basemap bytes");
    }
}
