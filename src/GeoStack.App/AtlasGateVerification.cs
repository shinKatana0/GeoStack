// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyAtlasGate(string[] arguments)
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45));
        if(arguments.Contains("--atlas-pilot"))for(var i=0;Atlas.AtlasPilot==null;i++){if(i>450)throw new TimeoutException("Pilot initialization");await Task.Delay(100);}
        if(Atlas.AtlasPilot!=null && (Atlas.Tiles.CacheCount!=0 || Atlas.Tiles.PendingCount!=0))throw new InvalidOperationException("Inactive production raster resources retained after pilot activation");
        var capture=arguments.Contains("--gate-capture");var asynchronous=arguments.Contains("--gate-async-capture");var heaps=arguments.Contains("--gate-heaps");
        var source=ViewModel.Items.ToArray();if(source.Length!=9 || source.Any(m=>!m.Path.Contains(Path.Combine(".local","browsing-ux","media"))))throw new InvalidOperationException("Approved fixtures required");
        var hashes=await Task.WhenAll(source.Select(m=>GeographicCopy.Hash(m.Path,default)));var original=App.Settings;
        using var profile=new AtlasProfiling(Dispatcher);using var stop=new CancellationTokenSource();
        var input=new List<(string Phase,double Delay)>();var inputGate=new object();var pending=0;var rows=new List<object>();var cameras=new List<object>();
        var sampler=Task.Run(async()=>{while(!stop.IsCancellationRequested){if(Interlocked.CompareExchange(ref pending,1,0)==0){var stamp=Stopwatch.GetTimestamp();var phase=profile.Phase;profile.Mark("SyntheticInput.Arrival");_ = Dispatcher.BeginInvoke(DispatcherPriority.Input,()=>{lock(inputGate){if(input.Count<16384)input.Add((phase,Stopwatch.GetElapsedTime(stamp).TotalMilliseconds));}profile.Mark("SyntheticInput.Dispatch");Interlocked.Exchange(ref pending,0);});}try{await Task.Delay(10,stop.Token);}catch(OperationCanceledException){break;}}});
        var clock=Stopwatch.StartNew();
        bool Loading()=>Atlas.AtlasPilot is {Enabled:true} pilot?pilot.IsLoading:Atlas.Tiles.IsLoading;
        void Sample(string phase,int cycle)=>rows.Add(AtlasGateMemory.Sample(this,Atlas.ResourceSnapshot(),phase,cycle));
        async Task Settle(string phase)
        {
            profile.Phase=phase;var timer=Stopwatch.StartNew();var camera=false;
            var plannedAfter=Atlas.CompletedRenderSequence;Atlas.InvalidateVisual();long? readyAfter=null;
            for(var i=0;i<600;i++)
            {
                await Task.Delay(10);
                if(!Atlas.IsAnimating && !camera){profile.Record("CameraComplete."+phase,timer.Elapsed.TotalMilliseconds);profile.Mark("Camera.Complete");camera=true;}
                // Loading flags describe the last rendered camera/theme until OnRender plans new work.
                if(Atlas.CompletedRenderSequence<=plannedAfter)continue;
                if(Atlas.IsAnimating || Loading()){readyAfter=null;continue;}
                if(readyAfter==null){readyAfter=Atlas.CompletedRenderSequence;Atlas.InvalidateVisual();continue;}
                if(Atlas.CompletedRenderSequence>readyAfter){profile.Record("Settling."+phase,timer.Elapsed.TotalMilliseconds);return;}
            }
            throw new TimeoutException("Gate settling: "+phase);
        }
        async Task HeapCheckpoint(string name)
        {
            if(!heaps)return;profile.Phase="heap-diagnostic";Sample(name+"-before-dump",0);
            File.WriteAllText(Path.Combine(App.DataDirectory,name+".ready"),Environment.ProcessId.ToString());
            var release=Path.Combine(App.DataDirectory,name+".release");
            for(var i=0;!File.Exists(release);i++){if(i>1200)throw new TimeoutException("Heap diagnostic barrier");await Task.Delay(100);}
            await Task.Delay(500);Sample(name+"-after-dump",0);
        }
        async Task Capture(string name)
        {
            if(!capture)return;profile.Phase="capture-acquisition";profile.Mark("Capture.Begin");var timer=Stopwatch.StartNew();
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);bitmap.Freeze();
            profile.Record("Capture.Acquisition.UI",timer.Elapsed.TotalMilliseconds);profile.Mark("Capture.Acquired");
            profile.Phase=asynchronous?"capture-encoding-background":"capture-encoding-ui";
            void Encode(){using var scope=AtlasProfiling.Measure(asynchronous?"Capture.Encoding.Background":"Capture.Encoding.UI");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(App.DataDirectory,name+".png"));encoder.Save(file);}
            if(asynchronous)await Task.Run(Encode);else Encode();profile.Mark("Capture.Encoded");
        }
        try
        {
            App.Settings=original with{Language="en-US",Theme="Light"};Strings.SetLanguage("en-US");App.ApplyTheme();Localize();ViewModel.RefreshLanguage();
            Sample("startup",0);
            for(var cycle=-1;cycle<=20;cycle++)
            {
                var prefix=cycle<=0?"warmup":"measured";
                foreach(var city in new[]{"Zurich","Warsaw"})
                {
                    var media=source.Single(m=>m.Name==city+".jpg");profile.Phase=prefix+"-navigation";profile.Mark("Navigation.Request");
                    using(AtlasProfiling.Measure("Navigation.Command"))await NavigateToMedia(media);
                    await Settle(prefix+"-navigation");
                    if(Atlas.SelectedMediaId!=media.Path)throw new InvalidOperationException("Exact selection lost");
                    if(cycle>0)cameras.Add(new{Cycle=cycle,City=city,Theme="Light",Atlas.Camera});
                    if(cycle>0)await Capture($"{cycle:D2}-{city}-Light");
                    profile.Phase=prefix+"-ordinary-pan";var before=Atlas.MediaPositionsForVerification();Atlas.SetDragForVerification(true);
                    for(var i=0;i<24;i++){Atlas.PanPixels(new Vector(i<12?-24:24,0));await Task.Delay(16);}
                    Atlas.SetDragForVerification(false);await Settle(prefix+"-pan-settle");
                    var after=Atlas.MediaPositionsForVerification();if(before.Any(p=>(after[p.Key]-p.Value).Length>.01))throw new InvalidOperationException("Marker drift");
                    profile.Phase=prefix+"-rapid-zoom";Atlas.SetDragForVerification(true);
                    for(var i=0;i<12;i++){Atlas.PanPixels(new Vector(i%2==0?32:-32,0));if(i%3==0)Atlas.ZoomBy(i%6==0?.85:1/.85);await Task.Delay(16);}
                    Atlas.SetDragForVerification(false);await Settle(prefix+"-zoom-settle");
                    Atlas.SetComparisonCamera(media.Latitude!.Value,media.Longitude!.Value,15);await Settle(prefix+"-reset-settle");
                    profile.Phase=prefix+"-theme";App.Settings=App.Settings with{Theme="Dark"};App.ApplyTheme();await Settle(prefix+"-dark-settle");
                    if(Atlas.AtlasPilot is {Enabled:true,LastRasterVisible:false})throw new InvalidOperationException("Dark city detail missing");
                    if(cycle==20){cameras.Add(new{Cycle=cycle,City=city,Theme="Dark",Atlas.Camera});await Capture($"{cycle:D2}-{city}-Dark");}
                    profile.Phase=prefix+"-dark-pan";Atlas.SetDragForVerification(true);for(var i=0;i<12;i++){Atlas.PanPixels(new Vector(i<6?-24:24,0));await Task.Delay(16);}Atlas.SetDragForVerification(false);await Settle(prefix+"-dark-pan-settle");
                    profile.Phase=prefix+"-theme";App.Settings=App.Settings with{Theme="Light"};App.ApplyTheme();await Settle(prefix+"-light-settle");
                }
                profile.Phase=prefix+"-resize";var width=Width;Width=width-64;await Task.Delay(40);Width=width;await Settle(prefix+"-resize-settle");
                Sample(cycle<=0?"warmup":"cycle",cycle);
                File.WriteAllText(Path.Combine(App.DataDirectory,"gate-progress.json"),JsonSerializer.Serialize(new{Cycle=cycle,Elapsed=clock.Elapsed.TotalSeconds,Memory=rows[^1]}));
                if(cycle==0)await HeapCheckpoint("warm");
            }
            profile.Phase="idle";for(var i=0;i<6;i++){await Task.Delay(5000);Sample("idle",i);}
            await HeapCheckpoint("final");
            for(var i=0;i<source.Length;i++)if(hashes[i]!=await GeographicCopy.Hash(source[i].Path,default))throw new InvalidOperationException("Source changed");
            Dictionary<string,object> inputs;lock(inputGate)inputs=input.GroupBy(s=>s.Phase).ToDictionary(g=>g.Key,g=>AtlasProfiling.Statistics(g.Select(s=>s.Delay)));
            File.WriteAllText(Path.Combine(App.DataDirectory,"atlas-gate.json"),JsonSerializer.Serialize(new{Status="COMPLETED",Pilot=Atlas.AtlasPilot!=null,Capture=capture,AsynchronousEncoding=asynchronous,HeapDiagnostics=heaps,ElapsedSeconds=clock.Elapsed.TotalSeconds,ClockFrequency=Stopwatch.Frequency,Scope="Worker-posted Input queue probes and programmed camera/pan commands; physical mouse NOT_TESTED. Heap snapshots are separate induced-GC diagnostic sessions.",Inputs=inputs,Stages=profile.Summary(),Timeline=profile.Timeline(),Memory=rows,Cameras=cameras,SourceHashesPreserved=source.Length},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{stop.Cancel();await sampler;App.Settings=original;Strings.SetLanguage(original.Language);App.ApplyTheme();Localize();ViewModel.RefreshLanguage();}
    }
}
