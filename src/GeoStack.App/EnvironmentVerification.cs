// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace GeoStack.App;

internal static class EnvironmentVerification
{
    // Developer verification only: do not inventory personal directories or write identifiers.
    internal static void Save()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct();
        var frameworks = roots.SelectMany(root =>
        {
            var shared = Path.Combine(root, "dotnet", "shared");
            return Directory.Exists(shared) ? Directory.EnumerateDirectories(shared).SelectMany(directory => Directory.EnumerateDirectories(directory).Select(version => Path.GetFileName(directory) + "/" + Path.GetFileName(version))) : [];
        }).ToArray();
        bool OnPath(string file) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(directory => File.Exists(Path.Combine(directory.Trim('"'), file)));
        using var identity = WindowsIdentity.GetCurrent();
        var report = new { OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            RuntimeFromPackage = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory()).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            InstalledSharedFrameworks = frameworks, ExifToolOnPath = OnPath("exiftool.exe"), FFmpegOnPath = OnPath("ffmpeg.exe"),
            HeifExtensionInventory = "NOT_TESTED", HeicDecoder = "Bundled FFmpeg; optional Store extensions are not used" };
        File.WriteAllText(Path.Combine(App.DataDirectory, "environment.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
