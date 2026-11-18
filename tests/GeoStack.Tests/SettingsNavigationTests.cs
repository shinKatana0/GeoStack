// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class SettingsNavigationTests
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    [Fact] public void AboutVersionComesFromInstalledAssemblyMetadata()
    {
        var metadata = typeof(GeoStack.App.App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(metadata.Split('+')[0], ApplicationInformation.InstalledVersion);
        Assert.True(Version.TryParse(ApplicationInformation.InstalledVersion, out _));
    }
    [Theory]
    [InlineData("en-US", "About GeoStack", "Help")]
    [InlineData("ru-RU", "О GeoStack", "Справка")]
    public void SettingsInformationAndDocumentationLabelsAreLocalized(string language, string about, string help)
    {
        Assert.Equal(about, Strings.GetForLanguage("About", language));
        Assert.Equal(help, Strings.GetForLanguage("Help", language));
        foreach (var key in ApplicationInformation.HelpDocuments.Select(d => d.Label).Concat(new[] { "InstalledVersion", "SettingsAttribution", "HelpText", "ThirdPartyLicenses", "RedistributionNotes", "SettingsLanguageLabel", "SettingsThemeLabel", "SettingsTooltip" }))
        {
            var text = Strings.GetForLanguage(key, language); Assert.False(string.IsNullOrWhiteSpace(text)); Assert.NotEqual(key, text);
        }
        Assert.Contains("{0}", Strings.GetForLanguage("InstalledVersion", language));
        Assert.Equal(Strings.GetForLanguage("Settings", language), Strings.GetForLanguage("SettingsTooltip", language));
    }
    [Fact] public void MainToolbarKeepsPrimaryActionsAndUsesASettingsGearWithoutAboutOrHelp()
    {
        var document = XDocument.Load(Path.Combine(Root, "src/GeoStack.App/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var toolbar = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ToolbarActions");
        var names = toolbar.Elements().Where(e => e.Name.LocalName == "Button").Select(e => (string?)e.Attribute(x + "Name")).ToArray();
        Assert.Equal(new[] { "OpenButton", "CancelButton", "LibraryButton", "MapsButton", "SettingsButton" }, names);
        var gear = toolbar.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "SettingsButton");
        Assert.Contains(gear.Descendants(), e => (string?)e.Attribute("Text") == "\u2699");
        foreach (var name in new[] { "CopyButton", "CopyStackButton", "ZoomInButton", "ZoomOutButton", "ResetButton", "FitButton" }) Assert.Contains(document.Descendants(), e => (string?)e.Attribute(x + "Name") == name);
    }
    [Fact] public void PortableHelpAndAttributionTargetsExistWithoutOnlineDependencies()
    {
        var package = Path.GetDirectoryName(Environment.GetEnvironmentVariable("GEOSTACK_EXE")) ?? Path.Combine(Root, "dist/GeoStack-portable-win-x64");
        foreach (var path in ApplicationInformation.HelpDocuments.Select(d => d.RelativePath).Append("docs/REDISTRIBUTION_STATUS.md")) Assert.True(File.Exists(Path.Combine(package, path)), path);
        Assert.True(Directory.Exists(Path.Combine(package, "licenses")));
    }
}
