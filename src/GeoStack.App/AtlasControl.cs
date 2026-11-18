// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public sealed partial class AtlasControl : FrameworkElement
{
    private sealed record Country(Geometry Shape, GeographicName Name, double X, double Y, int Rank);
    private sealed record City(GeographicName Name, string Region, double Latitude, double Longitude, int Rank);
    private readonly List<Country> countries = [];
    private readonly List<City> cities = [];
    private readonly List<(Rect Bounds, MediaCluster Cluster)> hits = [];
    private readonly Dictionary<string, ImageSource?> thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer animation;
    private Point center = new(.5, .46), targetCenter = new(.5, .46);
    private double zoom = 1, targetZoom = 1;
    private bool pilotAnimating;
    private long pilotAnimationStart;
    private Point pilotFrom,pilotTo;
    private double pilotFromZoom,pilotToZoom;
    private Point lastPointer, downPointer;
    private bool dragging;
    private IReadOnlyList<MediaItem> items = [];
    private IReadOnlyList<MediaCluster> clusters = [];
    private double clusteredWorld = -1;
    private string? selectedPath, hoveredPath;
    public bool ShowDiagnostics { get; set; }
    internal bool DiagnosticSuppressScene { get; set; }
    public NativeAtlasPilot? AtlasPilot { get; private set; }
    public async Task EnableAtlasPilotAsync(string folder)
    {
        AtlasPilot=await NativeAtlasPilot.LoadAsync(folder,App.DataDirectory,Tiles.Catalog,compactWorld:true);
        // Startup can fill the production raster cache while vector data loads. That layer is now inactive.
        // Keep its catalog for navigation; cancel pending work and release only its decoded images/frames.
        Tiles.Clear();
    }
    internal void SetComparisonCamera(double latitude,double longitude,int z){animation.Stop();pilotAnimating=false;var p=Geo.Project(latitude,longitude);center=targetCenter=new(p.X,p.Y);zoom=targetZoom=256*Math.Pow(2,z)*1.01/Math.Max(ActualWidth,850);InvalidateVisual();}
    public OfflineTileLayer Tiles { get; } = new(App.DataDirectory);
    internal OfflineTileLayer ActiveTiles => AtlasPilot is {Enabled:true} pilot ? pilot.RasterTiles : Tiles;
    public IReadOnlyList<MediaItem> Items { get => items; set { items = value; clusteredWorld = -1; thumbnails.Clear(); thumbnailGeneration++; InvalidateVisual(); } }
    public PreviewService? Previews { get; set; }
    public event Action<MediaCluster, string>? StackSelected;
    public int CountryCount => countries.Count;
    public int CityCount => cities.Count;
    private MapCamera lastNotifiedCamera;
    private double World => Math.Max(ActualWidth, 850) * zoom;
    internal object ResourceSnapshot()=>new{History=history.Count,Thumbnails=thumbnails.Count,Clusters=clusters.Count,Raster=Tiles.ResourceSnapshot(),Pilot=AtlasPilot?.ResourceSnapshot()};
    public double LastRenderMilliseconds { get; private set; }
    public AtlasControl()
    {
        ClipToBounds = true; Focusable = true;RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        StartupTrace.Mark("AtlasDataLoading"); LoadData();
        StartupTrace.Mark("AtlasDataLoaded");
        // Camera animation must yield to queued input, even when a render takes longer than one tick.
        animation = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => {
            if(AtlasPilot is {Enabled:true,UseOptimizedRendering:true})
            {
                if(!pilotAnimating || pilotTo!=targetCenter || pilotToZoom!=targetZoom){pilotAnimating=true;pilotAnimationStart=Environment.TickCount64;pilotFrom=center;pilotFromZoom=zoom;pilotTo=targetCenter;pilotToZoom=targetZoom;}
                var progress=Math.Clamp((Environment.TickCount64-pilotAnimationStart)/180d,0,1);var eased=1-Math.Pow(1-progress,3);
                zoom=pilotFromZoom+(pilotToZoom-pilotFromZoom)*eased;center=new((pilotFrom.X+Geo.WrappedDelta(pilotTo.X-pilotFrom.X)*eased+1)%1,pilotFrom.Y+(pilotTo.Y-pilotFrom.Y)*eased);
                if(progress>=1){zoom=targetZoom;center=targetCenter;pilotAnimating=false;animation!.Stop();}InvalidateVisual();return;
            }
            zoom += (targetZoom - zoom) * .24;
            center = new((center.X + Geo.WrappedDelta(targetCenter.X - center.X) * .24 + 1) % 1, center.Y + (targetCenter.Y - center.Y) * .24);
            if (Math.Abs(targetZoom - zoom) < .0001 && Math.Abs(Geo.WrappedDelta(targetCenter.X - center.X)) + Math.Abs(targetCenter.Y - center.Y) < .000001) { zoom = targetZoom; center = targetCenter; animation!.Stop(); }
            InvalidateVisual();
        }, Dispatcher); animation.Stop();
    }
    private void LoadData()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "geography/countries.geojson")));
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var geometry = feature.GetProperty("geometry"); var coordinates = geometry.GetProperty("coordinates");
            var shape = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var context = shape.Open())
            {
                void Polygon(JsonElement rings)
                {
                    foreach (var ring in rings.EnumerateArray())
                    {
                        var points = ring.EnumerateArray().Select(c => { var p = Geo.Project(c[1].GetDouble(), c[0].GetDouble()); return new Point(p.X, p.Y); }).ToArray();
                        if (points.Length == 0) continue;
                        context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, false);
                    }
                }
                if (geometry.GetProperty("type").GetString() == "Polygon") Polygon(coordinates);
                else foreach (var polygon in coordinates.EnumerateArray()) Polygon(polygon);
            }
            shape.Freeze(); var props = feature.GetProperty("properties");
            var location = Geo.Project(props.GetProperty("LABEL_Y").GetDouble(), props.GetProperty("LABEL_X").GetDouble());
            countries.Add(new(shape, GeographicName.Read(props, "NAME", "NE_ID"), location.X, location.Y, props.GetProperty("LABELRANK").GetInt32()));
        }
        using var cityDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "geography/cities.geojson")));
        foreach (var feature in cityDoc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = feature.GetProperty("properties");
            cities.Add(new(GeographicName.Read(p, "name", "ne_id"), p.GetProperty("adm1name").GetString() ?? "", p.GetProperty("latitude").GetDouble(), p.GetProperty("longitude").GetDouble(), p.GetProperty("scalerank").GetInt32()));
        }
    }
    private Point Screen(double x, double y) => new(Geo.WrappedDelta(x - center.X) * World + ActualWidth / 2, (y - center.Y) * World + ActualHeight / 2);
    private Point ScreenGeo(double lat, double lon) { var p = Geo.Project(lat, lon); return Screen(p.X, p.Y); }
    internal IReadOnlyDictionary<string, Point> MediaPositionsForVerification() => Items.Where(m => m.HasLocation).ToDictionary(m => m.Path, m => ScreenGeo(m.Latitude!.Value, m.Longitude!.Value));
    public void Fit() { targetZoom = 1; targetCenter = new(.5, .46); animation.Start(); }
    public void FitLocations(IEnumerable<MediaItem>? selection = null)
    {
        var bounds = Geo.Bounds(selection ?? Items); if (bounds == null) { Fit(); return; }
        var b = bounds.Value; targetCenter = new(b.X, b.Y);
        targetZoom = Math.Clamp(Math.Min(Math.Max(100, ActualWidth - 170) / Math.Max(.001, b.Width), Math.Max(100, ActualHeight - 170) / Math.Max(.001, b.Height)) / Math.Max(ActualWidth, 850), .75, 4096);
        animation.Start();
    }
    public bool CanZoomIn => ClampRequestedZoom(targetZoom,1.5,MaximumZoom(targetCenter))>targetZoom+1e-9;
    public bool CanZoomOut => ClampRequestedZoom(targetZoom,1/1.5,MaximumZoom(targetCenter))<targetZoom-1e-9;
    private (bool In,bool Out) lastZoomState;
    public void ZoomBy(double factor) => SetZoom(factor, new(ActualWidth / 2, ActualHeight / 2));
    public int TileZoom => Math.Clamp((int)Math.Floor(Math.Log2(World / 256)), 0, 22);
    internal bool IsAnimating => animation.IsEnabled;
    public (double Latitude, double Longitude) CameraLocation => Geo.Unproject(center.X, center.Y);
    public void ShowMap(MapPackage map, bool overview = false)
    {
        var span = map.East >= map.West ? map.East - map.West : 360 + map.East - map.West;
        var longitude = Geo.WrapLongitude(map.West + span / 2); var latitude = (map.South + map.North) / 2;
        var northEdge = Geo.Project(map.North, longitude); var southEdge = Geo.Project(map.South, longitude);
        var fillPixels = Math.Max(ActualWidth / Math.Max(.000001, span / 360), ActualHeight / Math.Max(.000001, southEdge.Y - northEdge.Y));
        var z = Math.Clamp((int)Math.Ceiling(Math.Log2(fillPixels / 256)), map.MinZoom, map.MaxZoom);
        if (overview || span > 30)
        {
            var north = Geo.Project(map.North, longitude); var south = Geo.Project(map.South, longitude);
            var pixels = Math.Min(Math.Max(100, ActualWidth - 100) / Math.Max(.000001, span / 360), Math.Max(100, ActualHeight - 100) / Math.Max(.000001, south.Y - north.Y));
            z = Math.Clamp((int)Math.Floor(Math.Log2(pixels / 256)), map.MinZoom, map.MaxZoom);
        }
        var p = Geo.Project(latitude, longitude); targetCenter = new(p.X, p.Y);
        targetZoom = 256 * Math.Pow(2, z) * 1.01 / Math.Max(ActualWidth, 850); animation.Start();
    }
    private double MaximumZoom(Point location) { var p=Geo.Unproject(location.X,location.Y);return Math.Max(.75,256*Math.Pow(2,RasterComposition.NativeZoomAt(Tiles.Catalog.Packages,Tiles.Catalog.Preferred,p.Latitude,p.Longitude))*1.01/Math.Max(ActualWidth,850)); }
    internal void SetProbeCamera(double latitude,double longitude,int z) { animation.Stop();pilotAnimating=false;var p=Geo.Project(latitude,longitude);center=targetCenter=new(p.X,p.Y);zoom=targetZoom=Math.Min(256*Math.Pow(2,z)*1.01/Math.Max(ActualWidth,850),MaximumZoom(center));InvalidateVisual(); }
    internal void PanPixels(Vector delta)
    {
        AtlasProfiling.Current?.Mark("Camera.Pan");
        center -= delta / World; center = new((center.X % 1 + 1) % 1, Math.Clamp(center.Y, 0, 1)); targetCenter = center; targetZoom = zoom; animation.Stop();pilotAnimating=false; InvalidateVisual();
    }
    private void SetZoom(double factor, Point anchor)
    {
        var newZoom = ClampRequestedZoom(targetZoom,factor,MaximumZoom(targetCenter));
        if(Math.Abs(newZoom-targetZoom)<1e-12)return;
        var oldWorld = Math.Max(ActualWidth, 850) * targetZoom;
        var newWorld = Math.Max(ActualWidth, 850) * newZoom;
        var delta = anchor - new Point(ActualWidth / 2, ActualHeight / 2);
        targetCenter += delta * (1 / oldWorld - 1 / newWorld); targetZoom = newZoom;
        targetCenter = new((targetCenter.X % 1 + 1) % 1, Math.Clamp(targetCenter.Y, 0, 1)); animation.Start();ViewportChanged?.Invoke();
    }
    public static double ClampRequestedZoom(double current,double factor,double maximum)
    {var candidate=Math.Clamp(current*factor,.75,Math.Max(current,maximum));return factor>=1?Math.Max(current,candidate):Math.Min(current,candidate);}
    public string Describe(double lat, double lon)
    {
        var nearest = cities.MinBy(c => Math.Pow(Geo.WrapLongitude(c.Longitude - lon) * Math.Cos(lat * Math.PI / 180), 2) + Math.Pow(c.Latitude - lat, 2));
        var distance = nearest == null ? double.MaxValue : Math.Sqrt(Math.Pow(Geo.WrapLongitude(nearest.Longitude - lon) * Math.Cos(lat * Math.PI / 180), 2) + Math.Pow(nearest.Latitude - lat, 2)) * 111;
        var prefix = distance < 100 ? nearest!.Name.Display(App.Settings.Language) + " · " : "";
        return prefix + Strings.Get("Area") + $" {lat:F3}°, {lon:F3}°";
    }
    internal long CompletedRenderSequence { get; private set; }
    protected override void OnRender(DrawingContext dc)
    {
        var zoomState=(CanZoomIn,CanZoomOut);
        if (lastNotifiedCamera != Camera || lastZoomState!=zoomState) { lastNotifiedCamera = Camera;lastZoomState=zoomState; ViewportChanged?.Invoke(); }
        if(Tiles.Diagnostics.Enabled)Tiles.Diagnostics.Frames++;
        var renderStarted=System.Diagnostics.Stopwatch.GetTimestamp();
        using var renderProbe=AtlasProfiling.Measure("Atlas.OnRender");
        base.OnRender(dc); if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if(DiagnosticSuppressScene)
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["Background"],null,new Rect(0,0,ActualWidth,ActualHeight));
            hits.Clear();LastRenderMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds;CompletedRenderSequence++;return;
        }
        var dark = ((SolidColorBrush)Application.Current.Resources["Background"]).Color.R < 100;
        var text = Brush(dark ? "#9BB8C4" : "#47666F");
        if(AtlasPilot is {Enabled:true} pilot)
        {
            pilot.Draw(dc,World,ActualWidth,ActualHeight,center,dark,dragging || animation.IsEnabled,InvalidateVisual);
            Text(dc,"© OpenStreetMap contributors · Natural Earth · Protomaps · openstreetmap.org/copyright",new(8,ActualHeight-17),Brush(dark?"#C8D9DB":"#354851"),10);
        }
        else
        {
        var ocean = Brush(dark ? "#152C39" : "#E7EFF2"); var land = Brush(dark ? "#29444D" : "#F9FBFA"); var border = Brush(dark ? "#3C5B65" : "#C1D0D3");
        dc.DrawRectangle(ocean, null, new(0, 0, ActualWidth, ActualHeight));
        var visible = new Rect(center.X - ActualWidth / World / 2, center.Y - ActualHeight / World / 2, ActualWidth / World, ActualHeight / World);
        for (var wrap = -1; wrap <= 1; wrap++)
        {
            dc.PushTransform(new MatrixTransform(World, 0, 0, World, ActualWidth / 2 + (wrap - center.X) * World, ActualHeight / 2 - center.Y * World));
            var area = new Rect(visible.X - wrap, visible.Y, visible.Width, visible.Height);
            foreach (var country in countries) if (country.Shape.Bounds.IntersectsWith(area)) dc.DrawGeometry(land, new Pen(border, .8 / World), country.Shape);
            dc.Pop();
        }
        if (ShowDiagnostics)
        {
        var grid = zoom < 5 ? 30d : zoom < 30 ? 10 : zoom < 200 ? 1 : zoom < 1500 ? .1 : .01;
        var topLeft = Geo.Unproject(visible.Left, visible.Top); var bottomRight = Geo.Unproject(visible.Right, visible.Bottom);
        var gridPen = new Pen(Brush(dark ? "#243E4B" : "#D5E2E6"), .6);
        for (var lon = Math.Ceiling(topLeft.Longitude / grid) * grid; lon <= bottomRight.Longitude; lon += grid)
        { var x = ScreenGeo(0, lon).X; dc.DrawLine(gridPen, new(x, 0), new(x, ActualHeight)); if (zoom > 100) Text(dc, lon.ToString("F2", CultureInfo.InvariantCulture) + "°", new(x + 5, ActualHeight - 34), text, 10); }
        for (var lat = Math.Ceiling(bottomRight.Latitude / grid) * grid; lat <= Math.Min(85, topLeft.Latitude); lat += grid)
        { var y = ScreenGeo(lat, 0).Y; dc.DrawLine(gridPen, new(0, y), new(ActualWidth, y)); if (zoom > 100) Text(dc, lat.ToString("F2", CultureInfo.InvariantCulture) + "°", new(8, y + 3), text, 10); }
        }
        var occupied = new List<Rect>();
        foreach (var c in countries.Where(c => zoom < 4 && (c.Rank <= 3 || zoom > 1.8)))
        {
            var p = Screen(c.X, c.Y); var name = c.Name.Display(App.Settings.Language);
            Label(dc, name.ToUpperInvariant(), p, text, 11, occupied);
        }
        if (zoom >= 1.4)
        foreach (var city in cities.Where(c => c.Rank <= (zoom < 3 ? 1 : zoom < 8 ? 3 : zoom < 24 ? 5 : 10)).OrderBy(c => c.Rank))
        {
            var p = ScreenGeo(city.Latitude, city.Longitude);
            if (p.X < 0 || p.X > ActualWidth || p.Y < 0 || p.Y > ActualHeight) continue;
            dc.DrawEllipse(text, null, p, 2, 2); Label(dc, city.Name.Display(App.Settings.Language), p + new Vector(4, -16), text, 11, occupied);
        }
        Tiles.Draw(dc, visible, World, ActualWidth, ActualHeight, center, InvalidateVisual,dragging || animation.IsEnabled);
        if (Tiles.Attributions.Count > 0)
        {
            var attribution = string.Join(" · ", Tiles.Attributions.Where(a => !string.IsNullOrWhiteSpace(a)));
            dc.DrawRectangle(ocean, null, new(0, ActualHeight - 18, ActualWidth, 18));
            Text(dc, attribution.Length > 200 ? attribution[..200] + "…" : attribution, new(8, ActualHeight - 17), text, 10);
        }
        }
        hits.Clear();
        var bucketWorld = Math.Pow(2, Math.Round(Math.Log2(World) * 4) / 4);
        if (clusteredWorld != bucketWorld) { clusters = Geo.Cluster(Items, bucketWorld); clusteredWorld = bucketWorld; }
        foreach (var cluster in clusters)
        {
            var p = ScreenGeo(cluster.Latitude, cluster.Longitude); if (p.X < -60 || p.Y < -60 || p.X > ActualWidth + 60 || p.Y > ActualHeight + 60) continue;
            var rect = new Rect(p.X - 27, p.Y - 28, 54, 54);
            var active = cluster.Items.Any(m => m.Path == selectedPath); var hover = cluster.Items[0].Path == hoveredPath;
            if (active || hover) dc.DrawRoundedRectangle(null, new Pen(Brush(active ? "#20CDBA" : "#6BCFC4"), active ? 4 : 2), new(rect.X - 5, rect.Y - 5, 64, 64), 10, 10);
            var first = cluster.Items.FirstOrDefault(m => m.Path == selectedPath) ?? cluster.Items[0];
            var cards = cluster.Items.Where(m => m.Path != first.Path).Take(2).Reverse().ToArray();
            for (var i = 0; i < cards.Length; i++) DrawPhotoCard(dc, cards[i], new Rect(rect.X + (cards.Length-i)*5, rect.Y - (cards.Length-i)*5,54,54));
            DrawPhotoCard(dc, first, rect);
            var count = cluster.Items.Count.ToString(CultureInfo.InvariantCulture);
            var badge = new Rect(rect.Right - 15, rect.Bottom - 14, Math.Max(25, count.Length * 8 + 12), 23);
            dc.DrawRoundedRectangle(Brush("#087F78"), new Pen(Brush("#FFFFFF"), 2), badge, 11, 11); Text(dc, count, badge.TopLeft + new Vector(7, 3), Brushes.White, 11);
            hits.Add((new Rect(rect.X - 5, rect.Y - 10, 72, 80), cluster));
        }
        var km = 40075 * Math.Cos(Geo.Unproject(center.X, center.Y).Latitude * Math.PI / 180) / World * 100;
        Text(dc, km >= 1 ? $"≈ {km:N0} {Strings.Get("Kilometers")}" : $"≈ {km * 1000:N0} {Strings.Get("Meters")}", new(20, ActualHeight - 28), text, 11);
        dc.DrawLine(new Pen(text, 2), new(20, ActualHeight - 34), new(120, ActualHeight - 34));
        LastRenderMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds;
        CompletedRenderSequence++;
        if(Tiles.Diagnostics.Enabled)Tiles.Diagnostics.MaxRenderMs=Math.Max(Tiles.Diagnostics.MaxRenderMs,LastRenderMilliseconds);
    }
    private void DrawPhotoCard(DrawingContext dc, MediaItem media, Rect rect)
    {
        dc.DrawRoundedRectangle(Brush("#25000000"), null, new(rect.X+2,rect.Y+3,rect.Width+2,rect.Height+2),7,7);
        dc.DrawRoundedRectangle(Brush("#FFFFFF"), new Pen(Brush("#FFFFFF"),3),rect,7,7);
        if(thumbnails.TryGetValue(media.Path,out var thumbnail) && thumbnail!=null)
        {
            dc.PushClip(new RectangleGeometry(rect,5,5));var aspect=thumbnail.Width/thumbnail.Height;
            var imageRect=aspect>1?new Rect(rect.X-(rect.Width*aspect-rect.Width)/2,rect.Y,rect.Width*aspect,rect.Height):new Rect(rect.X,rect.Y-(rect.Height/aspect-rect.Height)/2,rect.Width,rect.Height/aspect);
            dc.DrawImage(thumbnail,imageRect);dc.Pop();
        }
        else
        {
            dc.DrawRoundedRectangle(Brush("#187E77"),null,rect,5,5);Text(dc,media.Kind=="Video"?"\u25B6":"\u25C7",rect.TopLeft+new Vector(17,12),Brushes.White,23);
            if(!thumbnails.ContainsKey(media.Path) && Previews!=null) { thumbnails[media.Path]=null;_=LoadThumbnail(media.Path); }
        }
    }
    internal IReadOnlyList<Rect> VisibleMarkerBounds => hits.Select(h=>h.Bounds).ToArray();
    internal int LoadedMarkerThumbnails => thumbnails.Values.Count(i=>i!=null);
    private int thumbnailGeneration;
    private bool markerRefreshPending;
    private async Task LoadThumbnail(string path)
    {
        var generation=thumbnailGeneration;var previews=Previews!;
        // File fingerprints and preview lookup stay off the input/render thread.
        var image=await Task.Run(()=>previews.GetAsync(path,true)).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(()=>{
            if(generation!=thumbnailGeneration)return;
            if(thumbnails.Count>192)thumbnails.Clear();thumbnails[path]=image;
            if(markerRefreshPending)return;markerRefreshPending=true;
            Dispatcher.BeginInvoke(()=>{markerRefreshPending=false;InvalidateVisual();},DispatcherPriority.Background);
        },DispatcherPriority.Background);
    }
    private static SolidColorBrush Brush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    private void Text(DrawingContext dc, string text, Point point, Brush brush, double size) {
        var formatted=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth=Math.Max(1,ActualWidth-point.X-8),MaxTextHeight=size*1.6,Trimming=TextTrimming.CharacterEllipsis };dc.DrawText(formatted,point);
    }
    private void Label(DrawingContext dc, string text, Point p, Brush brush, double size, List<Rect> occupied)
    {
        var rect = new Rect(p.X, p.Y, text.Length * size * .57, size + 5);
        if (rect.Right < 0 || rect.X > ActualWidth || rect.Bottom < 0 || rect.Y > ActualHeight || occupied.Any(x => x.IntersectsWith(rect))) return;
        occupied.Add(rect); Text(dc, text, p, brush, size);
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { SetZoom(e.Delta > 0 ? 1.4 : 1 / 1.4, e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)
        {
            var hit = hits.LastOrDefault(h => h.Bounds.Contains(e.GetPosition(this)));
            if (hit.Cluster?.Items.Count > 1) { Remember(); FitLocations(hit.Cluster.Items); } else SetZoom(2, e.GetPosition(this));
            e.Handled = true; return;
        }
        dragging = true; lastPointer = downPointer = e.GetPosition(this); CaptureMouse(); Cursor = Cursors.SizeAll;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) { var hit = hits.LastOrDefault(h => h.Bounds.Contains(e.GetPosition(this))); var path = hit.Cluster?.Items[0].Path; Cursor = path != null ? Cursors.Hand : Cursors.Arrow; if (path != hoveredPath) { hoveredPath = path; InvalidateVisual(); } return; }
        var current = e.GetPosition(this); var delta = current - lastPointer; PanPixels(delta); lastPointer = current;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        dragging = false; ReleaseMouseCapture(); Cursor = Cursors.Arrow;InvalidateVisual();
        if ((e.GetPosition(this) - downPointer).Length < 5)
        {
            var hit = hits.LastOrDefault(h => h.Bounds.Contains(e.GetPosition(this)));
            if (hit.Cluster != null) { selectedPath = hit.Cluster.Items.FirstOrDefault(m => m.Path == selectedPath)?.Path ?? hit.Cluster.Items[0].Path; InvalidateVisual(); StackSelected?.Invoke(hit.Cluster, Describe(hit.Cluster.Latitude, hit.Cluster.Longitude)); }
        }
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { var ratio=Math.Max(sizeInfo.PreviousSize.Width,850)/Math.Max(sizeInfo.NewSize.Width,850);zoom*=ratio;targetZoom*=ratio;base.OnRenderSizeChanged(sizeInfo);InvalidateVisual(); }
    internal void SetDragForVerification(bool active){dragging=active;InvalidateVisual();}
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = false; InvalidateVisual();base.OnLostMouseCapture(e); }
}
