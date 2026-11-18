// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

/// <summary>UI-owned bounded caches and latest-viewport queue; one cancellable reader/decoder owns SQLite work.</summary>
public sealed class OfflineTileLayer : IAsyncDisposable
{
    public const int ImageLimit=192,MissingLimit=2048,RequestLimit=128,ReaderLimit=4;
    private readonly BoundedLru<RasterTileKey,ImageSource> images=new(ImageLimit);
    private readonly BoundedLru<RasterTileKey,long> missing=new(MissingLimit);
    private readonly HashSet<RasterTileKey> pending=[];
    private readonly Queue<TilePlacement> queue=new();
    private Dictionary<RasterTileKey,TilePlacement> needed=[];
    private readonly BoundedLru<string,ReaderSlot> readers=new(ReaderLimit);
    private readonly Dictionary<string,long> unavailable=[];
    private sealed record ReaderSlot(string Id,RasterTileReader Reader);
    private readonly SemaphoreSlim readerGate=new(1);
    private readonly DispatcherTimer redraw;
    private Action? invalidate;
    private CancellationTokenSource? activeCancellation;
    private RasterTileKey? activeKey;
    private readonly BoundedLru<string,RasterFrame> frames=new(2);
    private readonly BoundedLru<string,bool> failedFrames=new(32);
    private IReadOnlyList<RasterFramePlan> framePlans=[];
    public const double Overscan=192,PlanStep=96,QuietMilliseconds=120;
    public RasterPresentation Presentation { get; }=new();
    private Point plannedCenter,lastCenter;
    private double plannedWorld=-1,plannedWidth,plannedHeight,lastWorld=-1,lastWidth,lastHeight,lastMotion;
    private string? plannedPreferred;
    private bool settling;
    private Task compositor=Task.CompletedTask;
    private bool composing;
    private string? composingKey;
    private CancellationTokenSource? frameCancellation;
    public string? VisibleFrameKey { get; private set; }
    public int VisibleNativeZoom { get; private set; }
    private bool loading,disposed;
    private int generation,catalogEpoch,resetReaders;
    private Task worker=Task.CompletedTask,readerCleanup=Task.CompletedTask;
    public TileDiagnostics Diagnostics { get; }=new();
    public MapCatalog Catalog { get; }
    private bool nativeOnly;
    public bool NativeOnly {get=>nativeOnly;set{if(nativeOnly==value)return;nativeOnly=value;Clear();}}
    public int DecodedTileCount=>images.Count;
    public int CacheCount=>images.Count;
    public int MissingCacheCount=>missing.Count;
    public int PendingCount=>pending.Count;
    public int Generation=>generation;
    internal object ResourceSnapshot()
    {
        var retained=frames.Values.Concat(new[]{Presentation.Front,Presentation.Context,Presentation.Previous}.OfType<RasterFrame>()).DistinctBy(f=>f.Image).ToArray();
        long Pixels(System.Windows.Media.Imaging.BitmapSource b)=>(long)b.PixelWidth*b.PixelHeight*4;
        return new{Images=images.Count,Frames=frames.Count,RetainedFrames=retained.Length,EstimatedDecodedPixelBytes=images.Values.OfType<System.Windows.Media.Imaging.BitmapSource>().Sum(Pixels),EstimatedFramePixelBytes=retained.Sum(f=>Pixels(f.Image)),Missing=missing.Count,Pending=pending.Count,Queued=queue.Count,Readers=readers.Count,Composing=composing,Loading=loading,Generation=generation,Presentation.Swaps,Presentation.SourceChanges};
    }
    public int DrawnTileCount { get; private set; }
    public bool IsLoading=>loading || composing || pending.Count>0 || queue.Count>0 || settling && (Environment.TickCount64-lastMotion<QuietMilliseconds || Presentation.Transitioning(Environment.TickCount64));
    public HashSet<string> Attributions { get; }=[];
    public HashSet<string> RenderedMapIds { get; }=[];
    public OfflineTileLayer(string directory)
    {
        Catalog=new(directory);
        redraw=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(30)};
        redraw.Tick+=(_,_)=>{redraw.Stop();if(!disposed){if(Diagnostics.Enabled)Diagnostics.Redraws++;invalidate?.Invoke();}};
    }
    public void Clear()
    {
        generation++;catalogEpoch++;activeCancellation?.Cancel();frameCancellation?.Cancel();queue.Clear();pending.Clear();needed.Clear();images.Clear();missing.Clear();frames.Clear();failedFrames.Clear();framePlans=[];Presentation.Clear();plannedWorld=-1;settling=false;redraw.Stop();
        Interlocked.Exchange(ref resetReaders,1);
        if(!loading)_=CloseReadersAsync();
    }
    public void Draw(DrawingContext dc,Rect visible,double world,double width,double height,Point center,Action invalidate,bool interacting=false)
    {
        if(disposed)return;var started=Stopwatch.GetTimestamp();this.invalidate=invalidate;
        var now=(double)Environment.TickCount64;
        if(center!=lastCenter || world!=lastWorld || width!=lastWidth || height!=lastHeight){lastMotion=now;lastCenter=center;lastWorld=world;lastWidth=width;lastHeight=height;}
        var moving=interacting || now-lastMotion<QuietMilliseconds;
        if(plannedPreferred!=Catalog.Preferred){Presentation.Clear();plannedPreferred=Catalog.Preferred;plannedWorld=-1;}
        if(world!=plannedWorld || width!=plannedWidth || height!=plannedHeight || Math.Abs(GeoStack.Core.Geo.WrappedDelta(center.X-plannedCenter.X))*world>=PlanStep || Math.Abs(center.Y-plannedCenter.Y)*world>=PlanStep)
        {
            framePlans=RasterComposition.Plans(Catalog.Packages,Catalog.Preferred,world,width,height,center,Overscan,NativeOnly);
            plannedCenter=center;plannedWorld=world;plannedWidth=width;plannedHeight=height;
        }
        if(composingKey!=null && !framePlans.Any(p=>p.Key==composingKey))frameCancellation?.Cancel();
        var plan=framePlans.SelectMany(p=>p.Tiles).ToArray();
        var next=plan.DistinctBy(p=>p.Key).ToDictionary(p=>p.Key);
        if(!next.Keys.ToHashSet().SetEquals(needed.Keys))
        {
            generation++;if(Diagnostics.Enabled)Diagnostics.Cancelled+=queue.Count;
            queue.Clear();pending.Clear();needed=next;
            if(activeKey is { } active) { pending.Add(active);if(!needed.ContainsKey(active))activeCancellation?.Cancel(); }
        }
        if(Diagnostics.Enabled)Diagnostics.Viewport=new { CenterX=center.X,CenterY=center.Y,World=world,Width=width,Height=height,Preferred=Catalog.Preferred,Generation=generation,Placements=plan.Length };
        QueueCurrent();
        DrawnTileCount=0;Attributions.Clear();RenderedMapIds.Clear();VisibleFrameKey=null;VisibleNativeZoom=0;
        var ready=framePlans.Select(p=>frames.TryGet(p.Key,out var f)?f:null).Where(f=>f!=null).Cast<RasterFrame>().ToArray();
        Presentation.Update(ready,world,width,height,center,moving,now);
        void Paint(RasterFrame? frame,double opacity)
        {
            if(frame==null || opacity<=.001 || NativeOnly && frame.Plan.Zoom<Math.Floor(Math.Log2(world/256)))return;
            var coverage=frame.CoverageScreen(world,width,height,center);var clip=Rect.Intersect(coverage,new Rect(0,0,width,height));if(clip.IsEmpty || clip.Width<=0 || clip.Height<=0)return;
            dc.PushClip(new RectangleGeometry(clip));dc.PushOpacity(opacity);
            var masks=0;
            void Feather(double start,double end,double extent,bool horizontal)
            {
                if(start<=0 && end>=extent)return;
                var margin=Math.Min(.5,48/(end-start));
                var brush=new LinearGradientBrush {MappingMode=BrushMappingMode.Absolute,StartPoint=horizontal?new(start,0):new(0,start),EndPoint=horizontal?new(end,0):new(0,end)};
                brush.GradientStops.Add(new(start>0?Colors.Transparent:Colors.White,0));brush.GradientStops.Add(new(Colors.White,margin));
                brush.GradientStops.Add(new(Colors.White,1-margin));brush.GradientStops.Add(new(end<extent?Colors.Transparent:Colors.White,1));brush.Freeze();dc.PushOpacityMask(brush);masks++;
            }
            // Feather only the retained surface boundary, never individual tile edges.
            Feather(coverage.Left,coverage.Right,width,true);Feather(coverage.Top,coverage.Bottom,height,false);
            dc.DrawImage(frame.Image,frame.Screen(world,width,height,center));
            for(var i=0;i<masks;i++)dc.Pop();
            dc.Pop();dc.Pop();DrawnTileCount+=frame.Plan.Tiles.Count;Attributions.Add(frame.Plan.Package.Attribution);RenderedMapIds.Add(frame.Plan.Package.Id);
        }
        var opacity=Presentation.FrontOpacity(world,width,height,center);
        if(Presentation.Front==null || opacity<.999 || Presentation.Transitioning(now) || RasterPresentation.Coverage(Presentation.Front,world,width,height,center)<.999)Paint(Presentation.Context,1);
        Paint(Presentation.Front,Presentation.Transitioning(now)?Presentation.Progress(now):opacity);
        if(Presentation.Previous?.Plan.Key!=Presentation.Context?.Plan.Key)Paint(Presentation.Previous,Presentation.PreviousOpacity(now));
        if(Presentation.Front is {} front){VisibleFrameKey=front.Plan.Key;VisibleNativeZoom=front.Plan.Zoom;}
        settling=(Presentation.Front!=null && !interacting && now-lastMotion<QuietMilliseconds) || Presentation.Transitioning(now);
        if(settling && !redraw.IsEnabled)redraw.Start();
        ScheduleComposition();
        if(!loading && queue.Count>0)worker=Load();
        if(Diagnostics.Enabled)Diagnostics.MaxTileDrawMs=Math.Max(Diagnostics.MaxTileDrawMs,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    private void ScheduleComposition()
    {
        if(disposed || composing)return;
        var plan=framePlans.FirstOrDefault(p=>!frames.Contains(p.Key) && !failedFrames.Contains(p.Key) && p.Tiles.All(t=>images.Contains(t.Key)));
        if(plan==null)return;
        var sources=plan.Tiles.Select(t=>{images.TryGet(t.Key,out var image);return (System.Windows.Media.Imaging.BitmapSource)image;}).ToArray();
        composing=true;composingKey=plan.Key;var epoch=catalogEpoch;frameCancellation=new();
        compositor=Compose(plan,sources,epoch);
    }
    private async Task Compose(RasterFramePlan plan,IReadOnlyList<System.Windows.Media.Imaging.BitmapSource> sources,int epoch)
    {
        try
        {
            var completed=0L;var frame=await Task.Run(()=>{var prepared=RasterComposition.Assemble(plan,sources,frameCancellation!.Token);completed=Stopwatch.GetTimestamp();return prepared;});
            if(completed!=0)AtlasProfiling.Current?.Record("Raster.BackgroundToUI",Stopwatch.GetElapsedTime(completed).TotalMilliseconds);
            if(!disposed && epoch==catalogEpoch && framePlans.Any(p=>p.Key==plan.Key)) { frames.Set(plan.Key,frame);AtlasProfiling.Current?.Mark("Raster.Publish");if(!redraw.IsEnabled)redraw.Start(); }
        }
        catch(OperationCanceledException){}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException){failedFrames.Set(plan.Key,true); }
        finally { frameCancellation?.Dispose();frameCancellation=null;composing=false;composingKey=null;if(!disposed)ScheduleComposition(); }
    }
    private bool KnownMissing(RasterTileKey key)
    {
        if(!missing.TryGet(key,out var expires))return false;
        if(expires>DateTime.UtcNow.Ticks)return true;missing.Remove(key);return false;
    }
    private void QueueCurrent()
    {
        foreach(var tile in needed.Values.OrderByDescending(t=>t.Package.MaxZoom<15).ThenByDescending(t=>t.Priority))
            if(pending.Count<RequestLimit && !images.Contains(tile.Key) && !KnownMissing(tile.Key) && pending.Add(tile.Key))queue.Enqueue(tile);
        if(Diagnostics.Enabled)Diagnostics.MaxPending=Math.Max(Diagnostics.MaxPending,pending.Count);
    }
    private async Task Load()
    {
        loading=true;
        try
        {
            while(!disposed && queue.TryDequeue(out var tile))
            {
                if(!needed.ContainsKey(tile.Key)){pending.Remove(tile.Key);continue;}
                var epoch=catalogEpoch;var requestGeneration=generation;
                using var cancellation=new CancellationTokenSource();activeCancellation=cancellation;activeKey=tile.Key;
                Diagnostics.Request(tile.Key.Map,tile.Key.Z,tile.Key.X,tile.Key.Y,requestGeneration,pending.Count,1);
                ImageSource? image=null;var cancelled=false;var failed=false;
                try { image=await Task.Run(()=>Read(tile,cancellation.Token)); }
                catch(OperationCanceledException){cancelled=true;if(Diagnostics.Enabled)Diagnostics.Cancelled++;}
                catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException or System.Runtime.InteropServices.COMException){failed=true;}
                activeCancellation=null;activeKey=null;pending.Remove(tile.Key);
                if(!disposed && epoch==catalogEpoch && needed.ContainsKey(tile.Key) && !cancelled)
                {
                    if(image!=null)
                    {
                        if(images.Set(tile.Key,image)!=null && Diagnostics.Enabled)Diagnostics.Evictions++;
                        ScheduleComposition();
                    }
                    else { missing.Set(tile.Key,DateTime.UtcNow.AddSeconds(failed?30:120).Ticks);if(Diagnostics.Enabled)Diagnostics.Missing++; }
                    if(Diagnostics.Enabled) { Diagnostics.Completions++;Diagnostics.MaxCache=Math.Max(Diagnostics.MaxCache,images.Count);Diagnostics.MaxMissingCache=Math.Max(Diagnostics.MaxMissingCache,missing.Count); }
                }
                else if(Diagnostics.Enabled)Diagnostics.Obsolete++;
                if(!disposed)QueueCurrent();
            }
        }
        finally
        {
            activeCancellation=null;activeKey=null;loading=false;
            if(Volatile.Read(ref resetReaders)!=0 || disposed)await CloseReadersAsync();
        }
    }
    private ImageSource? Read(TilePlacement tile,CancellationToken token)
    {
        readerGate.Wait(token);
        try
        {
            if(Interlocked.Exchange(ref resetReaders,0)!=0)CloseReaders();token.ThrowIfCancellationRequested();
            if(unavailable.TryGetValue(tile.Key.Map,out var expires) && expires>DateTime.UtcNow.Ticks)return null;
            if(!readers.TryGet(tile.Key.Map,out var slot))
            {
                if(readers.Count>=ReaderLimit) { var old=readers.Values.First();readers.Remove(old.Id);old.Reader.Dispose();if(Diagnostics.Enabled)Diagnostics.ConnectionsClosed++; }
                try { slot=new(tile.Key.Map,new RasterTileReader(tile.Package)); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){unavailable[tile.Key.Map]=DateTime.UtcNow.AddSeconds(30).Ticks;return null;}
                readers.Set(tile.Key.Map,slot);if(Diagnostics.Enabled){Diagnostics.ConnectionsOpened++;Diagnostics.MaxOpenReaders=Math.Max(Diagnostics.MaxOpenReaders,readers.Count);}
            }
            var watch=Stopwatch.StartNew();var bytes=slot.Reader.Read(tile.Key.Z,tile.Key.X,tile.Key.Y,token);
            if(Diagnostics.Enabled)Diagnostics.MaxLookupMs=Math.Max(Diagnostics.MaxLookupMs,watch.Elapsed.TotalMilliseconds);
            token.ThrowIfCancellationRequested();if(bytes==null)return null;
            watch.Restart();var decoded=RasterTileDecoder.Decode(bytes);token.ThrowIfCancellationRequested();
            if(Diagnostics.Enabled)Diagnostics.MaxDecodeMs=Math.Max(Diagnostics.MaxDecodeMs,watch.Elapsed.TotalMilliseconds);return decoded;
        }
        finally{readerGate.Release();}
    }
    private void CloseReaders()
    {
        foreach(var slot in readers.Values){slot.Reader.Dispose();if(Diagnostics.Enabled)Diagnostics.ConnectionsClosed++;}readers.Clear();unavailable.Clear();
    }
    private Task CloseReadersAsync()
    {
        if(!readerCleanup.IsCompleted)return readerCleanup;
        readerCleanup=Task.Run(()=>{readerGate.Wait();try{if(Interlocked.Exchange(ref resetReaders,0)!=0 || disposed)CloseReaders();}finally{readerGate.Release();}});
        return readerCleanup;
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;activeCancellation?.Cancel();frameCancellation?.Cancel();queue.Clear();pending.Clear();needed.Clear();redraw.Stop();
        await worker;await compositor;await CloseReadersAsync();images.Clear();missing.Clear();frames.Clear();Presentation.Clear();
    }
}
