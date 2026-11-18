// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace GeoStack.Core;

public sealed record BrowsingPlace(MediaItem Media, double X, double Y, GeographicName? Country, GeographicName? Locality);
public sealed record OffscreenGroup(string Id, GeographicName? Name, IReadOnlyList<MediaItem> Items, double X, double Y);
public sealed record EdgePlacement(OffscreenGroup Group, double X, double Y, string Arrow);
public readonly record struct MapCamera(double X, double Y, double World, double Width, double Height)
{
    public (double X, double Y) Screen(double x, double y) => (Geo.WrappedDelta(x - X) * World + Width / 2, (y - Y) * World + Height / 2);
    public bool Contains(double x, double y) { var p = Screen(x,y); return p.X >= 0 && p.X <= Width && p.Y >= 0 && p.Y <= Height; }
}

/// <summary>Immutable projected locations and boundary-derived names; no database work during camera updates.</summary>
public sealed class OffscreenIndex
{
    public IReadOnlyList<BrowsingPlace> Places { get; }
    public OffscreenIndex(IEnumerable<MediaItem> items, Func<double?,double?,GeographicLocation> resolve,CancellationToken token=default)
    {
        var names = new Dictionary<(double,double), GeographicLocation>();
        Places = items.Where(m => m.HasLocation).DistinctBy(m => m.Path, StringComparer.OrdinalIgnoreCase).Select(m => {
            token.ThrowIfCancellationRequested();
            var key = (m.Latitude!.Value,m.Longitude!.Value);
            if (!names.TryGetValue(key,out var location)) names[key] = location = resolve(key.Item1,key.Item2);
            var p = Geo.Project(key.Item1,key.Item2);
            return new BrowsingPlace(m,p.X,p.Y,location.Country,location.City ?? location.Region);
        }).ToArray();
    }
    public IReadOnlyList<OffscreenGroup> Outside(MapCamera camera)
    {
        var outside = Places.Where(p => !camera.Contains(p.X,p.Y)).ToArray();
        var result = new List<OffscreenGroup>();
        foreach (var country in outside.GroupBy(p => p.Country?.Id ?? "unknown",StringComparer.Ordinal))
        {
            // Nearby destinations split by known locality, distant countries aggregate without double counts.
            var nearby = country.Any(p => Math.Abs(Geo.WrappedDelta(p.X-camera.X)) + Math.Abs(p.Y-camera.Y) < .025);
            var groups = nearby ? country.GroupBy(p => p.Locality?.Id ?? FormattableString.Invariant($"cell:{Math.Floor(p.X*1800)}:{Math.Floor(p.Y*1800)}"))
                : country.GroupBy(_ => country.Key);
            foreach (var group in groups)
            {
                var media = group.Select(p => p.Media).ToArray(); var b = Geo.Bounds(media)!.Value;
                result.Add(new(country.Key+"/"+group.Key,nearby ? group.First().Locality ?? group.First().Country : group.First().Country,media,b.X,b.Y));
            }
        }
        return result.OrderByDescending(g => g.Items.Count).ThenBy(g => g.Id,StringComparer.Ordinal).ToArray();
    }
    public static IReadOnlyList<EdgePlacement> Place(IReadOnlyList<OffscreenGroup> groups, MapCamera camera, int limit = 4, IReadOnlyList<(double X,double Y,double Width,double Height)>? reserved = null)
    {
        var result = new List<EdgePlacement>();
        if (camera.Width < 230 || camera.Height < 230) return result;
        const double width=148,height=32;
        var left=8d; var right=Math.Max(left,camera.Width-width-70); var top=108d; var bottom=Math.Max(top,camera.Height-100-height);
        foreach (var group in groups.Take(limit))
        {
            var dx=Geo.WrappedDelta(group.X-camera.X); var dy=group.Y-camera.Y;
            var arrow=Math.Abs(dx)>Math.Abs(dy) ? dx<0 ? "\u2190" : "\u2192" : dy<0 ? "\u2191" : "\u2193";
            var targetX=dx<0?left:right; var targetY=Math.Abs(dx)>Math.Abs(dy)?Math.Clamp(camera.Height/2+dy/Math.Max(Math.Abs(dx),1e-9)*camera.Width/2,top,bottom):dy<0?top:bottom;
            if (Math.Abs(dy)>=Math.Abs(dx)) targetX=Math.Clamp(camera.Width/2+dx/Math.Max(Math.Abs(dy),1e-9)*camera.Height/2-width/2,left,right);
            var candidates=new List<(double X,double Y)> { (targetX,targetY) };
            for(var y=top;y<=bottom;y+=height+6) { candidates.Add((left,y)); candidates.Add((right,y)); }
            var slot=candidates.OrderBy(p=>Math.Pow(p.X-targetX,2)+Math.Pow(p.Y-targetY,2)).FirstOrDefault(p=>result.All(r=>Math.Abs(r.X-p.X)>=width+4 || Math.Abs(r.Y-p.Y)>=height+4) && (reserved==null || reserved.All(r=>p.X+width+4<r.X || p.X>r.X+r.Width+4 || p.Y+height+4<r.Y || p.Y>r.Y+r.Height+4)), (X:double.NaN,Y:double.NaN));
            if (double.IsNaN(slot.X)) continue;
            result.Add(new(group,slot.X,slot.Y,arrow));
        }
        return result;
    }
}

public sealed class LocationHistory<T>
{
    private readonly List<T> entries=[];
    public int Count => entries.Count;
    public bool Push(T entry) { if(entries.Count>0 && EqualityComparer<T>.Default.Equals(entries[^1],entry)) return false; entries.Add(entry); if(entries.Count>20) entries.RemoveAt(0); return true; }
    public bool TryBack(out T value) { if(entries.Count==0) { value=default!;return false; } value=entries[^1];entries.RemoveAt(entries.Count-1);return true; }
}

