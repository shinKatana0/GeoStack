// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    private bool removingFromLibrary;
    private void MediaActions_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button || button.DataContext is not MediaViewModel)return;
        var menu=CreateMediaContextMenu();button.ContextMenu=menu;menu.PlacementTarget=button;
        menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen=true;e.Handled=true;
    }
    private ContextMenu CreateMediaContextMenu()
    {
        var menu=new ContextMenu();var remove=new MenuItem();menu.Items.Add(remove);
        menu.SetResourceReference(Control.BackgroundProperty,"Surface");menu.SetResourceReference(Control.ForegroundProperty,"Text");menu.SetResourceReference(Control.BorderBrushProperty,"Line");
        var menuTemplate=new ControlTemplate(typeof(ContextMenu));
        var frame=new FrameworkElementFactory(typeof(Border));frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Line");frame.SetValue(Border.BorderThicknessProperty,new Thickness(1));frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));frame.SetValue(Border.PaddingProperty,new Thickness(4));frame.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));menuTemplate.VisualTree=frame;menu.Template=menuTemplate;
        var style=new Style(typeof(MenuItem));
        var template=new ControlTemplate(typeof(MenuItem));
        var border=new FrameworkElementFactory(typeof(Border));border.Name="Chrome";border.SetValue(Border.PaddingProperty,new Thickness(12,8,12,8));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.Name="Label";content.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Text");content.SetValue(ContentPresenter.ContentSourceProperty,"Header");border.AppendChild(content);template.VisualTree=border;
        var highlight=new Trigger{Property=MenuItem.IsHighlightedProperty,Value=true};highlight.Setters.Add(new Setter(Border.BackgroundProperty,new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(8,127,120)),"Chrome"));highlight.Setters.Add(new Setter(System.Windows.Documents.TextElement.ForegroundProperty,System.Windows.Media.Brushes.White,"Label"));template.Triggers.Add(highlight);
        style.Setters.Add(new Setter(Control.TemplateProperty,template));remove.Style=style;
        menu.Opened+=(_,_)=>{
            remove.Header=Strings.Get("RemoveFromLibrary");AutomationProperties.SetName(remove,Strings.Get("RemoveFromLibrary"));
            remove.IsEnabled=!removingFromLibrary && !OwnedWindows.OfType<CopyWindow>().Any() && (menu.PlacementTarget as FrameworkElement)?.DataContext is MediaViewModel;
        };
        remove.Click+=async(_,_)=>{if((menu.PlacementTarget as FrameworkElement)?.DataContext is MediaViewModel media)await RemoveMediaFromLibrary(media.Item);};
        return menu;
    }
    internal Window CreateRemovalConfirmation(MediaItem media)
    {
        var dialog=DialogLayout.Window(this,"RemoveFromLibrary",500,260,out var panel);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var cancel=DialogLayout.Button("CancelCopy",()=>dialog.DialogResult=false);cancel.IsCancel=true;cancel.IsDefault=true;buttons.Children.Add(cancel);
        var confirm=DialogLayout.Button("RemoveFromLibrary",()=>dialog.DialogResult=true);confirm.Name="ConfirmRemovalButton";buttons.Children.Add(confirm);
        var text=new StackPanel();text.Children.Add(new TextBlock{Text=media.Name,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(4,4,4,12)});
        text.Children.Add(DialogLayout.Text("RemoveFromLibraryConfirm"));panel.Children.Add(text);return dialog;
    }
    internal Window CreateBatchRemovalConfirmation(int count)
    {
        var dialog=DialogLayout.Window(this,"RemoveSelected",520,280,out var panel);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var cancel=DialogLayout.Button("CancelCopy",()=>dialog.DialogResult=false);cancel.IsCancel=true;cancel.IsDefault=true;buttons.Children.Add(cancel);
        var confirm=DialogLayout.Button("RemoveSelected",()=>dialog.DialogResult=true);confirm.Name="ConfirmBatchRemovalButton";buttons.Children.Add(confirm);
        panel.Children.Add(new TextBlock{Text=string.Format(Strings.Get("RemoveSelectedConfirm"),count,HiddenSelectionCount()),TextWrapping=TextWrapping.Wrap});return dialog;
    }
    internal Task RemoveMediaFromLibrary(MediaItem media) => RemoveMediaBatch([media],true);
    internal async Task RemoveMediaBatch(IReadOnlyList<MediaItem> media,bool individual=false)
    {
        if(removingFromLibrary || OwnedWindows.OfType<CopyWindow>().Any() || media.Count==0)return;
        removingFromLibrary=true;
        try
        {
            if(individual) {if(CreateRemovalConfirmation(media[0]).ShowDialog()!=true)return;}
            else if(CreateBatchRemovalConfirmation(media.Count).ShowDialog()!=true)return;
            var selected=Atlas.SelectedMediaId;
            await ViewModel.RemoveManyFromLibraryAsync(media);
            this.selectedMedia.Reconcile(ViewModel.Items.Select(m=>m.Path));
            var alive=ViewModel.Items.ToDictionary(m=>m.Path,StringComparer.OrdinalIgnoreCase);
            if(sidebarPlace!=null){var remaining=sidebarPlace.Items.Where(m=>alive.ContainsKey(m.Path)).ToArray();sidebarPlace=remaining.Length==0?null:new(remaining);if(sidebarPlace==null)sidebarKey="AllMedia";}
            if(sidebarPlace==null)await RefreshLiveLibrary();
            else await ShowSelection(sidebarPlace.Items,ViewModel.SidebarTitle,selected);
            var selectedMedia=selected!=null?alive.GetValueOrDefault(selected):null;
            Atlas.Highlight(selectedMedia);
            if(selectedMedia!=null){var model=ViewModel.VisibleItems.FirstOrDefault(m=>m.Item.Path.Equals(selectedMedia.Path,StringComparison.OrdinalIgnoreCase));if(model!=null)MediaGrid.SelectedItem=model;}
            else{++previewVersion;PreviewImage.Source=null;PreviewName.Text=PreviewDetails.Text="";PreviewMessage.Text=Strings.Get("SelectMedia");}
            places=null;outside=[];EdgeIndicators.Children.Clear();placesRefresh=RebuildPlaces();await placesRefresh;
            ViewModel.Status=Strings.Get("RemovedFromLibrary");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ViewModel.Status=Strings.Get("RemoveFromLibraryFailed");}
        finally{removingFromLibrary=false;}
    }
}
