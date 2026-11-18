// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GeoStack.Core;

public enum CopyState { Ready, Rename, Identical, Skipped, Missing, Completed, Cancelled, Failed, Started, SourceRetained, NeedsReview }
public enum OrganizationOperation { Copy, Move }
public sealed class CopyProposal
{
    public string SourceName => Path.GetFileName(Source);
    public string PreviewDestination => Path.Combine(RelativeDirectory, Path.GetFileName(Destination));
    public required string Source { get; init; }
    public required string RelativeDirectory { get; set; }
    public required string Location { get; init; }
    public required string Destination { get; set; }
    public long Size { get; init; }
    public bool Include { get; set; } = true;
    public CopyState State { get; set; }
    public bool MissingGps { get; init; }
    public string? SourceHash { get; set; }
    public IReadOnlyList<string> GeographicWarnings { get; init; } = [];
    public IReadOnlyList<string> GeographicIds { get; init; } = [];
    public string GeographicVersion { get; init; } = "";
}
public sealed record CopyJournalEntry(Guid OperationId, DateTime Timestamp, string Source, string Destination, long Size, CopyState Status, string? Sha256, string? Failure,
    OrganizationOperation Operation = OrganizationOperation.Copy, string Phase = "Result", bool DestinationVerified = false, bool SourceRemoved = false, string TransferMethod = "Copy");

public static partial class GeographicCopy
{
    // Pin existing ancestor directories against rename/deletion while a destination is being written.
    // FILE_FLAG_OPEN_REPARSE_POINT validates the directory itself; links are refused above.
    private sealed class DestinationLease : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        public DestinationLease(string directory)
        {
            try
            {
                for (var current = Path.GetFullPath(directory); current != null; current = Path.GetDirectoryName(current))
                {
                    var handle = CreateFileW(current, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                    if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot secure destination directory", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error())); }
                    handles.Add(handle);
                }
                ValidateAncestors(directory);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); handles.Clear(); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    }
    public static string Component(string value)
    {
        var name = new string(value.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "_";
        if (Regex.IsMatch(name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) name = "_" + name;
        return name.Length > 100 ? name[..100] : name;
    }
    public static void ValidateAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse points are not allowed in copy paths");
    }
    public static string Destination(string root, string relativeDirectory, string filename)
    {
        if (Path.IsPathRooted(relativeDirectory)) throw new IOException("Destination must be relative");
        var parts = relativeDirectory.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or ".." || Component(p) != p)) throw new IOException("Invalid destination component");
        if (Path.GetFileName(filename) != filename || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("Invalid filename");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts), filename));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Destination escapes root");
        ValidateAncestors(destination); return destination;
    }
    public static async Task<string> Hash(string path, CancellationToken token)
    {
        ValidateAncestors(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    public static async Task<List<CopyProposal>> Plan(IEnumerable<MediaItem> selection, ReverseGeocoder geocoder, string root, string language, string hierarchy, bool skipMissing, CancellationToken token, string unknownFolder = "Unknown Location", string? displayLanguage = null)
    {
        var result = new List<CopyProposal>(); var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in selection.DistinctBy(i => Path.GetFullPath(i.Path), StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); var place = geocoder.Resolve(item.Latitude, item.Longitude);
            var parts = place.Hierarchy(language, hierarchy); var unknown = parts.Length == 0;
            var relative = unknown ? Component(unknownFolder) : Path.Combine(parts.Select(Component).ToArray());
            var destination = Destination(root, relative, item.Name); var exists = File.Exists(item.Path);
            var row = new CopyProposal { Source = Path.GetFullPath(item.Path), RelativeDirectory = relative, Location = string.Join(" / ", new[] { place.Country, place.Region, place.City, place.District }.Where(n => n != null).DistinctBy(n => n!.Id).Select(n => n!.Display(displayLanguage ?? language))), GeographicWarnings = place.Warnings, GeographicIds = new[] { place.Country, place.Region, place.City, place.District }.Where(n => n != null).Select(n => n!.Id).ToArray(), GeographicVersion = place.DatasetVersion, Destination = destination, MissingGps = !item.HasLocation, Size = exists ? new FileInfo(item.Path).Length : 0, State = exists ? CopyState.Ready : CopyState.Missing, Include = exists && !(skipMissing && !item.HasLocation) };
            if (!row.Include) row.State = exists ? CopyState.Skipped : CopyState.Missing;
            if (row.Include)
            {
                row.SourceHash = await Hash(row.Source, token);
                if (File.Exists(destination) && await Hash(destination, token) == row.SourceHash) { row.State = CopyState.Identical; row.Include = false; }
                else
                {
                    var count = 1;
                    while (File.Exists(destination) || Directory.Exists(destination) || !reserved.Add(destination)) destination = Destination(root, relative, Path.GetFileNameWithoutExtension(item.Name) + $" ({count++})" + Path.GetExtension(item.Name));
                    if (destination != row.Destination) row.State = CopyState.Rename;
                    row.Destination = destination;
                }
            }
            result.Add(row);
        }
        return result;
    }
    public static async Task Recheck(IReadOnlyList<CopyProposal> plan, string root, CancellationToken token)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in plan.Where(r => r.Include))
        {
            token.ThrowIfCancellationRequested();
            var original = Destination(root, row.RelativeDirectory, Path.GetFileName(row.Source)); var destination = original; var suffix = 1;
            if (!File.Exists(row.Source)) { row.State = CopyState.Missing; row.Include = false; continue; }
            if (File.Exists(destination) && await Hash(destination, token) == row.SourceHash) { row.State = CopyState.Identical; row.Include = false; row.Destination = destination; continue; }
            while (File.Exists(destination) || Directory.Exists(destination) || !reserved.Add(destination)) destination = Destination(root, row.RelativeDirectory, Path.GetFileNameWithoutExtension(original) + $" ({suffix++})" + Path.GetExtension(original));
            row.State = destination == original ? CopyState.Ready : CopyState.Rename; row.Destination = destination;
        }
    }
    public static async Task<IReadOnlyList<CopyJournalEntry>> Execute(IReadOnlyList<CopyProposal> plan, string root, string journalDirectory, IProgress<(int Done, int Total)>? progress, CancellationToken token)
    {
        var operation = Guid.NewGuid(); Directory.CreateDirectory(journalDirectory);
        var journal = Path.Combine(journalDirectory, operation.ToString("N") + ".jsonl");
        var results = new List<CopyJournalEntry>();
        void Record(CopyJournalEntry entry)
        {
            using var log = new FileStream(journal, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n"); log.Write(bytes); log.Flush(true);
        }
        foreach (var row in plan)
        {
            string? temporary = null; var state = row.State; string? failure = null; string? checksum = null;
            try
            {
                if (!row.Include) { state = row.State; }
                else
                {
                    token.ThrowIfCancellationRequested();
                    var destination = Destination(root, row.RelativeDirectory, Path.GetFileName(row.Destination));
                    if (string.Equals(row.Source, destination, StringComparison.OrdinalIgnoreCase)) throw new IOException("Source and destination overlap");
                    ValidateAncestors(row.Source); ValidateAncestors(destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!); ValidateAncestors(destination);
                    using var lease = new DestinationLease(Path.GetDirectoryName(destination)!);
                    if (new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace < row.Size) throw new IOException("Insufficient disk space");
                    Record(new(operation, DateTime.UtcNow, row.Source, destination, row.Size, CopyState.Started, row.SourceHash, null));
                    temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".geostack-" + Guid.NewGuid().ToString("N") + ".partial");
                    await using (var source = new FileStream(row.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.WriteThrough))
                        { await source.CopyToAsync(output, 131072, token); await output.FlushAsync(token); output.Flush(true); }
                        source.Position = 0; checksum = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
                        if (row.SourceHash != null && checksum != row.SourceHash) throw new IOException("Source changed since preview");
                        if (await Hash(temporary, token) != checksum) throw new IOException("Content verification failed");
                        token.ThrowIfCancellationRequested(); ValidateAncestors(destination);
                        // Never overwrite; also resolve files appearing after the preview.
                        var original = destination; var suffix = 1;
                        while (true)
                        {
                            try { File.Move(temporary!, destination, false); temporary = null; break; }
                            catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
                            {
                                if (File.Exists(destination) && await Hash(destination, token) == checksum) { state = CopyState.Identical; break; }
                                destination = Destination(root, row.RelativeDirectory, Path.GetFileNameWithoutExtension(original) + $" ({suffix++})" + Path.GetExtension(original));
                            }
                        }
                    }
                    row.Destination = destination; if (state != CopyState.Identical) state = CopyState.Completed;
                }
            }
            catch (OperationCanceledException) { state = CopyState.Cancelled; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { state = CopyState.Failed; failure = ex.Message; }
            finally { if (temporary != null && File.Exists(temporary)) { try { ValidateAncestors(temporary); File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failure = (failure ?? "") + " Temporary cleanup failed: " + ex.Message; } } }
            var entry = new CopyJournalEntry(operation, DateTime.UtcNow, row.Source, row.Destination, row.Size, state, checksum, failure,
                DestinationVerified: checksum!=null && state is CopyState.Completed or CopyState.Identical);
            Record(entry); results.Add(entry); progress?.Report((results.Count, plan.Count));
        }
        return results;
    }
    public static IReadOnlyList<CopyJournalEntry> ReadJournal(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<CopyJournalEntry>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl")) foreach (var line in File.ReadLines(file))
        { try { if (JsonSerializer.Deserialize<CopyJournalEntry>(line) is { } entry) result.Add(entry); } catch (JsonException) { /* An interrupted final append is not a success record. */ } }
        return result;
    }
}
