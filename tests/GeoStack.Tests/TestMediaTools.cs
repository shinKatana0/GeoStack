// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
namespace GeoStack.Tests;
internal static class TestMediaTools
{
    private static string Root => Environment.GetEnvironmentVariable("GEOSTACK_TEST_TOOLS") ?? Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../",AppContext.BaseDirectory),"tools");
    internal static string ExifTool => Path.GetFullPath(Path.Combine(Root,"ExifTool/ExifTool.exe"));
    internal static string FFmpeg => Path.GetFullPath(Path.Combine(Root,"FFmpeg/ffmpeg.exe"));
}
