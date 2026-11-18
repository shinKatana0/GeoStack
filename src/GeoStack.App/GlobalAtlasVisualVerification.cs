// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyGlobalVisual(string baseline,string optimized)
    {
        if(!App.DataDirectory.Contains("\\.local\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated synthetic profile required");
        await AtlasReady;await libraryInitialized.Task;Width=1360;Height=880;await SettleBrowsing();
        var media=ViewModel.Items.ToArray();if(media.Length!=9 || media.Any(m=>!m.Path.Contains("\\.local\\browsing-ux\\media\\",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Approved media required");
        var hashes=new Dictionary<string,string>();foreach(var m in media)hashes[m.Path]=await GeographicCopy.Hash(m.Path,default);
        var pilot=Atlas.AtlasPilot!;var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);
        var poses=new[]{("Warsaw",52.2297,21.0122,8),("Tokyo",35.6762,139.6503,8),("Zurich",47.3782,8.5402,10),("Kyoto",35.0116,135.7681,10),("Prague",50.0755,14.4378,10),("Europe-Rural",46.3,2.2,9),("USA-Rural",39.5,-99.1,8),("Central-Asia",43.2,76.9,8),("South-America",-16d,-49d,8),("Australia",-30.5,148.5,8),("Coast",35.4,140.2,9),("Date-Line",-17.8,179.8,8),("Tokyo-6",35.6762,139.6503,6),("Tokyo-7",35.6762,139.6503,7),("City-Edge",52.2297,21.09,12),("City-Detail",52.2297,21.0122,15)};
        var rows=new List<object>();var references=new Dictionary<string,(MapCamera Camera,IReadOnlyDictionary<string,Point> Points)>();
        foreach(var mode in new[]{"production-model","original","optimized"})
        {
            await pilot.InstallGlobalAtlas(mode=="optimized"?optimized:baseline);pilot.GlobalAtlas!.Enabled=mode!="production-model";
            foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
            {
                ApplySettings(language,theme);
                foreach(var pose in poses)
                {
                    Atlas.SetComparisonCamera(pose.Item2,pose.Item3,pose.Item4);var timer=Stopwatch.StartNew();var ready=false;
                    for(var i=0;i<600;i++)
                    {
                        Atlas.InvalidateVisual();await Task.Delay(20);var c=Atlas.Camera;
                        if(Atlas.IsAnimating || Atlas.ActiveTiles.IsLoading)continue;
                        if(pilot.GlobalAtlas.Enabled && !pilot.GlobalAtlas.CanCover(c.World,c.Width,c.Height,new(c.X,c.Y)))continue;
                        ready=true;break;
                    }
                    if(!ready)throw new TimeoutException("Visual viewport failed to settle: "+mode+"/"+pose.Item1);
                    var key=language+theme+pose.Item1;var points=Atlas.MediaPositionsForVerification();
                    if(mode=="production-model")references[key]=(Atlas.Camera,points);
                    else if(references[key].Camera!=Atlas.Camera || points.Any(p=>references[key].Points[p.Key]!=p.Value))throw new InvalidOperationException("Marker/camera drift");
                    await Task.Delay(180);var name=$"{mode}-{pose.Item1}-{language}-{theme}";FeatureVerification.Snapshot(this,Path.Combine(evidence,name+".png"));
                    rows.Add(new{Name=name,Mode=mode,Atlas.Camera,ReadyMilliseconds=timer.Elapsed.TotalMilliseconds,MarkerAlignmentVerified=true,Resources=pilot.ResourceSnapshot()});
                }
            }
        }
        foreach(var h in hashes)if(await GeographicCopy.Hash(h.Key,default)!=h.Value)throw new InvalidOperationException("Source changed");
        File.WriteAllText(Path.Combine(App.DataDirectory,"global-visual.json"),JsonSerializer.Serialize(new{Status="COMPLETED",Scope="Actual native WPF frames and programmatic camera/marker equality; physical gestures NOT_TESTED. Production-model is the candidate with global context disabled, not modified production binaries.",SourceHashes=hashes,Captures=rows},new JsonSerializerOptions{WriteIndented=true}));
    }
}
