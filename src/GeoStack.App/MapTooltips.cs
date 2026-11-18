// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GeoStack.App;

public static class MapTooltips
{
    public static string? Content(string key)
    {
        var value = Strings.Get(key);
        return string.IsNullOrWhiteSpace(value) || value == key ? null : value;
    }
    public static void Set(FrameworkElement control, string key)
    {
        control.ToolTip = Content(key);
        ToolTipService.SetPlacement(control, PlacementMode.Left);
        ToolTipService.SetHorizontalOffset(control, -8);
        ToolTipService.SetInitialShowDelay(control, 500);
        ToolTipService.SetShowDuration(control, 6000);
    }
}
