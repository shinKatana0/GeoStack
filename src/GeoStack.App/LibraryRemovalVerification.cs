// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyLibraryRemoval(bool restart)
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated synthetic profile required");
        await AtlasReady;await libraryInitialized.Task;await SettleBrowsing();
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/removal-ui-fixtures"));
        var photo=Path.Combine(root,"photo/Photo.jpg");var noGps=Path.Combine(root,"nogps/Without-GPS.jpg");var video=Path.Combine(root,"video/Video.mov");
        var hashes=new Dictionary<string,string>();foreach(var path in new[]{photo,noGps,video})hashes[path]=await GeographicCopy.Hash(path,default);
        var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void CheckEmpty(){UpdateLayout();Require(ViewModel.IsEmpty && ViewModel.VisibleItems.Count==0 && HintOverlay.Visibility==Visibility.Visible,"Empty state mismatch");Require(FindName("EmptyCard")==null && !HintOverlay.IsHitTestVisible,"Obstructive empty state");}
        async Task Drop(string path)
        {
            var constructor=typeof(DragEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(IDataObject),typeof(DragDropKeyStates),typeof(DragDropEffects),typeof(DependencyObject),typeof(Point)],null)!;
            var data=new DataObject(DataFormats.FileDrop,new[]{path});
            foreach(var routed in new[]{DragDrop.PreviewDragOverEvent,DragDrop.PreviewDropEvent})
            {var args=(DragEventArgs)constructor.Invoke([data,DragDropKeyStates.LeftMouseButton,DragDropEffects.Copy,this,new Point(120,200)]);args.RoutedEvent=routed;RaiseEvent(args);}
            for(var i=0;i<600 && (dropImportActive || ViewModel.IsScanning);i++)await Task.Delay(50);
            Require(!dropImportActive && !ViewModel.IsScanning && !ImportOverlayVisible,"Drop did not settle");await placesRefresh;await SettleBrowsing();
        }
        T? Find<T>(DependencyObject parent,Func<T,bool> match) where T:DependencyObject
        {if(parent is T candidate && match(candidate))return candidate;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var result=Find(VisualTreeHelper.GetChild(parent,i),match);if(result!=null)return result;}return null;}
        async Task Remove(bool confirm,string? snapshot=null)
        {
            var model=ViewModel.VisibleItems.First();MediaGrid.SelectedItem=model;MediaGrid.UpdateLayout();
            var container=(ListBoxItem)MediaGrid.ItemContainerGenerator.ContainerFromItem(model);
            ContextMenu menu;
            if(snapshot!=null)
            {
                var more=Find<Button>(container,b=>Equals(b.Tag,"MediaActions"))??throw new InvalidOperationException("Visible item actions button missing");
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu=more.ContextMenu;
            }
            else { menu=container.ContextMenu;menu.PlacementTarget=container;menu.IsOpen=true; }
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
            var action=(MenuItem)menu.Items[0];Require((string)action.Header==Strings.Get("RemoveFromLibrary") && action.IsEnabled,"Removal context action unavailable");
            if(snapshot!=null)FeatureVerification.Snapshot(menu,Path.Combine(evidence,"menu-"+snapshot+".png"));menu.IsOpen=false;
            Exception? failure=null;
            _=Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{
                try
                {
                    var dialog=OwnedWindows.Cast<Window>().Single(w=>w.Title==Strings.Get("RemoveFromLibrary"));dialog.UpdateLayout();
                    if(snapshot!=null)FeatureVerification.Snapshot(dialog,Path.Combine(evidence,"confirmation-"+snapshot+".png"));
                    var button=Find<Button>(dialog,b=>b.Name=="ConfirmRemovalButton")!;
                    if(confirm)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else dialog.DialogResult=false;
                }
                catch(Exception error){failure=error;foreach(Window dialog in OwnedWindows)dialog.Close();}
            });
            action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for(var i=0;i<600 && removingFromLibrary;i++)await Task.Delay(50);
            if(failure!=null)throw failure;Require(!removingFromLibrary,"Removal did not settle");await placesRefresh;await SettleBrowsing();
        }
        CheckEmpty();
        if(restart)
        {
            await ScanFolder(Path.GetDirectoryName(photo)!);CheckEmpty();
            await Drop(photo);Require(ViewModel.Items.Count==1 && ViewModel.Items[0].HasLocation,"Explicit individual restore failed after restart");
        }
        else
        {
            var before=Atlas.Camera;Atlas.PanPixels(new Vector(30,15));Require(Atlas.Camera!=before,"Empty map could not pan");ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ResetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await SettleBrowsing();
            foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
            {
                var suffix=language+"-"+theme;ApplySettings(language,theme);CheckEmpty();await SettleBrowsing();FeatureVerification.Snapshot(this,Path.Combine(evidence,"empty-"+suffix+".png"));
                await Drop(photo);Require(ViewModel.Items.Count==1 && HintOverlay.Visibility==Visibility.Collapsed,"Import did not hide compact hint");
                await Remove(false);Require(ViewModel.Items.Count==1,"Cancel removed metadata");
                await Remove(true,suffix);CheckEmpty();Require(Atlas.SelectedMediaId==null && Atlas.Items.Count==0 && OutsideLocations.Count==0,"Stale geographic selection");
                await ScanFolder(Path.GetDirectoryName(photo)!);CheckEmpty();
            }
            await Drop(noGps);Require(!ViewModel.IsEmpty && !ViewModel.Items.Single().HasLocation && HintOverlay.Visibility==Visibility.Collapsed,"No-GPS library treated as empty");
            FeatureVerification.Snapshot(this,Path.Combine(evidence,"no-gps-library.png"));await Remove(true);CheckEmpty();
            await Drop(video);Require(ViewModel.Items.Single().Kind=="Video","Video missing");await Remove(true);CheckEmpty();
        }
        foreach(var entry in hashes)Require(await GeographicCopy.Hash(entry.Key,default)==entry.Value,"Source content changed");
        using var index=new MediaIndex(Path.Combine(App.DataDirectory,"index.db"));
        File.WriteAllText(Path.Combine(App.DataDirectory,restart?"removal-restart.json":"removal.json"),JsonSerializer.Serialize(new{
            Status="PASS",Scope="Rendered WPF menus, modal confirmation and routed synthetic drops; physical Explorer/right-click NOT_TESTED",Restart=restart,
            Items=ViewModel.Items.Count,Exclusions=index.Exclusions().Count,HintVisible=HintOverlay.Visibility==Visibility.Visible,
            SourceHashes=hashes,SourceHashesPreserved=hashes.Count,AtlasSelected=Atlas.SelectedMediaId,Atlas.Camera
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
