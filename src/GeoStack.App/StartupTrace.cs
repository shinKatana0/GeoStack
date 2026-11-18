// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GeoStack.App;

// Opt-in diagnostics contain phase names and timings, never media paths.
internal static class StartupTrace
{
    private static readonly long origin = Stopwatch.GetTimestamp();
    private static readonly List<object> phases = [];
    private static bool enabled;
    internal static void Begin(string[] args)
    {
        enabled = args.Contains("--smoke-test") || args.Contains("--trace-startup");
        Mark("ManagedEntry");
    }
    internal static void Mark(string phase)
    {
        if (!enabled) return;
        using var process = Process.GetCurrentProcess();
        phases.Add(new { Phase = phase,
            ManagedElapsedMilliseconds = Stopwatch.GetElapsedTime(origin).TotalMilliseconds,
            ProcessElapsedMilliseconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds });
    }
    internal static void Save(string directory)
    {
        if (enabled) File.WriteAllText(Path.Combine(directory, "startup-phases.json"), JsonSerializer.Serialize(phases, new JsonSerializerOptions { WriteIndented = true }));
    }
}
