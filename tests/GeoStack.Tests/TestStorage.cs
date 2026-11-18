// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;

namespace GeoStack.Tests;

// The test entry point owns this run; successful runs can be cleaned without touching old or user data.
internal static class TestStorage
{
    internal static string Create(string repository)
    {
        var token = Environment.GetEnvironmentVariable("GEOSTACK_TEST_RUN");
        var parent = Path.Combine(repository, ".local/tests");
        if (!string.IsNullOrEmpty(token))
        {
            if (!Guid.TryParseExact(token, "N", out _)) throw new InvalidOperationException("Invalid test run identity");
            parent = Path.Combine(repository, ".local/test-runs", token);
            if (!File.Exists(Path.Combine(parent, ".owned-test-run.json"))) throw new InvalidOperationException("Test run ownership marker is missing");
        }
        var directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
