// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    private readonly TaskCompletionSource libraryInitialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OffscreenIndex? places;
    private readonly DispatcherTimer edgeTimer = new() { Interval=TimeSpan.FromMilliseconds(160) };
    private IReadOnlyList<OffscreenGroup> outside=[];
    private readonly LocationHistory<(MediaItem[] Items,string Title,string? Selected,MediaCluster? Place,string Key)> sidebarHistory=new();
    private int placesVersion;
    private CancellationTokenSource? placesCancellation;
    internal IReadOnlyList<OffscreenGroup> OutsideLocations => outside;
    private void InitializeBrowsing()
    {
        edgeTimer.Tick += (_,_) => { edgeTimer.Stop(); UpdateIndicators(); };
        Atlas.SizeChanged += (_,_) => LibraryBadge.MaxWidth=Math.Max(120,Atlas.ActualWidth-110);
        Atlas.ViewportChanged += () => { RefreshZoomActions();edgeTimer.Stop();edgeTimer.Start(); };
        Atlas.NavigationStarting += () => sidebarHistory.Push((ViewModel.Selection.ToArray(),ViewModel.SidebarTitle,Atlas.SelectedMediaId,sidebarPlace,sidebarKey));
        Closed += (_,_) => {edgeTimer.Stop();placesCancellation?.Cancel();};
        PreviewKeyDown += (_,e) => { if(e.Key==Key.Left && Keyboard.Modifiers==ModifierKeys.Alt) { BackLocation_Click(this,new());e.Handled=true; } };
    }
    private async Task RebuildPlaces()
    {
        placesCancellation?.Cancel();var cancellation=new CancellationTokenSource();placesCancellation=cancellation;
        var version=++placesVersion; var media=ViewModel.Items.ToArray();
        try {
            await Task.Delay(250,cancellation.Token);
            var built=await Task.Run(()=>{using var batch=App.LocationResolver.BeginBatch();return new OffscreenIndex(media,App.LocationResolver.Resolve,cancellation.Token);});
            if(version!=placesVersion)return;places=built;UpdateIndicators();
        } catch(OperationCanceledException) { }
        finally {if(placesCancellation==cancellation)placesCancellation=null;cancellation.Dispose();}
    }
    private void LocalizeBrowsing()
    {
        OpenPreviewButton.Content=Strings.Get("OpenPreview"); SortButton.Content=Strings.Get(ViewModel.SortNewest?"SortDate":"SortName");
        MapTooltips.Set(BackLocationButton,"BackLocation"); AutomationProperties.SetName(BackLocationButton,Strings.Get("BackLocation"));
        OpenPreviewButton.ToolTip=Strings.Get("PreviewShortcut"); UpdateIndicators();
    }
    private void UpdateIndicators()
    {
        if(places==null)return;
        MapContextLabel.Text=Strings.Get("ContextCoverage");
        var map=Atlas.Tiles.Catalog.Packages.FirstOrDefault(m=>m.Id==Atlas.Tiles.Catalog.Preferred);
        var camera=Atlas.Camera;var corners=new[]{Geo.Unproject(camera.X-camera.Width/camera.World/2,camera.Y-camera.Height/camera.World/2),Geo.Unproject(camera.X+camera.Width/camera.World/2,camera.Y+camera.Height/camera.World/2)};
        var fallback=Atlas.TileZoom>=10 && (map==null || corners.Any(p=>!map.CoversBounds(Math.Min(Atlas.TileZoom,map.MaxZoom),p.Latitude,Geo.WrapLongitude(p.Longitude))));
        MapContextNotice.Visibility=fallback && !ViewModel.IsEmpty && Atlas.ActualHeight>400?Visibility.Visible:Visibility.Collapsed;
        outside=places.Outside(Atlas.Camera); var positions=OffscreenIndex.Place(outside,Atlas.Camera,reserved:Atlas.VisibleMarkerBounds.Select(r=>(r.X,r.Y,r.Width,r.Height)).ToArray());
        EdgeIndicators.Children.Clear();
        foreach(var position in positions)
        {
            var group=position.Group;var label=group.Name?.Display(App.Settings.Language) ?? Strings.Get("OtherLocations");
            var button=new Button { Content=new TextBlock { Text=position.Arrow+" "+label+" · "+group.Items.Count,TextTrimming=TextTrimming.CharacterEllipsis },Width=148,Height=32,Padding=new(8,3,8,3),Margin=new(0),ToolTip=Strings.Get("GoToLocation")+": "+label+" · "+group.Items.Count+" "+Strings.Get("Count") };
            AutomationProperties.SetName(button,(string)button.ToolTip);button.Click+=async (_,_)=>await NavigateDestination(group);
            Canvas.SetLeft(button,position.X);Canvas.SetTop(button,position.Y);EdgeIndicators.Children.Add(button);
        }
        var remainder=outside.Where(g=>positions.All(p=>p.Group.Id!=g.Id)).ToArray();
        if(remainder.Length>0)
        {
            var button=new Button { Content=Strings.Get("OtherLocations")+" · "+remainder.Sum(g=>g.Items.Count),Height=30,MaxWidth=Math.Max(120,Atlas.ActualWidth-100),Padding=new(8,3,8,3),Margin=new(0) };
            AutomationProperties.SetName(button,Strings.Get("OtherLocations"));
            button.Click+=(_,_)=> { var menu=new ContextMenu(); foreach(var group in remainder) { var item=new MenuItem { Header=(group.Name?.Display(App.Settings.Language) ?? Strings.Get("OtherLocations"))+" · "+group.Items.Count };item.Click+=async (_,_)=>await NavigateDestination(group);menu.Items.Add(item); } button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true; };
            Canvas.SetLeft(button,8);Canvas.SetTop(button,Math.Max(4,Atlas.ActualHeight-100));EdgeIndicators.Children.Add(button);
        }
        BackLocationButton.Visibility=Atlas.CanGoBack?Visibility.Visible:Visibility.Collapsed;
    }
    private async Task RefreshLiveLibrary()
    {
        var selected=(MediaGrid.SelectedItem as MediaViewModel)?.Item.Path;
        var visible=FilteredLibrary();
        var pending=ViewModel.ShowItems(visible,Strings.Get(sidebarKey is "NoGps" or "WithGps" ? sidebarKey : "AllMedia"),ViewModel.PageIndex);
        var item=ViewModel.VisibleItems.FirstOrDefault(m=>m.Item.Path==selected);
        if(item!=null && selectedMedia.Ids.Count==0)MediaGrid.SelectedItem=item;
        await pending;
    }
    private async Task ShowSelection(IEnumerable<MediaItem> items,string title,string? selected)
    {
        var alive=ViewModel.Items.Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var show=ViewModel.ShowItems(items.Where(m=>alive.Contains(m.Path)),title);
        var index=ViewModel.Selection.ToList().FindIndex(m=>string.Equals(m.Path,selected,StringComparison.OrdinalIgnoreCase));
        if(index>=120) { await show;show=ViewModel.ShowPage(index/120); }
        var item=ViewModel.VisibleItems.FirstOrDefault(m=>m.Item.Path==selected);
        if(item!=null) { if(selectedMedia.Ids.Count==0)MediaGrid.SelectedItem=item;MediaGrid.ScrollIntoView(item); }
        await show;RefreshCopyActions();
    }
    internal async Task NavigateToMedia(MediaItem media)
    {
        if(!ViewModel.Items.Any(m=>m.Path.Equals(media.Path,StringComparison.OrdinalIgnoreCase)))return;
        ShowSidebar();
        if(ViewModel.Selection.Count==ViewModel.Items.Count) { sidebarPlace=null;sidebarKey="AllMedia";ViewModel.RefreshTitle(Strings.Get("AllMedia")); }
        if(!media.HasLocation) {
            if(!ViewModel.Selection.Any(m=>m.Path==media.Path)) { sidebarPlace=null;sidebarKey="AllMedia";await ShowSelection(ViewModel.Items,Strings.Get("AllMedia"),media.Path); }
            else await ShowSelection(ViewModel.Selection.ToArray(),ViewModel.SidebarTitle,media.Path);
            Atlas.Highlight(media);ViewModel.Status=Strings.Get("NoGpsNavigation");return;
        }
        Atlas.NavigateMedia(media);
        if(!ViewModel.Selection.Any(m=>m.Path==media.Path)) { sidebarPlace=null;sidebarKey="AllMedia";await ShowSelection(ViewModel.Items,Strings.Get("AllMedia"),media.Path); }
        else { var index=ViewModel.Selection.ToList().FindIndex(m=>m.Path==media.Path);if(index/120!=ViewModel.PageIndex)await ViewModel.ShowPage(index/120);var model=ViewModel.VisibleItems.First(m=>m.Item.Path==media.Path);MediaGrid.SelectedItem=model;MediaGrid.ScrollIntoView(model); }
        Atlas.Highlight(media);ViewModel.Status=Strings.Get("GoToLocation")+": "+media.Name;UpdateIndicators();
    }
    internal async Task NavigateDestination(OffscreenGroup group)
    {
        var alive=ViewModel.Items.Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var members=group.Items.Where(m=>alive.Contains(m.Path)).ToArray();if(members.Length==0)return;
        Atlas.NavigateGroup(members);sidebarPlace=new(members);ShowSidebar();
        await ShowSelection(members,group.Name?.Display(App.Settings.Language) ?? Strings.Get("OtherLocations"),Atlas.SelectedMediaId);UpdateIndicators();
    }
    private async void BackLocation_Click(object sender,RoutedEventArgs e) => await GoBackLocation();
    internal async Task<bool> GoBackLocation()
    {
        if(!Atlas.Back())return false;
        if(sidebarHistory.TryBack(out var previous)) { sidebarPlace=previous.Place;sidebarKey=previous.Key;await ShowSelection(previous.Items,previous.Items.Length==ViewModel.Items.Count?Strings.Get("AllMedia"):previous.Place==null?Strings.Get(previous.Key):Atlas.Describe(previous.Place.Latitude,previous.Place.Longitude),previous.Selected); }
        if(!ViewModel.Items.Any(m=>m.Path.Equals(Atlas.SelectedMediaId,StringComparison.OrdinalIgnoreCase)))Atlas.Highlight(null);
        UpdateIndicators();return true;
    }
    private async void Media_DoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject origin)
            for(var node=origin;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                if(node is Button)return;
        if(e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(MediaGrid,source) is ListBoxItem { DataContext:MediaViewModel media }) { e.Handled=true;await NavigateToMedia(media.Item); }
    }
    private async void Media_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.OriginalSource is Button && e.Key==Key.Enter)return;
        if(HandleSelectionKey(e))return;
        if(e.Key==Key.Enter && ActiveLibraryMedia() is { } media) { e.Handled=true;await NavigateToMedia(media.Item); }
        else if(e.Key==Key.F4) { e.Handled=true;OpenPreview_Click(sender,new()); }
    }
    private async void Sort_Click(object sender,RoutedEventArgs e) { ViewModel.SortNewest=!ViewModel.SortNewest;AdaptMainLayout();await ShowSelection(ViewModel.Selection.ToArray(),ViewModel.SidebarTitle,Atlas.SelectedMediaId); }
    private async void OpenPreview_Click(object sender,RoutedEventArgs e)
    {
        if(ActiveLibraryMedia() is not { } media) { ViewModel.Status=Strings.Get("SelectMedia");return; }
        var window=DialogLayout.Window(this,"OpenPreview",1000,760,out var panel);
        var close=DialogLayout.Button("Close",window.Close);close.IsCancel=true;close.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(close,Dock.Bottom);panel.Children.Add(close);
        var label=new TextBlock { Text=media.Name+(media.Item.Kind=="Video"?" · "+Strings.Get("VideoFramePreview"):""),TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,12) };DockPanel.SetDock(label,Dock.Top);panel.Children.Add(label);
        var image=new Image { Stretch=Stretch.Uniform };panel.Children.Add(image);window.Show();image.Source=await ViewModel.Previews.GetAsync(media.Item.Path);
    }
}
