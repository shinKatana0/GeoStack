// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GeoStack.App;

// Explicit synthetic verification only. No paths, coordinates or media identities are recorded.
internal sealed class UiResponsivenessProbe : IDisposable
{
    private const int ProbeMessage=0x8421;
    private sealed class Ticket(long id,long requested,string phase)
    {
        public long Id=id,Requested=requested,Returned,Handled,Frame;
        public double QueueMilliseconds;
        public string Phase=phase;
    }
    private readonly Window window;
    private readonly Func<string> phase;
    private readonly string mode;
    private readonly bool trackFrame;
    private readonly HwndSource source;
    private readonly IntPtr handle;
    private readonly Thread clock;
    private readonly UiNativeMessageProbe? nativeTiming;
    private readonly ManualResetEvent stop=new(false);
    private readonly ConcurrentQueue<Ticket> native=[];
    private readonly ConcurrentQueue<Ticket> queued=[];
    private readonly ConcurrentQueue<object> operations=[];
    private readonly ConcurrentQueue<object> resources=[];
    private readonly Dictionary<DispatcherOperation,(long Tick,string Label,string Phase)> running=[];
    private readonly Border marker=new(){Width=5,Height=5,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false,Margin=new(4)};
    private readonly FieldInfo? method=typeof(DispatcherOperation).GetField("_method",BindingFlags.Instance|BindingFlags.NonPublic);
    private Ticket? pendingNative,pendingDispatcher;
    private readonly Queue<Ticket> awaitingFrame=[];
    private bool markerState;
    private long sequence;
    private int stackRequests;
    public UiResponsivenessProbe(Window window,Func<string> phase,string mode)
    {
        this.window=window;this.phase=phase;this.mode=mode;trackFrame=mode!="native-only";handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle)!;source.AddHook(Hook);
        if(trackFrame && window.Content is Panel panel)panel.Children.Add(marker);
        if(trackFrame)CompositionTarget.Rendering+=Rendered;
        if(mode!="minimal"){window.Dispatcher.Hooks.OperationStarted+=Started;window.Dispatcher.Hooks.OperationCompleted+=Completed;}
        if(mode=="detailed")nativeTiming=new(handle,phase);
        clock=new Thread(Pulse){IsBackground=true,Name="GeoStack responsiveness clock"};clock.Start();
    }
    private static double Ms(long start,long end)=>end==0?0:Stopwatch.GetElapsedTime(start,end).TotalMilliseconds;
    internal static uint MessageAge(uint now,uint enqueued)=>unchecked(now-enqueued);
    private void Pulse()
    {
        using var process=Process.GetCurrentProcess();var lastResource=Stopwatch.GetTimestamp();
        while(!stop.WaitOne(40))
        {
            if(mode!="minimal" && resources.Count<512 && Ms(lastResource,Stopwatch.GetTimestamp())>=1000)
            {
                process.Refresh();lastResource=Stopwatch.GetTimestamp();
                resources.Enqueue(new{Phase=phase(),Tick=lastResource,process.PrivateMemorySize64,process.WorkingSet64,ManagedHeap=GC.GetTotalMemory(false),Handles=process.HandleCount,Threads=process.Threads.Count,ThreadPoolThreads=ThreadPool.ThreadCount,PendingThreadPool=ThreadPool.PendingWorkItemCount,Gc=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)}});
            }
            if(native.Count>=12000)continue;
            var current=Volatile.Read(ref pendingNative);
            if(current==null)
            {
                var ticket=new Ticket(Interlocked.Increment(ref sequence),Stopwatch.GetTimestamp(),phase());Volatile.Write(ref pendingNative,ticket);
                if(PostMessage(handle,ProbeMessage,new IntPtr(ticket.Id),IntPtr.Zero)){ticket.Returned=Stopwatch.GetTimestamp();native.Enqueue(ticket);}else Volatile.Write(ref pendingNative,null);
            }
            else if(mode=="detailed" && Ms(current.Requested,Stopwatch.GetTimestamp())>150 && Interlocked.CompareExchange(ref stackRequests,1,0)==0)
                CaptureStack();
            var waiting=Volatile.Read(ref pendingDispatcher);
            if(mode=="detailed" && waiting!=null && Ms(waiting.Requested,Stopwatch.GetTimestamp())>150 && Interlocked.CompareExchange(ref stackRequests,1,0)==0)CaptureStack();
            if(mode=="minimal" || queued.Count>=12000 || Volatile.Read(ref pendingDispatcher)!=null)continue;
            var sample=new Ticket(Interlocked.Increment(ref sequence),Stopwatch.GetTimestamp(),phase());Volatile.Write(ref pendingDispatcher,sample);
            window.Dispatcher.BeginInvoke(()=>{sample.Handled=Stopwatch.GetTimestamp();Volatile.Write(ref pendingDispatcher,null);},DispatcherPriority.Input);
            sample.Returned=Stopwatch.GetTimestamp();queued.Enqueue(sample);
        }
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if(message!=ProbeMessage)return IntPtr.Zero;
        var ticket=Volatile.Read(ref pendingNative);
        if(ticket!=null && ticket.Id==w.ToInt64())
        {
            ticket.Handled=Stopwatch.GetTimestamp();
            // The clock thread can be descheduled before/during PostMessage. MSG.time records OS enqueue time.
            ticket.QueueMilliseconds=MessageAge(unchecked((uint)Environment.TickCount),GetMessageTime());
            if(trackFrame){awaitingFrame.Enqueue(ticket);markerState=!markerState;marker.Background=markerState?Brushes.Teal:Brushes.Orange;}
            Volatile.Write(ref pendingNative,null);
        }
        handled=true;return IntPtr.Zero;
    }
    private void Rendered(object? sender,EventArgs args)
    {
        var tick=Stopwatch.GetTimestamp();while(awaitingFrame.TryDequeue(out var ticket))ticket.Frame=tick;
    }
    private void Started(object? sender,DispatcherHookEventArgs e)
    {
        if(running.Count>4096)return;var label=(method?.GetValue(e.Operation) as Delegate)?.Method;
        running[e.Operation]=(Stopwatch.GetTimestamp(),label==null?"unknown":label.DeclaringType?.FullName+"."+label.Name,phase());
    }
    private void Completed(object? sender,DispatcherHookEventArgs e)
    {
        if(!running.Remove(e.Operation,out var active))return;var ms=Ms(active.Tick,Stopwatch.GetTimestamp());
        if(ms>=8 && operations.Count<512)operations.Enqueue(new{active.Phase,active.Label,Priority=e.Operation.Priority.ToString(),Milliseconds=ms,Start=active.Tick});
    }
    private void CaptureStack()
    {
        var exe=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/diagnostic-tools/dotnet-stack.exe"));
        if(!File.Exists(exe))return;
        var folder=App.DataDirectory;
        // Separate diagnostic thread; attach overhead is explicitly excluded from minimal/timing runs.
        new Thread(()=>{try{using var process=new Process{StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};process.StartInfo.ArgumentList.Add("report");process.StartInfo.ArgumentList.Add("-p");process.StartInfo.ArgumentList.Add(Environment.ProcessId.ToString());process.Start();var output=process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();process.WaitForExit(10000);File.WriteAllText(Path.Combine(folder,"ui-stall-stacks.txt"),output+error);}catch(Exception ex) when(ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception){}}){IsBackground=true}.Start();
    }
    public object Report()
    {
        object Rows(IEnumerable<Ticket> tickets)=>tickets.Where(t=>t.Handled!=0).Select(t=>new{t.Id,t.Phase,t.Requested,t.Returned,t.Handled,t.Frame,t.QueueMilliseconds,PostCallMilliseconds=Ms(t.Requested,t.Returned),HandlerMilliseconds=Ms(t.Requested,t.Handled),AfterPostMilliseconds=Math.Max(0,Ms(t.Returned,t.Handled)),VisibleMilliseconds=Ms(t.Requested,t.Frame)}).ToArray();
        var completed=native.Where(t=>t.Handled!=0).ToArray();
        return new{Mode=mode,Scope=trackFrame?"Synthetic native-window messages; WPF Rendering event, not screen presentation or physical mouse acceptance":"Queue-only native-window and Dispatcher observations; no Rendering subscription or physical input",Unframed=completed.Count(t=>t.Frame==0),Stopwatch.Frequency,Native=AtlasProfiling.Statistics(completed.Select(t=>Ms(t.Requested,t.Handled))),NativeQueue=AtlasProfiling.Statistics(completed.Select(t=>t.QueueMilliseconds)),PostCall=AtlasProfiling.Statistics(completed.Select(t=>Ms(t.Requested,t.Returned))),QueueToWpfFrame=AtlasProfiling.Statistics(completed.Where(t=>t.Frame!=0).Select(t=>t.QueueMilliseconds+Ms(t.Handled,t.Frame))),Visible=AtlasProfiling.Statistics(completed.Where(t=>t.Frame!=0).Select(t=>Ms(t.Requested,t.Frame))),Dispatcher=AtlasProfiling.Statistics(queued.Where(t=>t.Handled!=0).Select(t=>Ms(t.Requested,t.Handled))),NativeSamples=Rows(native),DispatcherSamples=Rows(queued),Operations=operations.ToArray(),NativeMessages=nativeTiming?.Report(),Resources=resources.ToArray(),ThreadPoolThreads=ThreadPool.ThreadCount,PendingThreadPool=ThreadPool.PendingWorkItemCount,StackRequests=stackRequests};
    }
    public void Dispose(){stop.Set();var stopped=clock.Join(1000);nativeTiming?.Dispose();source.RemoveHook(Hook);CompositionTarget.Rendering-=Rendered;window.Dispatcher.Hooks.OperationStarted-=Started;window.Dispatcher.Hooks.OperationCompleted-=Completed;if(window.Content is Panel panel)panel.Children.Remove(marker);if(stopped)stop.Dispose();}
    [DllImport("user32.dll",SetLastError=true)]private static extern bool PostMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]private static extern uint GetMessageTime();
}
