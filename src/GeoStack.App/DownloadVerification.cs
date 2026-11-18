// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using GeoStack.Core;
namespace GeoStack.App;
public partial class MainWindow
{
    internal async Task VerifyDownloads05(bool acquire)
    {
        if(!Path.GetFullPath(App.DataDirectory).Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated local profile required");
        EnvironmentVerification.Save();await AtlasReady;var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        var fixture=Path.Combine(Path.GetFullPath(Path.Combine(App.DataDirectory,"..")),"download05-fixtures");
        var sourceHashes=Directory.GetFiles(fixture).ToDictionary(p=>p,p=>{using var input=File.OpenRead(p);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));});
        object? responsiveness=null;
        if(acquire)
        {
            await ScanFolder(fixture);
            if(ViewModel.Items.Count!=1 || !ViewModel.Items[0].HasLocation)throw new InvalidOperationException("Synthetic Prague GPS fixture required");
            var dialog=new MapDownloadsWindow(this,this);Exception? failure=null;
            dialog.ContentRendered+=async(_,_)=>
            {
                var phase="coverage-analysis";
                using var probe=new UiResponsivenessProbe(dialog,()=>phase,"timing");
                try
                {
                    for(var i=0;i<200 && dialog.Rows.Count==0;i++)await Task.Delay(100);
                    if(!dialog.Rows.Any(r=>!r.Area.Installed) || !dialog.Rows.Any(r=>r.Area.IsContext))throw new InvalidOperationException("Missing regional coverage not proposed");
                    FeatureVerification.Snapshot(dialog,Path.Combine(evidence,"download-preview.png"));
                    phase="download-conversion-installation";
                    await dialog.DownloadSelected();
                    phase="settling";await Task.Delay(500);responsiveness=probe.Report();
                    if(dialog.Rows.Any(r=>!Atlas.Tiles.Catalog.Packages.Any(m=>m.Id==r.Area.Id)))throw new InvalidOperationException("Acquisition failed; not all proposed levels registered");
                }
                catch(Exception ex){failure=ex;}
                finally{dialog.Close();}
            };
            dialog.ShowDialog();if(failure!=null)throw failure;
        }
        var map=Atlas.Tiles.Catalog.Packages.Single(m=>m.Id.StartsWith("library-"));
        await NavigateToMedia(ViewModel.Items.Single());await SettleBrowsing();
        if(Atlas.SelectedMediaId!=ViewModel.Items.Single().Path)throw new InvalidOperationException("Downloaded map media selection lost");
        var captures=new List<object>();
        foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
        {
            ApplySettings(language,theme);Atlas.SetProbeCamera(50.087,14.421,15);
            await Task.Delay(250);await SettleBrowsing();
            for(var i=0;i<100 && !Atlas.AtlasPilot!.LastRasterVisible;i++){Atlas.InvalidateVisual();await Task.Delay(100);}
            if(!Atlas.AtlasPilot!.LastRasterVisible)throw new InvalidOperationException("Downloaded raster never displayed");
            var active=Atlas.ActiveTiles.Catalog.Packages.Single(p=>p.Id==Atlas.ActiveTiles.Catalog.Preferred);
            if(!NativeAtlasPilot.MatchesCityTheme(Path.GetFileName(active.Path),theme=="Dark",language))throw new InvalidOperationException("Wrong map style");
            captures.Add(new{Theme=theme,Language=language,Map=Path.GetFileName(active.Path),Atlas.ActiveTiles.DrawnTileCount});
            FeatureVerification.Snapshot(this,Path.Combine(evidence,"Prague-"+language+"-"+theme+".png"));
        }
        var plan=await Task.Run(()=>LibraryMapPlanner.Build(ViewModel.Items,Atlas.Tiles.Catalog.Packages,App.LocationResolver.Resolve,CancellationToken.None));
        if(plan.Areas.Any(a=>!a.Installed))throw new InvalidOperationException("Installed coverage still missing");
        foreach(var pair in sourceHashes){using var input=File.OpenRead(pair.Key);if(pair.Value!=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)))throw new InvalidOperationException("Source fixture changed");}
        File.WriteAllText(Path.Combine(App.DataDirectory,acquire?"download05.json":"download05-restart.json"),JsonSerializer.Serialize(new{Status="PASS",Mode=acquire?"Actual provider acquisition and routed dialog workflow":"Offline catalog restart and render",map.Id,map.InstalledSize,SourceHashesPreserved=sourceHashes.Count,Captures=captures,Variants=map.Variants?.Count,Atlas.Camera,Atlas.AtlasPilot!.LastRasterVisible,Responsiveness=responsiveness,PhysicalInteraction="NOT_TESTED"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
