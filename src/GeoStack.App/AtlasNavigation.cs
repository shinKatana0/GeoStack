// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using GeoStack.Core;

namespace GeoStack.App;

public sealed partial class AtlasControl
{
    private readonly LocationHistory<(Point Center,double Zoom,string Map,string? Selected)> history = new();
    public event Action? ViewportChanged;
    public bool CanGoBack => history.Count > 0;
    public string? SelectedMediaId => selectedPath;
    public MapCamera Camera => new(center.X,center.Y,World,ActualWidth,ActualHeight);
    public void Highlight(MediaItem? item) { selectedPath=item?.Path;InvalidateVisual(); }
    public event Action? NavigationStarting;
    private void Remember() { if(history.Push((targetCenter,targetZoom*Math.Max(ActualWidth,850),Tiles.Catalog.Preferred,selectedPath))) NavigationStarting?.Invoke(); }
    public bool Back()
    {
        if(!history.TryBack(out var previous)) return false;
        targetCenter=previous.Center;targetZoom=previous.Zoom/Math.Max(ActualWidth,850);selectedPath=previous.Selected;
        Tiles.Catalog.Preferred=previous.Map;Tiles.Catalog.Save();Tiles.Clear();animation.Start();ViewportChanged?.Invoke();return true;
    }
    public bool NavigateMedia(MediaItem media)
    {
        if(!media.HasLocation) { Highlight(media);return false; }
        Remember();Highlight(media);
        var p=Geo.Project(media.Latitude!.Value,media.Longitude!.Value);
        var nearby=Math.Abs(Geo.WrappedDelta(p.X-center.X))+Math.Abs(p.Y-center.Y)<.0005;
        var map=Tiles.Catalog.Packages.Where(m=>m.Covers(Math.Clamp(15,m.MinZoom,m.MaxZoom),media.Latitude.Value,media.Longitude.Value)).OrderByDescending(m=>m.MaxZoom).ThenByDescending(m=>m.Id==Tiles.Catalog.Preferred).FirstOrDefault();
        Tiles.Catalog.Preferred=map?.Id ?? "world";Tiles.Catalog.Save();Tiles.Clear();
        var z=map==null ? 12 : Math.Clamp(15,map.MinZoom,map.MaxZoom);
        targetCenter=new(p.X,p.Y);targetZoom=nearby && TileZoom>=z && (map==null || TileZoom<=map.MaxZoom) ? targetZoom : 256*Math.Pow(2,z)*1.01/Math.Max(ActualWidth,850);
        animation.Start();ViewportChanged?.Invoke();return true;
    }
    public void NavigateGroup(IReadOnlyList<MediaItem> group)
    {
        if(group.Count==0) return;
        Remember();var selection=group.FirstOrDefault(m=>m.Path==selectedPath) ?? group[0];Highlight(selection);
        if(group.Count==1 || Geo.Bounds(group) is { Width:<.0001,Height:<.0001 })
        {
            var p=Geo.Project(selection.Latitude!.Value,selection.Longitude!.Value);targetCenter=new(p.X,p.Y);
            var map=Tiles.Catalog.Packages.Where(m=>m.Covers(Math.Clamp(15,m.MinZoom,m.MaxZoom),selection.Latitude.Value,selection.Longitude.Value)).OrderByDescending(m=>m.MaxZoom).FirstOrDefault();
            Tiles.Catalog.Preferred=map?.Id ?? "world";targetZoom=256*Math.Pow(2,map==null?12:Math.Clamp(15,map.MinZoom,map.MaxZoom))*1.01/Math.Max(ActualWidth,850);animation.Start();
        }
        else { Tiles.Catalog.Preferred=Tiles.Catalog.Packages.Where(m=>group.All(i=>m.Covers(m.MinZoom,i.Latitude!.Value,i.Longitude!.Value))).OrderByDescending(m=>m.MaxZoom).FirstOrDefault()?.Id ?? "world";FitLocations(group); }
        Tiles.Catalog.Save();Tiles.Clear();ViewportChanged?.Invoke();
    }
}
