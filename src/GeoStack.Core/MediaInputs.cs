// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
namespace GeoStack.Core;

public sealed record MediaDiscovery(IReadOnlyList<string> Files, IReadOnlyList<string> Errors, int Skipped, int Examined);

/// <summary>Shared file/folder discovery. Never follows file or directory reparse points.</summary>
public static class MediaInputs
{
    public static string NormalizePath(string path)
    {
        var full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\?\",StringComparison.Ordinal) && full.Length>6 && full[5]==':')full=full[4..];
        return Path.TrimEndingDirectorySeparator(full);
    }
    // Pure normalization: restoration preflight must not stat invalid or network inputs.
    public static IReadOnlyList<string> NormalizeInputs(IEnumerable<string> inputs)
    {
        var result=new List<string>();
        foreach(var input in inputs)
        {
            try{result.Add(NormalizePath(input));}
            catch(Exception ex) when(ex is IOException or ArgumentException or NotSupportedException){}
        }
        return result;
    }
    public static MediaDiscovery Discover(IEnumerable<string> inputs, CancellationToken token, Action<ScanProgress>? progress = null)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var pending = new Stack<string>(inputs.Reverse());
        var skipped = 0; var examined = 0;
        while (pending.TryPop(out var input))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path = NormalizePath(input);
                // Network paths are outside the fully offline import contract.
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\") && !path.StartsWith(@"\\?\", StringComparison.Ordinal))
                    throw new IOException("Network sources are not supported by offline import");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!visited.Add(path)) continue;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(path)) { token.ThrowIfCancellationRequested(); pending.Push(entry); }
                }
                else
                {
                    examined++;
                    if (MediaTypes.Detect(path) == null) { skipped++; continue; }
                    if (!files.Add(path)) { skipped++; continue; }
                    try { using var readable = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
                    catch { files.Remove(path); throw; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { skipped++; if (errors.Count < 200) errors.Add(input + ": " + e.Message); }
            if ((examined + visited.Count) % 128 == 0) progress?.Invoke(new(files.Count, 0, 0, errors.Count, "", skipped));
        }
        progress?.Invoke(new(files.Count, 0, 0, errors.Count, "", skipped));
        return new(files.Order(StringComparer.OrdinalIgnoreCase).ToArray(), errors, skipped, examined);
    }
}
