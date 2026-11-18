// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GeoStack.Core;

namespace GeoStack.App;

internal sealed record AcquisitionProgress(int Completed,int Total,long Bytes,string Stage);
internal static class MapAcquisition
{
    // A single STA owns all WPF graphics and SQLite writers. No live UI objects cross this boundary.
    public static Task<MapPackage> Prepare(MapDownloadArea area,string storage,IProgress<AcquisitionProgress> progress,CancellationToken token,Func<IRegionalMapSource>? sourceFactory=null)
    {
        var completion=new TaskCompletionSource<MapPackage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=>{try{completion.SetResult(PrepareOnWorker(area,storage,progress,token,sourceFactory));}catch(OperationCanceledException){completion.SetCanceled(token);}catch(Exception ex){completion.SetException(ex);}finally{System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();}}){IsBackground=true,Name="GeoStack map preparation"};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();return completion.Task;
    }
    private static MapPackage PrepareOnWorker(MapDownloadArea area,string storage,IProgress<AcquisitionProgress> progress,CancellationToken token,Func<IRegionalMapSource>? sourceFactory)
    {
        if(!area.Supported || area.Installed || area.Tiles.Count>LibraryMapPlanner.TileLimit)throw new InvalidDataException("DownloadUnsupported");
        Directory.CreateDirectory(storage);var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(storage))!);
        if(drive.AvailableFreeSpace<area.MaximumInstalledBytes+64L*1024*1024)throw new IOException("DownloadDiskSpace");
        var temporary=Path.Combine(storage,".pending-"+Guid.NewGuid().ToString("N"));var final=Path.Combine(storage,area.Id+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        var styles=new[]{(false,"en-US","-city"),(false,"ru-RU","-light-ru-RU"),(true,"en-US","-dark-en-US"),(true,"ru-RU","-dark-ru-RU")};
        var writers=new List<RasterPackageWriter>();var files=new List<string>();var clock=System.Diagnostics.Stopwatch.StartNew();var stage=System.Diagnostics.Stopwatch.StartNew();double sourceMs=0,decodeMs=0,rasterMs=0;var completed=0;long features=0,roads=0,water=0;Exception? failure=null;
        try
        {
            using var source=sourceFactory?.Invoke()??new PmtilesSource();source.Initialize(token).GetAwaiter().GetResult();sourceMs+=stage.Elapsed.TotalMilliseconds;
            foreach(var style in styles)
            {
                var file=Path.Combine(temporary,area.Id+style.Item3+".mbtiles");files.Add(file);
                writers.Add(new(file,new Dictionary<string,string>{{"name",(area.Place?.Display(style.Item2)??"")+" / "+area.CoordinateLabel+" / "+style.Item2},{"format","png"},{"type","baselayer"},{"version","1"},{"minzoom",area.Tiles.Min(t=>t.Zoom).ToString(System.Globalization.CultureInfo.InvariantCulture)},{"maxzoom",area.Tiles.Max(t=>t.Zoom).ToString(System.Globalization.CultureInfo.InvariantCulture)},{"bounds",FormattableString.Invariant($"{area.West},{area.South},{area.East},{area.North}")},{"attribution",PmtilesSource.Attribution},{"source",PmtilesSource.ProviderUrl}}));
            }
            foreach(var tile in area.Tiles)
            {
                token.ThrowIfCancellationRequested();stage.Restart();var bytes=source.Tile(tile.Zoom,tile.X,tile.Y,token).GetAwaiter().GetResult();
                // Missing source tiles fail this area rather than registering holes as complete coverage.
                if(bytes==null)throw new InvalidDataException("DownloadSourceMissing");
                sourceMs+=stage.Elapsed.TotalMilliseconds;stage.Restart();var data=VectorTile.Decode(bytes);decodeMs+=stage.Elapsed.TotalMilliseconds;stage.Restart();features+=data.Count;roads+=data.Count(f=>f.Layer=="roads");water+=data.Count(f=>f.Layer=="water");
                for(var i=0;i<styles.Length;i++){token.ThrowIfCancellationRequested();writers[i].Add(tile,MapTileRasterizer.Render(data,styles[i].Item1,styles[i].Item2));}
                rasterMs+=stage.Elapsed.TotalMilliseconds;progress.Report(new(++completed,area.Tiles.Count,source.DownloadedBytes,"DownloadPreparing"));
            }
            foreach(var writer in writers){writer.Complete();writer.Dispose();}writers.Clear();
            var inspected=files.Select(MapPackage.Inspect).ToArray();
            foreach(var file in files){token.ThrowIfCancellationRequested();using var probe=new MapProbe(file);probe.Verify();}
            var checksums=files.ToDictionary(f=>Path.GetFileName(f)!,f=>{using var input=File.OpenRead(f);return Convert.ToHexString(SHA256.HashData(input));});
            File.WriteAllText(Path.Combine(temporary,"manifest.json"),JsonSerializer.Serialize(new{Schema=1,area.IsContext,MinZoom=area.Tiles.Min(t=>t.Zoom),MaxZoom=area.Tiles.Max(t=>t.Zoom),ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds,SourceMilliseconds=sourceMs,DecodeMilliseconds=decodeMs,RasterAndWriteMilliseconds=rasterMs,Provider=PmtilesSource.ProviderUrl,SourceIdentity=source.Identity,SourceFreshness=source.Freshness,SourceBytes=source.DownloadedBytes,Attribution=PmtilesSource.Attribution,area.West,area.South,area.East,area.North,Tiles=completed,Features=features,Roads=roads,Water=water,Checksums=checksums},new JsonSerializerOptions{WriteIndented=true}));
            token.ThrowIfCancellationRequested();Directory.Move(temporary,final);
            var maps=inspected.Select(m=>m with {Path=Path.Combine(final,Path.GetFileName(m.Path))}).ToArray();
            return maps[0] with { Id=area.Id,Variants=maps.Skip(1).ToArray() };
        }
        catch(Exception ex){failure=ex;throw;}
        finally
        {
            foreach(var writer in writers)writer.Dispose();
            // Only this operation's generated directory is eligible for cleanup; external maps are never touched.
            var attempts=0;
            bool StillListed()
            {
                try{return Directory.EnumerateFileSystemEntries(storage,Path.GetFileName(temporary),SearchOption.TopDirectoryOnly).Any();}
                catch(DirectoryNotFoundException){return false;}
            }
            // A successful Windows removal can briefly leave the empty directory visible to its
            // parent enumeration. Complete the operation only after that owned entry disappears.
            for(var retry=0;retry<6;retry++)
            {
                try{attempts++;Directory.Delete(temporary,true);}
                catch(DirectoryNotFoundException){ }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ }
                if(!StillListed())break;
                if(retry<5)Thread.Sleep(50*(retry+1));
            }
            if(failure!=null)failure.Data["PendingCleanup"]=$"attempts={attempts}; stillListed={StillListed()}";
        }
    }
}
