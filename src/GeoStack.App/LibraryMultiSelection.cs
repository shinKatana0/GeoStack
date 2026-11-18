// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    private readonly LibrarySelection selectedMedia=new();
    private bool replacingPage;
    private void InitializeMultiSelection()
    {
        ViewModel.PageChanging+=()=>replacingPage=true;
        ViewModel.PagePublished+=()=>{replacingPage=false;RestoreGridSelection();RefreshFilterStates();};
        ViewModel.LibraryChanged+=()=>{selectedMedia.Reconcile(ViewModel.Items.Select(m=>m.Path));RefreshCopyActions();};
    }
    private IEnumerable<MediaItem> FilteredLibrary()=>LibraryFilter.Apply(ViewModel.Items,sidebarKey=="WithGps"?LibraryGpsFilter.WithGps:sidebarKey=="NoGps"?LibraryGpsFilter.WithoutGps:LibraryGpsFilter.All);
    private void RefreshFilterStates()
    {
        foreach(var (button,key) in new[]{(AllButton,"AllMedia"),(WithGpsButton,"WithGps"),(NoGpsButton,"NoGps")})
        {
            button.FontWeight=sidebarPlace==null && (sidebarKey==key || key=="AllMedia" && sidebarKey=="Library")?FontWeights.Bold:FontWeights.Normal;
            button.BorderThickness=new Thickness(button.FontWeight==FontWeights.Bold?2:1);
        }
    }
    private async void WithGps_Click(object sender,RoutedEventArgs e)
    { sidebarPlace=null;sidebarKey="WithGps";ShowSidebar();await ShowSelection(FilteredLibrary(),Strings.Get("WithGps"),null); }
    private void RestoreGridSelection()
    {
        replacingPage=true;
        try {MediaGrid.SelectedItems.Clear();foreach(var m in ViewModel.VisibleItems)if(selectedMedia.Ids.Contains(m.Item.Path))MediaGrid.SelectedItems.Add(m);}
        finally{replacingPage=false;}
        RefreshCopyActions();
    }
    private void CaptureGridSelection(SelectionChangedEventArgs e)
    {
        if(replacingPage)return;
        foreach(MediaViewModel m in e.RemovedItems)selectedMedia.Set(m.Item.Path,false);
        foreach(MediaViewModel m in e.AddedItems)selectedMedia.Set(m.Item.Path,true);
    }
    private async void Media_SelectPointer(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is not DependencyObject origin)return;
        for(var node=origin;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is Button)return;
        if(ItemsControl.ContainerFromElement(MediaGrid,origin) is not ListBoxItem {DataContext:MediaViewModel model} container)return;
        selectedMedia.Click(model.Item.Path,ViewModel.Selection.Select(m=>m.Path).ToArray(),Keyboard.Modifiers.HasFlag(ModifierKeys.Control),Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled=true;replacingPage=true;try{container.Focus();}finally{replacingPage=false;}RestoreGridSelection();
        Media_SelectionChanged(MediaGrid,new SelectionChangedEventArgs(Selector.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
        if(e.ClickCount==2)await NavigateToMedia(model.Item);
    }
    private MediaViewModel? ActiveLibraryMedia()
    {
        if(Keyboard.FocusedElement is DependencyObject origin && ItemsControl.ContainerFromElement(MediaGrid,origin) is ListBoxItem {DataContext:MediaViewModel item} && selectedMedia.Ids.Contains(item.Item.Path))return item;
        return MediaGrid.SelectedItem as MediaViewModel;
    }
    private bool HandleSelectionKey(KeyEventArgs e)
    {
        if(e.Key==Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){selectedMedia.All(ViewModel.Selection.Select(m=>m.Path));RestoreGridSelection();}
        else if(e.Key==Key.Escape){selectedMedia.Clear();RestoreGridSelection();}
        else return false;
        e.Handled=true;return true;
    }
    private void ClearSelection_Click(object sender,RoutedEventArgs e){selectedMedia.Clear();RestoreGridSelection();}
    private int HiddenSelectionCount()=>selectedMedia.Ids.Except(ViewModel.Selection.Select(m=>m.Path),StringComparer.OrdinalIgnoreCase).Count();
    private void RefreshSelectionSummary()
    {
        if(MultiSelectionCount==null)return;
        MultiSelectionCount.Text=string.Format(Strings.Get("SelectionCount"),selectedMedia.Ids.Count,HiddenSelectionCount());
        ClearSelectionButton.IsEnabled=RemoveSelectedButton.IsEnabled=selectedMedia.Ids.Count>0;
    }
    private bool ConfirmHiddenSelection(int count)=>HiddenSelectionCount()==0 || MessageBox.Show(this,string.Format(Strings.Get("HiddenSelectionConfirm"),count,HiddenSelectionCount()),Strings.Get("CopyToLocation"),MessageBoxButton.OKCancel,MessageBoxImage.Information)==MessageBoxResult.OK;
    private async void RemoveSelected_Click(object sender,RoutedEventArgs e)=>await RemoveMediaBatch(selectedMedia.Resolve(ViewModel.Items));
}
