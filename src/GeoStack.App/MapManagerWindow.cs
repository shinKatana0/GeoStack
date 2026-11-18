// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using GeoStack.Core;
using Microsoft.Win32;

namespace GeoStack.App;

public partial class MainWindow
{
    private async void Maps_Click(object sender, RoutedEventArgs e) { new MapManagerWindow(this, Atlas).ShowDialog(); await RebuildPlaces(); }
}

public sealed class MapManagerWindow : Window
{
    private readonly AtlasControl atlas;
    private readonly DataGrid entries = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 100 };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4, 10, 8, 6) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4, 4, 4, 4) };
    private readonly WrapPanel actions = new();
    private readonly Button activate, toggle, remove, coverage;
    private MapCatalog Catalog => atlas.Tiles.Catalog;
    internal int InstalledCount => Catalog.Packages.Count;
    internal MapPackage? SelectedPackage => (entries.SelectedItem as MapRow)?.Package;
    internal string StatusText => status.Text;
    public static string LocalMapDirectory
    {
        get
        {
            var prepared = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../offline-maps"));
            return Directory.Exists(prepared) ? prepared : Path.Combine(App.DataDirectory, "Maps");
        }
    }
    public MapManagerWindow(Window owner, AtlasControl atlas)
    {
        this.atlas = atlas; Owner = owner; Icon = owner.Icon; Title = Strings.Get("MapManager"); Width = 1000; Height = 700;
        SetResourceReference(StyleProperty, typeof(Window)); WindowLayout.Attach(this, "MapManager");
        SourceInitialized += (_, _) => App.ApplyTheme();
        var root = new DockPanel { Margin = new(16) }; Content = root;
        var hint = DialogLayout.Text("VisualMapsHint"); DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var close = DialogLayout.Button("Close", Close); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var actionScroll = new ScrollViewer { Content = actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 112 };
        DockPanel.SetDock(actionScroll, Dock.Bottom); root.Children.Add(actionScroll);
        var body = new StackPanel(); var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll); body.Children.Add(entries); body.Children.Add(details);
        foreach (var (key, property, width) in new[] { ("Name", "Name", 2d), ("Zoom", "Zoom", 1d), ("Bytes", "Size", 1d), ("Status", "State", 1.5d) })
        {
            var text = new Style(typeof(TextBlock), (Style)FindResource(typeof(TextBlock)));
            text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)); text.Setters.Add(new Setter(ToolTipProperty, new Binding(property)));
            entries.Columns.Add(new DataGridTextColumn { Header = Strings.Get(key), Binding = new Binding(property), Width = new(width, DataGridLengthUnitType.Star), MinWidth = property == "Name" ? 170 : 95, ElementStyle = text });
        }
        actions.Children.Add(DialogLayout.Button("ImportMap", async () => {
            var picker = new OpenFileDialog { Filter = Strings.Get("RasterMapFilter"), InitialDirectory = LocalMapDirectory, CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            actions.IsEnabled = false;
            try { await ImportPackageAsync(picker.FileName); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException) { status.Text = Strings.Get("MapImportFailed") + ": " + Strings.Get(ex.Message); }
            finally { actions.IsEnabled = true; }
        }));
        actions.Children.Add(DialogLayout.Button("DownloadLibraryMaps", () => { if(owner is MainWindow main) new MapDownloadsWindow(main,this).ShowDialog(); Refresh(); }));
        var globalButton=DialogLayout.Button("GlobalAtlas",()=>{if(Owner is MainWindow main)main.OpenGlobalAtlasManager(this);});
        globalButton.IsEnabled=atlas.AtlasPilot!=null;if(App.ExperimentalGlobalAtlasEnabled)actions.Children.Add(globalButton);
        activate = DialogLayout.Button("ActivateMap", () => ActivateSelected()); actions.Children.Add(activate);
        coverage = DialogLayout.Button("ShowMapCoverage", () => { if (SelectedPackage is { } map) atlas.ShowMap(map, true); }); actions.Children.Add(coverage);
        actions.Children.Add(DialogLayout.Button("ReturnWorldMap", () => ActivateWorld()));
        toggle = DialogLayout.Button("ToggleMap", ToggleSelected); actions.Children.Add(toggle);
        remove = DialogLayout.Button("RemoveReference", () => RemoveSelected()); actions.Children.Add(remove);
        actions.Children.Add(DialogLayout.Button("OpenMapStorage", () => {
            var directory = SelectedPackage is { Available: true } map ? Path.GetDirectoryName(map.Path)! : LocalMapDirectory;
            try { Directory.CreateDirectory(directory); Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { status.Text = Strings.Get("OpenDocumentationFailed"); }
        }));
        actions.Children.Add(DialogLayout.Button("RefreshMap", async () => {
            if (SelectedPackage is not { } map) return;
            var picker = new OpenFileDialog { Filter = Strings.Get("RasterMapFilter"), CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            try { await ImportPackageAsync(picker.FileName, map.Id); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException) { status.Text = Strings.Get("MapImportFailed") + ": " + Strings.Get(ex.Message); }
        }));
        actions.Children.Add(DialogLayout.Button("GeographicData", () => GeographicDataWindow.Show(this)));
        entries.SelectionChanged += (_, _) => UpdateDetails();
        entries.MouseDoubleClick += (_, _) => ActivateSelected();
        SizeChanged += (_, _) => { entries.Height = Math.Clamp(ActualHeight * .32, 100, 260); Dispatcher.BeginInvoke(() => entries.ScrollIntoView(entries.SelectedItem), DispatcherPriority.Loaded); };
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => UpdateStatus(), Dispatcher);
        Closed += (_, _) => timer.Stop(); Refresh();
    }
    internal async Task<MapPackage> ImportPackageAsync(string path, string? replaceId = null)
    {
        var map = await Task.Run(() => { var inspected = MapPackage.Inspect(path); using var probe = new MapProbe(path); probe.Verify(); return inspected; });
        var existing = Catalog.Packages.FirstOrDefault(p => p.Id == replaceId || p.Path.Equals(map.Path, StringComparison.OrdinalIgnoreCase));
        if (existing == null) Catalog.Packages.Add(map);
        else { map = map with { Id = existing.Id }; Catalog.Packages[Catalog.Packages.IndexOf(existing)] = map; }
        Catalog.Preferred = map.Id; Changed(map.Id); atlas.ShowMap(map); return map;
    }
    internal void SelectMap(string id) => entries.SelectedItem = entries.Items.Cast<MapRow>().First(r => r.Package?.Id == id);
    internal void ActivateSelected(bool navigate = true)
    {
        if (SelectedPackage is not { Available: true } map) return;
        var enabled = map with { Enabled = true }; Catalog.Packages[Catalog.Packages.IndexOf(map)] = enabled; Catalog.Preferred = map.Id; Changed(map.Id);
        if (navigate) atlas.ShowMap(enabled);
    }
    internal void ActivateWorld(bool navigate = true) { Catalog.Preferred = "world"; Changed(null); if (navigate) atlas.Fit(); }
    internal void ToggleSelected() { if (SelectedPackage is { } map) { Catalog.Packages[Catalog.Packages.IndexOf(map)] = map with { Enabled = !map.Enabled }; Changed(map.Id); } }
    internal void RemoveSelected()
    {
        if (SelectedPackage is not { } map) return;
        Catalog.Packages.Remove(map); if (Catalog.Preferred == map.Id) Catalog.Preferred = "world"; Changed(null);
    }
    private void Changed(string? id) { Catalog.Save(); atlas.Tiles.Clear(); atlas.InvalidateVisual(); Refresh(id); }
    private void Refresh(string? selectedId = null)
    {
        entries.ItemsSource = new[] { new MapRow(null, Catalog.Preferred) }.Concat(Catalog.Packages.Select(p => new MapRow(p, Catalog.Preferred))).ToArray();
        entries.SelectedItem = entries.Items.Cast<MapRow>().FirstOrDefault(r => r.Package?.Id == (selectedId ?? Catalog.Preferred)) ?? entries.Items[0]; UpdateDetails();
        Dispatcher.BeginInvoke(() => entries.ScrollIntoView(entries.SelectedItem), DispatcherPriority.Loaded);
    }
    private void UpdateDetails()
    {
        var row = entries.SelectedItem as MapRow;
        details.Text = row == null ? "" : string.Join(Environment.NewLine, new[] { ("Name", row.Name), ("Coverage", row.Coverage), ("Format", row.Format), ("Zoom", row.Zoom), ("Source", row.Source), ("Attribution", row.Attribution), ("Storage", row.Path) }.Select(v => Strings.Get(v.Item1) + ": " + v.Item2));
        activate.IsEnabled = coverage.IsEnabled = SelectedPackage?.Available == true;
        toggle.IsEnabled = remove.IsEnabled = SelectedPackage != null; UpdateStatus();
    }
    private void UpdateStatus()
    {
        if (Catalog.Preferred == "world") { status.Text = Strings.Get("BuiltInWorld") + " | " + (Catalog.Packages.Count == 0 ? Strings.Get("NoRegionalMaps") : Strings.Get("VisualMapsHint")); return; }
        var map = Catalog.Packages.FirstOrDefault(p => p.Id == Catalog.Preferred);
        var point = atlas.CameraLocation;
        var state = map == null || !map.Available ? "MapFileUnavailable" : !map.Enabled ? "Disabled" : atlas.TileZoom < map.MinZoom || atlas.TileZoom > map.MaxZoom ? "MapZoomOutside" : !map.Covers(atlas.TileZoom, point.Latitude, point.Longitude) ? "MapOutsideCoverage" : atlas.ActiveTiles.RenderedMapIds.Contains(map.Id) ? "MapTilesVisible" : atlas.ActiveTiles.IsLoading ? "MapTilesLoading" : "MapNoTilesHere";
        status.Text = (map?.Name ?? Strings.Get("BuiltInWorld")) + " | " + Strings.Get(state) + " | " + Strings.Get("InstalledStorage") + ": " + Catalog.Packages.Sum(p => p.InstalledSize).ToString("N0") + " " + Strings.Get("Bytes");
    }
}

public sealed class MapRow(MapPackage? package, string preferred = "world")
{
    public MapPackage? Package => package;
    public string Name => package?.Name ?? Strings.Get("BuiltInWorld");
    public string Coverage => package == null ? Strings.Get("WorldCoverage") : FormattableString.Invariant($"{package.West:F4}, {package.South:F4} - {package.East:F4}, {package.North:F4}");
    public long Size => package?.InstalledSize ?? 0;
    public string Format => package == null ? "GeoJSON" : "MBTiles / " + package.Encoding;
    public string Zoom => package == null ? "-" : $"{package.MinZoom}-{package.MaxZoom}";
    public string Source => package?.Source ?? "Natural Earth";
    public string Attribution => package?.Attribution ?? "Natural Earth (public domain)";
    public string Path => package?.Path ?? System.IO.Path.Combine(AppContext.BaseDirectory, "geography");
    public string Enabled => Strings.Get(package?.Enabled == false ? "Disabled" : "Enabled");
    public string Available => Strings.Get(package?.Available == false ? "Unavailable" : "Available");
    public string State => Strings.Get(package?.Available == false ? "Unavailable" : package?.Enabled == false ? "Disabled" : (package?.Id ?? "world") == preferred ? "PreferredMap" : "Enabled");
}

internal sealed class MapProbe(string path) : IDisposable
{
    internal static bool CanDecode(byte[] tile)
    {
        try{var frame=RasterTileDecoder.Decode(tile);var stride=(frame.PixelWidth*frame.Format.BitsPerPixel+7)/8;frame.CopyPixels(new byte[frame.PixelHeight*stride],stride,0);return true;}
        catch(Exception ex) when(ex is IOException or InvalidDataException or FormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){return false;}
    }
    public void Verify()
    {
        var tile = MapPackage.Sample(path); var frame = RasterTileDecoder.Decode(tile);
        var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8; frame.CopyPixels(new byte[frame.PixelHeight * stride], stride, 0);
    }
    public void Dispose() { }
}
