// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace GeoStack.Core;

public static class ToolProcess
{
    public static async Task<bool> ExtractBinary(string executable, IEnumerable<string> args, string destination, CancellationToken token, string? standardInput = null)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.RedirectStandardInput = standardInput != null;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        if (standardInput != null) process.StartInfo.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["LC_ALL"] = "C"; process.StartInfo.Environment["LC_CTYPE"] = "C";
        process.Start(); var errors = process.StandardError.ReadToEndAsync();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        try
        {
            if (standardInput != null) { await process.StandardInput.WriteAsync(standardInput.AsMemory(), token); process.StandardInput.Close(); }
            await using var output = File.Create(destination); var buffer = new byte[16384]; long total = 0;
            int count;
            while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer, token)) > 0)
            {
                total += count; if (total > 32 * 1024 * 1024) throw new IOException("Embedded preview exceeds the size limit");
                await output.WriteAsync(buffer.AsMemory(0,count),token);
            }
            await process.WaitForExitAsync(token); await errors;
            // The cancellation callback may kill the child before the wait observes the cancelled token.
            token.ThrowIfCancellationRequested();
            return process.ExitCode == 0 && total > 0;
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    public static async Task<(int ExitCode, string Output, string Error)> Run(string executable, IEnumerable<string> args, CancellationToken token, string? standardInput = null)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.RedirectStandardInput = standardInput != null;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        if (standardInput != null) process.StartInfo.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["LANG"] = "C";
        process.StartInfo.Environment["LC_CTYPE"] = "C";
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        try { if (standardInput != null) { await process.StandardInput.WriteAsync(standardInput.AsMemory(), token); process.StandardInput.Close(); } await process.WaitForExitAsync(token); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
        var standardOutput=await output;var standardError=await error;
        token.ThrowIfCancellationRequested();
        return (process.ExitCode, standardOutput, standardError);
    }
}

public sealed record ScanProgress(int Discovered, int Indexed, int Located, int Errors, string Current, int Skipped = 0, IReadOnlyList<MediaItem>? Committed = null);
public sealed record ScanResult(IReadOnlyList<MediaItem> Items, IReadOnlyList<string> Errors, int Reused = 0, int Added = 0, int Skipped = 0, int Discovered = 0);

public sealed record ScanMeasurements(double DiscoveryMilliseconds,double MetadataMilliseconds,double IndexMilliseconds,int MetadataProcesses);
public sealed class ScanService(string exifTool)
{
    public int BatchSize { get; init; } = 40;
    public ScanMeasurements Measurements { get; private set; } = new(0,0,0,0);
    public Task<ScanResult> ScanAsync(string root, MediaIndex index, IProgress<ScanProgress>? progress, CancellationToken token)
        => ScanAsync(new[]{root}, index, progress, token);

    public async Task<ScanResult> ScanAsync(IEnumerable<string> roots, MediaIndex index, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var phaseClock=System.Diagnostics.Stopwatch.StartNew();double metadataMs=0,indexMs=0;var processes=0;
        var inputs=roots.ToArray();
        var initialExclusions=index.ExclusionTokens();
        var discovery = MediaInputs.Discover(inputs, token, p => progress?.Report(p));
        var discovered=discovery.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Only a direct, supported and accessible file input restores an exclusion. Folder rescans never do.
        index.RestoreExplicitFiles(MediaInputs.NormalizeInputs(inputs).Where(discovered.Contains),initialExclusions);
        var exclusions=index.Exclusions();
        var discoveryMs=phaseClock.Elapsed.TotalMilliseconds;
        token.ThrowIfCancellationRequested();
        var errors = discovery.Errors.ToList();
        var files = discovery.Files.Where(f=>!exclusions.Contains(f)).ToArray();
        var skipped=discovery.Skipped+discovery.Files.Count-files.Length;
        var items = new List<MediaItem>();
        var cached = index.ReadAll().ToDictionary(m => System.IO.Path.GetFullPath(m.Path), StringComparer.OrdinalIgnoreCase);
        var fingerprints = index.Fingerprints();
        var changed = new List<string>(); var reused = 0;var reusedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if (cached.TryGetValue(file, out var item) && item.Error == null && fingerprints.TryGetValue(file, out var stamp) && stamp == (info.Length, info.LastWriteTimeUtc.Ticks)) { items.Add(item); reused++;reusedPaths.Add(file); }
                else changed.Add(cached.TryGetValue(file, out var previous) ? previous.Path : file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(System.IO.Path.GetFileName(file) + ": " + ex.Message); }
        }
        foreach (var batch in changed.Chunk(Math.Clamp(BatchSize,40,160)))
        {
            token.ThrowIfCancellationRequested();
            IReadOnlyList<MediaItem>? committed=null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                var args = new[] { "-config", "", "-json", "-n", "-charset", "filename=UTF8", "-GPSLatitude", "-GPSLongitude", "-GPSCoordinates", "-GPSPosition", "-DateTimeOriginal", "-CreateDate", "-MIMEType", "-Error", "-Warning", "-@", "-" };
                // UTF-8 argument streams avoid Windows ANSI command-line conversion in Perl.
                processes++;phaseClock.Restart();
                var output = await ToolProcess.Run(exifTool, args, timeout.Token, "--\n" + string.Join('\n', batch) + "\n");
                metadataMs+=phaseClock.Elapsed.TotalMilliseconds;
                var parsed = ExifParser.Parse(output.Output).ToDictionary(m => System.IO.Path.GetFullPath(m.Path), StringComparer.OrdinalIgnoreCase);
                token.ThrowIfCancellationRequested();
                var records = new List<(MediaItem Item, long Size, long Modified)>();
                foreach (var file in batch)
                {
                    var item = (parsed.GetValueOrDefault(file) ?? new(file, null, null, MediaTypes.Detect(file)!, Error: "No metadata returned")) with { Path = file };
                    if (item.Error != null) errors.Add(file + ": " + item.Error);
                    var info = new FileInfo(file); records.Add((item, info.Length, info.LastWriteTimeUtc.Ticks));
                }
                phaseClock.Restart();index.StoreMany(records);indexMs+=phaseClock.Elapsed.TotalMilliseconds; var excludedNow=index.Exclusions(); committed=records.Select(r=>r.Item).Where(m=>!excludedNow.Contains(MediaInputs.NormalizePath(m.Path))).ToArray(); items.AddRange(committed);
                if (output.ExitCode != 0 && !parsed.Values.Any(x => x.Error != null)) errors.Add(output.Error);
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or JsonException or ArgumentException || ex is OperationCanceledException && !token.IsCancellationRequested)
            {
                foreach (var file in batch) { var item = new MediaItem(file, null, null, MediaTypes.Detect(file)!, Error: ex.Message); index.Upsert(item); items.Add(item); }
                errors.Add(ex.Message);
            }
            progress?.Report(new(files.Length, items.Count, items.Count(x => x.HasLocation), errors.Count, batch[^1], skipped,committed));
        }
        progress?.Report(new(files.Length, items.Count, items.Count(x => x.HasLocation), errors.Count, "", skipped));
        var finalExclusions=index.Exclusions();skipped+=items.RemoveAll(m=>finalExclusions.Contains(MediaInputs.NormalizePath(m.Path)));reused=items.Count(m=>reusedPaths.Contains(m.Path));
        Measurements=new(discoveryMs,metadataMs,indexMs,processes);
        return new(items, errors, reused, items.Count(m => !cached.ContainsKey(m.Path)), skipped, files.Length);
    }
}

public sealed record Preferences(string Language = "en-US", string Theme = "System", string FolderLanguage = "en-US", string FolderHierarchy = "CountryCity")
{
    public static Preferences Load(string path,string? systemCulture=null)
    {
        var defaults=new Preferences(DetectLanguage(systemCulture ?? System.Globalization.CultureInfo.CurrentUICulture.Name));
        if(!File.Exists(path))return defaults;
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(path));var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return defaults;
            string? Value(string key)=>root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
            var language=Value("Language");var theme=Value("Theme");
            return new(language==null?defaults.Language:language=="ru-RU"?"ru-RU":"en-US",theme is "Light" or "Dark"?theme:"System",
                Value("FolderLanguage")=="ru-RU"?"ru-RU":"en-US",Value("FolderHierarchy") is "CountryRegionCity" or "CountryCityDistrict"?Value("FolderHierarchy")!:"CountryCity");
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public static string DetectLanguage(string? culture)=>string.Equals(culture,"ru",StringComparison.OrdinalIgnoreCase) || culture?.StartsWith("ru-",StringComparison.OrdinalIgnoreCase)==true?"ru-RU":"en-US";
    public static bool IsDarkTheme(string theme,bool systemDark)=>theme=="Dark" || theme=="System"&&systemDark;
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
