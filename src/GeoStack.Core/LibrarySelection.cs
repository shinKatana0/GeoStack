// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
namespace GeoStack.Core;

/// <summary>Catalog identities survive sorting, filtering and virtualized page replacement.</summary>
public sealed class LibrarySelection
{
    private readonly HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    private string? anchor;
    public IReadOnlySet<string> Ids => ids;
    public void Clear() { ids.Clear(); anchor=null; }
    public void Set(string id,bool selected) { if(selected)ids.Add(id);else ids.Remove(id); }
    public void Reconcile(IEnumerable<string> alive) => ids.IntersectWith(alive);
    public void All(IEnumerable<string> ordered) { ids.UnionWith(ordered); }
    public void Click(string id,IReadOnlyList<string> ordered,bool control,bool shift)
    {
        var end=ordered.ToList().FindIndex(x=>string.Equals(x,id,StringComparison.OrdinalIgnoreCase));
        var start=anchor==null?-1:ordered.ToList().FindIndex(x=>string.Equals(x,anchor,StringComparison.OrdinalIgnoreCase));
        if(shift && start>=0 && end>=0) { if(!control)ids.Clear();foreach(var item in ordered.Skip(Math.Min(start,end)).Take(Math.Abs(end-start)+1))ids.Add(item); }
        else { if(!control)ids.Clear();if(control && ids.Contains(id))ids.Remove(id);else ids.Add(id);anchor=id; }
    }
    public IReadOnlyList<MediaItem> Resolve(IEnumerable<MediaItem> catalog) => catalog.Where(m=>ids.Contains(m.Path)).ToArray();
}

public enum LibraryGpsFilter { All, WithGps, WithoutGps }
public static class LibraryFilter
{
    public static IEnumerable<MediaItem> Apply(IEnumerable<MediaItem> items,LibraryGpsFilter filter) => items.Where(m=>filter==LibraryGpsFilter.All || m.HasLocation==(filter==LibraryGpsFilter.WithGps));
}
