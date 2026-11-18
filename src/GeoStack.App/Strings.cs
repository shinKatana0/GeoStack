// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Resources;

namespace GeoStack.App;

public static class Strings
{
    // Embed both resource sets in the executable assembly for deterministic portable loading.
    private static readonly ResourceManager English = new("GeoStack.App.Resources.English", typeof(Strings).Assembly);
    private static readonly ResourceManager Russian = new("GeoStack.App.Resources.Russian", typeof(Strings).Assembly);
    private static CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
    public static void SetLanguage(string language)
    {
        culture = CultureInfo.GetCultureInfo(language == "ru-RU" ? "ru-RU" : "en-US");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
    public static string Get(string key) => (culture.Name == "ru-RU" ? Russian : English).GetString(key, CultureInfo.InvariantCulture) ?? English.GetString(key, CultureInfo.InvariantCulture) ?? key;
    public static string GetForLanguage(string key, string language) => (language == "ru-RU" ? Russian : English).GetString(key, CultureInfo.InvariantCulture) ?? English.GetString(key, CultureInfo.InvariantCulture) ?? key;
}
