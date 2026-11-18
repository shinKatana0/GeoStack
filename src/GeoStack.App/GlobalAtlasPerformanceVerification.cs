// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyGlobalPerformance(string package,string[] args)
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Synthetic isolated profile required");
        await AtlasReady;await libraryInitialized.Task;await placesRefresh;Width=1360;Height=880;
        var pilot=Atlas.AtlasPilot!;var install=Stopwatch.StartNew();await pilot.InstallGlobalAtlas(package);var installation=install.Elapsed.TotalMilliseconds;
        pilot.GlobalAtlas!.Enabled=!args.Contains("--global-off");
        var original=Atlas.Items;var previews=Atlas.Previews;
        if(args.Contains("--global-no-media")){Atlas.Items=[];Atlas.Previews=null;}
        var phase="setup";using var profiler=new AtlasProfiling(Dispatcher);
        using var probe=new UiResponsivenessProbe(this,()=>phase,args.Contains("--global-trace")?"detailed":"native-only");
        using var composition=new WpfCompositionProbe(Dispatcher,()=>phase);using var process=Process.GetCurrentProcess();
        var memoryBefore=AtlasGateMemory.Sample(this,pilot.ResourceSnapshot(),"warmup",0);var results=new List<object>();var poses=new[]{("Tokyo",35.6762,139.6503),("Warsaw",52.2297,21.0122),("Rural",46.3,2.2),("Australia",-30.5,148.5)};
        async Task<double> Settle()
        {
            var timer=Stopwatch.StartNew();for(var i=0;i<600;i++)
            {
                Atlas.InvalidateVisual();await Task.Delay(20);var c=Atlas.Camera;
                if(Atlas.IsAnimating || Atlas.ActiveTiles.IsLoading)continue;
                if(pilot.GlobalAtlas.Enabled && !pilot.GlobalAtlas.CanCover(c.World,c.Width,c.Height,new(c.X,c.Y)))continue;
                return timer.Elapsed.TotalMilliseconds;
            }
            throw new TimeoutException("Global performance viewport did not settle");
        }
        var rounds=args.Contains("--global-short")?1:2;
        foreach(var theme in args.Contains("--global-short")?new[]{"Light"}:new[]{"Light","Dark"})
        {
            ApplySettings("en-US",theme);
            for(var round=0;round<rounds;round++)foreach(var pose in poses)foreach(var z in new[]{6,7,8,9,10,12})
            {
                phase=$"{theme}/{round}/{pose.Item1}/{z}/prepare";profiler.Phase=phase;
                var before=GC.GetTotalAllocatedBytes(false);Atlas.SetComparisonCamera(pose.Item2,pose.Item3,z);var ready=await Settle();
                phase=$"{theme}/{round}/{pose.Item1}/{z}/pan";profiler.Phase=phase;Atlas.SetDragForVerification(true);
                for(var i=0;i<24;i++){Atlas.PanPixels(new Vector(i<12?-12:12,i%2==0?3:-3));await Task.Delay(16);}
                Atlas.SetDragForVerification(false);var settled=await Settle();process.Refresh();
                results.Add(new{Phase=phase,Atlas.Camera,ReadyMilliseconds=ready,SettlingMilliseconds=settled,AllocatedBytes=GC.GetTotalAllocatedBytes(false)-before,process.PrivateMemorySize64,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false),Resources=pilot.ResourceSnapshot()});
                if(args.Contains("--global-captures"))FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,$"{theme}-{round}-{pose.Item1}-{z}.png"));
            }
        }
        phase="idle";await Task.Delay(1000);process.Refresh();
        File.WriteAllText(Path.Combine(App.DataDirectory,"global-performance.json"),JsonSerializer.Serialize(new{Status="COMPLETED",PID=Environment.ProcessId,InstallationMilliseconds=installation,GlobalEnabled=pilot.GlobalAtlas.Enabled,MediaOverlays=!args.Contains("--global-no-media"),CaptureEnabled=args.Contains("--global-captures"),Input=probe.Report(),Composition=composition.Report(),Stages=profiler.Summary(),Timeline=profiler.Timeline(),Results=results,process.PrivateMemorySize64,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false),Gc=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)},MemoryBefore=memoryBefore,MemoryAfter=AtlasGateMemory.Sample(this,pilot.ResourceSnapshot(),"settled",rounds),FinalResources=pilot.ResourceSnapshot()},new JsonSerializerOptions{WriteIndented=true}));
        Atlas.Items=original;Atlas.Previews=previews;
    }
}
