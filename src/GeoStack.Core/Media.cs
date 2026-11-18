// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeoStack.Core;

public sealed record MediaItem(string Path, double? Latitude, double? Longitude, string Kind, string? Captured = null, string? Error = null)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public bool HasLocation => Geo.IsValid(Latitude, Longitude);
}

public static class MediaTypes
{
    private static readonly HashSet<string> Photos = new(".jpg .jpeg .heic .heif .png .webp .tif .tiff .dng .cr2 .cr3 .nef .nrw .arw .srf .sr2 .orf .rw2 .raf .pef .srw .raw .rwl .3fr .iiq .kdc .mos .mrw .x3f".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Videos = new(".mov .mp4 .m4v .mkv .avi .webm .mts .m2ts".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static string? Detect(string path) => Videos.Contains(System.IO.Path.GetExtension(path)) ? "Video" : Photos.Contains(System.IO.Path.GetExtension(path)) ? "Photo" : null;
}

public static class Geo
{
    public static bool IsValid(double? lat, double? lon) => lat is double a && lon is double b && double.IsFinite(a) && double.IsFinite(b) && a >= -90 && a <= 90 && b >= -180 && b <= 180;
    public static (double X, double Y) Project(double latitude, double longitude)
    {
        var lat = Math.Clamp(latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        return ((longitude + 180) / 360, (1 - Math.Log(Math.Tan(Math.PI / 4 + lat / 2)) / Math.PI) / 2);
    }
    public static (double Latitude, double Longitude) Unproject(double x, double y) => (Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180 / Math.PI, x * 360 - 180);
    public static double WrapLongitude(double longitude) => ((longitude + 180) % 360 + 360) % 360 - 180;
    public static double WrappedDelta(double delta) => delta - Math.Round(delta);
    public static IReadOnlyList<MediaCluster> Cluster(IEnumerable<MediaItem> items, double worldPixels, double cellPixels = 74)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worldPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellPixels);
        var bins = Math.Max(1, (int)Math.Ceiling(worldPixels / cellPixels));
        var groups = new List<List<MediaItem>>();
        var anchors = new List<(double X, double Y)>();
        var buckets = new Dictionary<(int X, int Y), List<int>>();
        foreach (var item in items.Where(x => x.HasLocation).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            var p = Project(item.Latitude!.Value, item.Longitude!.Value);
            var bx = (int)Math.Floor(p.X * bins) % bins; var by = (int)Math.Floor(p.Y * bins);
            var found = -1; var best = cellPixels * cellPixels;
            for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++)
                if (buckets.TryGetValue(((bx + dx + bins) % bins, by + dy), out var neighbors))
                    foreach (var candidate in neighbors)
                    {
                        var a = anchors[candidate];
                        var distance = Math.Pow(WrappedDelta(p.X - a.X) * worldPixels, 2) + Math.Pow((p.Y - a.Y) * worldPixels, 2);
                        if (distance < best) { best = distance; found = candidate; }
                    }
            if (found < 0)
            {
                found = groups.Count; groups.Add([]); anchors.Add(p);
                if (!buckets.TryGetValue((bx, by), out var bucket)) buckets[(bx, by)] = bucket = [];
                bucket.Add(found);
            }
            groups[found].Add(item);
        }
        return groups.Select(g => new MediaCluster(g)).ToArray();
    }
    public static (double X, double Y, double Width, double Height)? Bounds(IEnumerable<MediaItem> items)
    {
        var points = items.Where(m => m.HasLocation).Select(m => Project(m.Latitude!.Value, m.Longitude!.Value)).OrderBy(p => p.X).ToArray();
        if (points.Length == 0) return null;
        var gap = -1d; var start = points[0].X;
        for (var i = 0; i < points.Length; i++)
        {
            var next = i + 1 < points.Length ? points[i + 1].X : points[0].X + 1;
            if (next - points[i].X > gap) { gap = next - points[i].X; start = next % 1; }
        }
        var width = 1 - gap; var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        return ((start + width / 2) % 1, (minY + maxY) / 2, width, maxY - minY);
    }
}

public sealed record MediaCluster(IReadOnlyList<MediaItem> Items)
{
    public double Latitude => Items.Average(x => x.Latitude!.Value);
    public double Longitude => Geo.WrapLongitude(Items[0].Longitude!.Value + Items.Average(x => Geo.WrapLongitude(x.Longitude!.Value - Items[0].Longitude!.Value)));
}

public static class ExifParser
{
    public static IReadOnlyList<MediaItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected an ExifTool result array");
        return doc.RootElement.EnumerateArray().Select(e => {
            string? Str(string key) => e.TryGetProperty(key, out var v) ? v.ToString() : null;
            double? Num(string key) => double.TryParse(Str(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
            var path = Str("SourceFile") ?? "";
            double? lat = Num("GPSLatitude"), lon = Num("GPSLongitude");
            if (!Geo.IsValid(lat, lon))
            {
                var location = Str("GPSCoordinates") ?? Str("GPSPosition");
                if (TryIso6709(location, out var a, out var b)) { lat = a; lon = b; }
            }
            if (!Geo.IsValid(lat, lon)) { lat = null; lon = null; }
            var mime = Str("MIMEType");
            var error = Str("Error") ?? Str("Warning");
            if (mime != null && !mime.StartsWith("image/", StringComparison.Ordinal) && !mime.StartsWith("video/", StringComparison.Ordinal) && mime != "application/octet-stream") error ??= "Unsupported media contents: " + mime;
            var captured = Str("DateTimeOriginal") ?? Str("CreateDate");
            if (captured?.StartsWith("0000", StringComparison.Ordinal) == true) captured = null;
            return new MediaItem(path, lat, lon, MediaTypes.Detect(path) ?? "Unknown", captured, error);
        }).ToArray();
    }
    public static bool TryIso6709(string? value, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (value == null || value.Length > 128) return false;
        var match = Regex.Match(value.Trim(), @"^([+-]\d{2}(?:\.\d+)?)([+-]\d{3}(?:\.\d+)?)(?:[+-]\d+(?:\.\d+)?)?/?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) && Geo.IsValid(latitude, longitude);
    }
}
