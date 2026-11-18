// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public class WindowBoundsTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void StartupWindowFitsScaledWorkArea(double scale)
    {
        var workArea = new Rect(0, 0, 1660 / scale, 1080 / scale);
        var bounds = WindowBounds.Fit(workArea, new Size(1360, 880));
        Assert.True(workArea.Contains(bounds));
        Assert.Equal(Math.Min(1360, workArea.Width), bounds.Width);
        Assert.Equal(Math.Min(880, workArea.Height), bounds.Height);
    }

    [Fact]
    public void CenteringPreservesWorkAreaOffset()
    {
        var bounds = WindowBounds.Fit(new Rect(-1920, 40, 1920, 1040), new Size(1360, 880));
        Assert.Equal(new Rect(-1640, 120, 1360, 880), bounds);
    }
}
