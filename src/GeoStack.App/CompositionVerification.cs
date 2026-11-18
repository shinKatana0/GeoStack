// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace GeoStack.App;

public partial class MainWindow
{
    internal void PrepareCompositionVerification(string mode)
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase) || mode is not ("idle" or "no-map" or "no-markers" or "normal" or "no-thumbnails" or "warm" or "resize"))
            throw new InvalidOperationException("Approved isolated composition diagnostic required");
        if(mode=="no-map")Atlas.DiagnosticSuppressScene=true;
        if(mode=="no-markers"){Atlas.Items=[];ViewModel.LibraryChanged+=()=>Atlas.Items=[];}
        ViewModel.DiagnosticSuppressThumbnailDelivery=mode=="no-thumbnails";
        if(Environment.GetCommandLineArgs().Contains("--composition-visible"))Topmost=true;
        if(Environment.GetCommandLineArgs().Contains("--composition-quiet-progress"))
        {
            var control=FindProgress((DependencyObject)Content) ?? throw new InvalidOperationException("Scan progress control not found");control.Style=(Style)FindResource("QuietProgress");
        }
        if(Environment.GetCommandLineArgs().Contains("--composition-animated-progress"))
        {
            var control=FindProgress((DependencyObject)Content) ?? throw new InvalidOperationException("Scan progress control not found");control.Style=null;control.IsIndeterminate=true;
        }
    }

    private static System.Windows.Controls.ProgressBar? FindProgress(DependencyObject root)
    {
        if(root is System.Windows.Controls.ProgressBar progress)return progress;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindProgress(VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}return null;
    }

    internal async Task VerifyComposition(string mode)
    {
        await AtlasReady;await libraryInitialized.Task;
        if(Atlas.AtlasPilot is null)throw new InvalidOperationException("Hybrid atlas must be initialized before composition verification");
        var phase="cold-scene";using var input=new UiResponsivenessProbe(this,()=>phase,Environment.GetCommandLineArgs().Contains("--composition-native-only")?"native-only":"timing");
        using var composition=new WpfCompositionProbe(Dispatcher,()=>phase);
        var phases=new List<object>();var resizeStates=new List<object>();var layouts=0;EventHandler layout=(_,_)=>layouts++;LayoutUpdated+=layout;
        SizeChangedEventHandler sizeChanged=(_,_)=>{if(resizeStates.Count<256)resizeStates.Add(new{Phase=phase,Tick=Stopwatch.GetTimestamp(),Atlas.Camera,WorldBits=BitConverter.DoubleToInt64Bits(Atlas.Camera.World)});};Atlas.SizeChanged+=sizeChanged;
        var renderStart=Atlas.CompletedRenderSequence;var clock=Stopwatch.StartNew();double importMs=0;
        try
        {
            async Task Resize(string label)
            {
                phase=label;var width=Width;var timer=Stopwatch.StartNew();var before=Atlas.CompletedRenderSequence;var beforeLayouts=layouts;
                for(var i=0;i<16;i++){Width=width-(i<8?i:15-i)*12;await Task.Delay(25);}Width=width;
                await Task.Delay(250);phases.Add(new{Phase=label,Milliseconds=timer.Elapsed.TotalMilliseconds,ScenePublications=Atlas.CompletedRenderSequence-before,LayoutPasses=layouts-beforeLayouts});
            }
            if(mode=="resize")await Resize("cold-resize");
            if(mode is not ("idle" or "warm" or "resize"))
            {
                phase="cold-import";var timer=Stopwatch.StartNew();
                var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/library05-fixtures-v2/1000"));
                var import=ViewModel.ImportAsync([folder]);var step=0;
                while(!import.IsCompleted){await Dispatcher.Yield(DispatcherPriority.Input);Atlas.PanPixels(new Vector(step++%2==0?8:-8,0));await Task.Delay(50);}
                await import;importMs=timer.Elapsed.TotalMilliseconds;
                if(ViewModel.Items.Count!=1000)throw new InvalidOperationException("Exactly 1000 approved fixtures required");
            }
            else
            {
                phase=mode=="warm"?"warm-pan":"cold-pan";
                for(var i=0;i<120;i++){await Dispatcher.Yield(DispatcherPriority.Input);Atlas.PanPixels(new Vector(i%2==0?8:-8,0));await Task.Delay(50);}
            }
            phase="settling";await Task.Delay(1500);
            await Resize("warm-resize");
            phase="warm-pan";for(var i=0;i<40;i++){await Dispatcher.Yield(DispatcherPriority.Input);Atlas.PanPixels(new Vector(i%2==0?8:-8,0));await Task.Delay(25);}
            phase="idle";await Task.Delay(750);
            using var process=Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(App.DataDirectory,"composition.json"),JsonSerializer.Serialize(new{
                Status="MEASURED",Mode=mode,PID=Environment.ProcessId,UTC=DateTime.UtcNow,ClockFrequency=Stopwatch.Frequency,
                ActualWidth,ActualHeight,Dpi=VisualTreeHelper.GetDpi(this).DpiScaleX,Camera=Atlas.Camera,
                ImportMilliseconds=importMs,ViewModel.LastScanMeasurements,Items=ViewModel.Items.Count,ViewModel.Previews.MemoryEntries,
                ThumbnailDeliverySuppressed=ViewModel.DiagnosticSuppressThumbnailDelivery,Markers=Atlas.Items.Count,MapVisible=Atlas.IsVisible,SceneSuppressed=Atlas.DiagnosticSuppressScene,
                ScenePublications=Atlas.CompletedRenderSequence-renderStart,LayoutPasses=layouts,Phases=phases,ResizeStates=resizeStates,
                Input=input.Report(),Composition=composition.Report(),ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds,
                process.PrivateMemorySize64,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false),Resources=Atlas.ResourceSnapshot(),
                Visibility=CompositionWindowState.Read(this),Scope="Explicit isolated scene A/B; programmatic panning/resizing, no physical interaction acceptance"
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{LayoutUpdated-=layout;Atlas.SizeChanged-=sizeChanged;Topmost=false;}
    }
}

internal sealed class WpfCompositionProbe : IDisposable
{
    private readonly UiNativeMessageProbe? notifications;
    private readonly object? context;
    private readonly Type? type;
    private readonly List<object> commits=[];
    private readonly EventInfo? committing;
    private readonly EventHandler handler;
    private readonly Dictionary<int,string> names=[];
    private readonly string? error;
    internal WpfCompositionProbe(Dispatcher dispatcher,Func<string> phase)
    {
        handler=(_,_)=>{if(commits.Count<4096)commits.Add(new{Tick=Stopwatch.GetTimestamp(),Phase=phase(),State=State()});};
        try
        {
            var assembly=typeof(Visual).Assembly;type=assembly.GetType("System.Windows.Media.MediaContext");
            context=type?.GetMethod("From",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public,null,[typeof(Dispatcher)],null)?.Invoke(null,[dispatcher]);
            var notice=type?.GetField("_notificationWindow",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(context);
            var wrapper=notice?.GetType().GetField("_hwndNotification",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(notice);
            var handle=(IntPtr?)wrapper?.GetType().GetProperty("Handle",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(wrapper);
            if(handle is {} hwnd)notifications=new(hwnd,phase,State);
            committing=type?.GetEvent("CommittingBatch",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);committing?.GetAddMethod(true)?.Invoke(context,[handler]);
            var events=new[]{assembly,typeof(Dispatcher).Assembly,typeof(Window).Assembly}.Select(a=>a.GetType("MS.Utility.EventTrace+Event")).FirstOrDefault(t=>t!=null);
            if(events!=null)foreach(var value in Enum.GetValues(events))names[Convert.ToInt32(value)]=value.ToString()!;
        }
        catch(Exception ex) when(ex is TargetInvocationException or ArgumentException or MethodAccessException or InvalidOperationException){error=ex.GetType().Name;}
    }
    private object State()
    {
        object? Field(string name)=>type?.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(context);
        return new{Interlock=Field("_interlockState")?.ToString(),Pending=Field("_needToCommitChannel"),Rendering=Field("_isRendering"),PendingAfterRender=Field("_commitPendingAfterRender")};
    }
    public object Report()=>new{Available=context!=null,Error=error,Notification=notifications?.Report(),Commits=commits,EventNames=names,WpfVersion=FileVersionInfo.GetVersionInfo(typeof(Visual).Assembly.Location).ProductVersion};
    public void Dispose(){if(committing!=null && context!=null)committing.GetRemoveMethod(true)?.Invoke(context,[handler]);notifications?.Dispose();}
}
