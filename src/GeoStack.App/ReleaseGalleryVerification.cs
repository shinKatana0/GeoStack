// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    // Bounded, explicitly requested release screenshots; no download or organization execution.
    internal async Task VerifyReleaseGallery()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."));
        var expected=Path.GetFullPath(Path.Combine(root,".local/release055/gallery"));
        if(!App.DataDirectory.Equals(expected,StringComparison.OrdinalIgnoreCase) || !Directory.GetCurrentDirectory().Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Dedicated gallery profile and working directory required");
        var inputs=Path.Combine(root,".local/geographic-acceptance/media");
        if(Directory.EnumerateDirectories(inputs,"*",SearchOption.AllDirectories).Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0))throw new InvalidDataException("No reparse points in demo inputs");
        var files=Directory.GetFiles(inputs,"*",SearchOption.AllDirectories);if(files.Length is <1 or >32)throw new InvalidDataException("Existing small procedural demonstration kit required");
        var hashes=new Dictionary<string,string>();foreach(var file in files)hashes[file]=await GeographicCopy.Hash(file,default);
        await AtlasReady;await libraryInitialized.Task;
        foreach(var folder in new[]{"GeoStack-geographic-data-0.4","GeoStack-geographic-data-0.5.5"})foreach(var path in Directory.GetFiles(Path.Combine(root,"dist",folder),"*.gsgeo"))App.GeographicData.Import(await Task.Run(()=>GeographicPackage.Inspect(path)));
        var catalogs=new[]{new MapCatalog(Path.Combine(root,".local/owner053/preview")),new MapCatalog(Path.Combine(root,".local/download05-final-runtime"))};
        foreach(var map in catalogs.SelectMany(c=>c.Packages).Where(p=>p.Available))if(!Atlas.Tiles.Catalog.Packages.Any(p=>p.Id==map.Id))Atlas.Tiles.Catalog.Packages.Add(map);
        Atlas.Tiles.Catalog.Save();await ScanFolder(inputs);await placesRefresh;Width=1360;Height=880;
        if(Atlas.AtlasPilot?.UrbanRoads==null || Atlas.AtlasPilot.GlobalAtlas!=null)throw new InvalidDataException("Accepted lightweight normal atlas required");
        var output=Path.Combine(App.DataDirectory,"screenshots");Directory.CreateDirectory(output);var frames=new List<object>();
        async Task Capture(string name,double lat,double lon,int zoom,string? map=null)
        {
            Atlas.Tiles.Catalog.Preferred=map??"world";var sequence=Atlas.CompletedRenderSequence;Atlas.SetComparisonCamera(lat,lon,zoom);
            for(var i=0;i<200;i++){await Task.Delay(50);if(Atlas.CompletedRenderSequence>sequence && !Atlas.ActiveTiles.IsLoading && !Atlas.IsAnimating)break;}
            if(Atlas.CompletedRenderSequence<=sequence || Atlas.ActiveTiles.IsLoading || Atlas.IsAnimating)throw new TimeoutException("Gallery frame did not settle");
            await Task.Delay(100);var file=name+".png";FeatureVerification.Snapshot(this,Path.Combine(output,file));
            frames.Add(new{File=file,Latitude=lat,Longitude=lon,Zoom=zoom,Atlas.Camera,Atlas.AtlasPilot.LastRasterVisible,Roads=Atlas.AtlasPilot.UrbanRoads.LastDrawn});
        }
        void RelativeDemoPaths(DependencyObject element)
        {
            if(element is TextBlock text)text.Text=text.Text.Replace(App.DataDirectory,"[demo profile]",StringComparison.OrdinalIgnoreCase);
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)RelativeDemoPaths(VisualTreeHelper.GetChild(element,i));
        }
        foreach(var theme in new[]{"Light","Dark"})
        {
            ApplySettings("en-US",theme);
            await Capture("world-"+theme,0,0,2);
            await Capture("urban-context-"+theme,31.2304,121.4737,9);
            var prague=Atlas.Tiles.Catalog.Packages.First(p=>p.Id.StartsWith("library-",StringComparison.Ordinal));
            await Capture("detailed-city-"+theme,50.087,14.421,15,prague.Id);
            if(!Atlas.AtlasPilot.LastRasterVisible)throw new InvalidOperationException("Installed detailed map not rendered");
            await Capture("photo-stacks-"+theme,35.6812,139.7671,12);
            var chosen=ViewModel.Items.Where(m=>m.HasLocation).DistinctBy(m=>(m.Latitude,m.Longitude)).Take(3).ToArray();selectedMedia.Clear();selectedMedia.All(chosen.Select(m=>m.Path));RestoreGridSelection();
            var copy=new CopyWindow(this,chosen);copy.Show();await copy.PrepareVerification("Organized demo media");FeatureVerification.Snapshot(copy,Path.Combine(output,"organization-"+theme+".png"));copy.Close();
            var download=new MapDownloadsWindow(this,this);download.Show();
            try
            {
                for(var i=0;i<200 && download.Rows.Count==0;i++)await Task.Delay(50);
                if(download.Rows.Count==0)throw new InvalidOperationException("No offline download plan");
                download.UpdateLayout();RelativeDemoPaths(download);FeatureVerification.Snapshot(download,Path.Combine(output,"download-preview-"+theme+".png"));
            }
            finally{download.Close();}
        }
        foreach(var pair in hashes)if(await GeographicCopy.Hash(pair.Key,default)!=pair.Value)throw new IOException("Demonstration source changed");
        if(Directory.GetFiles(output).Sum(p=>new FileInfo(p).Length)>8*1024*1024)throw new IOException("Bounded gallery budget exceeded");
        File.WriteAllText(Path.Combine(App.DataDirectory,"gallery.json"),JsonSerializer.Serialize(new{Status="PASS",Version=RuntimeIdentity.ProductVersion,RuntimeIdentity.Executable,Profile=App.DataDirectory,PID=Environment.ProcessId,Assemblies=RuntimeIdentity.Assemblies(),Frames=frames,SourceHashes=hashes,NetworkAcquisitionInvoked=false,OperationExecuted=false,Privacy="Procedural scenery and synthetic GPS only; storage labels relative to demo profile",PhysicalAcceptance="NOT_TESTED"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
