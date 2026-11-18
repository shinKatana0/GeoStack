// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeoStack.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    internal Task AtlasReady { get; set; } = Task.CompletedTask;
    private int previewVersion;
    private Task placesRefresh = Task.CompletedTask;
    private bool sidebarVisible = true;
    private bool? compactMediaLayout;
    private GeoStack.Core.MediaCluster? sidebarPlace;
    private string sidebarKey = "Library";
    public MainWindow()
    {
        InitializeComponent(); StartupTrace.Mark("WindowXamlLoaded");
        ViewModel = new(); StartupTrace.Mark("ViewModelAndIndexLoaded"); DataContext = ViewModel;
        Atlas.Items = ViewModel.Items; Atlas.Previews = ViewModel.Previews;
        ViewModel.LibraryChanged += () => { Atlas.Items = ViewModel.Items; Atlas.InvalidateVisual(); placesRefresh = RebuildPlaces(); if(ViewModel.IsScanning && sidebarPlace==null) _=RefreshLiveLibrary(); };
        Atlas.StackSelected += async (cluster, title) => { sidebarPlace = cluster; ShowSidebar(); await ShowSelection(cluster.Items, title, Atlas.SelectedMediaId); };
        InitializeBrowsing(); InitializeMultiSelection();
        Deactivated += (_,_) => HideImportOverlay();
        Localize(); Closing += OnClosing; Closed += async (_,_) => { await Atlas.Tiles.DisposeAsync();if(Atlas.AtlasPilot!=null)await Atlas.AtlasPilot.DisposeAsync(); };
        WindowLayout.Attach(this, "Main"); SourceInitialized += (_, _) => App.ApplyTheme();
        SizeChanged += (_, _) => AdaptMainLayout();
        Loaded += async (_, _) => { await ShowSelection(ViewModel.Items, Strings.Get("AllMedia"), null); await RebuildPlaces(); var map = Atlas.Tiles.Catalog.Packages.FirstOrDefault(p => p.Id == Atlas.Tiles.Catalog.Preferred && p.Enabled && p.Available); if (map != null) Atlas.ShowMap(map); libraryInitialized.TrySetResult(); };

    }
    public int MapCountryCount => Atlas.CountryCount;
    public int MapCityCount => Atlas.CityCount;
    internal void RefreshTheme() => Atlas.InvalidateVisual();
    private async void PreviousPage_Click(object sender, RoutedEventArgs e) => await ViewModel.ShowPage(ViewModel.PageIndex - 1);
    private async void NextPage_Click(object sender, RoutedEventArgs e) => await ViewModel.ShowPage(ViewModel.PageIndex + 1);
    internal async Task<bool> VerifySelectedPreview(GeoStack.Core.MediaItem item)
    {
        await ViewModel.ShowItems([item], Strings.Get("Library"));
        MediaGrid.SelectedIndex = 0;
        var expected = await ViewModel.Previews.GetAsync(item.Path);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return expected != null && PreviewImage.Source != null && PreviewName.Text == item.Name;
    }
    private void Localize()
    {
        Resources["MediaActionsLabel"]=Strings.Get("MediaActions");
        CopyStackButton.Content = Strings.Get("CopyStack"); JournalButton.Content = Strings.Get("CopyHistory"); MapsButton.Content = Strings.Get("MapManager");
        OpenButton.Content = Strings.Get("OpenFolder"); CancelButton.Content = Strings.Get("Cancel"); LibraryButton.Content = Strings.Get("Library");
        OfflineLabel.Text = Strings.Get("Offline"); HintLabel.Text = Strings.Get("CompactLibraryHint"); AtlasTitle.Text = Strings.Get("Library");
        WithGpsButton.Content=Strings.Get("WithGps");ClearSelectionButton.Content=Strings.Get("ClearSelection");RemoveSelectedButton.Content=Strings.Get("RemoveSelected");
        AllButton.Content = Strings.Get("AllMedia"); NoGpsButton.Content = Strings.Get("NoGps"); FitButton.ToolTip = Strings.Get("Fit"); SidebarButton.ToolTip = Strings.Get("Sidebar"); PreviewMessage.Text = Strings.Get("NoSelection"); DetailsButton.Content = Strings.Get("ErrorDetails"); Atlas.InvalidateVisual();
        foreach (var (control, key) in new[] { (ZoomInButton, "ZoomIn"), (ZoomOutButton, "ZoomOut"), (ResetButton, "ResetMap"), (FitButton, "FitAll"), (SidebarButton, "Sidebar"), (PreviousPageButton, "PreviousPage"), (NextPageButton, "NextPage") }) MapTooltips.Set(control, key);
        System.Windows.Automation.AutomationProperties.SetName(ZoomInButton, Strings.Get("ZoomIn"));
        System.Windows.Automation.AutomationProperties.SetName(ZoomOutButton, Strings.Get("ZoomOut"));
        MapTooltips.Set(SettingsButton, "SettingsTooltip");
        System.Windows.Automation.AutomationProperties.SetName(SettingsButton, Strings.Get("Settings"));
        System.Windows.Automation.AutomationProperties.SetName(PreviousPageButton, Strings.Get("PreviousPage"));
        System.Windows.Automation.AutomationProperties.SetName(NextPageButton, Strings.Get("NextPage"));
        ImportHint.Text = Strings.Get("DropToAdd");
        LocalizeBrowsing(); AdaptMainLayout();
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = Strings.Get("Folder"), Multiselect = false };
        if (picker.ShowDialog(this) == true) await ScanFolder(picker.FolderName);
    }
    public async Task ScanFolder(string path) { sidebarPlace = null; sidebarKey = "AllMedia"; await ViewModel.ScanAsync(path); if (ViewModel.Items.Count > 0) { var show = ViewModel.ShowItems(ViewModel.Items, Strings.Get("AllMedia")); MediaGrid.SelectedIndex = 0; await show; } }
    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.ScanCancellation?.Cancel();
    private async void Library_Click(object sender, RoutedEventArgs e) { sidebarPlace = null; sidebarKey = "AllMedia"; ShowSidebar(); await ShowSelection(ViewModel.Items, Strings.Get("AllMedia"), Atlas.SelectedMediaId); }
    private async void NoGps_Click(object sender, RoutedEventArgs e) { sidebarPlace = null; sidebarKey = "NoGps"; ShowSidebar(); PreviewMessage.Text = Strings.Get("NoGpsHint"); await ViewModel.ShowItems(ViewModel.Items.Where(x => !x.HasLocation), Strings.Get("NoGps")); }
    private void ShowSidebar() { sidebarVisible = true; AdaptMainLayout(); }
    private void Sidebar_Click(object sender, RoutedEventArgs e) { sidebarVisible = !sidebarVisible; AdaptMainLayout(); }
    private void AdaptMainLayout()
    {
        SidebarColumn.Width = new(sidebarVisible ? ActualWidth < 1100 ? 330 : 390 : 0);
        var compact = ActualHeight < 800;
        MediaHeaderTitle.TextWrapping=compact?TextWrapping.NoWrap:TextWrapping.Wrap;
        MediaHeaderTitle.TextTrimming=TextTrimming.CharacterEllipsis;
        OpenPreviewButton.Content=Strings.Get(compact?"PreviewCompact":"OpenPreview");
        SortButton.Content=Strings.Get(compact?(ViewModel.SortNewest?"DateCompact":"NameCompact"):(ViewModel.SortNewest?"SortDate":"SortName"));
        SortButton.ToolTip=Strings.Get(ViewModel.SortNewest?"SortDate":"SortName");
        var shortest=ActualHeight<600;
        LibraryBadgeHost.Margin=new Thickness(shortest?8:24);LibraryBadge.Padding=shortest?new Thickness(8,6,8,6):new Thickness(16,12,16,12);
        AtlasTitle.FontSize=shortest?13:17;LibraryCounters.FontSize=shortest?10:13;
        if(shortest) { OpenPreviewButton.Content="\u25A3";SortButton.Content="\u2195"; }
        OpenPreviewButton.Width=SortButton.Width=shortest?28:double.NaN;
        System.Windows.Automation.AutomationProperties.SetName(OpenPreviewButton,Strings.Get("OpenPreview"));System.Windows.Automation.AutomationProperties.SetName(SortButton,Strings.Get(ViewModel.SortNewest?"SortDate":"SortName"));
        SelectionSummaryLabel.Visibility=ActualHeight<600?Visibility.Collapsed:Visibility.Visible;
        MapNavigation.Orientation = ActualHeight < 600 ? Orientation.Horizontal : Orientation.Vertical;
        MapNavigation.Margin = ActualHeight < 600 ? new Thickness(8,8,8,24) : new Thickness(18,18,18,65);
        foreach (var (button, full, brief) in new[] { (CopyStackButton, "CopyStack", "CopyGroupCompact"), (JournalButton, "CopyHistory", "CopyHistoryCompact") })
        { button.Content = Strings.Get(compact ? brief : full); MapTooltips.Set(button, full); System.Windows.Automation.AutomationProperties.SetName(button, Strings.Get(full)); }
        if (compactMediaLayout != compact)
        {
            compactMediaLayout = compact;
            MediaHeader.Margin = compact ? new Thickness(8) : new Thickness(20,20,20,12);
            MediaHeaderTitle.FontSize = compact ? 16 : 19;
            PreviewMetadata.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            MediaGrid.ItemTemplate = (DataTemplate)FindResource(compact ? "MediaCompactTemplate" : "MediaLibraryRowTemplate");
            var itemStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty,CreateMediaContextMenu()));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(compact ? 1 : 3)));
            itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(compact ? 1 : 2))); MediaGrid.ItemContainerStyle = itemStyle;
            foreach (var button in new[] { AllButton, WithGpsButton, NoGpsButton, OpenPreviewButton, SortButton, CopyButton, CopyStackButton, JournalButton })
            { button.FontSize = compact ? 10 : 11; button.Padding = compact ? new Thickness(6,3,6,3) : new Thickness(8,6,8,6); }
        }
        RefreshCopyActions();RefreshZoomActions();
        PreviewRow.Height = new(0); PreviewPanel.Visibility = Visibility.Collapsed;
        PreviewMetadata.Visibility = Visibility.Collapsed;
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { Atlas.ZoomBy(1.5); RefreshZoomActions(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { Atlas.ZoomBy(1 / 1.5); RefreshZoomActions(); }
    private void Fit_Click(object sender, RoutedEventArgs e) => Atlas.FitLocations();
    private void Reset_Click(object sender, RoutedEventArgs e) => Atlas.Fit();
    private void ShowMessage(string message, string title)
    {
        var panel = new DockPanel { Margin = new(24) };
        var dialog = new Window { Owner = this, Icon = Icon, Title = title, Width = 540, Height = 350, MinWidth = 400, MinHeight = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        var close = new Button { Content = Strings.Get("Close"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,16,0,0) }; DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close); close.Click += (_, _) => dialog.Close();
        panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        WindowLayout.Attach(dialog, "Message"); dialog.SourceInitialized += (_, _) => App.ApplyTheme(); dialog.ShowDialog();
    }
    private async void Media_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(replacingPage)return;
        CaptureGridSelection(e); RefreshCopyActions();
        var version = ++previewVersion; PreviewImage.Source = null;
        if (ActiveLibraryMedia() is not { } media) { PreviewMessage.Text = Strings.Get("SelectMedia"); PreviewName.Text = PreviewDetails.Text = ""; return; }
        Atlas.Highlight(media.Item);
        PreviewName.Text = media.Name; PreviewName.ToolTip = media.Item.Path; PreviewDetails.Text = media.Details; PreviewMessage.Text = Strings.Get("Loading");
        var image = await ViewModel.Previews.GetAsync(media.Item.Path, true); if (version != previewVersion) return;
        PreviewImage.Source = image; PreviewMessage.Text = image == null ? Strings.Get(File.Exists(media.Item.Path) ? "PreviewUnavailable" : "SourceMissing") : "";
        PreviewDetails.Text = Strings.Get(media.Item.Kind) + " · " + media.Details + (media.Item.Captured == null ? "" : " · " + media.Item.Captured);
    }
    private void Details_Click(object sender, RoutedEventArgs e) => ShowMessage(ViewModel.Errors.Count == 0 ? ViewModel.Status : string.Join(Environment.NewLine, ViewModel.Errors.Take(30)), Strings.Get("ErrorDetails"));
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(this);
        window.ShowDialog();
        ApplySettings(window.SelectedLanguage, window.SelectedTheme);
    }
    internal void ApplySettings(string language, string theme)
    {
        App.Settings = App.Settings with { Language = language, Theme = theme };
        try { App.Settings.Save(App.SettingsPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowMessage(ex.Message, Strings.Get("Settings")); }
        Strings.SetLanguage(App.Settings.Language); App.ApplyTheme(); Localize(); ViewModel.RefreshLanguage(); ViewModel.Status = Strings.Get("Saved");
        foreach (var media in ViewModel.VisibleItems) media.RefreshLanguage();
        ViewModel.RefreshTitle(sidebarPlace == null ? Strings.Get(sidebarKey) : Atlas.Describe(sidebarPlace.Latitude, sidebarPlace.Longitude));
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if(dropImportActive){closeAfterDrop=true;ViewModel.ScanCancellation?.Cancel();e.Cancel=true;return;} if (ViewModel.IsScanning) { ViewModel.ScanCancellation?.Cancel(); e.Cancel = true; ViewModel.PropertyChanged -= CloseWhenReady; ViewModel.PropertyChanged += CloseWhenReady; } }
    private void CloseWhenReady(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(MainViewModel.IsScanning) && !ViewModel.IsScanning) { ViewModel.PropertyChanged -= CloseWhenReady; Close(); } }
}
