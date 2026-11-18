// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Media;
using GeoStack.Core;
using Microsoft.Win32;

namespace GeoStack.App;

public partial class App : Application
{
    internal static string UrbanMode {get;private set;}="c";
    internal static bool LegacyGlobalFallbackForVerification { get; private set; }
    private ValidationSession? validationSession;
    public static bool ExperimentalGlobalAtlasEnabled { get; private set; }
    public static string DataDirectory { get; private set; } = "";
    public static string StartupUICulture { get; private set; }="";
    public static Preferences Settings { get; set; } = new();
    private static readonly Lazy<GeographicCatalog> geographicData = new(() => new(DataDirectory));
    private static readonly Lazy<ReverseGeocoder> locationResolver = new(() => new(Path.Combine(AppContext.BaseDirectory, "geography/countries.geojson"), Path.Combine(DataDirectory, "geography/admin.geojson"), () => GeographicData.Datasets));
    public static GeographicCatalog GeographicData => geographicData.Value;
    public static ReverseGeocoder LocationResolver => locationResolver.Value;
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var urbanMode=Array.IndexOf(e.Args,"--urban-mode");
        if(urbanMode>=0 && urbanMode+1<e.Args.Length)UrbanMode=e.Args[urbanMode+1];
        if(UrbanMode is not ("a" or "b" or "c" or "d"))throw new ArgumentException("Unknown urban pilot mode");
        LegacyGlobalFallbackForVerification=e.Args.Contains("--legacy-global-fallback-verification");
        StartupTrace.Mark("OnStartup");
        try
        {
            var dataArg = Array.IndexOf(e.Args, "--data");
            DataDirectory = dataArg >= 0 && dataArg + 1 < e.Args.Length ? Path.GetFullPath(e.Args[dataArg + 1]) : Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(DataDirectory);
            validationSession=ValidationSession.Open(e.Args);
            if(e.Args.Contains("--verify-software-rendering"))
            {
                if(!DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated diagnostic profile required");
                RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            ExperimentalGlobalAtlasEnabled=e.Args.Any(a=>a is "--enable-experimental-global-atlas" or "--verify-global-atlas" or "--verify-global-performance" or "--verify-global-visual");
            StartupUICulture=System.Globalization.CultureInfo.CurrentUICulture.Name;
            Settings = Preferences.Load(SettingsPath,StartupUICulture);
            Strings.SetLanguage(Settings.Language);
            ApplyTheme();
            StartupTrace.Mark("PreferencesAndThemeLoaded");
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            var window = new MainWindow(); MainWindow = window;window.Title=RuntimeIdentity.WindowTitle;
            if(e.Args.Contains("--verify-release-gallery"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyReleaseGallery();Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"gallery-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--prepare-organization055") || e.Args.Contains("--verify-organization055"))window.Dispatcher.BeginInvoke(async()=>{try{await window.PrepareOrganization055(e.Args.Contains("--verify-organization055"));if(e.Args.Contains("--verify-organization055"))Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"organization055-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--prepare-urban-preview") || e.Args.Contains("--verify-urban-lite"))window.Dispatcher.BeginInvoke(async()=>{try{await window.PrepareUrbanPilot(e.Args.Contains("--verify-urban-lite"),e.Args.Contains("--urban-captures"));if(e.Args.Contains("--verify-urban-lite"))Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"urban-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--prepare-basemap-preview") || e.Args.Contains("--verify-compact-basemap"))window.Dispatcher.BeginInvoke(async()=>{try{await window.PrepareCompactBasemapPreview(e.Args.Contains("--verify-compact-basemap"),e.Args.Contains("--basemap-captures"));if(e.Args.Contains("--verify-compact-basemap"))Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"basemap-error.txt"),ex.ToString());Shutdown(1);}});
            if(validationSession!=null)window.Dispatcher.BeginInvoke(async()=>{try{await window.PrepareValidationSession(validationSession);}catch(Exception ex){validationSession.Fail(ex);Shutdown(1);}});
            if(e.Args.Contains("--verify-regional-release"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyRegionalRelease();Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"regional-release-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-owner052-maps"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyOwner052Maps(e.Args.Contains("--acquire-owner052-maps"));Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"owner052-maps-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-owner052"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyOwner052();Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"owner052-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-owner051-context"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyOwner051Context();Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"owner051-context-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-owner051"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyOwner051();Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"owner051-error.txt"),ex.ToString());Shutdown(1);}});
            StartupTrace.Mark("MainWindowConstructed");
            window.Loaded += (_, _) => StartupTrace.Mark("WindowLoaded");
            if(e.Args.Contains("--verify-composition"))
            {
                var modeAt=Array.IndexOf(e.Args,"--composition-mode");var mode=modeAt>=0?e.Args[modeAt+1]:"normal";
                window.PrepareCompositionVerification(mode);
            }
            window.ContentRendered += async (_, _) =>
            {
                StartupTrace.Mark("ContentRendered");
                await Dispatcher.InvokeAsync(() => StartupTrace.Mark("ApplicationIdle"), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                StartupTrace.Save(DataDirectory);
            };
            window.Loaded+=async(_,_)=>{
                try
                {
                    var interrupted=await Task.Run(()=>GeographicCopy.ReadJournal(Path.Combine(DataDirectory,"CopyJournal")).Where(r=>r.Operation==OrganizationOperation.Move).GroupBy(r=>(r.OperationId,r.Source)).Select(g=>g.Last()).Any(r=>r.Phase!="CatalogReconciled" && (r.DestinationVerified || r.Phase!="Result")));
                    if(interrupted && !window.Dispatcher.HasShutdownStarted)window.ViewModel.Status=Strings.Get("MoveRecovery");
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){if(!window.Dispatcher.HasShutdownStarted)window.ViewModel.Status=Strings.Get("MoveRecovery");}
            };
            if(e.Args.Contains("--verify-download05") || e.Args.Contains("--verify-download05-restart")) window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyDownloads05(e.Args.Contains("--verify-download05"));Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"download05-error.txt"),ex.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-large05")) window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyLargeLibrary05();Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"large-library05-error.txt"),ex.ToString());Shutdown(1);}});
            var scanArg = Array.IndexOf(e.Args, "--scan");
            if (scanArg >= 0 && scanArg + 1 < e.Args.Length) window.Loaded += async (_, _) => await window.ScanFolder(e.Args[scanArg + 1]);
            if (e.Args.Contains("--smoke-test"))
            {
                window.Loaded += async (_, _) => {
                    using var process = System.Diagnostics.Process.GetCurrentProcess();
                    File.WriteAllText(Path.Combine(DataDirectory, "startup-metrics.json"), System.Text.Json.JsonSerializer.Serialize(new { ProcessToLoadedMilliseconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds, process.PrivateMemorySize64, process.WorkingSet64, Language = Settings.Language, Theme = Settings.Theme }));
                    await Task.Delay(1500); await window.AtlasReady; File.WriteAllText(Path.Combine(DataDirectory, "startup-ok.txt"), $"WPF window loaded; {window.MapCountryCount} countries; {window.MapCityCount} cities; atlas={window.Atlas.AtlasPilot != null}; " + DateTime.UtcNow.ToString("O")); Shutdown();
                };
            }
            var verifyArg = Array.IndexOf(e.Args, "--verify");
            if (verifyArg >= 0 && verifyArg + 1 < e.Args.Length)
            {
                window.Loaded += async (_, _) =>
                {
                    try { await WorkflowVerification.Run(window, e.Args[verifyArg + 1]); Shutdown(); }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(DataDirectory, "verification-error.txt"), ex.ToString()); Shutdown(1); }
                };
            }
            window.Show();
            var atlasPilotArg=Array.IndexOf(e.Args,"--atlas-pilot");
            var atlasFolder=atlasPilotArg>=0 && atlasPilotArg+1<e.Args.Length ? e.Args[atlasPilotArg+1] : Path.Combine(AppContext.BaseDirectory,"atlas");
            if(atlasPilotArg<0 && !e.Args.Contains("--legacy-raster"))
                window.AtlasReady=InitializeDefaultAtlas(window,atlasFolder);
            var globalArg=Array.IndexOf(e.Args,"--verify-global-atlas");
            var globalPerformanceArg=Array.IndexOf(e.Args,"--verify-global-performance");
            var globalVisualArg=Array.IndexOf(e.Args,"--verify-global-visual");
            if(globalVisualArg>=0 && globalVisualArg+2<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyGlobalVisual(e.Args[globalVisualArg+1],e.Args[globalVisualArg+2]);Shutdown();}catch(Exception error){File.WriteAllText(Path.Combine(DataDirectory,"global-visual-error.txt"),error.ToString());Shutdown(1);}});
            if(globalPerformanceArg>=0 && globalPerformanceArg+1<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyGlobalPerformance(e.Args[globalPerformanceArg+1],e.Args);Shutdown();}catch(Exception error){File.WriteAllText(Path.Combine(DataDirectory,"global-performance-error.txt"),error.ToString());Shutdown(1);}});
            if(globalArg>=0 && globalArg+1<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyGlobalAtlas(e.Args[globalArg+1],e.Args.Contains("--global-restart"));Shutdown();}catch(Exception error){File.WriteAllText(Path.Combine(DataDirectory,"global-atlas-error.txt"),error.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-library-removal") || e.Args.Contains("--verify-library-removal-restart"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyLibraryRemoval(e.Args.Contains("--verify-library-removal-restart"));Shutdown();}catch(Exception error){File.WriteAllText(Path.Combine(DataDirectory,"removal-error.txt"),error.ToString());Shutdown(1);}});
            if(e.Args.Contains("--verify-composition"))
            {
                var modeAt=Array.IndexOf(e.Args,"--composition-mode");var mode=modeAt>=0?e.Args[modeAt+1]:"normal";
                window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyComposition(mode);Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"composition-error.txt"),ex.ToString());Shutdown(1);}});
            }
            if(atlasPilotArg>=0 && atlasPilotArg+1<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{if(e.Args.Contains("--verify-atlas-performance"))_=new AtlasProfiling(window.Dispatcher);await window.Atlas.EnableAtlasPilotAsync(e.Args[atlasPilotArg+1]);window.Atlas.AtlasPilot!.UseOptimizedRendering=!e.Args.Contains("--atlas-baseline-rendering");window.Title="GeoStack · "+Strings.Get("AtlasPilot");window.Atlas.InvalidateVisual();if(e.Args.Contains("--verify-atlas-performance")){await window.VerifyAtlasPerformance();Shutdown();}else if(e.Args.Contains("--verify-atlas-pilot")){await window.VerifyAtlasPilot();Shutdown();}}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"atlas-pilot-error.txt"),ex.ToString());Shutdown(1);}});
            var dragArg=Array.IndexOf(e.Args,"--verify-drag-continuity");
            if(e.Args.Contains("--verify-atlas-gate"))window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyAtlasGate(e.Args);Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"atlas-gate-error.txt"),ex.ToString());Shutdown(1);}});
            if(dragArg>=0 && dragArg+1<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyDragContinuity(e.Args[dragArg+1]);Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"drag-error.txt"),ex.ToString());Shutdown(1);}});
            var finalRenderArg=Array.IndexOf(e.Args,"--verify-render-final");
            if(finalRenderArg>=0 && finalRenderArg+1<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyFinalRendering(e.Args[finalRenderArg+1]);Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(DataDirectory,"final-render-error.txt"),ex.ToString());Shutdown(1);}});
            var p0Arg=Array.IndexOf(e.Args,"--verify-map-p0");
            if(p0Arg>=0 && p0Arg+1<e.Args.Length) window.Dispatcher.BeginInvoke(async () => { try { await window.VerifyMapP0(e.Args[p0Arg+1],e.Args.Contains("--p0-baseline"));Shutdown(); } catch(Exception ex) { File.WriteAllText(Path.Combine(DataDirectory,"p0-error.txt"),ex.ToString());Shutdown(1); } });
            var browsingArg = Array.IndexOf(e.Args,"--verify-browsing");
            var browsingRestart = Array.IndexOf(e.Args,"--verify-browsing-restart");
            var browsingIndex = browsingArg >= 0 ? browsingArg : browsingRestart;
            if (browsingIndex >= 0 && browsingIndex+1 < e.Args.Length)
                window.Dispatcher.BeginInvoke(async () => { try { await window.VerifyBrowsing(e.Args[browsingIndex+1],browsingRestart>=0);Shutdown(); } catch(Exception ex) { File.WriteAllText(Path.Combine(DataDirectory,"browsing-error.txt"),ex.ToString());Shutdown(1); } });
            var importArg=Array.IndexOf(e.Args,"--verify-import");
            if(importArg>=0 && importArg+1<e.Args.Length)window.Dispatcher.BeginInvoke(async()=>{try{await window.VerifyImport(Path.GetFullPath(e.Args[importArg+1]),e.Args.Contains("--import-restart"));Shutdown();}catch(Exception error){File.WriteAllText(Path.Combine(DataDirectory,"import-verification-error.txt"),error.ToString());Shutdown(1);}});
            var recoveryArg = Array.IndexOf(e.Args, "--verify-map-recovery");
            var recoveryRestart = Array.IndexOf(e.Args, "--verify-map-recovery-restart");
            var recoveryIndex = recoveryArg >= 0 ? recoveryArg : recoveryRestart;
            if (recoveryIndex >= 0 && recoveryIndex + 1 < e.Args.Length)
                window.Dispatcher.BeginInvoke(async () => { try { await window.VerifyMapRecovery(Path.GetFullPath(e.Args[recoveryIndex + 1]), recoveryRestart >= 0); Shutdown(); } catch (Exception ex) { File.WriteAllText(Path.Combine(DataDirectory, "map-recovery-error.txt"), ex.ToString()); Shutdown(1); } });
            if (e.Args.Contains("--verify-settings"))
                window.Dispatcher.BeginInvoke(async () => { try { await window.VerifySettingsNavigation(); Shutdown(); } catch (Exception ex) { File.WriteAllText(Path.Combine(DataDirectory, "settings-verification-error.txt"), ex.ToString()); Shutdown(1); } });
            var featuresArg = Array.IndexOf(e.Args, "--verify-features");
            if (featuresArg >= 0 && featuresArg + 1 < e.Args.Length)
                window.Dispatcher.BeginInvoke(async () => { try { await FeatureVerification.Run(window, e.Args[featuresArg + 1]); Shutdown(); } catch (Exception ex) { File.WriteAllText(Path.Combine(DataDirectory,"feature-verification-error.txt"),ex.ToString()); Shutdown(1); } });
            var geographicArg = Array.IndexOf(e.Args, "--verify-geography");
            var restartArg = Array.IndexOf(e.Args, "--verify-geographic-restart");
            if (restartArg >= 0 && restartArg + 1 < e.Args.Length)
                window.Dispatcher.BeginInvoke(async () => { try { await window.VerifyGeographicRestart(e.Args[restartArg + 1]); Shutdown(); } catch (Exception ex) { File.WriteAllText(Path.Combine(DataDirectory,"geographic-restart-error.txt"),ex.ToString()); Shutdown(1); } });
            if (geographicArg >= 0 && geographicArg + 1 < e.Args.Length)
                window.Dispatcher.BeginInvoke(async () => {
                    try
                    {
                        var mediaArg = Array.IndexOf(e.Args, "--verify-media"); var mapArg = Array.IndexOf(e.Args, "--verify-map");
                        if (mediaArg < 0 || mediaArg + 1 >= e.Args.Length) throw new ArgumentException("Controlled fixture directory required");
                        await GeographicVerification.Run(window, e.Args[geographicArg + 1], e.Args[mediaArg + 1], mapArg >= 0 && mapArg + 1 < e.Args.Length ? e.Args[mapArg + 1] : null); Shutdown();
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(DataDirectory,"geographic-verification-error.txt"),ex.ToString()); Shutdown(1); }
                });
        }
        catch (Exception ex)
        {
            validationSession?.Fail(ex);
            if(validationSession==null)ValidationSession.FailStartup(e.Args,ex);
            if(e.Args.Contains("--verify-composition"))File.WriteAllText(Path.Combine(DataDirectory,"composition-startup-error.txt"),ex.ToString());
            try { if (!string.IsNullOrEmpty(DataDirectory)) File.WriteAllText(Path.Combine(DataDirectory, "startup-error.txt"), ex.GetType().Name + ": startup failed"); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
            if (!e.Args.Contains("--smoke-test") && !e.Args.Contains("--verify") && !e.Args.Contains("--validation-input")) MessageBox.Show(Strings.Get("StartupError"), "GeoStack"); Shutdown(1);
        }
    }
    private static async Task InitializeDefaultAtlas(MainWindow window,string folder)
    {
        try { await window.Atlas.EnableAtlasPilotAsync(folder); window.Atlas.InvalidateVisual(); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        { window.ViewModel.Status=Strings.Get("AtlasFallback"); }
    }
    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { if (Settings.Theme == "System") Dispatcher.Invoke(ApplyTheme); }
    public static void ApplyTheme()
    {
        var dark = Preferences.IsDarkTheme(Settings.Theme, (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0);
        foreach (var (key, value) in new[] { ("Surface", dark ? "#202C35" : "#FFFFFF"), ("Background", dark ? "#14212A" : "#F3F5F7"), ("Text", dark ? "#E6F0F3" : "#172B3A"), ("Muted", dark ? "#9BAEB8" : "#637482"), ("Line", dark ? "#364852" : "#DFE6EA") }) Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        foreach (Window window in Current.Windows) { WindowTheme.Apply(window, dark); if (window is MainWindow main) main.RefreshTheme(); }
    }
    protected override void OnExit(ExitEventArgs e) { SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;validationSession?.Dispose(); base.OnExit(e); }
}
