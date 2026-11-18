// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyMapP0(string root,bool baseline)
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45)); await AtlasReady;
        var output=App.DataDirectory;var evidence=Path.Combine(output,"evidence");Directory.CreateDirectory(evidence);
        var source=new MapCatalog(Path.Combine(root,".local/browsing-ux/app-data"));
        Atlas.Tiles.Catalog.Packages.Clear();Atlas.Tiles.Catalog.Packages.AddRange(source.Packages);Atlas.Tiles.Catalog.Preferred=source.Packages.Single(p=>p.Name=="GeoStack Warsaw city streets").Id;
        Atlas.Tiles.Clear();Atlas.Tiles.Diagnostics.Enabled=true;ApplySettings("en-US","Dark");
        using var controlledIndex=new MediaIndex(Path.Combine(root,".local/browsing-ux/app-data/index.db"));var approved=controlledIndex.ReadAll().ToArray();
        if(approved.Length!=9 || approved.Any(m=>!m.Path.StartsWith(Path.GetFullPath(Path.Combine(root,".local/browsing-ux/media"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Controlled media required");
        var hashes=await Task.WhenAll(approved.Select(m=>GeographicCopy.Hash(m.Path,default)));
        var stage="setup";var stageGaps=new Dictionary<string,double>();long monitoredPeak=0;
        var heartbeats=0;var maxGap=0d;var last=Stopwatch.GetTimestamp();var durations=new List<double>();
        var heartbeat=new DispatcherTimer(DispatcherPriority.Normal) { Interval=TimeSpan.FromMilliseconds(25) };
        heartbeat.Tick+=(_,_)=>{var now=Stopwatch.GetTimestamp();var gap=Stopwatch.GetElapsedTime(last,now).TotalMilliseconds;maxGap=Math.Max(maxGap,gap);last=now;stageGaps[stage]=Math.Max(stageGaps.GetValueOrDefault(stage),gap);heartbeats++;};heartbeat.Start();
        var inputBeats=0;var inputMaxGap=0d;var inputLast=Stopwatch.GetTimestamp();var inputStageGaps=new Dictionary<string,double>();
        var inputHeartbeat=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(25)};
        inputHeartbeat.Tick+=(_,_)=>{var now=Stopwatch.GetTimestamp();var gap=Stopwatch.GetElapsedTime(inputLast,now).TotalMilliseconds;inputLast=now;inputMaxGap=Math.Max(inputMaxGap,gap);inputStageGaps[stage]=Math.Max(inputStageGaps.GetValueOrDefault(stage),gap);inputBeats++;};inputHeartbeat.Start();
        using var monitorCancel=new CancellationTokenSource();var monitor=Task.Run(async()=>{
            try { while(!monitorCancel.IsCancellationRequested) { using var process=Process.GetCurrentProcess();Interlocked.Exchange(ref monitoredPeak,Math.Max(Interlocked.Read(ref monitoredPeak),process.PrivateMemorySize64));File.WriteAllText(Path.Combine(output,"p0-heartbeat.json"),JsonSerializer.Serialize(new{InputHeartbeats=inputBeats,MillisecondsSinceInputHeartbeat=Stopwatch.GetElapsedTime(inputLast).TotalMilliseconds,Heartbeats=heartbeats,MillisecondsSinceHeartbeat=Stopwatch.GetElapsedTime(last).TotalMilliseconds,process.WorkingSet64,process.PrivateMemorySize64,Threads=process.Threads.Count,Metrics=Atlas.Tiles.Diagnostics.Snapshot()}));await Task.Delay(1000,monitorCancel.Token); } }catch(OperationCanceledException){}
        });
        async Task Capture(string name) { stage="evidence-capture";FeatureVerification.Snapshot(this,Path.Combine(evidence,name));await Task.Delay(30);stage="settling"; }
        var reproduced=false;var runs=new List<object>();var stopwatch=Stopwatch.StartNew();using var proc=Process.GetCurrentProcess();var beforeMemory=proc.PrivateMemorySize64;var peakMemory=beforeMemory;
        foreach(var media in new[]{false,true})
        {
            Atlas.Items=media?approved:[];Atlas.SetProbeCamera(52.2297,21.0122,15);await Task.Delay(1000);
            await Capture(media?"warsaw-with-media.png":"warsaw-no-media.png");
            var old=new Vector();var changes=baseline?450:600;
            for(var i=0;i<changes;i++)
            {
                var step=Stopwatch.GetTimestamp();stage=media?"panning-with-media":"panning-no-media";
                var x=(i%30<15?i%15-7:7-i%15)*256d;var y=((i/30)%9-4)*256d;
                if(!baseline && i%100==0) { Atlas.SetProbeCamera(52.2297,21.0122,12+(i/100)%4);old=new(); }
                var next=new Vector(x,y);Atlas.PanPixels(next-old);old=next;
                if(!baseline && i%73==0){Width=i%146==0?1100:1360;Height=i%146==0?700:880;}
                if(!baseline && i%75==0)Atlas.PanPixels(new Vector(i%150==0?8000:-8000,0));
                if(!baseline && i%133==0) { Atlas.Tiles.Catalog.Preferred=source.Packages.Single(p=>p.Name==(i%266==0?"GeoStack Kyoto city streets":"GeoStack Warsaw city streets")).Id;Atlas.Tiles.Clear(); }
                await Task.Delay(16);durations.Add(Stopwatch.GetElapsedTime(step).TotalMilliseconds);
                if(baseline && Atlas.Tiles.Diagnostics.DuplicateRequests>500)break;
            }
            stage="settling";Atlas.SetProbeCamera(52.2297,21.1622,15);await Task.Delay(800);await Capture(media?"boundary-with-media.png":"boundary-no-media.png");
            Atlas.SetProbeCamera(34.9858,135.7588,15);var stationary=Atlas.Tiles.Diagnostics.Requests;await Task.Delay(2500);var first=Atlas.Tiles.Diagnostics.Requests;await Task.Delay(1500);var repeated=Atlas.Tiles.Diagnostics.Requests-first;
            await Capture(media?"kyoto-after-pan.png":"kyoto-basemap-only.png");
            runs.Add(new{MediaOverlays=media,ViewportChanges=changes,StationaryRequests=first-stationary,ContinuedStationaryRequests=repeated,Cache=Atlas.Tiles.CacheCount,Pending=Atlas.Tiles.PendingCount,Diagnostics=Atlas.Tiles.Diagnostics.Snapshot()});
            if(!baseline && (repeated>0 || Atlas.ActiveTiles.IsLoading))throw new InvalidOperationException("Stationary tile workload did not settle");
            if(baseline && repeated>30){reproduced=true;break;}
        }
        long missingQueries=0,missingStationaryRepeats=0;
        if(!baseline)
        {
            var hole=MapPackage.Inspect(Path.Combine(root,".local/map-fixtures/raster.mbtiles"));Atlas.Tiles.Catalog.Packages.Add(hole);Atlas.Tiles.Catalog.Preferred=hole.Id;Atlas.Tiles.Clear();Atlas.SetProbeCamera(0,0,3);
            stage="missing-heavy";var startMissing=Atlas.Tiles.Diagnostics.Missing;
            for(var i=0;i<180;i++){Atlas.PanPixels(new Vector(i%2==0?190:-155,i%3==0?90:-30));await Task.Delay(12);}
            await SettleBrowsing();missingQueries=Atlas.Tiles.Diagnostics.Missing-startMissing;var afterMissing=Atlas.Tiles.Diagnostics.Requests;await Task.Delay(1000);missingStationaryRepeats=Atlas.Tiles.Diagnostics.Requests-afterMissing;
            if(missingStationaryRepeats!=0 || missingQueries==0)throw new InvalidOperationException("Missing-tile fixture did not settle");
            Atlas.Tiles.Catalog.Packages.Remove(hole);
            Atlas.Items=approved;
            var warsaw=Atlas.Tiles.Catalog.Packages.Single(p=>p.Name=="GeoStack Warsaw city streets");
            Atlas.Tiles.Catalog.Preferred=warsaw.Id;Atlas.Tiles.Clear();
            foreach(var location in new[]{(Label:"warsaw-returned",Lon:21.0122),(Label:"approaching-boundary",Lon:21.0722),(Label:"crossed-boundary",Lon:21.1622),(Label:"western-boundary",Lon:20.8522),(Label:"outside-context",Lon:23.5),(Label:"warsaw-recovered",Lon:21.0122)}) {
                Atlas.SetProbeCamera(52.2297,location.Lon,15);await SettleBrowsing();await Capture(location.Label+".png");
            }
            await NavigateToMedia(approved.Single(m=>m.Name=="Kyoto.jpg"));await SettleBrowsing();
            var japan=outside.FirstOrDefault(g=>g.Items.Any(m=>m.Name=="Tokyo.jpg"));if(japan!=null){await NavigateDestination(japan);await SettleBrowsing();}
            var manager=new MapManagerWindow(this,Atlas);manager.Show();manager.SelectMap(Atlas.Tiles.Catalog.Packages.Single(p=>p.Name=="GeoStack Zurich city streets").Id);manager.ActivateSelected();manager.Close();await SettleBrowsing();
            await Capture("second-city-recovered.png");
            var before=Atlas.MediaPositionsForVerification();Atlas.Tiles.Catalog.Preferred="world";Atlas.Tiles.Clear();await Task.Delay(300);var after=Atlas.MediaPositionsForVerification();if(before.Any(p=>!after.TryGetValue(p.Key,out var point)||(point-p.Value).Length>.01))throw new InvalidOperationException("Marker alignment changed");
            Atlas.Tiles.Catalog.Preferred=warsaw.Id;Atlas.Tiles.Clear();Atlas.SetProbeCamera(52.2297,21.0122,15);await SettleBrowsing();
        }
        if(!baseline && maxGap>1000)throw new InvalidOperationException("UI dispatcher stalled for more than one second");
        Atlas.Tiles.Clear();await Atlas.Tiles.DisposeAsync();heartbeat.Stop();inputHeartbeat.Stop();monitorCancel.Cancel();await monitor;
        for(var i=0;i<approved.Length;i++)if(hashes[i]!=await GeographicCopy.Hash(approved[i].Path,default))throw new InvalidOperationException("Fixture changed");
        proc.Refresh();peakMemory=Math.Max(peakMemory,Interlocked.Read(ref monitoredPeak));File.WriteAllText(Path.Combine(output,"p0-report.json"),JsonSerializer.Serialize(new{Status=baseline?(reproduced?"FAIL":"NOT_REPRODUCED"):"PASS",Scope=baseline?"Original reproduction sequence; physical drag NOT_TESTED":"Packaged programmatic stress; physical drag NOT_TESTED",ElapsedSeconds=stopwatch.Elapsed.TotalSeconds,MaxDispatcherGapMilliseconds=maxGap,DispatcherStageMaxima=stageGaps,MaxInputDispatcherGapMilliseconds=inputMaxGap,InputDispatcherStageMaxima=inputStageGaps,InputHeartbeats=inputBeats,Heartbeats=heartbeats,PeakPrivateMemory=peakMemory,PrivateMemoryBefore=beforeMemory,PrivateMemoryAfter=proc.PrivateMemorySize64,WorkingSet=proc.WorkingSet64,Threads=proc.Threads.Count,MissingTileQueries=missingQueries,MissingStationaryRepeats=missingStationaryRepeats,SourceHashesPreserved=9,FinalDiagnostics=Atlas.Tiles.Diagnostics.Snapshot(),Runs=runs},new JsonSerializerOptions{WriteIndented=true}));
    }
}
