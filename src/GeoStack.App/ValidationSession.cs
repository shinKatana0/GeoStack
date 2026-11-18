// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using GeoStack.Core;

namespace GeoStack.App;

internal sealed record ValidationMap(string Role,string Id,string Path,string[] Variants,bool Required);
internal sealed record ValidationInput(string ExpectedExecutable,string ExpectedProfile,string MediaDirectory,string DiagnosticsDirectory,string Session,bool CloseAfterChecks,bool CaptureEvidence,ValidationMap[] Maps);

/// <summary>Explicit, offline acceptance bootstrap. No normal-profile import or global process reuse.</summary>
internal sealed class ValidationSession : IDisposable
{
    internal ValidationInput Input { get; }
    private readonly FileStream profileLock;
    internal string ReportPath=>Path.Combine(Input.DiagnosticsDirectory,"startup-current.json");
    private ValidationSession(ValidationInput input)
    {
        Input=input;
        if(!Guid.TryParseExact(input.Session,"N",out _) || !Path.GetFullPath(input.ExpectedProfile).Equals(App.DataDirectory,StringComparison.OrdinalIgnoreCase) || !RuntimeIdentity.Executable.Equals(Path.GetFullPath(input.ExpectedExecutable),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Validation identity arguments do not match the running application.");
        var expected=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/owner052/validation"));
        if(!expected.Equals(App.DataDirectory,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The dedicated validation profile is required.");
        var local=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/owner052"))+Path.DirectorySeparatorChar;
        foreach(var path in new[]{input.MediaDirectory,input.DiagnosticsDirectory})if(!Path.GetFullPath(path).StartsWith(local,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Validation inputs must stay in the owned acceptance directory.");
        Directory.CreateDirectory(input.DiagnosticsDirectory);
        // An occupied profile fails explicitly. Production and other profiles have independent lifetimes.
        profileLock=new FileStream(Path.Combine(App.DataDirectory,".validation-instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    internal static ValidationSession? Open(string[] args)
    {
        var at=Array.IndexOf(args,"--validation-input");
        if(at<0)return null;
        if(at+1>=args.Length)throw new InvalidDataException("Missing validation input manifest.");
        return new(JsonSerializer.Deserialize<ValidationInput>(File.ReadAllText(args[at+1]))??throw new InvalidDataException("Invalid validation input manifest."));
    }
    internal static void FailStartup(string[] args,Exception error)
    {
        var at=Array.IndexOf(args,"--validation-input");if(at<0 || at+1>=args.Length)return;
        try
        {
            var input=JsonSerializer.Deserialize<ValidationInput>(File.ReadAllText(args[at+1]));
            var expected=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/owner052/diagnostics"));
            if(input==null || !Guid.TryParseExact(input.Session,"N",out _) || !Path.GetFullPath(input.DiagnosticsDirectory).Equals(expected,StringComparison.OrdinalIgnoreCase))return;
            var file=Path.Combine(expected,"startup-current.json");var tmp=file+".tmp";
            File.WriteAllText(tmp,JsonSerializer.Serialize(new{Status="FAIL",input.Session,ProcessId=Environment.ProcessId,Error=error.GetType().Name+": "+error.Message,TimeUTC=DateTime.UtcNow}));File.Move(tmp,file,true);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){ }
    }
    internal void Write(object report)
    {
        var tmp=ReportPath+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));File.Move(tmp,ReportPath,true);
    }
    internal void Fail(Exception error)=>Write(new{Status="FAIL",Input.Session,ProcessId=Environment.ProcessId,ActualExecutable=RuntimeIdentity.Executable,ActualProfile=App.DataDirectory,Error=error.GetType().Name+": "+error.Message,TimeUTC=DateTime.UtcNow});
    public void Dispose()=>profileLock.Dispose();
}

public partial class MainWindow
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    internal async Task PrepareValidationSession(ValidationSession session)
    {
        await AtlasReady;await libraryInitialized.Task;
        var input=session.Input;
        if(RuntimeIdentity.Channel!="Preview" || RuntimeIdentity.Version!="0.5.2")throw new InvalidDataException("The requested preview version/channel is not installed.");
        ViewModel.Status=Strings.Get("ValidationPreparing");
        var maps=await Task.Run(()=>input.Maps.Select(seed=>{
            MapPackage Inspect(string path)
            {
                var map=MapPackage.Inspect(path);var lat=(map.South+map.North)/2;var lon=(map.West+map.East)/2;
                var point=Geo.Project(lat,lon);var n=1<<map.MaxZoom;
                var bytes=map.ReadTile(map.MaxZoom,Math.Clamp((int)(point.X*n),0,n-1),Math.Clamp((int)(point.Y*n),0,n-1));
                if(bytes==null || !MapProbe.CanDecode(bytes))throw new InvalidDataException("Map center tile cannot be decoded: "+path);
                return map;
            }
            return (Seed:seed,Map:Inspect(seed.Path) with{Id=seed.Id,Variants=seed.Variants.Select(Inspect).ToArray()});
        }).ToArray());
        foreach(var pair in maps)
        {
            var existing=Atlas.Tiles.Catalog.Packages.FirstOrDefault(m=>m.Id==pair.Map.Id);
            if(existing!=null && !Path.GetFullPath(existing.Path).Equals(Path.GetFullPath(pair.Map.Path),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An existing validation map identity has another path; it was preserved.");
        }
        foreach(var pair in maps)if(!Atlas.Tiles.Catalog.Packages.Any(m=>m.Id==pair.Map.Id))Atlas.Tiles.Catalog.Packages.Add(pair.Map);
        Atlas.Tiles.Catalog.Save();
        await ScanFolder(input.MediaDirectory);await placesRefresh;
        var expected=Directory.GetFiles(input.MediaDirectory).Where(p=>MediaTypes.Detect(p)!=null).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fixtures=ViewModel.Items.Where(m=>expected.Contains(Path.GetFullPath(m.Path))).ToArray();
        var excluded=await Task.Run(()=>{using var index=new MediaIndex(Path.Combine(App.DataDirectory,"index.db"));return index.Exclusions();});
        var excludedCount=expected.Count(p=>excluded.Contains(MediaInputs.NormalizePath(p)));
        if(fixtures.Length+excludedCount!=expected.Count)throw new InvalidDataException("Approved fixtures could not be indexed. The existing profile was preserved.");
        if(excludedCount==0 && (!fixtures.Any(m=>!m.HasLocation) || !fixtures.Any(m=>m.Kind=="Video" && m.HasLocation) || !fixtures.Any(m=>Path.GetExtension(m.Path).Equals(".heic",StringComparison.OrdinalIgnoreCase) && m.HasLocation)))throw new InvalidDataException("The approved mixed-media kit is incomplete.");
        var saint=fixtures.FirstOrDefault(m=>m.Name.StartsWith("SaintPetersburg",StringComparison.Ordinal) && m.HasLocation)??throw new InvalidDataException("Both Saint Petersburg samples are excluded. Explicitly re-add one individual sample; exclusions were preserved.");
        await NavigateToMedia(saint);Atlas.SetComparisonCamera(saint.Latitude!.Value,saint.Longitude!.Value,10);await SettleBrowsing();
        var pilot=Atlas.AtlasPilot??throw new InvalidDataException("Hybrid Atlas failed to initialize.");
        // Inspection creates transient variant IDs; a preserved catalog owns the actual installed IDs.
        var contextIds=maps.Where(m=>m.Seed.Role=="SaintPetersburgContext").SelectMany(m=>NativeAtlasPilot.FamilyIds(Atlas.Tiles.Catalog.Packages.Single(p=>p.Id==m.Seed.Id))).ToHashSet(StringComparer.Ordinal);
        bool RegionalFrameReady()=>pilot.LastRasterVisible && pilot.RasterTiles.RenderedMapIds.Any(contextIds.Contains);
        // Tile loading may be idle before the asynchronous scene has published its first raster frame.
        // Wait for the actual required surface, without rebuilding scenes or accepting world fallback.
        var publication=Stopwatch.StartNew();
        while(!RegionalFrameReady() && publication.Elapsed<TimeSpan.FromSeconds(30))await Task.Delay(50);
        if(!RegionalFrameReady())throw new InvalidDataException("Saint Petersburg regional raster did not render; world fallback is not acceptance.");
        var text=new StringBuilder(256);GetWindowText(new WindowInteropHelper(this).Handle,text,text.Capacity);
        if(text.ToString()!=RuntimeIdentity.WindowTitle)throw new InvalidDataException("The native preview caption is incorrect.");
        if(input.CaptureEvidence)
        {
            if(ViewModel.Items.Count!=fixtures.Length)throw new InvalidDataException("Evidence capture refused because non-fixture media is present.");
            FeatureVerification.Snapshot(this,Path.Combine(input.DiagnosticsDirectory,"validation-main.png"));
            var settings=new SettingsWindow(this);settings.Show();settings.UpdateLayout();settings.ContentScroller.ScrollToVerticalOffset(240);await Task.Delay(100);
            FeatureVerification.Snapshot(settings,Path.Combine(input.DiagnosticsDirectory,"validation-about.png"));settings.Close();
        }
        using var process=Process.GetCurrentProcess();
        session.Write(new{
            Status="READY",input.Session,ProcessId=Environment.ProcessId,CreationTimeUTC=process.StartTime.ToUniversalTime(),ActualExecutable=RuntimeIdentity.Executable,
            CommandLine=RuntimeIdentity.NativeCommandLine,Arguments=Environment.GetCommandLineArgs(),ActualProfile=App.DataDirectory,
            ProductVersion=RuntimeIdentity.ProductVersion,RuntimeIdentity.BuildCommit,RuntimeIdentity.Channel,WindowTitle=text.ToString(),MapEngine=RuntimeIdentity.MapEngine(this),
            MapCatalog=Path.Combine(App.DataDirectory,"Maps","catalog.json"),ExecutableSHA256=RuntimeIdentity.Hash(RuntimeIdentity.Executable),FirstPartyAssemblies=RuntimeIdentity.Assemblies(),
            IndexedFixtureCount=fixtures.Length,ExcludedFixtureCount=excludedCount,Warnings=excludedCount>0?new[]{"Explicit sample exclusions were preserved; re-add individual sample files to restore the complete kit."}:Array.Empty<string>(),TotalLibraryCount=ViewModel.Items.Count,GpsFixtureCount=fixtures.Count(m=>m.HasLocation),WithoutGpsCount=fixtures.Count(m=>!m.HasLocation),
            OverlappingStack=Geo.Cluster(fixtures,256*Math.Pow(2,12)).Any(g=>g.Items.Count>1),
            InstalledMaps=maps.Select(m=>new{m.Seed.Role,m.Map.Id,m.Map.Path,m.Map.MinZoom,m.Map.MaxZoom,m.Map.West,m.Map.South,m.Map.East,m.Map.North,Variants=m.Map.Variants?.Select(v=>v.Path)}),
            RenderedMapIds=pilot.RasterTiles.RenderedMapIds.ToArray(),pilot.LastRasterVisible,PublicationWaitMilliseconds=publication.Elapsed.TotalMilliseconds,TimeUTC=DateTime.UtcNow,NetworkOperations=0
        });
        ViewModel.Status=string.Format(Strings.Get("ValidationReady"),RuntimeIdentity.Version,fixtures.Length);
        if(input.CloseAfterChecks)
        {
            var acknowledgement=Path.Combine(input.DiagnosticsDirectory,"launcher-ack.json");
            for(var i=0;i<400;i++)
            {
                if(File.Exists(acknowledgement) && File.ReadAllText(acknowledgement).Trim()==input.Session){App.Current.Shutdown();return;}
                await Task.Delay(100);
            }
            throw new TimeoutException("The launcher did not acknowledge runtime identity; test process was not treated as accepted.");
        }
    }
}
