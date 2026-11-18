// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
namespace GeoStack.Core;

/// <summary>Explicit recency ordering; dictionary slot reuse cannot evict newly admitted entries repeatedly.</summary>
public sealed class BoundedLru<TKey,TValue>(int capacity) where TKey:notnull
{
    private readonly Dictionary<TKey,LinkedListNode<(TKey Key,TValue Value)>> entries=[];
    private readonly LinkedList<(TKey Key,TValue Value)> order=[];
    public int Count=>entries.Count;
    public bool Contains(TKey key)=>entries.ContainsKey(key);
    public bool TryGet(TKey key,out TValue value) { if(!entries.TryGetValue(key,out var node)){value=default!;return false;}order.Remove(node);order.AddLast(node);value=node.Value.Value;return true; }
    public TValue? Set(TKey key,TValue value)
    {
        if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));
        Remove(key);TValue? evicted=default;
        if(entries.Count>=capacity) { var first=order.First!;evicted=first.Value.Value;entries.Remove(first.Value.Key);order.RemoveFirst(); }
        entries[key]=order.AddLast((key,value));return evicted;
    }
    public void Remove(TKey key){if(entries.Remove(key,out var node))order.Remove(node);}
    public IEnumerable<TValue> Values=>order.Select(n=>n.Value);
    public void Clear(){entries.Clear();order.Clear();}
}

public sealed class RasterTileReader : IDisposable
{
    private readonly ReadOnlySqlite db;
    private readonly MapPackage package;
    public RasterTileReader(MapPackage package){this.package=package;db=new(package.Path);}
    public byte[]? Read(int zoom,int x,int y,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!package.IntersectsTileBounds(zoom,x,y))return null;
        return db.Blob(FormattableString.Invariant($"SELECT tile_data FROM tiles WHERE zoom_level={zoom} AND tile_column={x} AND tile_row={MapPackage.TmsRow(zoom,y)} LIMIT 1"),token);
    }
    public void Dispose()=>db.Dispose();
}
