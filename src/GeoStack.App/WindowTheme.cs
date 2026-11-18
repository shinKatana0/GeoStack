// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GeoStack.App;

internal static class WindowTheme
{
    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
