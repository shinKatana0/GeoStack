// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.Core;

namespace GeoStack.App;

/// <summary>Invoked on the acquisition worker's STA thread, never the main Dispatcher.</summary>
internal static class MapTileRasterizer
{
    public static byte[] Render(IReadOnlyList<TileFeature> features,bool dark,string language)
    {
        Brush Color(string hex){var b=new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
        var land=Color(dark?"#24343A":"#F6F2E8");var water=Color(dark?"#152B38":"#A9D6E5");var ink=Color(dark?"#D0DEE0":"#354851");
        var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
        {
            draw.PushClip(new RectangleGeometry(new Rect(0,0,512,512)));draw.DrawRectangle(land,null,new Rect(0,0,512,512));
            foreach(var layer in new[]{"earth","landuse","water","buildings","boundaries","roads"})foreach(var feature in features.Where(f=>f.Layer==layer))
            {
                var geometry=new StreamGeometry{FillRule=FillRule.EvenOdd};var factor=512d/feature.Extent;
                using(var context=geometry.Open())foreach(var path in feature.Paths.Where(p=>p.Length>1))
                {
                    context.BeginFigure(new(path[0].X*factor,path[0].Y*factor),feature.Type==3,feature.Type==3);
                    context.PolyLineTo(path.Skip(1).Select(p=>new Point(p.X*factor,p.Y*factor)).ToArray(),true,false);
                }
                geometry.Freeze();var kind=feature.Properties.GetValueOrDefault("kind","");
                var major=kind is "highway" or "major_road";var rail=kind=="rail";
                Brush fill=layer switch{"earth"=>land,"water"=>water,"landuse"=>Color(kind is "park" or "forest" or "wood" or "grass" or "nature_reserve" or "recreation_ground" or "cemetery"?dark?"#2C4C3D":"#DCE8C9":dark?"#303F42":"#E9E4DA"),"buildings"=>Color(dark?"#435159":"#D4CDC1"),_=>land};
                if(feature.Type==3)draw.DrawGeometry(fill,null,geometry);
                if(feature.Type==2)
                {
                    var width=layer=="roads"?major?7:kind=="minor_road"?3:2:layer=="water"?3:2;
                    var stroke=layer=="water"?water:Color(layer=="roads"?rail?dark?"#61757E":"#B1A7AB":major?dark?"#BBA76F":"#EFD18B":dark?"#7A8E95":"#FFFFFF":dark?"#8D7D9B":"#BDABC7");
                    if(layer=="roads")draw.DrawGeometry(null,new Pen(Color(dark?"#263336":"#BDB7AB"),width+2),geometry);
                    draw.DrawGeometry(null,new Pen(stroke,width),geometry);
                }
            }
            var occupied=new List<Rect>();
            foreach(var feature in features.Where(f=>f.Layer is "places" or "roads" or "water").OrderBy(f=>f.Layer=="places"?0:1))
            {
                var text=(language=="ru-RU"?feature.Properties.GetValueOrDefault("name:ru"):feature.Properties.GetValueOrDefault("name:en"))??feature.Properties.GetValueOrDefault("name:en")??feature.Properties.GetValueOrDefault("name");
                var path=feature.Paths.FirstOrDefault();if(string.IsNullOrWhiteSpace(text) || text.Length>40 || path==null || path.Length==0 || feature.Type==3)continue;
                var point=path[feature.Type==1?0:path.Length/2];var x=point.X*512d/feature.Extent;var y=point.Y*512d/feature.Extent;
                var label=new FormattedText(text,CultureInfo.GetCultureInfo(language),FlowDirection.LeftToRight,new Typeface("Segoe UI"),22,ink,1);
                var box=new Rect(x-label.Width/2-5,y-label.Height/2-3,label.Width+10,label.Height+6);
                if(box.Left<12 || box.Top<12 || box.Right>500 || box.Bottom>500 || occupied.Any(b=>b.IntersectsWith(box)))continue;
                occupied.Add(box);draw.DrawRoundedRectangle(land,null,box,3,3);draw.DrawText(label,new(x-label.Width/2,y-label.Height/2));if(occupied.Count>=48)break;
            }
            draw.Pop();
        }
        var bitmap=new RenderTargetBitmap(512,512,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
    }
}
