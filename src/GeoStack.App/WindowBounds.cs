// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;

namespace GeoStack.App;

public static class WindowBounds
{
    public static Rect Fit(Rect workArea, Size preferred)
    {
        var width = Math.Min(preferred.Width, workArea.Width);
        var height = Math.Min(preferred.Height, workArea.Height);
        return new(workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height);
    }
}
