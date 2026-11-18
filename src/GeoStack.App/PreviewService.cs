// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.Core;

namespace GeoStack.App;

public sealed class PreviewService(string? dataDirectory = null)
{
    private readonly SemaphoreSlim slots = new(2);
    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> order = new();
    private readonly string cache = Path.Combine(dataDirectory ?? App.DataDirectory, "cache");
    private int generated;
    public const int MemoryEntryLimit = 96;
    public const long DiskBudget = 256L * 1024 * 1024;
    public int MemoryEntries { get { lock (memory) return memory.Count; } }
    public static string Tool(string folder, string file)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", folder, file);
        if (File.Exists(bundled)) return bundled;
        var development = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tools", folder, file));
        return File.Exists(development) ? development : bundled;
    }
    public async Task<ImageSource?> GetAsync(string path, bool thumbnail = false)
    {
        string key;
        try
        {
            var info = new FileInfo(path);
            key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v2|" + info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + (thumbnail ? 256 : 1000))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        lock (memory) if (memory.TryGetValue(key, out var image)) return image;
        var lazy = pending.GetOrAdd(key, _ => new(() => Generate(path, key, thumbnail ? 256 : 1000)));
        try
        {
            var image = await lazy.Value;
            if (image != null) lock (memory)
            {
                if (!memory.ContainsKey(key)) { memory[key] = image; order.Enqueue(key); }
                while (order.Count > MemoryEntryLimit) memory.Remove(order.Dequeue());
            }
            return image;
        }
        finally { pending.TryRemove(key, out _); }
    }
    private async Task<ImageSource?> Generate(string path, string key, int width)
    {
        await slots.WaitAsync();
        var temporary = Path.Combine(cache, key + "." + Guid.NewGuid().ToString("N") + ".tmp.jpg");
        try
        {
            return await Task.Run(async () => {
                Directory.CreateDirectory(cache);
                var target = Path.Combine(cache, key + ".jpg");
                if (File.Exists(target))
                {
                    try { return (ImageSource?)Load(target, width); }
                    catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException) { File.Delete(target); }
                }
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var raw = MediaTypes.Detect(path) == "Photo" && extension is not (".jpg" or ".jpeg" or ".png" or ".heic" or ".heif" or ".webp" or ".tif" or ".tiff");
                if (raw)
                {
                    using var extractionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    foreach (var tag in new[] { "PreviewImage", "JpgFromRaw", "ThumbnailImage" })
                    {
                        var extracted = await ToolProcess.ExtractBinary(Tool("ExifTool", "ExifTool.exe"), ["-config", "", "-b", "-" + tag, "-charset", "filename=UTF8", "-@", "-"], temporary, extractionTimeout.Token, "--\n" + path + "\n");
                        if (extracted) break;
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
                // HEIC always uses the bundled decoder, independent of optional Windows extensions.
                if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp")
                {
                    try
                    {
                        var source = Load(path, width, extension is ".jpg" or ".jpeg");
                        var encoder = new JpegBitmapEncoder { QualityLevel = 88 }; encoder.Frames.Add(BitmapFrame.Create(source, null, null, null));
                        using var stream = File.Create(temporary); encoder.Save(stream);
                    }
                    catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException) { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                if (!File.Exists(temporary))
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    var scale = $"scale={width}:{width}:force_original_aspect_ratio=decrease";
                    var result = await ToolProcess.Run(Tool("FFmpeg", "ffmpeg.exe"), ["-nostdin", "-v", "error", "-protocol_whitelist", "file,pipe", "-i", path, "-frames:v", "1", "-vf", scale, "-q:v", "3", "-y", temporary], timeout.Token);
                    if (result.ExitCode != 0 || !File.Exists(temporary)) return null;
                }
                var decoded = Load(temporary, width);
                File.Move(temporary, target, true);
                if (Interlocked.Increment(ref generated) % 16 == 0) PruneDisk();
                return (ImageSource?)decoded;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or OperationCanceledException or ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException) { return null; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            slots.Release();
        }
    }
    private void PruneDisk()
    {
        var files = new DirectoryInfo(cache).EnumerateFiles("*.jpg").Where(f => f.Name.Length == 68 && f.Name[..64].All(Uri.IsHexDigit)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long total = 0;
        foreach (var file in files) { total += file.Length; if (total > DiskBudget) try { file.Delete(); } catch (IOException) { } }
    }
    private static BitmapSource Load(string path, int width, bool orient = false)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze();
        BitmapSource result = image;
        if (orient)
        {
            stream.Position = 0;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            var orientation = (decoder.Frames[0].Metadata as BitmapMetadata)?.GetQuery("/app1/ifd/{ushort=274}");
            var value = int.TryParse(Convert.ToString(orientation, System.Globalization.CultureInfo.InvariantCulture), out var parsed) ? parsed : 1;
            var matrix = value switch { 2 => new Matrix(-1,0,0,1,0,0), 3 => new Matrix(-1,0,0,-1,0,0), 4 => new Matrix(1,0,0,-1,0,0), 5 => new Matrix(0,1,1,0,0,0), 6 => new Matrix(0,1,-1,0,0,0), 7 => new Matrix(0,-1,-1,0,0,0), 8 => new Matrix(0,-1,1,0,0,0), _ => Matrix.Identity };
            if (!matrix.IsIdentity) { result = new TransformedBitmap(result, new MatrixTransform(matrix)); result.Freeze(); }
        }
        var factor = width / (double)Math.Max(result.PixelWidth, result.PixelHeight);
        if (factor < 1) { result = new TransformedBitmap(result, new ScaleTransform(factor, factor)); result.Freeze(); }
        return result;
    }
}
