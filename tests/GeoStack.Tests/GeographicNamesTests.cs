// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class GeographicNamesTests
{
    [Theory]
    [InlineData("Tokyo", "\u0422\u043e\u043a\u0438\u043e")]
    [InlineData("Kyoto", "\u041a\u0438\u043e\u0442\u043e")]
    [InlineData("Kanazawa", "\u041a\u0430\u043d\u0430\u0434\u0437\u0430\u0432\u0430")]
    [InlineData("Shanghai", "\u0428\u0430\u043d\u0445\u0430\u0439")]
    [InlineData("Z\u00fcrich", "\u0426\u044e\u0440\u0438\u0445")]
    [InlineData("Warsaw", "\u0412\u0430\u0440\u0448\u0430\u0432\u0430")]
    public void BundledCitiesUseDatasetTranslationsAndStableIds(string english, string russian)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "geography/cities.geojson")));
        var names = doc.RootElement.GetProperty("features").EnumerateArray().Select(f => GeographicName.Read(f.GetProperty("properties"), "name", "ne_id")).ToArray();
        var city = Assert.Single(names, n => n.Display("en-US") == english);
        Assert.Equal(russian, city.Display("ru-RU")); Assert.NotEmpty(city.Id);
    }
    [Fact] public void MissingTranslationFallsBackToVerifiedSource() => Assert.Equal("Source", new GeographicName("id", "Source", null, " ").Display("ru-RU"));
    [Theory][InlineData("en-US")][InlineData("ru-RU")]
    public void NavigationTooltipsHaveLocalizedContentAndMissingKeysAreSuppressed(string language)
    {
        Strings.SetLanguage(language);
        foreach (var key in new[] { "ZoomIn", "ZoomOut", "ResetMap", "FitAll", "Sidebar", "PreviousPage", "NextPage" }) Assert.False(string.IsNullOrWhiteSpace(MapTooltips.Content(key)));
        Assert.Null(MapTooltips.Content("MissingTooltip")); Strings.SetLanguage("en-US");
    }
}
