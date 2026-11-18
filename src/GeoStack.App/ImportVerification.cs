// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyImport(string fixture, bool restart)
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45)); await AtlasReady; await SettleBrowsing();
        if (!fixture.EndsWith(Path.Combine(".local","promotion-import-fixtures"),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Approved import fixtures required");
        if (Atlas.AtlasPilot is not {Enabled:true} pilot) throw new InvalidOperationException("Default hybrid atlas unavailable");
        var originals=Directory.GetFiles(fixture,"*",SearchOption.AllDirectories);
        var hashes=await Task.WhenAll(originals.Select(p=>GeographicCopy.Hash(p,default)));
        var stages=new List<object>();var maximumGap=0d;var last=Stopwatch.GetTimestamp();
        var heartbeat=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
        heartbeat.Tick+=(_,_)=>{maximumGap=Math.Max(maximumGap,Stopwatch.GetElapsedTime(last).TotalMilliseconds);last=Stopwatch.GetTimestamp();};
        DragEventArgs Routed(IDataObject data,RoutedEvent routed)
        {
            var constructor=typeof(DragEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(IDataObject),typeof(DragDropKeyStates),typeof(DragDropEffects),typeof(DependencyObject),typeof(Point)},null) ?? throw new InvalidOperationException("WPF drag event constructor unavailable");
            var args=(DragEventArgs)constructor.Invoke(new object[]{data,DragDropKeyStates.LeftMouseButton,DragDropEffects.Copy,this,new Point(100,140)});args.RoutedEvent=routed;return args;
        }
        IDataObject Data(params string[] paths)=>new DataObject(DataFormats.FileDrop,paths);
        async Task Drop(string name,int count,params string[] paths)
        {
            var before=Atlas.Camera;var data=Data(paths);var timer=Stopwatch.StartNew();last=Stopwatch.GetTimestamp();heartbeat.Start();
            var over=Routed(data,DragDrop.PreviewDragOverEvent);RaiseEvent(over);
            if(!ImportOverlayVisible || over.Effects!=DragDropEffects.Copy)throw new InvalidOperationException("File drop recognition failed");
            var drop=Routed(data,DragDrop.PreviewDropEvent);RaiseEvent(drop);
            if(drop.Effects!=DragDropEffects.Copy)throw new InvalidOperationException("OLE completion effect was not synchronous");
            for(var i=0;dropImportActive || ViewModel.IsScanning;i++){if(i>1800)throw new TimeoutException("Import did not finish");await Task.Delay(50);}
            heartbeat.Stop();
            if(ViewModel.Items.Count!=count || ViewModel.VisibleItems.Count!=count || ViewModel.VisibleItems.Any(m=>m.Thumbnail==null))throw new InvalidOperationException("Additive library/thumbnail count: "+name);
            if(ImportOverlayVisible || Atlas.Camera!=before)throw new InvalidOperationException("Import changed camera or left an overlay");
            stages.Add(new{Name=name,Items=count,Milliseconds=timer.Elapsed.TotalMilliseconds,ViewModel.Status});
        }
        try
        {
            if(!restart)
            {
                if(ViewModel.Items.Count!=0)throw new InvalidOperationException("Empty initial library required");
                await Drop("single-jpeg",1,Path.Combine(fixture,"single.jpg"));
                await Drop("single-heic",2,Path.Combine(fixture,"single.heic"));
                if(!ViewModel.Items.Single(m=>m.Name=="single.heic").HasLocation)throw new InvalidOperationException("HEIC GPS missing");
                await Drop("single-mov",3,Path.Combine(fixture,"single.mov"));
                await Drop("nested-folder",8,Path.Combine(fixture,"folder"));
                await Drop("mixed-overlap",10,Path.Combine(fixture,"folder"),Path.Combine(fixture,"folder/nested"),Path.Combine(fixture,"other/same.jpg"),Path.Combine(fixture,"raw/camera.NEF"));
                await Drop("repeat",10,fixture,Path.Combine(fixture,"single.jpg").ToUpperInvariant());
                await Drop("missing-and-unsupported",10,Path.Combine(fixture,"absent.jpg"),Path.Combine(fixture,"folder/notes.txt"));
                if(ShowImportOverlay(new DataObject(DataFormats.Text,"not a file drop")))throw new InvalidOperationException("Text treated as media");
                var files=Data(Path.Combine(fixture,"single.jpg"));ShowImportOverlay(files);RaiseEvent(Routed(files,DragDrop.PreviewDragLeaveEvent));if(ImportOverlayVisible)throw new InvalidOperationException("Drag leave did not clear overlay");
                var cancel=Path.Combine(App.DataDirectory,"cancel-fixtures");Directory.CreateDirectory(cancel);
                for(var i=0;i<85;i++)File.Copy(Path.Combine(fixture,"single.jpg"),Path.Combine(cancel,i+".jpg"));
                var running=ImportDroppedData(Data(cancel));if(!ViewModel.IsScanning)throw new InvalidOperationException("Import did not acquire scan ownership");
                if(await ImportDroppedData(files))throw new InvalidOperationException("Concurrent drop accepted");
                CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await running;
                if(ViewModel.Items.Count!=10 || ViewModel.IsScanning || ImportOverlayVisible)throw new InvalidOperationException("Cancelled import changed committed library");
            }
            else if(ViewModel.Items.Count!=10 || ViewModel.VisibleItems.Any(m=>m.Thumbnail==null))throw new InvalidOperationException("Library restart persistence failed");
            var raw=ViewModel.Items.Single(m=>m.Name=="camera.NEF");if(await ViewModel.Previews.GetAsync(raw.Path)==null)throw new InvalidOperationException("RAW preview failed");
            var jpeg=ViewModel.Items.Single(m=>m.Name=="single.jpg");await NavigateToMedia(jpeg);await SettleBrowsing();
            if(Atlas.SelectedMediaId!=jpeg.Path || Atlas.ActiveTiles.DrawnTileCount==0)throw new InvalidOperationException("Media/map navigation failed");
            Atlas.SetDragForVerification(true);var mediaCount=ViewModel.Items.Count;
            for(var i=0;i<50;i++){Atlas.PanPixels(new Vector(i<25?-24:24,0));await Task.Delay(16);}Atlas.SetDragForVerification(false);await SettleBrowsing();
            if(ImportOverlayVisible || ViewModel.Items.Count!=mediaCount)throw new InvalidOperationException("Ordinary map pan activated import");
            var original=App.Settings;
            foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
            {
                ApplySettings(language,theme);await SettleBrowsing();
                FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,$"import-library-{language}-{theme}.png"));
                ShowImportOverlay(Data(Path.Combine(fixture,"single.jpg")));FeatureVerification.Snapshot(this,Path.Combine(App.DataDirectory,$"import-overlay-{language}-{theme}.png"));HideImportOverlay();
            }
            App.Settings=original;App.ApplyTheme();Localize();
            var copied=new List<object>();
            if(!restart)foreach(var names in new[]{new[]{"Kyoto.jpg"},new[]{"Zurich.jpg","single.mov"}})
            {
                var root=Path.Combine(App.DataDirectory,"copy-"+copied.Count);var selection=ViewModel.Items.Where(m=>names.Contains(m.Name)).ToArray();
                var plan=await Task.Run(()=>GeographicCopy.Plan(selection,App.LocationResolver,root,"en-US","CountryCity",true,default));
                var result=await Task.Run(()=>GeographicCopy.Execute(plan,root,Path.Combine(App.DataDirectory,"CopyJournal"),null,default));
                foreach(var row in result)if(row.Status!=CopyState.Completed || await GeographicCopy.Hash(row.Source,default)!=await GeographicCopy.Hash(row.Destination,default))throw new InvalidOperationException("Geographic copy verification failed");
                copied.Add(new{Files=result.Count,Verified=true,Destinations=plan.Select(p=>p.RelativeDirectory)});
            }
            for(var i=0;i<originals.Length;i++)if(hashes[i]!=await GeographicCopy.Hash(originals[i],default))throw new InvalidOperationException("Imported source modified");
            File.WriteAllText(Path.Combine(App.DataDirectory,restart?"import-restart.json":"import-verification.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Routed synthetic WPF drag events and real metadata/thumbnail/index/map/copy operations; physical Explorer drag and mouse/key gestures NOT_TESTED",DefaultHybrid=true,Restart=restart,Items=ViewModel.Items.Count,Located=ViewModel.Items.Count(m=>m.HasLocation),Stages=stages,ImportHeartbeatMaximumMilliseconds=maximumGap,SourceHashesPreserved=originals.Length,CopyOperations=copied,TileCacheEntries=Atlas.ActiveTiles.CacheCount,Previews=ViewModel.Previews.MemoryEntries},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{heartbeat.Stop();HideImportOverlay();}
    }
}
