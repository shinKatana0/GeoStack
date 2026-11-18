// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using GeoStack.Core;

namespace GeoStack.App;
public partial class MainWindow
{
    private FileStream? organizationProfileLock;
    internal async Task PrepareOrganization055(bool verify)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."));var owned=Path.GetFullPath(Path.Combine(root,".local","owner055"))+Path.DirectorySeparatorChar;
        if(!App.DataDirectory.StartsWith(owned,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Isolated 0.5.5 profile required");
        organizationProfileLock=new FileStream(Path.Combine(App.DataDirectory,".organization-preview.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);Closed+=(_,_)=>organizationProfileLock.Dispose();
        await AtlasReady;await libraryInitialized.Task;Width=1360;Height=880;
        var fixture=Path.Combine(root,".local/owner052/validation-media");var hashes=new Dictionary<string,string>();foreach(var file in Directory.GetFiles(fixture))hashes[file]=await GeographicCopy.Hash(file,default);
        foreach(var folder in new[]{"GeoStack-geographic-data-0.4","GeoStack-geographic-data-0.5.5"})foreach(var path in Directory.GetFiles(Path.Combine(root,"dist",folder),"*.gsgeo"))App.GeographicData.Import(await Task.Run(()=>GeographicPackage.Inspect(path)));
        var maps=new MapCatalog(Path.Combine(root,".local/owner053/preview"));foreach(var map in maps.Packages.Where(m=>m.Available))if(!Atlas.Tiles.Catalog.Packages.Any(m=>m.Path==map.Path))Atlas.Tiles.Catalog.Packages.Add(map);Atlas.Tiles.Catalog.Save();
        var media=Path.Combine(App.DataDirectory,"media");var seeded=Path.Combine(App.DataDirectory,".approved-media-seeded.json");Directory.CreateDirectory(media);
        if(!File.Exists(seeded))
        {
            foreach(var source in hashes.Keys){var destination=Path.Combine(media,Path.GetFileName(source));if(!File.Exists(destination))File.Copy(source,destination,false);if(await GeographicCopy.Hash(destination,default)!=hashes[source])throw new IOException("Existing preview media differs; preserved without overwrite");}
            File.WriteAllText(seeded,JsonSerializer.Serialize(hashes));
        }
        await ScanFolder(media);await placesRefresh;
        var poses=new[]{("Kanazawa",36.5781,136.6477),("Kyoto",34.9858,135.7588),("Tokyo",35.6812,139.7671),("SaintPetersburg",59.9343,30.3351),("Shanghai",31.2304,121.4737),("Zurich",47.3782,8.5402),("Warsaw",52.2297,21.0122)};
        var proposals=new List<object>();foreach(var pose in poses)
        {
            var place=App.LocationResolver.Resolve(pose.Item2,pose.Item3);proposals.Add(new{Case=pose.Item1,Latitude=pose.Item2,Longitude=pose.Item3,English=place.Hierarchy("en-US","CountryCity"),Russian=place.Hierarchy("ru-RU","CountryCity"),place.Country,place.Region,place.City,place.District,place.Warnings});
            if(place.Country==null || place.City==null && place.Region==null){if(verify)throw new InvalidDataException("Required locality containment missing: "+pose.Item1);ViewModel.Status=Strings.Get("CityDataRequired");}
        }
        IReadOnlyList<CopyJournalEntry> copies=[];IReadOnlyList<CopyJournalEntry> moved=[];
        if(verify)
        {
            var chosen=ViewModel.Items.Where(m=>m.HasLocation && App.LocationResolver.Resolve(m.Latitude,m.Longitude) is { } p && (p.City!=null || p.Region!=null)).DistinctBy(m=>(m.Latitude,m.Longitude)).Take(3).ToArray();if(chosen.Length!=3)throw new InvalidDataException("Three fixture locations required");
            var copy=new CopyWindow(this,chosen);copy.Show();await copy.PrepareVerification(Path.Combine(App.DataDirectory,"copy"));FeatureVerification.Snapshot(copy,Path.Combine(App.DataDirectory,"copy-preview.png"));copies=await copy.ExecutePreparedForVerification();copy.Close();
            if(copies.Count(r=>r.Status==CopyState.Completed)!=3)throw new IOException("Three-file copy failed");
            var input=Path.Combine(App.DataDirectory,"inputs");Directory.CreateDirectory(input);var disposable=new List<string>();
            foreach(var item in chosen){var target=Path.Combine(input,item.Name);if(File.Exists(target))throw new IOException("Disposable input already exists; use a fresh named verification profile");File.Copy(item.Path,target,false);disposable.Add(target);}
            await ViewModel.ImportAsync(disposable);await placesRefresh;var originals=ViewModel.Items.Where(m=>disposable.Contains(m.Path)).ToArray();if(originals.Length!=3)throw new IOException("Disposable import failed");
            selectedMedia.Clear();selectedMedia.All(originals.Select(m=>m.Path));RestoreGridSelection();Atlas.Highlight(originals[0]);
            var move=new CopyWindow(this,originals);move.Show();move.SelectOperationForVerification(OrganizationOperation.Move);await move.PrepareVerification(Path.Combine(App.DataDirectory,"moved"));
            foreach(var language in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"}){ApplySettings(language,theme);move.Close();move=new CopyWindow(this,originals);move.Show();move.SelectOperationForVerification(OrganizationOperation.Move);await move.PrepareVerification(Path.Combine(App.DataDirectory,"moved"));FeatureVerification.Snapshot(move,Path.Combine(App.DataDirectory,$"move-{language}-{theme}.png"));}
            // Explicit release-only diagnostic; normal preview startup does not generate extra captures.
            if(Environment.GetEnvironmentVariable("GEOSTACK_RELEASE_ALIGNMENT")=="1")
            {
                foreach(var lang in new[]{"en-US","ru-RU"})foreach(var theme in new[]{"Light","Dark"})
                {
                    move.Close();ApplySettings(lang,theme);move=new CopyWindow(this,originals);move.Show();move.SelectOperationForVerification(OrganizationOperation.Move);await move.PrepareVerification(Path.Combine(App.DataDirectory,"moved"));move.CaptureReleaseAlignment(App.DataDirectory,lang+"-"+theme);
                }
            }
            moved=await move.ExecutePreparedForVerification();move.Close();if(moved.Count(r=>r.Status==CopyState.Completed)!=3)throw new IOException("Three-file move failed");
            if(!selectedMedia.Ids.SetEquals(moved.Select(r=>r.Destination)) || Atlas.SelectedMediaId!=moved.Single(r=>r.Source==originals[0].Path).Destination)throw new IOException("Move selection or map highlight lost its exact identity");
            foreach(var result in copies.Concat(moved))if(await GeographicCopy.Hash(result.Destination,default)!=result.Sha256)throw new IOException("Destination hash mismatch");
            using(var db=new MediaIndex(Path.Combine(App.DataDirectory,"index.db")))foreach(var result in moved)if(File.Exists(result.Source) || !db.Exclusions().Contains(result.Source) || !db.ReadAll().Any(m=>m.Path==result.Destination))throw new IOException("Move catalog consistency failure");
            if((await GeographicCopy.InspectInterruptedMoves(Path.Combine(App.DataDirectory,"CopyJournal"),default)).Count!=0)throw new IOException("Completed catalog moves remain unreconciled");
        }
        foreach(var pair in hashes)if(await GeographicCopy.Hash(pair.Key,default)!=pair.Value)throw new IOException("Approved original changed");
        File.WriteAllText(Path.Combine(App.DataDirectory,"organization055.json"),JsonSerializer.Serialize(new{Status="PASS",Scope=verify?"Packaged routed preview/mode selection and exact three-file copy/move; physical confirmation and cross-volume NOT_TESTED":"Prepared isolated owner preview; no move executed",RuntimeIdentity.ProductVersion,RuntimeIdentity.Executable,Profile=App.DataDirectory,PID=Environment.ProcessId,Assemblies=RuntimeIdentity.Assemblies(),Proposals=proposals,Copies=copies,Moves=moved,OriginalHashes=hashes,GeographicPackages=App.GeographicData.Datasets,Count=ViewModel.Items.Count},new JsonSerializerOptions{WriteIndented=true}));
    }
}
