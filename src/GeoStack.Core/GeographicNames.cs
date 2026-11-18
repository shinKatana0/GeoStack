// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace GeoStack.Core;

public sealed record GeographicName(string Id, string Source, string? English, string? Russian)
{
    public string Display(string language) => (language == "ru-RU" ? Russian : English) is { } name && !string.IsNullOrWhiteSpace(name) ? name : Source;
    public static GeographicName Read(JsonElement properties, string sourceKey, string idKey)
    {
        string? Value(params string[] keys) => keys.Select(k => properties.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return new(Value(idKey) ?? throw new InvalidDataException("Missing stable geographic identifier"), Value(sourceKey) ?? "", Value("name_en", "NAME_EN", "name:en"), Value("name_ru", "NAME_RU", "name:ru"));
    }
}
