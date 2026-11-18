// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace GeoStack.Core;

public enum BoundaryMatch { Outside, Inside, Edge }

/// <summary>Longitude-unwrapped ray casting, preserving holes and antimeridian rings.</summary>
public sealed class BoundaryGeometry
{
    private readonly double[][][][] polygons;
    public long EstimatedBytes { get; }
    public BoundaryGeometry(string geoJson)
    {
        using var document = JsonDocument.Parse(geoJson);
        var geometry = document.RootElement;
        polygons = geometry.GetProperty("type").GetString() switch
        {
            "Polygon" => [geometry.GetProperty("coordinates").Deserialize<double[][][]>()!],
            "MultiPolygon" => geometry.GetProperty("coordinates").Deserialize<double[][][][]>()!,
            _ => throw new InvalidDataException("GeoInvalidGeometry")
        };
        if (polygons.Length == 0) throw new InvalidDataException("GeoInvalidGeometry");
        foreach (var polygon in polygons)
        {
            if (polygon.Length == 0) throw new InvalidDataException("GeoInvalidGeometry");
            foreach (var ring in polygon)
            {
                if (ring.Length < 4 || ring.Any(p => p.Length != 2 || !Geo.IsValid(p[1], p[0])) || !ring[0].SequenceEqual(ring[^1])) throw new InvalidDataException("GeoInvalidGeometry");
                var previous = ring[0][0];
                for (var i = 1; i < ring.Length; i++) { var longitude = previous + Geo.WrapLongitude(ring[i][0] - previous); ring[i][0] = longitude; previous = longitude; }
                EstimatedBytes += ring.Length * 56L + 128;
            }
        }
    }
    public IEnumerable<(double West, double South, double East, double North)> Bounds()
    {
        foreach (var polygon in polygons)
        {
            var ring = polygon[0]; var west = ring.Min(p => p[0]); var east = ring.Max(p => p[0]); var south = ring.Min(p => p[1]); var north = ring.Max(p => p[1]);
            while (west < -180) { west += 360; east += 360; }
            while (west > 180) { west -= 360; east -= 360; }
            if (east > 180) { yield return (west, south, 180, north); yield return (-180, south, east - 360, north); }
            else yield return (west, south, east, north);
        }
    }
    private static BoundaryMatch Ring(double[][] ring, double latitude, double longitude, double tolerance)
    {
        var center = (ring.Min(p => p[0]) + ring.Max(p => p[0])) / 2;
        longitude += Math.Round((center - longitude) / 360) * 360;
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var a = ring[i]; var b = ring[j]; var dx = b[0] - a[0]; var dy = b[1] - a[1];
            if (Math.Abs(dx) + Math.Abs(dy) < 1e-14) continue;
            // Angular edge tolerance comes from the dataset's recorded simplification accuracy.
            if (Math.Abs((longitude - a[0]) * dy - (latitude - a[1]) * dx) <= tolerance * Math.Sqrt(dx * dx + dy * dy) && longitude >= Math.Min(a[0], b[0]) - tolerance && longitude <= Math.Max(a[0], b[0]) + tolerance && latitude >= Math.Min(a[1], b[1]) - tolerance && latitude <= Math.Max(a[1], b[1]) + tolerance) return BoundaryMatch.Edge;
            if ((a[1] > latitude) != (b[1] > latitude) && longitude < dx * (latitude - a[1]) / dy + a[0]) inside = !inside;
        }
        return inside ? BoundaryMatch.Inside : BoundaryMatch.Outside;
    }
    public BoundaryMatch Contains(double latitude, double longitude, double tolerance = 1e-8)
    {
        var edge = false;
        foreach (var polygon in polygons)
        {
            var outer = Ring(polygon[0], latitude, longitude, tolerance);
            if (outer == BoundaryMatch.Edge) { edge = true; continue; }
            if (outer != BoundaryMatch.Inside) continue;
            var excluded = false;
            foreach (var hole in polygon.Skip(1))
            {
                var match = Ring(hole, latitude, longitude, tolerance);
                if (match == BoundaryMatch.Edge) { edge = true; excluded = true; break; }
                if (match == BoundaryMatch.Inside) { excluded = true; break; }
            }
            if (!excluded) return BoundaryMatch.Inside;
        }
        return edge ? BoundaryMatch.Edge : BoundaryMatch.Outside;
    }
}
