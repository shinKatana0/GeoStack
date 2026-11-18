// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyOwner052()
    {
        if(!App.DataDirectory.Contains("\\.local\\owner052\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned 0.5.2 profile required");
        await AtlasReady;await libraryInitialized.Task;
        var fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/geostack05-owner-acceptance/media"));
        var hashes=new Dictionary<string,string>();foreach(var file in Directory.GetFiles(fixture))hashes[file]=await GeographicCopy.Hash(file,default);
        await ScanFolder(fixture);await placesRefresh;
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var media=ViewModel.Items.ToArray();var picked=media.Where(m=>m.HasLocation).DistinctBy(m=>(m.Latitude,m.Longitude)).Take(3).ToArray();Require(picked.Length==3,"Three distinct GPS fixtures required");
        selectedMedia.Clear();selectedMedia.All(picked.Select(m=>m.Path));RestoreGridSelection();
        Require(CurrentCopySelection().Select(m=>m.Path).ToHashSet().SetEquals(picked.Select(m=>m.Path)),"Copy selection differs from exact IDs");
        MediaGrid.UpdateLayout();var focused=ViewModel.VisibleItems.First(m=>m.Item.Path==picked[2].Path);MediaGrid.ScrollIntoView(focused);MediaGrid.UpdateLayout();
        replacingPage=true;try{((ListBoxItem)MediaGrid.ItemContainerGenerator.ContainerFromItem(focused)).Focus();}finally{replacingPage=false;}RestoreGridSelection();
        Require(ActiveLibraryMedia()?.Item.Path==picked[2].Path,"Preview/Enter must resolve the focused selected media identity");
        var copy=new CopyWindow(this,CurrentCopySelection());copy.Show();await copy.PrepareVerification(Path.Combine(App.DataDirectory,"copy"));
        var plan=copy.PreviewRows.ToArray();Require(plan.Length==3,"Copy preview count differs");
        FeatureVerification.Snapshot(copy,Path.Combine(App.DataDirectory,"copy-preview.png"));copy.Close();
        var results=await Task.Run(()=>GeographicCopy.Execute(plan,Path.Combine(App.DataDirectory,"copy"),Path.Combine(App.DataDirectory,"CopyJournal"),null,default));
        Require(results.Count(r=>r.Status==CopyState.Completed)==3,"Three-file copy failed");
        foreach(var result in results)Require(hashes[result.Source]==await GeographicCopy.Hash(result.Destination,default),"Copy hash mismatch");
        sidebarKey="NoGps";await ShowSelection(FilteredLibrary(),Strings.Get("NoGps"),null);Require(HiddenSelectionCount()==3 && CurrentCopySelection().Length==3,"Hidden identity/count lost");
        sidebarKey="WithGps";await ShowSelection(FilteredLibrary(),Strings.Get("WithGps"),null);Require(ViewModel.Selection.All(m=>m.HasLocation),"With GPS contains invalid coordinates");
        foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
        {
            Width=1360;Height=880;ApplySettings(language,theme);await SettleBrowsing();FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,$"selection-{language}-{theme}.png"));
            Width=1000;Height=640;await SettleBrowsing();UpdateLayout();Require(MediaGrid.ActualHeight>=56 && CopyButton.IsVisible && RemoveSelectedButton.IsVisible,"Compact selection actions or list inaccessible");
            FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,$"compact-{language}-{theme}.png"));
        }
        Width=1360;Height=880;
        sidebarKey="AllMedia";await ShowSelection(media,Strings.Get("AllMedia"),null);selectedMedia.Clear();selectedMedia.All(picked.Take(2).Select(m=>m.Path));RestoreGridSelection();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=(_,_)=>{
            var dialog=OwnedWindows.Cast<Window>().FirstOrDefault(w=>w.Title==Strings.Get("RemoveSelected"));if(dialog==null)return;
            dialog.UpdateLayout();FeatureVerification.Snapshot(dialog,Path.Combine(App.DataDirectory,"batch-removal.png"));
            var root=(DockPanel)dialog.Content;var buttons=(WrapPanel)root.Children[0];((Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));timer.Stop();};
        timer.Start();try{await RemoveMediaBatch(selectedMedia.Resolve(ViewModel.Items));}finally{timer.Stop();}
        Require(ViewModel.Items.Count==media.Length-2 && selectedMedia.Ids.Count==0,"Batch removal did not reconcile library/selection");
        using(var index=new MediaIndex(Path.Combine(App.DataDirectory,"index.db")))Require(picked.Take(2).All(m=>index.Exclusions().Contains(m.Path)),"Missing exclusions");
        await ScanFolder(fixture);Require(ViewModel.Items.Count==media.Length-2,"Folder rescan resurrected removal");
        await ViewModel.ImportAsync([picked[0].Path]);Require(ViewModel.Items.Count==media.Length-1 && ViewModel.Items.Any(m=>m.Path==picked[0].Path) && !ViewModel.Items.Any(m=>m.Path==picked[1].Path),"Explicit restoration scope differs");
        foreach(var pair in hashes)Require(pair.Value==await GeographicCopy.Hash(pair.Key,default),"Original modified");
        File.WriteAllText(Path.Combine(App.DataDirectory,"owner052-ui.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Packaged WPF controls, programmatic stable-ID selection, real copy preview/execution, routed batch confirmation, catalog reopen/rescan/reimport; physical mouse NOT_TESTED",Copied=results.Select(r=>new{r.Source,r.Destination,r.Status}),OriginalHashes=hashes,OriginalCount=media.Length,FinalCount=ViewModel.Items.Count,Excluded= picked[1].Path},new JsonSerializerOptions{WriteIndented=true}));
    }
}
