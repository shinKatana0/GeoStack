// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GeoStack.Core;
using Microsoft.Win32;

namespace GeoStack.App;

public sealed class GeographicWarningConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is IEnumerable<string> warnings ? string.Join(" · ", warnings.Select(Strings.Get)) : "";
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public static class GeographicDataWindow
{
    public static void Show(Window owner)
    {
        var window = Create(owner); window.ShowDialog();
    }
    internal static Window Create(Window owner)
    {
        var window = DialogLayout.Window(owner, "GeographicData", 1180, 680, out var panel);
        var explanation = DialogLayout.Text("GeoManagerHint"); DockPanel.SetDock(explanation, Dock.Top); panel.Children.Add(explanation);
        var close = DialogLayout.Button("Close", () => window.Close()); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var actions = new WrapPanel(); var actionScroll = new ScrollViewer { Content = actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 105 }; DockPanel.SetDock(actionScroll, Dock.Bottom); panel.Children.Add(actionScroll);
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(6, 10, 6, 8) };
        var body = new StackPanel(); var bodyScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
        grid.SelectionChanged += (_, _) => {
            details.Text = grid.SelectedItem is GeographicDatasetRow row ? string.Join(Environment.NewLine, new[] {
                ("Name", row.Name), ("Coverage", row.Coverage), ("GeoLevels", row.Levels), ("Source", row.Source),
                ("GeoVersion", row.Version), ("Bytes", row.Size.ToString("N0", CultureInfo.CurrentCulture)),
                ("GeoLicense", row.License), ("Attribution", row.Attribution), ("Storage", row.Path),
                ("Enabled", row.Enabled), ("GeoValidation", row.Validation)
            }.Select(field => Strings.Get(field.Item1) + ": " + field.Item2)) : "";
        };
        window.SizeChanged += (_, _) => grid.Height = Math.Clamp(window.ActualHeight * .3, 100, 250);
        var catalog = App.GeographicData;
        foreach (var (key, property) in new[] { ("Name", "Name"), ("Coverage", "Coverage"), ("GeoLevels", "Levels"), ("Source", "Source"), ("GeoVersion", "Version"), ("Bytes", "Size"), ("GeoLicense", "License"), ("Attribution", "Attribution"), ("Storage", "Path"), ("Enabled", "Enabled"), ("GeoValidation", "Validation") })
        {
            var text = new Style(typeof(TextBlock), (Style)window.FindResource(typeof(TextBlock)));
            text.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
            text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            grid.Columns.Add(new DataGridTextColumn { Header = Strings.Get(key), Binding = new Binding(property), ElementStyle = text, Width = new(1, DataGridLengthUnitType.Star), MinWidth = property == "Name" ? 170 : 120 });
        }
        void Refresh()
        {
            grid.ItemsSource = new[] { new GeographicDatasetRow(null) }.Concat(catalog.Datasets.Select(d => new GeographicDatasetRow(d))).ToArray();
            grid.SelectedIndex = catalog.Datasets.Count > 0 ? 1 : 0;
            status.Text = Strings.Get(catalog.LoadStatus) + $" · {catalog.Datasets.Count} · {catalog.Datasets.Where(d => d.Available).Sum(d => d.Size):N0} {Strings.Get("Bytes")}";
        }
        void Change(Action mutation)
        {
            try { mutation(); catalog.Save(); Refresh(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { status.Text = Strings.Get("GeoOperationFailed") + ": " + ex.Message; }
        }
        actions.Children.Add(DialogLayout.Button("GeoImport", async () => {
            var picker = new OpenFileDialog { Filter = Strings.Get("GeoPackageFilter"), CheckFileExists = true };
            if (picker.ShowDialog(window) != true) return;
            actions.IsEnabled = false;
            try { var dataset = await Task.Run(() => GeographicPackage.Inspect(picker.FileName)); if (catalog.Datasets.Count >= 32 && !catalog.Datasets.Any(d => d.Manifest.Id == dataset.Manifest.Id)) throw new InvalidDataException("GeoPackageLimit"); catalog.Import(dataset); Refresh(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { status.Text = Strings.Get("GeoOperationFailed") + ": " + Strings.Get(ex.Message); }
            finally { actions.IsEnabled = true; }
        }));
        actions.Children.Add(DialogLayout.Button("ToggleMap", () => { if (grid.SelectedItem is GeographicDatasetRow { Dataset: { } d }) Change(() => catalog.Datasets[catalog.Datasets.IndexOf(d)] = d with { Enabled = !d.Enabled }); }));
        actions.Children.Add(DialogLayout.Button("RemoveReference", () => { if (grid.SelectedItem is GeographicDatasetRow { Dataset: { } d }) Change(() => catalog.Datasets.Remove(d)); }));
        actions.Children.Add(DialogLayout.Button("GeoRevalidate", async () => {
            if (grid.SelectedItem is not GeographicDatasetRow { Dataset: { } d }) return;
            actions.IsEnabled = false;
            try { var refreshed = await Task.Run(() => GeographicPackage.Inspect(d.Path)); if (refreshed.Manifest.Id != d.Manifest.Id) throw new InvalidDataException("GeoInvalidPackage"); catalog.Import(refreshed); Refresh(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { status.Text = Strings.Get("GeoOperationFailed") + ": " + Strings.Get(ex.Message); }
            finally { actions.IsEnabled = true; }
        }));
        body.Children.Add(grid); body.Children.Add(details); panel.Children.Add(bodyScroll); Refresh(); return window;
    }
}

public sealed class GeographicDatasetRow(GeographicDataset? dataset)
{
    public GeographicDataset? Dataset => dataset;
    public string Name => dataset?.Manifest.Name ?? Strings.Get("GeoBuiltInFallback");
    public string Coverage => dataset?.Manifest.Coverage ?? Strings.Get("WorldCoverage");
    public string Levels => dataset == null ? Strings.Get("GeoCountryLevel") : string.Join(", ", dataset.Manifest.Levels.Select(l => Strings.Get(l switch { "country" => "GeoLevel_country", "region" => "GeoLevel_region", "city" => "GeoLevel_city", _ => "GeoLevel_district" })));
    public string Source => dataset?.Manifest.Source ?? "Natural Earth";
    public string Version => dataset?.Manifest.Version ?? "5.1.2";
    public long Size => dataset?.Size ?? new FileInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "geography/countries.geojson")).Length;
    public string License => dataset?.Manifest.License ?? Strings.Get("GeoPublicDomain");
    public string Attribution => dataset?.Manifest.Attribution ?? "Made with Natural Earth";
    public string Path => dataset?.Path ?? System.IO.Path.Combine(AppContext.BaseDirectory, "geography/countries.geojson");
    public string Enabled => Strings.Get(dataset?.Enabled == false ? "Disabled" : "Enabled");
    public string Validation
    {
        get
        {
            if (dataset == null) return Strings.Get("GeoCoarseCountry");
            try
            {
                var file = new FileInfo(dataset.Path);
                return Strings.Get(!file.Exists ? "GeoPackageUnavailable" : file.Length != dataset.Size || file.LastWriteTimeUtc.Ticks != dataset.ModifiedTicks ? "GeoDatasetChanged" : dataset.Outdated ? "GeoDatasetOutdated" : dataset.Validation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Strings.Get("GeoPackageUnavailable"); }
        }
    }
}
