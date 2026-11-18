// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

public sealed partial class NativeAtlasPilot
{
    public static int BasemapLod(double zoom)=>zoom<7?0:zoom<10?8:12;
    public bool UseOptimizedRendering { get; set; }=true;
    private readonly BoundedLru<string,FormattedText> optimizedText=new(512);
    private readonly List<Rect> occupiedLabels=new(48);
    private readonly HashSet<string> renderedNames=[];
    private readonly Dictionary<Dataset,((int,int,int,int) Key,Place[] Places)> labelQueries=[];
    public long LabelCacheHits { get; private set; }
    public long LabelCacheMisses { get; private set; }
    internal object ResourceSnapshot()=>new{Geometry=FeatureCount,CoverageMasks=datasets.Sum(d=>d.CoverageBands.Count),Vertices=VertexCount,FormattedLabels=optimizedText.Count,LabelQueryWindows=labelQueries.Count,LightPens=lightStyle.Pens.Count,DarkPens=darkStyle.Pens.Count,LabelCacheHits,LabelCacheMisses,Global=GlobalAtlas?.Snapshot(),UrbanRoads=UrbanRoads?.Snapshot(),Raster=cityTiles.ResourceSnapshot()};
    private sealed class Style(bool dark)
    {
        public readonly Dictionary<string,Brush> Brushes=new(){["ocean"]=Color(dark?"#152B38":"#A9D6E5"),["land"]=Color(dark?"#24343A":"#F6F2E8"),["urban"]=Color(dark?"#303F42":"#E9E4DA"),["park"]=Color(dark?"#2C4C3D":"#DCE8C9"),["water"]=Color(dark?"#152B38":"#A9D6E5"),["river"]=Color(dark?"#152B38":"#A9D6E5"),["region"]=Color(dark?"#6C6279":"#BDABC7"),["boundary"]=Color(dark?"#8D7D9B":"#BDABC7"),["rail"]=Color(dark?"#717278":"#B1A7AB"),["building"]=Color(dark?"#435159":"#D4CDC1"),["road-major"]=Color(dark?"#BBA76F":"#EFD18B"),["road-secondary"]=Color(dark?"#9C946F":"#F0D9A0"),["road-local"]=Color(dark?"#7A8E95":"#FFFFFF")};
        public readonly Brush Ink=Color(dark?"#D0DEE0":"#354851"),Background=Color(dark?"#C024343A":"#C0F6F2E8"),Casing=Color(dark?"#263336":"#C9C1B1");
        public readonly Dictionary<(string,bool),Pen> Pens=[];
        public double World=-1;
        public Pen Stroke(string kind,double width,double world,bool casing=false)
        {
            if(World!=world){Pens.Clear();World=world;}
            if(!Pens.TryGetValue((kind,casing),out var pen)){pen=new(casing?Casing:Brushes[kind],(width+(casing?1.1:0))/world);pen.Freeze();Pens[(kind,casing)]=pen;}return pen;
        }
    }
    private readonly Style lightStyle=new(false),darkStyle=new(true),globalLightStyle=new(false),globalDarkStyle=new(true);
    private readonly Brush urbanLight=Color("#E2DFD3"),urbanDark=Color("#35474A");
    private Place[] QueryLabels(Dataset dataset,Rect view)
    {
        var key=((int)Math.Floor(view.Left*4096),(int)Math.Floor(view.Right*4096),(int)Math.Floor(view.Top*4096),(int)Math.Floor(view.Bottom*4096));
        if(labelQueries.TryGetValue(dataset,out var cached) && cached.Key==key){LabelCacheHits++;return cached.Places;}
        LabelCacheMisses++;Place[] result;
        if((long)(key.Item2-key.Item1+1)*(key.Item4-key.Item3+1)>4096)result=dataset.OrderedLabels;
        else
        {
            var candidates=new List<Place>();for(var x=key.Item1;x<=key.Item2;x++)for(var y=key.Item3;y<=key.Item4;y++)if(dataset.LabelIndex.TryGetValue((WrapLabelCell(x),y),out var cell))candidates.AddRange(cell);
            result=candidates.OrderBy(p=>p.MinZoom).ToArray();
        }
        labelQueries[dataset]=(key,result);return result;
    }
    public static int WrapLabelCell(int x)=>(x%4096+4096)%4096;
    public static bool MatchesCityTheme(string fileName,bool dark,string language)=>dark?fileName.EndsWith($"-dark-{language}.mbtiles",StringComparison.OrdinalIgnoreCase):!fileName.Contains("-dark-",StringComparison.OrdinalIgnoreCase) && (!fileName.Contains("-light-",StringComparison.OrdinalIgnoreCase) || fileName.EndsWith($"-light-{language}.mbtiles",StringComparison.OrdinalIgnoreCase));
    public static MapPackage? ChooseRasterSurface(IEnumerable<MapPackage> available,MapPackage? regionalContext,IReadOnlySet<string> family,bool dark,string language,double z,double world,double width,double height,Point center)
    {
        // Choose a themed package that can actually supply the full surface. An ineligible detailed
        // preference must not leave frame composition to pick an arbitrary context-language/theme.
        var surfaces=available.Where(m=>RasterComposition.CoversViewport(m,world,width,height,center)).ToArray();
        if(regionalContext!=null && z<=regionalContext.MaxZoom && surfaces.Any(m=>m.Id==regionalContext.Id))return regionalContext;
        MapPackage? Select(bool useDark)=>surfaces.Where(m=>MatchesCityTheme(System.IO.Path.GetFileName(m.Path),useDark,language))
            .OrderByDescending(m=>m.MaxZoom>=Math.Floor(z)).ThenByDescending(m=>family.Contains(m.Id))
            .ThenByDescending(m=>System.IO.Path.GetFileName(m.Path).EndsWith($"-light-{language}.mbtiles",StringComparison.OrdinalIgnoreCase)).ThenByDescending(m=>m.MaxZoom).FirstOrDefault();
        return Select(dark)??Select(false);
    }
    public void Draw(DrawingContext dc,double world,double width,double height,Point center,bool dark,bool interacting,Action invalidate)
    {
        if(!UseOptimizedRendering){DrawBaseline(dc,world,width,height,center,dark,interacting,invalidate);return;}
        using var total=AtlasProfiling.Measure("Vector.Total");var clock=Stopwatch.StartNew();var z=Math.Log2(world/256);var visible=new Rect(center.X-width/world/2,center.Y-height/world/2,width/world,height/world);var style=dark?darkStyle:lightStyle;
        dc.DrawRectangle(style.Brushes["ocean"],null,new(0,0,width,height));LastDrawnShapes=0;
        UrbanRoads?.BeginFrame();
        var hasGlobal=GlobalAtlas is {Enabled:true};
        var region=hasGlobal?null:datasets.Skip(1).FirstOrDefault(d=>Contains(d.Data.Coverage,visible));
        foreach(var dataset in datasets)
        {
            if(hasGlobal && (dataset.Data.Id!="world" || GlobalAtlas!.CanCover(world,width,height,center)))continue;
            if(dataset.Data.Id!="world" && z<6 || LegacyCoverageForVerification && dataset.Data.Id=="world" && region!=null && z>=8)continue;
            var requested=dataset.Data.Id=="world"?BasemapLod(z):(z<9?8:z<12?10:12);var lod=dataset.Levels.Where(s=>s<=requested).DefaultIfEmpty(0).Max();LastNativeLod=lod;
            var targetLod=z>=14 && dataset.CoverageBands.Keys.Any(k=>k.Target==15)?15:lod;
            var useBands=!LegacyCoverageForVerification && dataset.CoverageBands.Count>0;
            for(var wrap=-1;wrap<=1;wrap++)
            {
                var query=new Rect(visible.X-wrap,visible.Y,visible.Width,visible.Height);Shape[] shapes;
                using(AtlasProfiling.Measure("Vector.QueryFilterSort"))
                {
                    Shape[] candidates;using(AtlasProfiling.Measure("Vector.Query"))candidates=dataset.Query(query).ToArray();
                    using(AtlasProfiling.Measure("Vector.FilterAndOrder"))shapes=candidates.Where(s=>(useBands?dataset.CoverageBands.ContainsKey((targetLod,s.Lod)):(s.Lod==lod || s.Lod==15 && z>=14)) && s.Bounds.IntersectsWith(query)).OrderBy(s=>s.Lod).ThenBy(s=>Array.IndexOf(Order,s.Kind)).ToArray();
                }
                using var commands=AtlasProfiling.Measure("Vector.CommandsAndTransform");
                dc.PushTransform(new MatrixTransform(world,0,0,world,width/2+(wrap-center.X)*world,height/2-center.Y*world));
                var clipped=false;var clipLevel=-1;
                foreach(var shape in shapes)
                {
                    if(useBands && clipLevel!=shape.Lod)
                    {
                        if(clipped)dc.Pop();dc.PushClip(dataset.CoverageBands[(targetLod,shape.Lod)]);clipped=true;clipLevel=shape.Lod;
                    }
                    if(shape.Kind=="region" && z<3 || shape.Kind=="road-local" && z<11)continue;
                    if(dataset.Data.Id=="world" && shape.Polygon && shape.Bounds.Width*world<.75 && shape.Bounds.Height*world<.75)continue;
                    if(shape.Polygon)dc.DrawGeometry(dataset.Data.Id=="world" && shape.Kind=="urban" && App.UrbanMode!="a"?(dark?urbanDark:urbanLight):style.Brushes[shape.Kind],null,shape.Geometry);
                    else
                    {
                        var w=shape.Kind switch{"road-major"=>z>=14?3.4:2.2,"road-secondary"=>z>=14?2.2:1.4,"road-local"=>1.05,"river"=>1.4,"rail"=>.65,_=>.75};
                        if(shape.Kind.StartsWith("road-",StringComparison.Ordinal))dc.DrawGeometry(null,style.Stroke(shape.Kind,w,world,true),shape.Geometry);
                        dc.DrawGeometry(null,style.Stroke(shape.Kind,w,world),shape.Geometry);
                    }
                    LastDrawnShapes++;
                }
                if(clipped)dc.Pop();
                if(dataset.Data.Id=="world" && UrbanRoads!=null && UrbanRoadLayer.VisibleAt(z) && region==null &&
                    !(CityRasterEnabled && sourceCatalog!=null && sourceCatalog.Packages.Any(m=>m.Enabled && z>=m.MinZoom && z<m.MaxZoom+RasterTilePlan.MaximumOverzoom+1 && RasterComposition.CoversViewport(m,world,width,height,center))))
                    UrbanRoads.Draw(dc,query,world,dark);
                dc.Pop();
            }
        }
        if(hasGlobal){var globalStyle=dark?globalDarkStyle:globalLightStyle;GlobalAtlas!.Draw(dc,world,width,height,center,globalStyle.Brushes,(kind,w,scale,casing)=>globalStyle.Stroke(kind,w,scale,casing),invalidate);}
        using(AtlasProfiling.Measure("Raster.Composite"))DrawCity(dc,visible,world,width,height,center,dark,z,interacting,invalidate);
        using(AtlasProfiling.Measure("Vector.Labels"))
        {
            occupiedLabels.Clear();renderedNames.Clear();
            var labelGroups=new List<Place[]>();
            if(hasGlobal)labelGroups.Add(GlobalAtlas!.VisiblePlaces);
            foreach(var dataset in datasets.Reverse()){if(hasGlobal && dataset.Data.Id!="world")continue;labelGroups.Add(QueryLabels(dataset,visible));}
            foreach(var group in labelGroups)
            {
                foreach(var place in group)
                {
                    if(place.MinZoom>z)break;
                    if(place.Country && z>7)continue;
                    var x=Geo.WrappedDelta(place.X-center.X)*world+width/2;var y=(place.Y-center.Y)*world+height/2;
                    if(x<30 || y<30 || x>width-30 || y>height-32)continue;
                    if(LastRasterVisible && cityTiles.Catalog.Packages.Any(m=>m.Id==cityTiles.Catalog.Preferred && m.CoversBounds(m.MaxZoom,Geo.Unproject(place.X,place.Y).Latitude,Geo.Unproject(place.X,place.Y).Longitude)))continue;
                    var name=App.Settings.Language=="ru-RU"?place.Russian:place.English;if(string.IsNullOrWhiteSpace(name))name=place.Name;if(!renderedNames.Add(name))continue;
                    var key=$"{name}:{dark}:{place.Country}:{App.Settings.Language}";
                    if(!optimizedText.TryGet(key,out var text)){text=new(name,CultureInfo.GetCultureInfo(App.Settings.Language),FlowDirection.LeftToRight,new Typeface(place.Country?"Segoe UI Semibold":"Segoe UI"),place.Country?12:11,style.Ink,1);optimizedText.Set(key,text);}
                    var labelX=place.Country?x-text.Width/2:x+7;
                    var labelY=!place.Country && App.UrbanMode!="a"?y+18:y-text.Height/2;
                    var rect=new Rect(labelX-4,labelY-2,text.Width+8,text.Height+4);if(occupiedLabels.Any(r=>r.IntersectsWith(rect)))continue;
                    occupiedLabels.Add(rect);if(!place.Country)dc.DrawEllipse(style.Ink,null,new Point(x,y),2,2);dc.DrawRoundedRectangle(style.Background,null,rect,3,3);dc.DrawText(text,new(labelX,labelY));if(occupiedLabels.Count>=48)break;
                }
                if(occupiedLabels.Count>=48)break;
            }
        }
        LastRenderMilliseconds=clock.Elapsed.TotalMilliseconds;MaximumRenderMilliseconds=Math.Max(MaximumRenderMilliseconds,LastRenderMilliseconds);
    }
    private void DrawCity(DrawingContext dc,Rect visible,double world,double width,double height,Point center,bool dark,double z,bool interacting,Action invalidate)
    {
        SynchronizeCatalog();
        cityTiles.NativeOnly=GlobalAtlas is {Enabled:true};
        var regionalContext=cityTiles.Catalog.Packages.FirstOrDefault(m=>m.Enabled && m.Available && m.MinZoom<=Math.Floor(z) && (LegacyCoverageForVerification?m.MaxZoom==10 && System.IO.Path.GetFileName(m.Path).StartsWith("region-",StringComparison.Ordinal):RegionalLodCoverage.IsContext(m)) && MatchesCityTheme(System.IO.Path.GetFileName(m.Path),dark,App.Settings.Language) && m.CoversBounds(m.MaxZoom,Geo.Unproject(center.X,center.Y).Latitude,Geo.Unproject(center.X,center.Y).Longitude));
        if(!LegacyCoverageForVerification && regionalContext!=null && Math.Floor(z)>regionalContext.MaxZoom && datasets.Any(d=>d.Data.Id!="world" && Contains(d.Data.Coverage,visible)))regionalContext=null;
        var nativeMaximum = Math.Min(15, sourceCatalog?.Packages.FirstOrDefault(m => m.Id == sourceCatalog.Preferred)?.MaxZoom ?? 15);
        rasterOpacity=CityRasterEnabled?(regionalContext!=null?1:Math.Clamp((z-(nativeMaximum-1.5))/1.5,0,1)):0;
        if(rasterOpacity<=0)return;
        var location=Geo.Unproject(center.X,center.Y);var language=App.Settings.Language;
        var available=cityTiles.Catalog.Packages.Where(m=>m.Enabled && m.Available && (!cityTiles.NativeOnly || m.MaxZoom>=Math.Floor(z)) && m.CoversBounds(m.MaxZoom,location.Latitude,location.Longitude));
        // Prefer the accepted crisp vector context over enlarging a lower-native raster where vector coverage exists.
        if(!LegacyCoverageForVerification && datasets.Any(d=>d.Data.Id!="world" && Contains(d.Data.Coverage,visible)))
            available=available.Where(m=>!RegionalLodCoverage.IsContext(m) || Math.Floor(z)<=m.MaxZoom);

        // The vector fallback is already complete. At rest, do not leave a retained raster island
        // when no installed package can provide the current full surface. Preserve active drag continuity.
        if(!interacting && !available.Any(m=>z>=m.MinZoom && z<m.MaxZoom+RasterTilePlan.MaximumOverzoom+1 && RasterComposition.CoversViewport(m,world,width,height,center))){rasterOpacity=0;return;}
        var map=ChooseRasterSurface(available,regionalContext,preferredFamily,dark,language,z,world,width,height,center);
        if(map==null && cityTiles.NativeOnly){rasterOpacity=0;return;}
        if(map==null && cityTiles.Catalog.Packages.FirstOrDefault(m=>m.Id==cityTiles.Catalog.Preferred) is {} selected && !MatchesCityTheme(System.IO.Path.GetFileName(selected.Path),dark,language)){cityTiles.Catalog.Preferred="world";cityTiles.Clear();}
        if(map!=null && cityTiles.Catalog.Preferred!=map.Id){cityTiles.Catalog.Preferred=map.Id;cityTiles.Clear();}
        dc.PushOpacity(rasterOpacity);cityTiles.Draw(dc,visible,world,width,height,center,invalidate,interacting);dc.Pop();
    }
}
