// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Xml.Linq;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class PrimaryActionTests
{
    private static string Root=>Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;
    [Fact] public void ZoomTemplateSeparatesPressedHoverDisabledAndKeyboardFocus()
    {
        XNamespace n="http://schemas.microsoft.com/winfx/2006/xaml/presentation";XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var app=XDocument.Load(Path.Combine(Root,"src/GeoStack.App/App.xaml"));var style=app.Descendants(n+"Style").Single(e=>(string?)e.Attribute(x+"Key")=="MapZoomButton");
        var triggers=style.Descendants(n+"Trigger").Select(e=>(string?)e.Attribute("Property")).ToArray();
        Assert.Contains("IsMouseOver",triggers);Assert.Contains("IsPressed",triggers);Assert.Contains("IsEnabled",triggers);Assert.DoesNotContain("IsKeyboardFocused",triggers);
        Assert.Contains(style.Elements(n+"Setter"),e=>(string?)e.Attribute("Property")=="FocusVisualStyle");
    }
    [Theory][InlineData(.75,1/1.5,12,.75)][InlineData(12,1.5,12,12)][InlineData(20,1.5,12,20)][InlineData(20,.5,12,10)][InlineData(.6,.5,12,.6)]
    public void ZoomAffordancesUseTheExistingCameraClamp(double current,double factor,double limit,double expected)
        =>Assert.Equal(expected,AtlasControl.ClampRequestedZoom(current,factor,limit),8);
    [Fact] public void PrimaryCopyIsFirstAndUsesTheExistingAccent()
    {
        XNamespace n="http://schemas.microsoft.com/winfx/2006/xaml/presentation";XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var window=XDocument.Load(Path.Combine(Root,"src/GeoStack.App/MainWindow.xaml"));var button=window.Descendants(n+"Button").Single(e=>(string?)e.Attribute(x+"Name")=="CopyButton");
        Assert.Equal("{StaticResource PrimaryActionButton}",(string?)button.Attribute("Style"));Assert.Same(button,button.Parent!.Elements().First());
        var app=XDocument.Load(Path.Combine(Root,"src/GeoStack.App/App.xaml"));var style=app.Descendants(n+"Style").Single(e=>(string?)e.Attribute(x+"Key")=="PrimaryActionButton");
        Assert.Contains(style.Elements(n+"Setter"),e=>(string?)e.Attribute("Property")=="Background" && (string?)e.Attribute("Value")=="{DynamicResource Accent}");
    }
    [Fact] public void EveryMediaLayoutExposesTheSameItemActions()
    {
        XNamespace n="http://schemas.microsoft.com/winfx/2006/xaml/presentation";XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var window=XDocument.Load(Path.Combine(Root,"src/GeoStack.App/MainWindow.xaml"));
        foreach(var key in new[]{"MediaThumbnailTemplate","MediaLibraryRowTemplate","MediaCompactTemplate"})
        {
            var template=window.Descendants(n+"DataTemplate").Single(e=>(string?)e.Attribute(x+"Key")==key);
            Assert.Single(template.Descendants(n+"Button"),e=>(string?)e.Attribute("Click")=="MediaActions_Click");
        }
        Assert.NotEqual("MediaActions",Strings.GetForLanguage("MediaActions","en-US"));
        Assert.NotEqual(Strings.GetForLanguage("MediaActions","en-US"),Strings.GetForLanguage("MediaActions","ru-RU"));
    }
}
