// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class UiSchedulingTests
{
    [Theory]
    [InlineData(120u,100u,20u)]
    [InlineData(5u,4294967291u,10u)]
    public void NativeQueueAgeUsesOsMessageTimeAndHandlesTickWrap(uint now,uint enqueued,uint expected)
        =>Assert.Equal(expected,UiResponsivenessProbe.MessageAge(now,enqueued));

    private static Task Sta(Func<Task> action)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async()=>{try{await action();done.SetResult();}catch(Exception ex){done.SetException(ex);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});
            Dispatcher.Run();
        }){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public Task DueCameraAnimationYieldsToAlreadyQueuedInput()=>Sta(async()=>{
        var atlas=new AtlasControl();
        var timer=(DispatcherTimer)typeof(AtlasControl).GetField("animation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(atlas)!;
        var order=new List<string>();var tick=new TaskCompletionSource();
        timer.Tick+=(_,_)=>{order.Add("animation");timer.Stop();tick.TrySetResult();};
        timer.Start();Thread.Sleep(35);
        atlas.Dispatcher.BeginInvoke(()=>order.Add("input"),DispatcherPriority.Input);
        await tick.Task;
        Assert.Equal("input",order[0]);await atlas.Tiles.DisposeAsync();
    });

    [Fact]
    public Task ObsoleteMarkerThumbnailCannotRepopulateAChangedLibrary()=>Sta(async()=>{
        var atlas=new AtlasControl{Previews=new PreviewService(System.IO.Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local","ui-scheduling-cache"))};
        var load=typeof(AtlasControl).GetMethod("LoadThumbnail",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var pending=(Task)load.Invoke(atlas,[System.IO.Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!,".local","absent-synthetic.jpg")])!;
        atlas.Items=Array.Empty<MediaItem>();
        await pending;
        var cache=(System.Collections.IDictionary)typeof(AtlasControl).GetField("thumbnails",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(atlas)!;
        Assert.Empty(cache);await atlas.Tiles.DisposeAsync();
    });

    [Fact]
    public Task DetailedNativeTimingObservesOnlyItsOwnUiThreadMessages()=>Sta(async()=>{
        using var source=new HwndSource(new HwndSourceParameters("GeoStack synthetic timing window"){ParentWindow=new IntPtr(-3)});
        using var probe=new UiNativeMessageProbe(source.Handle,()=>"synthetic-native-handler");
        var arrived=new TaskCompletionSource();
        source.AddHook((IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)=>{
            if(message==0x8422){Thread.Sleep(20);arrived.TrySetResult();handled=true;}return IntPtr.Zero;
        });
        Assert.True(PostMessage(source.Handle,0x8422,IntPtr.Zero,IntPtr.Zero));
        await arrived.Task;await Dispatcher.Yield(DispatcherPriority.Background);
        using var report=JsonDocument.Parse(JsonSerializer.Serialize(probe.Report()));
        Assert.True(report.RootElement.GetProperty("Installed").GetBoolean());
        Assert.Contains(report.RootElement.GetProperty("Samples").EnumerateArray(),s=>s.GetProperty("Message").GetString()=="0x8422" && s.GetProperty("Milliseconds").GetDouble()>=15);
    });

    [DllImport("user32.dll",SetLastError=true)]private static extern bool PostMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
}
