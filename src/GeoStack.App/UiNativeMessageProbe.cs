// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GeoStack.App;

// Explicit diagnostic mode only: subclass our own window, never another process or input payload.
internal sealed class UiNativeMessageProbe : IDisposable
{
    private delegate IntPtr Procedure(IntPtr window,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
    private readonly Procedure procedure;
    private readonly IntPtr window;
    private readonly bool installed;
    private readonly List<object> samples=[];
    private static readonly UIntPtr Identifier=new(0x8423);
    public UiNativeMessageProbe(IntPtr window,Func<string> phase,Func<object>? state=null)
    {
        this.window=window;
        procedure=(hwnd,message,w,l,id,data)=>{
            var start=Stopwatch.GetTimestamp();var label=phase();var before=state?.Invoke();
            try{return DefSubclassProc(hwnd,message,w,l);}
            finally
            {
                var elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if(elapsed>=8 && samples.Count<4096)samples.Add(new{Message="0x"+message.ToString("X4"),Phase=label,Start=start,Milliseconds=elapsed,Before=before,After=state?.Invoke()});
            }
        };
        installed=SetWindowSubclass(window,procedure,Identifier,UIntPtr.Zero);
    }
    public object Report()=>new{Installed=installed,Scope="Own window-procedure duration including posted messages; no message payloads",Samples=samples.ToArray()};
    public void Dispose(){if(installed)RemoveWindowSubclass(window,procedure,Identifier);}
    [DllImport("comctl32.dll",SetLastError=true)]private static extern bool SetWindowSubclass(IntPtr window,Procedure callback,UIntPtr id,UIntPtr data);
    [DllImport("comctl32.dll")]private static extern bool RemoveWindowSubclass(IntPtr window,Procedure callback,UIntPtr id);
    [DllImport("comctl32.dll")]private static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr w,IntPtr l);
}
