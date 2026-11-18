// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GeoStack.App;

/// <summary>Static, bounded major-road skeleton. Uses the atlas's existing world transform.</summary>
internal sealed class UrbanRoadLayer
{
    private sealed record Piece(byte Kind,Rect Bounds,StreamGeometry Geometry);
    private readonly Dictionary<(int,int),List<Piece>> index=[];
    private readonly Dictionary<(bool,byte,bool),Pen> pens=[];
    private double penWorld;
    internal int GeometryCount {get;private set;}
    internal int VertexCount {get;private set;}
    internal int LastDrawn {get;private set;}
    internal double InitializationMilliseconds {get;private set;}
    internal static bool VisibleAt(double zoom)=>zoom>=7 && zoom<=12.1;
    internal static bool ClassVisibleAt(byte kind,double zoom)=>VisibleAt(zoom) && kind<=2 && (kind<2 || zoom>=8);
    internal void BeginFrame()=>LastDrawn=0;
    internal object Snapshot()=>new{GeometryCount,VertexCount,LastDrawn,PenCount=pens.Count,InitializationMilliseconds,NativeSourceZoom=7,MaximumContextZoom=12};
    internal UrbanRoadLayer(string path)
    {
        var timer=Stopwatch.StartNew();using var file=File.OpenRead(path);
        if(file.Length>30*1024*1024)throw new InvalidDataException("Urban road compressed budget exceeded");
        using var compressed=new GZipStream(file,CompressionMode.Decompress);using var reader=new BinaryReader(compressed);
        if(Encoding.ASCII.GetString(reader.ReadBytes(8))!="GSROAD01" || reader.ReadInt32()!=1)throw new InvalidDataException("Unsupported urban road schema");
        var count=reader.ReadInt32();if(count<1 || count>30000)throw new InvalidDataException("Urban road geometry budget exceeded");
        for(var i=0;i<count;i++)
        {
            var kind=reader.ReadByte();var x0=reader.ReadSingle();var y0=reader.ReadSingle();var x1=reader.ReadSingle();var y1=reader.ReadSingle();var paths=reader.ReadInt32();
            if(kind>2 || !float.IsFinite(x0+y0+x1+y1) || x0<0 || y0<0 || x1>1 || y1>1 || x1<x0 || y1<y0 || paths<1 || paths>384)throw new InvalidDataException("Invalid urban road bounds/paths");
            var geometry=new StreamGeometry();var featureVertices=0;
            using(var context=geometry.Open())for(var p=0;p<paths;p++)
            {
                var length=reader.ReadInt32();if(length<2 || length>768 || (featureVertices+=length)>768 || (VertexCount+=length)>2000000)throw new InvalidDataException("Urban road vertex budget exceeded");
                Point Point(){var x=reader.ReadUInt32();var y=reader.ReadUInt32();if(x>1000000000 || y>1000000000)throw new InvalidDataException("Invalid urban road coordinate");return new(x/1e9,y/1e9);}
                context.BeginFigure(Point(),false,false);var points=new Point[length-1];for(var j=0;j<points.Length;j++)points[j]=Point();context.PolyLineTo(points,true,false);
            }
            geometry.Freeze();var bounds=new Rect(new Point(x0,y0),new Point(x1,y1));bounds.Inflate(1e-7,1e-7);var piece=new Piece(kind,bounds,geometry);GeometryCount++;
            for(var x=(int)Math.Floor(bounds.Left*128);x<=(int)Math.Floor(bounds.Right*128);x++)for(var y=(int)Math.Floor(bounds.Top*128);y<=(int)Math.Floor(bounds.Bottom*128);y++)
            {if(!index.TryGetValue((x,y),out var values))index[(x,y)]=values=[];values.Add(piece);}
        }
        if(reader.BaseStream.ReadByte()!=-1)throw new InvalidDataException("Trailing urban road data");InitializationMilliseconds=timer.Elapsed.TotalMilliseconds;
    }
    internal void Draw(DrawingContext dc,Rect visible,double world,bool dark)
    {
        if(penWorld!=world){pens.Clear();penWorld=world;}
        var candidates=new HashSet<Piece>();
        for(var x=(int)Math.Floor(visible.Left*128);x<=(int)Math.Floor(visible.Right*128);x++)for(var y=(int)Math.Floor(visible.Top*128);y<=(int)Math.Floor(visible.Bottom*128);y++)if(index.TryGetValue((x,y),out var values))candidates.UnionWith(values);
        Pen Stroke(byte kind,bool casing)
        {
            if(pens.TryGetValue((dark,kind,casing),out var pen))return pen;
            var color=casing?(dark?"#263336":"#C9C1B1"):kind switch{0=>dark?"#C3AD70":"#EBC879",1=>dark?"#B0A57F":"#EED49B",_=>dark?"#8B9998":"#EDE3C9"};
            var brush=(Brush)new BrushConverter().ConvertFromString(color)!;brush.Freeze();var width=kind switch{0=>2.2,1=>1.6,_=>1.0};
            pen=new(brush,(width+(casing ? 0.8 : 0))/world){LineJoin=PenLineJoin.Bevel};pen.Freeze();pens[(dark,kind,casing)]=pen;return pen;
        }
        var zoom=Math.Log2(world/256);
        foreach(var piece in candidates.Where(p=>ClassVisibleAt(p.Kind,zoom) && p.Bounds.IntersectsWith(visible)).OrderByDescending(p=>p.Kind))
        {dc.DrawGeometry(null,Stroke(piece.Kind,true),piece.Geometry);dc.DrawGeometry(null,Stroke(piece.Kind,false),piece.Geometry);LastDrawn++;}
    }
}
