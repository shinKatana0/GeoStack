// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using GeoStack.App;
using Xunit;

namespace GeoStack.Tests;

public sealed class DownloadLocalizationTests
{
    [Fact]
    public void RussianDownloadMessagesPreserveUnicodeAndMatchRuntimeResources()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!, "src/GeoStack.App/Resources/DownloadStrings.json");
        using var source = JsonDocument.Parse(File.ReadAllText(path));
        Strings.SetLanguage("ru-RU");
        try
        {
            Assert.Equal("Отмена", Strings.Get("DownloadCancel"));
            foreach (var pair in source.RootElement.EnumerateObject())
            {
                var translated = pair.Value[1].GetString()!;
                Assert.DoesNotContain("??", translated);
                Assert.DoesNotContain("\uFFFD", translated);
                Assert.Equal(translated, Strings.Get(pair.Name));
            }
        }
        finally { Strings.SetLanguage("en-US"); }
    }
}
