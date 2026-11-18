// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using GeoStack.Core;

namespace GeoStack.App;

public sealed class TileDiagnostics
{
    public bool Enabled { get; set; }
    private readonly object gate=new();
    private readonly Queue<object> events=new();
    private readonly Dictionary<string,int> requests=[];
    public long Requests,Missing,DuplicateRequests,Completions,Redraws,Obsolete,Evictions,CacheHits,CacheMisses,Cancelled;
    public long Frames,CoverageChecks;
    public object? Viewport { get; set; }
    public int MaxPending,MaxActive,MaxCache,MaxMissingCache,MaxOpenReaders,ConnectionsOpened,ConnectionsClosed;
    public double MaxLookupMs,MaxDecodeMs,MaxRenderMs,MaxTileDrawMs;
    public void Request(string map,int z,int x,int y,int generation,int pending,int active)
    {
        if(!Enabled)return;lock(gate) {
            Requests++;var key=$"{map}:{z}:{x}:{y}";if(requests.TryGetValue(key,out var n)){DuplicateRequests++;requests[key]=n+1;}else if(requests.Count<4096)requests[key]=1;
            MaxPending=Math.Max(MaxPending,pending);MaxActive=Math.Max(MaxActive,active);
            if(events.Count>=256)events.Dequeue();events.Enqueue(new { Map=map,Z=z,X=x,Y=y,Generation=generation,Pending=pending,Active=active });
        }
    }
    public object Snapshot() { lock(gate)return new { Enabled,Frames,CoverageChecks,Viewport,Requests,Missing,DuplicateRequests,Completions,Redraws,Obsolete,Evictions,CacheHits,CacheMisses,Cancelled,MaxPending,MaxActive,MaxCache,MaxMissingCache,MaxOpenReaders,ConnectionsOpened,ConnectionsClosed,MaxLookupMs,MaxDecodeMs,MaxRenderMs,MaxTileDrawMs,RecentRequests=events.ToArray() }; }
}
