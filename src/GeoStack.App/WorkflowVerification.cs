// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

// Explicit developer verification mode. Only pass an authorized synthetic fixture directory.
internal static class WorkflowVerification
{
    public static async Task Run(MainWindow window, string fixtureDirectory)
    {
        EnvironmentVerification.Save();
        using var process = Process.GetCurrentProcess();
        var startupMilliseconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        var idlePrivateBytes = process.PrivateMemorySize64;
        static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var source = Directory.GetFiles(fixtureDirectory, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash);
        var vm = window.ViewModel;
        var heartbeat = Stopwatch.StartNew(); double lastTick = 0, maxGap = 0; int ticks = 0;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => { var now = heartbeat.Elapsed.TotalMilliseconds; maxGap = Math.Max(maxGap, now - lastTick); lastTick = now; ticks++; }, window.Dispatcher);
        var scanClock = Stopwatch.StartNew(); await vm.ScanAsync(fixtureDirectory); scanClock.Stop(); timer.Stop();
        var located = vm.Items.Where(x => x.HasLocation).ToArray();
        if (vm.Items.Count == 0 || located.Length == 0) throw new InvalidOperationException("Scan returned no located media: " + vm.Status);
        var previews = new List<object>();
        var previewClock = Stopwatch.StartNew();
        Dictionary<string, bool>? expected = null;
        var expectationPath = Path.Combine(fixtureDirectory, "expected.json");
        if (File.Exists(expectationPath)) expected = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(expectationPath));
        foreach (var item in vm.Items)
        {
            var image = await vm.Previews.GetAsync(item.Path);
            var thumbnail = await vm.Previews.GetAsync(item.Path, true);
            var displayed = image != null && await window.VerifySelectedPreview(item);
            if (expected?.TryGetValue(item.Name, out var shouldDecode) == true && (shouldDecode != (image != null) || shouldDecode && (thumbnail == null || !displayed))) throw new InvalidOperationException("Packaged decoder verification failed: " + item.Name);
            previews.Add(new { item.Name, Decoded = image != null, Thumbnail = thumbnail != null, PreviewDisplayed = displayed, Width = image?.Width, Height = image?.Height, item.HasLocation, item.Latitude, item.Longitude, item.Error });
        }
        previewClock.Stop();
        using (var rescanIndex = new MediaIndex(Path.Combine(App.DataDirectory, "index.db")))
        {
            var rescanClock = Stopwatch.StartNew();
            var rescan = await Task.Run(() => new ScanService(PreviewService.Tool("ExifTool", "ExifTool.exe")).ScanAsync(fixtureDirectory, rescanIndex, null, CancellationToken.None));
            File.WriteAllText(Path.Combine(App.DataDirectory, "rescan.json"), JsonSerializer.Serialize(new { rescan.Reused, Milliseconds = rescanClock.Elapsed.TotalMilliseconds }));
        }
        var unchanged = source.All(p => p.Value == Hash(p.Key));
        if (!unchanged) throw new InvalidOperationException("Source fixtures changed during verification");
        using var index = new MediaIndex(Path.Combine(App.DataDirectory, "index.db"));
        var saved = index.ReadAll();
        var preferences = App.Settings;
        Strings.SetLanguage("ru-RU"); var russian = Strings.Get("OpenFolder");
        Strings.SetLanguage("en-US"); var english = Strings.Get("OpenFolder");
        if (russian == english || string.IsNullOrWhiteSpace(russian) || english != "Open folder") throw new InvalidOperationException("Runtime localization verification failed");
        var themes = new Dictionary<string, string>();
        foreach (var theme in new[] { "System", "Light", "Dark" })
        {
            App.Settings = preferences with { Theme = theme }; App.ApplyTheme(); themes[theme] = ((SolidColorBrush)System.Windows.Application.Current.Resources["Background"]).Color.ToString();
        }
        App.Settings = preferences; Strings.SetLanguage(preferences.Language); App.ApplyTheme();
        await vm.ShowItems(located.Take(3), Strings.Get("Library"));
        if (vm.VisibleItems.Count != Math.Min(3, located.Length)) throw new InvalidOperationException("Sidebar contents did not update");
        var points = Enumerable.Range(0,10000).Select(i => new MediaItem(i.ToString("D5"), -70 + i % 140, Geo.WrapLongitude(i * 2.39996), "Photo")).ToArray();
        var clusterTimes = new List<double>();
        for (var run = 0; run < 7; run++) { var clock = Stopwatch.StartNew(); Geo.Cluster(points, 16000); clusterTimes.Add(clock.Elapsed.TotalMilliseconds); }
        process.Refresh();
        var report = new { Status = "PASS", Countries = window.MapCountryCount, Cities = window.MapCityCount, Indexed = saved.Count, Located = located.Length, SourceUnchanged = unchanged, RuntimeEnglish = english, RuntimeRussian = russian, ThemeBackgrounds = themes, SidebarItems = vm.VisibleItems.Count, ClustersAtWorld = Geo.Cluster(located, 1000).Count, ClustersAtDetail = Geo.Cluster(located, 1000000).Count, ScanErrors = vm.Errors, Previews = previews, Performance = new { StartupToLoadedMilliseconds = startupMilliseconds, IdlePrivateBytes = idlePrivateBytes, ScanMilliseconds = scanClock.Elapsed.TotalMilliseconds, DispatcherTicksDuringScan = ticks, MaximumDispatcherGapMilliseconds = maxGap, PreviewMilliseconds = previewClock.Elapsed.TotalMilliseconds, Cluster10000Milliseconds = clusterTimes, PreviewMemoryEntries = vm.Previews.MemoryEntries, PrivateBytesAfter = process.PrivateMemorySize64, CacheBytes = Directory.EnumerateFiles(Path.Combine(App.DataDirectory,"cache")).Sum(p=>new FileInfo(p).Length), IndexBytes = new FileInfo(Path.Combine(App.DataDirectory,"index.db")).Length } };
        File.WriteAllText(Path.Combine(App.DataDirectory, "verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
