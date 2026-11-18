// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class RuntimeIdentityTests
{
    [Fact] public void NativeCommandLineIsAvailableForAppHostDiagnostics()
    {
        Assert.False(string.IsNullOrWhiteSpace(RuntimeIdentity.NativeCommandLine));
    }
    [Fact] public void CaptionUsesRealAssemblyVersionAndPreviewChannel()
    {
        var version=typeof(GeoStack.App.App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(version,RuntimeIdentity.ProductVersion);
        Assert.Equal(version.Split('+')[0],RuntimeIdentity.Version);
        Assert.Equal(version.Split('+')[1],RuntimeIdentity.BuildCommit);
        Assert.Equal("Preview",RuntimeIdentity.Channel);
        Assert.Equal("GeoStack \u2014 PREVIEW "+RuntimeIdentity.Version,RuntimeIdentity.WindowTitle);
    }
    [Fact] public void LoadedAssemblyDiagnosticsContainAppAndCoreOnly()
    {
        Assert.Equal(2,RuntimeIdentity.Assemblies().Length);
        Assert.Equal(64,RuntimeIdentity.Hash(typeof(GeoStack.Core.MediaItem).Assembly.Location).Length);
    }
    [Theory][InlineData("en-US")][InlineData("ru-RU")]
    public void RuntimeDiagnosticLabelsAreLocalized(string language)
    {
        foreach(var key in new[]{"RuntimeVersion","RuntimeCommit","RuntimeChannel","RuntimeExecutable","RuntimeProfile","RuntimeEngine","RuntimeCatalog","CopyDiagnostics","ClipboardUnavailable","ValidationPreparing","ValidationReady"})
            Assert.NotEqual(key,Strings.GetForLanguage(key,language));
    }
}

