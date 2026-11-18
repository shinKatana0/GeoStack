// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GeoStack.App;

public static class WindowLayout
{
    public static Size Minimum(string purpose) => purpose switch {
        "Main" => new(860, 600), "Settings" => new(440, 420), "CopyToLocation" => new(700, 500),
        "MapManager" or "GeographicData" => new(600, 430), "CopyHistory" => new(600, 400), _ => new(400, 260)
    };
    public static Rect Clamp(Rect work, Rect desired, Size minimum)
    {
        var width = Math.Clamp(desired.Width, Math.Min(minimum.Width, work.Width), work.Width);
        var height = Math.Clamp(desired.Height, Math.Min(minimum.Height, work.Height), work.Height);
        return new(Math.Clamp(desired.Left, work.Left, work.Right - width), Math.Clamp(desired.Top, work.Top, work.Bottom - height), width, height);
    }
    public static void Attach(Window window, string purpose)
    {
        var minimum = Minimum(purpose); window.MinWidth = minimum.Width; window.MinHeight = minimum.Height;
        HwndSource? source = null;
        void Fit(bool center)
        {
            var work = WorkArea(center && window.Owner != null ? window.Owner : window); window.MinWidth = Math.Min(minimum.Width, work.Width); window.MinHeight = Math.Min(minimum.Height, work.Height);
            if (window.WindowState != WindowState.Normal) return;
            var width = double.IsFinite(window.Width) ? window.Width : minimum.Width;
            var height = double.IsFinite(window.Height) ? window.Height : minimum.Height;
            var left = center || !double.IsFinite(window.Left) ? work.Left + (work.Width - width) / 2 : window.Left;
            var top = center || !double.IsFinite(window.Top) ? work.Top + (work.Height - height) / 2 : window.Top;
            var bounds = Clamp(work, new(left, top, width, height), minimum);
            window.Width = bounds.Width; window.Height = bounds.Height; window.Left = bounds.Left; window.Top = bounds.Top;
        }
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message is 0x02E0 or 0x007E or 0x001A) window.Dispatcher.BeginInvoke(() => Fit(false), DispatcherPriority.Loaded);
            return IntPtr.Zero;
        }
        window.SourceInitialized += (_, _) => {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle); source?.AddHook(Hook);
            window.WindowStartupLocation = WindowStartupLocation.Manual; Fit(true);
        };
        window.StateChanged += (_, _) => { if (window.WindowState == WindowState.Normal) Fit(false); };
        window.Closed += (_, _) => source?.RemoveHook(Hook);
    }
    private static Rect WorkArea(Window window)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY, (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
