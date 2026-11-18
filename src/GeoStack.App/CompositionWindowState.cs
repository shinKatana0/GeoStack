// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GeoStack.App;

internal static class CompositionWindowState
{
    [StructLayout(LayoutKind.Sequential)]private struct Bounds{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;public Point(int x,int y){X=x;Y=y;}}
    internal static object Read(Window window)
    {
        var h=new WindowInteropHelper(window).Handle;GetWindowRect(h,out var r);var uncovered=0;
        foreach(var p in new[]{new Point((r.Left+r.Right)/2,(r.Top+r.Bottom)/2),new Point(r.Left+60,r.Top+100),new Point(r.Right-60,r.Top+100),new Point(r.Left+60,r.Bottom-60),new Point(r.Right-60,r.Bottom-60)})
            if(GetAncestor(WindowFromPoint(p),2)==h)uncovered++;
        return new{Foreground=GetForegroundWindow()==h,UncoveredSamples=uncovered,window.Topmost,window.IsVisible,Session=System.Diagnostics.Process.GetCurrentProcess().SessionId};
    }
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr h,out Bounds r);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr h,uint flags);
}
