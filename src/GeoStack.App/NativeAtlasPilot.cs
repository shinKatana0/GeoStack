// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

/// <summary>Opt-in native geometry experiment. Production atlas and its catalog remain independent.</summary>
public sealed partial class NativeAtlasPilot : IAsyncDisposable
{
    public sealed record Feature(int Lod,string Kind,bool Polygon,double[][][] Paths);
    public sealed record Place(string Id,double X,double Y,string Name,string English,string Russian,double MinZoom,bool Country);
    public sealed record Package(int Schema,string Id,double[] Coverage,Feature[] Features,Place[] Labels);
    private sealed record Shape(int Lod,string Kind,bool Polygon,Geometry Geometry,Rect Bounds);
    private sealed class Dataset
    {
        public required Package Data;
        public Dictionary<(int Target,int Level),Geometry> CoverageBands=[];
        public List<Shape> Shapes=[];
        public Dictionary<(int,int),List<Shape>> Index=[];
        public List<Shape> Wide=[];
        public Dictionary<(int,int),Place[]> LabelIndex=[];
        public Place[] OrderedLabels=[];
        public int[] Levels=[];
        public IEnumerable<Shape> Query(Rect bounds)
        {
            var x0=(int)Math.Floor(bounds.Left*256);var x1=(int)Math.Floor(bounds.Right*256);var y0=(int)Math.Floor(bounds.Top*256);var y1=(int)Math.Floor(bounds.Bottom*256);
            if((long)(x1-x0+1)*(y1-y0+1)>4096)return Shapes;
            var result=new HashSet<Shape>(Wide);
            for(var x=x0;x<=x1;x++)for(var y=y0;y<=y1;y++)if(Index.TryGetValue((x,y),out var cell))result.UnionWith(cell);
            return result;
        }
    }
    private readonly Dataset[] datasets;
    private readonly OfflineTileLayer cityTiles;
    internal UrbanRoadLayer? UrbanRoads {get;private set;}
    private readonly Dictionary<string,FormattedText> textCache=[];
    public bool Enabled { get; set; }=true;
    internal bool LegacyCoverageForVerification {get;set;}
    public bool CityRasterEnabled { get; set; }=true;
    public double InitializationMilliseconds { get; }
    public int FeatureCount=>datasets.Sum(d=>d.Shapes.Count);
    public int VertexCount { get; }
    public int LastDrawnShapes { get; private set; }
    public double LastRenderMilliseconds { get; private set; }
    public double MaximumRenderMilliseconds { get; private set; }
    public bool IsLoading=>cityTiles.IsLoading;
    public int LastNativeLod { get; private set; }
    public bool LastRasterVisible=>cityTiles.DrawnTileCount>0 && rasterOpacity>0;
    internal OfflineTileLayer RasterTiles => cityTiles;
    private MapCatalog? sourceCatalog;
    private MapPackage[] optionalDarkMaps = [];
    private long sourceRevision = -1;
    private string? sourcePreferred;
    private readonly HashSet<string> preferredFamily=[];
    internal static IEnumerable<string> FamilyIds(MapPackage? map)=>map==null?[]:new[]{map.Id}.Concat((map.Variants??[]).Select(v=>v.Id));
    internal void ConnectCatalog(MapCatalog source, MapPackage[] darkMaps)
    { sourceCatalog = source; optionalDarkMaps = darkMaps; SynchronizeCatalog(); }
    private void SynchronizeCatalog()
    {
        if (sourceCatalog == null || sourceRevision == sourceCatalog.Revision && sourcePreferred == sourceCatalog.Preferred) return;
        preferredFamily.Clear();preferredFamily.UnionWith(FamilyIds(sourceCatalog.Packages.FirstOrDefault(p=>p.Id==sourceCatalog.Preferred)));
        cityTiles.Clear(); cityTiles.Catalog.Packages.Clear(); cityTiles.Catalog.Packages.AddRange(sourceCatalog.Packages);
        foreach(var package in sourceCatalog.Packages) foreach(var variant in package.Variants ?? []) cityTiles.Catalog.Packages.Add(variant with { Enabled = package.Enabled && variant.Enabled });
        foreach (var dark in optionalDarkMaps)
        {
            var city = Path.GetFileName(dark.Path).Split("-dark-")[0];
            if (sourceCatalog.Packages.Any(p => p.Enabled && Path.GetFileName(p.Path).Equals(city + "-city.mbtiles", StringComparison.OrdinalIgnoreCase))) {cityTiles.Catalog.Packages.Add(dark);if(sourceCatalog.Packages.Any(p=>p.Id==sourceCatalog.Preferred && Path.GetFileName(p.Path).Equals(city+"-city.mbtiles",StringComparison.OrdinalIgnoreCase)))preferredFamily.Add(dark.Id);}
        }
        CityRasterEnabled = sourceCatalog.Preferred != "world";
        sourceRevision = sourceCatalog.Revision; sourcePreferred = sourceCatalog.Preferred;
    }
    private double rasterOpacity;
    private static readonly string[] Order=["ocean","land","urban","park","water","river","region","boundary","rail","road-major","road-secondary","road-local","building"];
    private NativeAtlasPilot(Dataset[] data,OfflineTileLayer tiles,double milliseconds,int vertices){datasets=data;cityTiles=tiles;InitializationMilliseconds=milliseconds;VertexCount=vertices;}
    public static async Task<NativeAtlasPilot> LoadAsync(string folder,string profile, MapCatalog? source = null,bool compactWorld=false)
    {
        var clock=Stopwatch.StartNew();var vertices=0;
        var data=await Task.Run(()=>new[]{"world","zurich","warsaw"}.Where(id=>id=="world" || File.Exists(Path.Combine(folder,id+".gsatlas"))).Select(id=>{
            var path=Path.Combine(folder,id+".gsatlas");if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Pilot package is too large");
            using var input=File.OpenRead(path);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var expanded=new MemoryStream();
            var buffer=new byte[65536];int read;while((read=gzip.Read(buffer))>0){if(expanded.Length+read>64*1024*1024)throw new InvalidDataException("Pilot expanded package is too large");expanded.Write(buffer,0,read);}expanded.Position=0;
            var package=JsonSerializer.Deserialize<Package>(expanded) ?? throw new InvalidDataException("Invalid pilot package");
            if(package.Schema!=1 || package.Id!=id || package.Features.Length>25000 || package.Labels.Length>30000)throw new InvalidDataException("Unsupported pilot package");
            var dataset=new Dataset{Data=package,CoverageBands=RegionalLodCoverage.Load(id,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))};
            var compact=Path.Combine(AppContext.BaseDirectory,"geography","world.gsbase");
            if(id=="world" && compactWorld && !App.LegacyGlobalFallbackForVerification && File.Exists(compact))
            {
                dataset.Data=package with{Schema=2,Features=[]};LoadCompactWorld(compact,dataset,ref vertices);
            }
            else foreach(var feature in package.Features)
            {
                if(!Order.Contains(feature.Kind) || feature.Lod is not (0 or 8 or 10 or 12 or 15))throw new InvalidDataException("Invalid pilot feature");
                using var construction=AtlasProfiling.Measure("Load.GeometryConstruction");
                var geometry=new StreamGeometry{FillRule=FillRule.EvenOdd};using(var context=geometry.Open())foreach(var pathPoints in feature.Paths)
                {
                    vertices+=pathPoints.Length;if(vertices>4000000)throw new InvalidDataException("Pilot vertex budget exceeded");
                    var points=pathPoints.Select(p=>p.Length==2 && double.IsFinite(p[0]) && double.IsFinite(p[1]) && p[0]>=-.001 && p[0]<=1.001 && p[1]>=-.001 && p[1]<=1.001?new Point(p[0],p[1]):throw new InvalidDataException("Invalid pilot coordinate")).ToArray();
                    if(points.Length<2)continue;context.BeginFigure(points[0],feature.Polygon,feature.Polygon);context.PolyLineTo(points.Skip(1).ToArray(),true,false);
                }
                geometry.Freeze();var b=geometry.Bounds;var shape=new Shape(feature.Lod,feature.Kind,feature.Polygon,geometry,b);dataset.Shapes.Add(shape);
                if(b.IsEmpty)continue;var x0=(int)Math.Floor(b.Left*256);var x1=(int)Math.Floor(b.Right*256);var y0=(int)Math.Floor(b.Top*256);var y1=(int)Math.Floor(b.Bottom*256);
                if((long)(x1-x0+1)*(y1-y0+1)>64){dataset.Wide.Add(shape);continue;}
                for(var x=x0;x<=x1;x++)for(var y=y0;y<=y1;y++){if(!dataset.Index.TryGetValue((x,y),out var cell))dataset.Index[(x,y)]=cell=[];cell.Add(shape);}
            }
            dataset.OrderedLabels=package.Labels.OrderBy(p=>p.MinZoom).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            dataset.Levels=dataset.Shapes.Select(s=>s.Lod).Distinct().Order().ToArray();
            dataset.LabelIndex=package.Labels.GroupBy(p=>((int)Math.Floor(p.X*4096),(int)Math.Floor(p.Y*4096))).ToDictionary(g=>g.Key,g=>g.OrderBy(p=>p.MinZoom).ToArray());
            dataset.Data=dataset.Data with{Features=[]}; // The frozen geometry replaces expanded JSON coordinate arrays.
            return dataset;
        }).ToArray());
        var darkFolder=Path.GetFullPath(Path.Combine(folder,"..","atlas-pilot-dark"));
        if (!Directory.Exists(darkFolder)) darkFolder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../offline-maps/atlas-pilot-dark"));
        var darkMaps=await Task.Run(()=>Directory.Exists(darkFolder)?Directory.GetFiles(darkFolder,"*-dark-*.mbtiles").Order().Take(4).Select(MapPackage.Inspect).ToArray():[]);
        var tiles=new OfflineTileLayer(profile);tiles.Catalog.Packages.RemoveAll(m=>!new[]{"zurich-city.mbtiles","warsaw-city.mbtiles"}.Contains(Path.GetFileName(m.Path)));tiles.Catalog.Packages.AddRange(darkMaps);tiles.Catalog.Preferred="world";
        var atlas = new NativeAtlasPilot(data,tiles,clock.Elapsed.TotalMilliseconds,vertices);
        if (source != null) atlas.ConnectCatalog(source, darkMaps);
        await atlas.LoadGlobalAtlas(profile);
        var roadPath=Path.Combine(AppContext.BaseDirectory,"geography/urban-roads.gslite");
        if(compactWorld && App.UrbanMode=="c" && File.Exists(roadPath))atlas.UrbanRoads=await Task.Run(()=>new UrbanRoadLayer(roadPath));
        return atlas;
    }
    private static Brush Color(string hex){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;b.Freeze();return b;}
    private void DrawBaseline(DrawingContext dc,double world,double width,double height,Point center,bool dark,bool interacting,Action invalidate)
    {
        using var totalProbe=AtlasProfiling.Measure("Vector.Total");
        var watch=Stopwatch.StartNew();var z=Math.Log2(world/256);var visible=new Rect(center.X-width/world/2,center.Y-height/world/2,width/world,height/world);LastDrawnShapes=0;
        var land=Color(dark?"#24343A":"#F6F2E8");var ocean=Color(dark?"#152B38":"#A9D6E5");var ink=Color(dark?"#D0DEE0":"#354851");
        dc.DrawRectangle(ocean,null,new(0,0,width,height));
        var palette=new Dictionary<string,Brush>{["ocean"]=ocean,["land"]=land,["urban"]=Color(dark?"#303F42":"#E9E4DA"),["park"]=Color(dark?"#2C4C3D":"#DCE8C9"),["water"]=ocean,["river"]=ocean,["region"]=Color(dark?"#6C6279":"#BDABC7"),["boundary"]=Color(dark?"#8D7D9B":"#BDABC7"),["rail"]=Color(dark?"#717278":"#B1A7AB"),["building"]=Color(dark?"#435159":"#D4CDC1"),["road-major"]=Color(dark?"#BBA76F":"#EFD18B"),["road-secondary"]=Color(dark?"#9C946F":"#F0D9A0"),["road-local"]=Color(dark?"#737F81":"#FFFFFF")};
        var region=datasets.Skip(1).FirstOrDefault(d=>Contains(d.Data.Coverage,visible));
        foreach(var dataset in datasets)
        {
            if(dataset.Data.Id!="world" && z<6)continue;
            if(dataset.Data.Id=="world" && region!=null && z>=8)continue;
            var requested=z<9?8:z<12?10:12;var lod=dataset.Data.Id=="world"?0:dataset.Shapes.Where(s=>s.Lod<=requested).Select(s=>s.Lod).DefaultIfEmpty(0).Max();LastNativeLod=lod;
            for(var wrap=-1;wrap<=1;wrap++)
            {
                var query=new Rect(visible.X-wrap,visible.Y,visible.Width,visible.Height);
                var queryStarted=Stopwatch.GetTimestamp();
                var shapes=dataset.Query(query).Where(s=>(s.Lod==lod || s.Lod==15 && z>=14) && s.Geometry.Bounds.IntersectsWith(query)).OrderBy(s=>s.Lod).ThenBy(s=>Array.IndexOf(Order,s.Kind)).ToArray();
                AtlasProfiling.Current?.Record("Vector.QueryFilterSort",Stopwatch.GetElapsedTime(queryStarted).TotalMilliseconds);
                using var drawingProbe=AtlasProfiling.Measure("Vector.CommandsAndTransform");
                dc.PushTransform(new MatrixTransform(world,0,0,world,width/2+(wrap-center.X)*world,height/2-center.Y*world));
                foreach(var item in shapes)
                {
                    if(item.Kind=="region" && z<3 || item.Kind=="road-local" && z<11)continue;
                    var brush=palette[item.Kind];
                    if(item.Polygon)dc.DrawGeometry(brush,null,item.Geometry);
                    else
                    {
                        var w=item.Kind switch{"road-major"=>z>=14?3.4:2.2,"road-secondary"=>z>=14?2.2:1.4,"road-local"=>1.05,"river"=>1.4,"rail"=>.65,_=>.75};
                        if(item.Kind.StartsWith("road-"))dc.DrawGeometry(null,new Pen(Color(dark?"#263336":"#C9C1B1"),(w+1.1)/world),item.Geometry);
                        dc.DrawGeometry(null,new Pen(brush,w/world),item.Geometry);
                    }
                    LastDrawnShapes++;
                }
                dc.Pop();
            }
        }
        using(var rasterProbe=AtlasProfiling.Measure("Raster.Composite"))
        {
        rasterOpacity=!dark && CityRasterEnabled?Math.Clamp((z-13.5)/1.5,0,1):0;
        if(rasterOpacity>0)
        {
            var location=Geo.Unproject(center.X,center.Y);var map=cityTiles.Catalog.Packages.FirstOrDefault(m=>m.CoversBounds(m.MaxZoom,location.Latitude,location.Longitude));
            if(map!=null && cityTiles.Catalog.Preferred!=map.Id){cityTiles.Catalog.Preferred=map.Id;cityTiles.Clear();}
            dc.PushOpacity(rasterOpacity);cityTiles.Draw(dc,visible,world,width,height,center,invalidate,interacting);dc.Pop();
        }
        }
        using var labelProbe=AtlasProfiling.Measure("Vector.Labels");
        var occupied=new List<Rect>();var names=new HashSet<string>();
        foreach(var dataset in datasets.Reverse())foreach(var place in dataset.Data.Labels.Where(p=>p.MinZoom<=z).OrderBy(p=>p.MinZoom))
        {
            if(place.Country && z>7 || !place.Country && dataset.Data.Id=="world" && region!=null && z>=8)continue;
            var x=Geo.WrappedDelta(place.X-center.X)*world+width/2;var y=(place.Y-center.Y)*world+height/2;
            if(x<30 || y<30 || x>width-30 || y>height-32)continue;
            if(LastRasterVisible && cityTiles.Catalog.Packages.Any(m=>m.Id==cityTiles.Catalog.Preferred && m.CoversBounds(m.MaxZoom,Geo.Unproject(place.X,place.Y).Latitude,Geo.Unproject(place.X,place.Y).Longitude)))continue;
            var name=App.Settings.Language=="ru-RU"?place.Russian:place.English;if(string.IsNullOrWhiteSpace(name))name=place.Name;if(!names.Add(name))continue;
            var key=$"{name}:{dark}:{place.Country}";
            if(!textCache.TryGetValue(key,out var text)){if(textCache.Count>=512)textCache.Clear();text=new(name,CultureInfo.GetCultureInfo(App.Settings.Language),FlowDirection.LeftToRight,new Typeface(place.Country?"Segoe UI Semibold":"Segoe UI"),place.Country?12:11,ink,1);textCache[key]=text;}
            var rect=new Rect(x-text.Width/2-4,y-text.Height/2-2,text.Width+8,text.Height+4);if(occupied.Any(r=>r.IntersectsWith(rect)))continue;
            occupied.Add(rect);dc.DrawRoundedRectangle(Color(dark?"#C024343A":"#C0F6F2E8"),null,rect,3,3);dc.DrawText(text,new(x-text.Width/2,y-text.Height/2));if(occupied.Count>=48)break;
        }
        LastRenderMilliseconds=watch.Elapsed.TotalMilliseconds;MaximumRenderMilliseconds=Math.Max(MaximumRenderMilliseconds,LastRenderMilliseconds);
    }
    private static bool Contains(double[] b,Rect view){var n=Geo.Project(b[3],b[0]);var s=Geo.Project(b[1],b[2]);return view.Left>=n.X && view.Right<=s.X && view.Top>=n.Y && view.Bottom<=s.Y;}
    public async ValueTask DisposeAsync(){if(GlobalAtlas!=null)await GlobalAtlas.DisposeAsync();await cityTiles.DisposeAsync();}
}
