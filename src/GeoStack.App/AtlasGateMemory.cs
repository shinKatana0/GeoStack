// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace GeoStack.App;

/// <summary>Explicit acceptance diagnostics; no collections are induced and no live UI is read on workers.</summary>
internal static class AtlasGateMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Region { public IntPtr Address,Allocation; public uint AllocationProtection; public ushort Partition,Padding; public nuint Size; public uint State,Protection,Type; }
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(nuint address,out Region information,nuint length);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process,uint flags);
    internal static object Sample(FrameworkElement root,object resources,string phase,int cycle)
    {
        using var process=Process.GetCurrentProcess();process.Refresh();var info=GC.GetGCMemoryInfo();var counts=new Dictionary<string,int>();
        void Visit(DependencyObject node){var name=node.GetType().Name;counts[name]=counts.GetValueOrDefault(name)+1;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Visit(VisualTreeHelper.GetChild(node,i));}
        Visit(root);
        long privateCommit=0,imageCommit=0,mappedCommit=0;var regions=0;
        for(nuint address=0;VirtualQuery(address,out var region,(nuint)Marshal.SizeOf<Region>())!=0 && region.Size>0;)
        {
            if(region.State==0x1000){regions++;if(region.Type==0x20000)privateCommit+=(long)region.Size;else if(region.Type==0x1000000)imageCommit+=(long)region.Size;else if(region.Type==0x40000)mappedCommit+=(long)region.Size;}
            var next=(nuint)region.Address+region.Size;if(next<=address)break;address=next;
        }
        return new{Phase=phase,Cycle=cycle,Tick=Stopwatch.GetTimestamp(),process.PrivateMemorySize64,process.WorkingSet64,ManagedBytes=GC.GetTotalMemory(false),info.HeapSizeBytes,info.FragmentedBytes,info.TotalCommittedBytes,AllocatedBytes=GC.GetTotalAllocatedBytes(false),Collections=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)},Threads=process.Threads.Count,process.HandleCount,GdiObjects=GetGuiResources(process.Handle,0),UserObjects=GetGuiResources(process.Handle,1),VirtualMemory=new{PrivateCommit=privateCommit,ImageCommit=imageCommit,MappedCommit=mappedCommit,CommittedRegions=regions,Scope="VirtualQuery committed region accounting; not allocation call stacks or GPU residency"},VisualObjects=counts,Resources=resources};
    }
}
