// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyOwner051()
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated profile required");
        await AtlasReady;await libraryInitialized.Task;
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/geostack05-owner-acceptance/media"));
        var files=Directory.GetFiles(root);var hashes=new Dictionary<string,string>();
        foreach(var file in files)hashes[file]=await GeographicCopy.Hash(file,default);
        await ScanFolder(root);await placesRefresh;await SettleBrowsing();
        var output=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(output);var rows=new List<object>();
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(element){Stretch=Stretch.Fill},null,new Rect(0,0,element.ActualWidth,element.ActualHeight));
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
        }
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        T? Find<T>(DependencyObject parent,Func<T,bool> match) where T:DependencyObject
        {if(parent is T value && match(value))return value;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=Find(VisualTreeHelper.GetChild(parent,i),match);if(found!=null)return found;}return null;}
        foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
        {
            Width=1360;Height=880;ApplySettings(language,theme);await SettleBrowsing();
            MediaGrid.SelectedItems.Clear();RefreshCopyActions();Require(!CopyButton.IsEnabled,"Copy must be disabled without selection");
            Capture(MediaHeader,$"copy-disabled-{language}-{theme}.png");
            foreach(var media in ViewModel.VisibleItems.Take(2))MediaGrid.SelectedItems.Add(media);RefreshCopyActions();UpdateLayout();
            Require(CopyButton.IsEnabled && copyActionLabel!.Text.EndsWith("(2)"),"Copy count mismatch");
            Require(((SolidColorBrush)copyActionLabel!.Foreground).Color==Colors.White,"Primary copy label lost contrast");
            var first=(ListBoxItem)MediaGrid.ItemContainerGenerator.ContainerFromIndex(0);
            var more=Find<Button>(first,b=>Equals(b.Tag,"MediaActions"));Require(more is {IsVisible:true} && more.ActualWidth>=28,"Discoverable media actions absent");
            Capture(this,$"library-{language}-{theme}.png");
            more!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Background);
            Require(more.ContextMenu.IsOpen && ((MenuItem)more.ContextMenu.Items[0]).IsEnabled,"Three-dot menu did not open");
            Capture(more.ContextMenu,$"more-{language}-{theme}.png");more.ContextMenu.IsOpen=false;
            Atlas.ZoomBy(1e6);await SettleBrowsing();Require(!ZoomInButton.IsEnabled && ZoomOutButton.IsEnabled,"Maximum zoom state mismatch");
            var max=Atlas.Camera;ZoomIn_Click(ZoomInButton,new());await SettleBrowsing();Require(Atlas.Camera==max,"Redundant maximum command moved camera");
            Capture(MapNavigation,$"zoom-max-{language}-{theme}.png");
            var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=MouseWheelEvent};Atlas.RaiseEvent(wheel);await SettleBrowsing();Require(ZoomInButton.IsEnabled,"Wheel zoom did not refresh state");
            Atlas.ZoomBy(1e-6);await SettleBrowsing();Require(!ZoomOutButton.IsEnabled,"Minimum zoom state mismatch");
            Capture(MapNavigation,$"zoom-min-{language}-{theme}.png");
            ZoomInButton.Focus();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Background);
            Require(ZoomInButton.IsKeyboardFocused && ZoomInButton.FocusVisualStyle!=null,"Keyboard focus unavailable");
            Capture(MapNavigation,$"zoom-focus-{language}-{theme}.png");
            Atlas.Focus();ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await SettleBrowsing();
            Require(!ZoomInButton.IsPressed,"Zoom remained pressed after routed click");
            rows.Add(new{language,theme,CopyLabel=copyActionLabel.Text,MoreVisible=more.IsVisible,ZoomInButton.IsPressed,ZoomInButton.IsMouseOver,KeyboardFocusAvailable=true});
            Width=1000;Height=640;await SettleBrowsing();UpdateLayout();
            first=(ListBoxItem)MediaGrid.ItemContainerGenerator.ContainerFromIndex(0);Require(Find<Button>(first,b=>Equals(b.Tag,"MediaActions")) is {IsVisible:true},"Compact item menu absent");
            Capture(this,$"compact-{language}-{theme}.png");
        }
        foreach(var entry in hashes)Require(await GeographicCopy.Hash(entry.Key,default)==entry.Value,"Original changed");
        File.WriteAllText(Path.Combine(App.DataDirectory,"owner051-ui.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Actual packaged WPF controls, routed clicks/wheel, programmatic focus and snapshots; physical pointer/keyboard/Explorer NOT_TESTED",Items=ViewModel.Items.Count,SourceHashes=hashes,Results=rows},new JsonSerializerOptions{WriteIndented=true}));
    }
}
