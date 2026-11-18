// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

/// <summary>Lazy, bounded native vector context. All SQLite/decoding/geometry construction stays off the UI thread.</summary>
public sealed partial class GlobalVectorAtlas : IAsyncDisposable
{
    private sealed record Shape(string Kind,bool Polygon,Geometry Geometry,Rect Bounds);
    private sealed record Tile(Shape[] Shapes,NativeAtlasPilot.Place[] Places,int Vertices);
    private sealed record Batch(long Generation,(int Z,int X,int Y)[] Keys);
    private readonly Channel<Batch> work=Channel.CreateBounded<Batch>(new BoundedChannelOptions(1){SingleReader=true,FullMode=BoundedChannelFullMode.DropOldest});
    private readonly CancellationTokenSource stop=new();
    private readonly Dictionary<(int,int,int),Tile?> cache=[];
    private readonly Queue<(int,int,int)> order=[];
    private readonly object gate=new();
    private readonly Dispatcher dispatcher;
    private readonly Task worker;
    private HashSet<(int,int,int)> wanted=[];
    private HashSet<(int,int,int)> visible=[];
    private long generation;
    private int publication,vertices;
    private Action? invalidate;
    private bool enabled=true;
    public bool Enabled {get=>enabled;set{if(enabled==value)return;enabled=value;if(!value)lock(gate){wanted.Clear();visible.Clear();generation++;}}}
    public string Path {get;}
    public GlobalAtlasManifest Manifest {get;}
    public int DrawnTiles {get;private set;}
    public int PendingTiles {get;private set;}
    public int FailedTiles {get;private set;}
    public int LastShapes {get;private set;}
    public double LastPreparationMilliseconds {get;private set;}
    public long CacheHits {get;private set;}
    public long CacheMisses {get;private set;}
    internal NativeAtlasPilot.Place[] VisiblePlaces {get;private set;}=[];
    public GlobalVectorAtlas(string path,Dispatcher dispatcher)
    {
        Path=path;this.dispatcher=dispatcher;using(var package=new GlobalAtlasPackage(path,true))Manifest=package.Manifest;
        worker=Task.Run(Run);
    }
    internal object Snapshot(){lock(gate)return new{Path,Enabled,DrawnTiles,VisibleLabels=VisiblePlaces.Length,PendingTiles=wanted.Count(k=>!cache.ContainsKey(k)),LastShapes,FailedTiles,CacheHits,CacheMisses,CacheEntries=cache.Count,CachedVertices=vertices,LastPreparationMilliseconds,Generation=generation,QueueCapacity=1,WorkerConcurrency=1,Manifest.MaxZoom};}
    public static string? Classify(TileFeature feature)
    {
        var kind=feature.Properties.GetValueOrDefault("kind","");var detail=feature.Properties.GetValueOrDefault("kind_detail","");
        return feature.Layer switch{
            "earth"=>"land","water"=>feature.Type==3?"water":"river",
            "boundaries"=>"boundary",
            "landuse" when kind is "urban_area" or "residential"=>"urban",
            "landuse" when kind is "forest" or "park" or "national_park" or "nature_reserve" or "wood"=>"park",
            "natural" when kind is "forest" or "wood"=>"park",
            "roads" when kind=="highway" && detail is "motorway" or "trunk" or "primary"=>"road-major",
            "roads" when kind=="major_road" || detail is "primary" or "secondary" or "tertiary"=>"road-secondary",
            "roads" when kind is "highway" or "ferry"=>"road-major",
            "roads" when kind=="rail"=>"rail",_=>null};
    }
    private static Tile Prepare(byte[] bytes,(int Z,int X,int Y) key)
    {
        using var preparation=AtlasProfiling.Measure("Global.Prepare");
        using var gzip=new System.IO.Compression.GZipStream(new MemoryStream(bytes),System.IO.Compression.CompressionMode.Decompress);using var output=new MemoryStream();
        var decompression=Stopwatch.StartNew();var buffer=new byte[65536];int read;while((read=gzip.Read(buffer))>0){if(output.Length+read>32*1024*1024)throw new InvalidDataException("Global tile expanded budget exceeded");output.Write(buffer,0,read);}
        AtlasProfiling.Current?.Record("Global.Decompress",decompression.Elapsed.TotalMilliseconds);
        if(output.Length>=5 && output.GetBuffer()[0]==(byte)'G' && output.GetBuffer()[1]==(byte)'S')return PrepareCompact(output.ToArray(),key);
        IReadOnlyList<TileFeature> features;using(AtlasProfiling.Measure("Global.Parse"))features=VectorTile.Decode(output.ToArray());
        using var geometryScope=AtlasProfiling.Measure("Global.GeometryAllocation");
        var shapes=new List<Shape>();var places=new List<NativeAtlasPilot.Place>();var vertices=0;var n=1<<key.Z;
        foreach(var feature in features)
        {
            if(feature.Layer!="places" || feature.Type!=1 || !feature.Properties.TryGetValue("name",out var name) || feature.Paths.Count==0 || feature.Paths[0].Length==0)continue;
            var point=feature.Paths[0][0];var x=(key.X+point.X/(double)feature.Extent)/n;var y=(key.Y+point.Y/(double)feature.Extent)/n;
            if(point.X>=0 && point.Y>=0 && point.X<feature.Extent && point.Y<feature.Extent)
                places.Add(new(feature.Properties.GetValueOrDefault("wikidata",FormattableString.Invariant($"{x:F6}/{y:F6}/{name}")),x,y,name,feature.Properties.GetValueOrDefault("name:en",name),feature.Properties.GetValueOrDefault("name:ru",name),double.TryParse(feature.Properties.GetValueOrDefault("min_zoom","6"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var min)?min:6,feature.Properties.GetValueOrDefault("kind","")=="country"));
        }
        // MVT polygon winding separates exterior and hole rings. Nonzero fill also preserves overlapping
        // same-style features while batching them into one immutable WPF resource per class and tile.
        foreach(var group in features.Where(f=>f.Type!=1).Select(f=>(Feature:f,Kind:Classify(f))).Where(f=>f.Kind!=null).GroupBy(f=>(Kind:f.Kind!,Polygon:f.Feature.Type==3)))
        {
            var geometry=new StreamGeometry{FillRule=FillRule.Nonzero};
            using(var context=geometry.Open())foreach(var entry in group)foreach(var path in entry.Feature.Paths)
            {
                if(path.Length<2)continue;vertices+=path.Length;if(vertices>250000)throw new InvalidDataException("Global tile vertex budget exceeded");
                var points=path.Select(p=>new Point(p.X/(double)entry.Feature.Extent,p.Y/(double)entry.Feature.Extent)).ToArray();
                context.BeginFigure(points[0],group.Key.Polygon,group.Key.Polygon);context.PolyLineTo(points[1..],true,false);
            }
            geometry.Freeze();shapes.Add(new(group.Key.Kind,group.Key.Polygon,geometry,geometry.Bounds));
        }
        var priority=new[]{"land","urban","park","water","river","boundary","rail","road-major","road-secondary"};
        return new(shapes.OrderBy(s=>Array.IndexOf(priority,s.Kind)).ToArray(),places.OrderBy(p=>p.MinZoom).ThenBy(p=>p.Id,StringComparer.Ordinal).Take(200).ToArray(),vertices);
    }
    private async Task Run()
    {
        try
        {
            using var package=new GlobalAtlasPackage(Path);
            await foreach(var batch in work.Reader.ReadAllAsync(stop.Token))foreach(var key in batch.Keys)
            {
                lock(gate){if(batch.Generation!=generation)break;if(cache.ContainsKey(key))continue;}
                var timer=Stopwatch.StartNew();Tile? tile=null;
                try{byte[]? data;using(AtlasProfiling.Measure("Global.SQLiteRead"))data=package.ReadTile(key.Z,key.X,key.Y,stop.Token);if(data!=null)tile=Prepare(data,key);}
                catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or OverflowException){FailedTiles++; }
                lock(gate)
                {
                    AtlasProfiling.Current?.Record("Global.ReadAndPrepare",timer.Elapsed.TotalMilliseconds);
                    LastPreparationMilliseconds=timer.Elapsed.TotalMilliseconds;cache[key]=tile;vertices+=tile?.Vertices??0;order.Enqueue(key);
                    while(cache.Count>64 || vertices>4000000){var old=order.Dequeue();if(cache.Remove(old,out var removed))vertices-=removed?.Vertices??0;}
                    if(!visible.Any(v=>v.Item1>=key.Z && (v.Item2>>(v.Item1-key.Z))==key.X && (v.Item3>>(v.Item1-key.Z))==key.Y))continue;
                }
                if(Interlocked.Exchange(ref publication,1)==0)_=dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{Interlocked.Exchange(ref publication,0);if(!stop.IsCancellationRequested){AtlasProfiling.Current?.Mark("Global.Publish");invalidate?.Invoke();}});
            }
        }
        catch(OperationCanceledException){}
        catch(IOException){}
    }
    private (Tile Tile,int Z,int X,int Y)? Resolve((int Z,int X,int Y) key)
    {
        for(var z=key.Z;z>=0;z--){var shift=key.Z-z;var x=key.X>>shift;var y=key.Y>>shift;if(cache.TryGetValue((z,x,y),out var tile) && tile!=null)return(tile,z,x,y);}return null;
    }
    // Reduce source LOD for large windows so visible tiles plus one-ring overscan fit the cache.
    public static int SelectSourceZoom(double world,double width,double height,int maximum)
    {
        var z=Math.Clamp((int)Math.Floor(Math.Log2(world/256)),0,maximum);
        while(z>0 && (Math.Ceiling(width/(world/(1<<z)))+3)*(Math.Ceiling(height/(world/(1<<z)))+3)>42)z--;
        return z;
    }
    internal bool CanCover(double world,double width,double height,Point center)
    {
        if(!Enabled || world<256*Math.Pow(2,5.8))return false;
        var z=SelectSourceZoom(world,width,height,Manifest.MaxZoom);var n=1<<z;
        var x0=(int)Math.Floor((center.X-width/world/2)*n);var x1=(int)Math.Floor((center.X+width/world/2)*n);var y0=Math.Max(0,(int)Math.Floor((center.Y-height/world/2)*n));var y1=Math.Min(n-1,(int)Math.Floor((center.Y+height/world/2)*n));
        lock(gate){for(var x=x0;x<=x1;x++)for(var y=y0;y<=y1;y++)if(Resolve((z,(x%n+n)%n,y))==null)return false;}return true;
    }
    internal void Draw(DrawingContext dc,double world,double width,double height,Point center,IReadOnlyDictionary<string,Brush> brushes,Func<string,double,double,bool,Pen> stroke,Action refresh)
    {
        using var drawing=AtlasProfiling.Measure("Global.DrawingCommands");
        DrawnTiles=0;LastShapes=0;VisiblePlaces=[];if(!Enabled || world<256*Math.Pow(2,5.8)){lock(gate){if(wanted.Count>0){wanted.Clear();visible.Clear();generation++;}}return;}
        invalidate=refresh;var z=SelectSourceZoom(world,width,height,Manifest.MaxZoom);var n=1<<z;
        var x0=(int)Math.Floor((center.X-width/world/2)*n)-1;var x1=(int)Math.Floor((center.X+width/world/2)*n)+1;var y0=Math.Max(0,(int)Math.Floor((center.Y-height/world/2)*n)-1);var y1=Math.Min(n-1,(int)Math.Floor((center.Y+height/world/2)*n)+1);
        var requests=new List<(int Z,int X,int Y)>();var visibleKeys=new HashSet<(int,int,int)>();var labels=new List<NativeAtlasPilot.Place>();
        var tilePixels=world/n;dc.PushClip(new RectangleGeometry(new Rect(0,0,width,height)));
        for(var x=x0;x<=x1;x++)for(var y=y0;y<=y1;y++)
        {
            var key=(z,(x%n+n)%n,y);requests.Add(key);
            if(z>0)requests.Add((z-1,key.Item2/2,y/2));
            var left=width/2+(x/(double)n-center.X)*world;var top=height/2+(y/(double)n-center.Y)*world;
            if(left>=width || top>=height || left+tilePixels<=0 || top+tilePixels<=0)continue;
            visibleKeys.Add(key);(Tile Tile,int Z,int X,int Y)? resolved;
            lock(gate){resolved=Resolve(key);if(resolved==null){CacheMisses++;continue;}CacheHits++;}
            var chosen=resolved.Value;var tile=chosen.Tile;var scale=world/(1<<chosen.Z);var shift=z-chosen.Z;
            var parentX=Math.Floor(x/(double)(1<<shift));var sourceLeft=width/2+(parentX/(1<<chosen.Z)-center.X)*world;var sourceTop=height/2+(chosen.Y/(double)(1<<chosen.Z)-center.Y)*world;
            dc.PushClip(new RectangleGeometry(new Rect(left,top,tilePixels,tilePixels)));dc.DrawRectangle(brushes["ocean"],null,new Rect(left,top,tilePixels,tilePixels));
            dc.PushTransform(new MatrixTransform(scale,0,0,scale,sourceLeft,sourceTop));
            if(Manifest.Schema==2)dc.PushGuidelineSet(CompactGuidelines);
            var localView=new Rect(-sourceLeft/scale,-sourceTop/scale,width/scale,height/scale);localView.Inflate(4/scale,4/scale);
            foreach(var shape in tile.Shapes)
            {
                if(!shape.Bounds.IntersectsWith(localView))continue;
                if(shape.Polygon)dc.DrawGeometry(brushes[shape.Kind],null,shape.Geometry);
                else{var w=shape.Kind=="road-major"?2.2:shape.Kind=="road-secondary"?1.4:shape.Kind=="river"?1.4:.75;if(shape.Kind.StartsWith("road-"))dc.DrawGeometry(null,stroke(shape.Kind,w,scale,true),shape.Geometry);dc.DrawGeometry(null,stroke(shape.Kind,w,scale,false),shape.Geometry);}
            }
            if(Manifest.Schema==2)dc.Pop();
            dc.Pop();dc.Pop();DrawnTiles++;LastShapes+=tile.Shapes.Length;labels.AddRange(tile.Places);
        }
        dc.Pop();VisiblePlaces=labels.DistinctBy(p=>p.Id).OrderBy(p=>p.MinZoom).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        var next=requests.ToHashSet();
        lock(gate)
        {
            visible=visibleKeys;
            PendingTiles=next.Count(k=>!cache.ContainsKey(k));
            if(wanted.SetEquals(next))return;wanted=next;generation++;
            work.Writer.TryWrite(new(generation,requests.Distinct().OrderBy(k=>k.Z).ThenBy(k=>Math.Abs(Geo.WrappedDelta((k.X+.5)/(1<<k.Z)-center.X))+Math.Abs((k.Y+.5)/(1<<k.Z)-center.Y)).ToArray()));
        }
    }
    public async ValueTask DisposeAsync(){stop.Cancel();work.Writer.TryComplete();await worker.ConfigureAwait(false);stop.Dispose();lock(gate){cache.Clear();order.Clear();vertices=0;}}
}
