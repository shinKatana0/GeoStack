// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MediaViewModel(MediaItem item) : Observable
{
    public MediaItem Item => item;
    public string Name => item.Name;
    public void RefreshLanguage() => Changed(nameof(Details));
    public string Details => item.HasLocation ? $"{item.Latitude:F4}°, {item.Longitude:F4}°" : Strings.Get("NoGps");
    private ImageSource? thumbnail;
    public ImageSource? Thumbnail { get => thumbnail; set { thumbnail = value; Changed(); } }
}

public sealed class MainViewModel : Observable
{
    public List<MediaItem> Items { get; private set; } = [];
    public ObservableCollection<MediaViewModel> VisibleItems { get; } = [];
    public PreviewService Previews { get; }
    internal bool DiagnosticSuppressThumbnailDelivery { get; set; }
    private readonly string dataDirectory;
    private readonly HashSet<string> excludedPaths;
    private int scanVersion;
    public CancellationTokenSource? ScanCancellation { get; private set; }
    public ScanMeasurements LastScanMeasurements { get; private set; } = new(0,0,0,0);
    public bool LastImportChanged { get; private set; }
    public bool IsScanning => ScanCancellation != null;
    private double scanFraction;
    public double ScanFraction { get=>scanFraction; private set {scanFraction=value;Changed();} }
    public bool IsRemoving { get; private set; }
    public bool IsOrganizing {get;private set;}
    public bool CanScan => !IsScanning && !IsRemoving && !IsOrganizing;
    internal bool BeginOrganization()
    {if(!CanScan)return false;IsOrganizing=true;Changed(nameof(CanScan));return true;}
    internal void EndOrganization(){IsOrganizing=false;Changed(nameof(CanScan));}
    internal async Task ReconcileMoves(IReadOnlyList<CopyJournalEntry> results)
    {
        var moves=results.Where(r=>r.Operation==OrganizationOperation.Move && r.Status==CopyState.Completed && r.SourceRemoved && r.DestinationVerified)
            .Select(r=>(Original:Items.FirstOrDefault(m=>m.Path.Equals(r.Source,StringComparison.OrdinalIgnoreCase)),r.Destination)).Where(m=>m.Original!=null).Select(m=>(Original:m.Original!,m.Destination)).ToArray();
        if(moves.Length==0)return;
        var state=await Task.Run(()=>{using var db=new MediaIndex(Path.Combine(dataDirectory,"index.db"));db.RelocateMany(moves);return (Items:db.ReadAll(),Exclusions:db.Exclusions());});
        ++selectionVersion;Items=state.Items.ToList();excludedPaths.UnionWith(state.Exclusions);
        var relocated=moves.ToDictionary(m=>m.Original.Path,m=>m.Original with{Path=m.Destination},StringComparer.OrdinalIgnoreCase);
        await ShowItems(selection.Select(m=>relocated.GetValueOrDefault(m.Path,m)),SidebarTitle,PageIndex);
        Changed(nameof(IsEmpty));Changed(nameof(Counters));LibraryChanged?.Invoke();
    }
    public bool IsEmpty => Items.Count == 0;
    private string status = Strings.Get("Ready");
    public string Status { get => status; set { status = value; Changed(); } }
    public string Counters => $"{Items.Count:N0} {Strings.Get("Files")}   ·   {Items.Count(x => x.HasLocation):N0} {Strings.Get("Located")}   ·   {Items.Count(x => !x.HasLocation):N0} {Strings.Get("NoGps")}";
    public string SidebarTitle { get; private set; } = Strings.Get("Library");
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public event Action? LibraryChanged;
    public void RefreshTitle(string title) { SidebarTitle = title; Changed(nameof(SidebarTitle)); Changed(nameof(SelectionSummary)); }
    public void RefreshLanguage() { Changed(nameof(Counters)); Changed(nameof(PageLabel)); Changed(nameof(SelectionSummary)); }
    public MainViewModel(string? dataDirectory = null)
    {
        this.dataDirectory = dataDirectory ?? App.DataDirectory; Previews = new(this.dataDirectory);
        StartupTrace.Mark("IndexOpening");
        using var index = new MediaIndex(Path.Combine(this.dataDirectory, "index.db"));
        StartupTrace.Mark("IndexOpened");
        Items = index.ReadAll().ToList();
        excludedPaths=index.Exclusions();
        StartupTrace.Mark("IndexRead");
    }
    public Task ScanAsync(string path) => ScanInputsAsync([path], false);
    public Task ImportAsync(IEnumerable<string> paths) => ScanInputsAsync(paths.ToArray(), true);
    private async Task ScanInputsAsync(IReadOnlyList<string> paths, bool dropped)
    {
        if (!CanScan) return;
        var version=++scanVersion;
        foreach(var path in MediaInputs.NormalizeInputs(paths).Where(p=>MediaTypes.Detect(p)!=null))excludedPaths.Remove(MediaInputs.NormalizePath(path));
        LastImportChanged=true;
        ScanCancellation = new(); Changed(nameof(IsScanning)); Changed(nameof(CanScan));
        ScanFraction=0;
        Status = Strings.Get("Scanning");
        var token = ScanCancellation.Token;
        var live=Items.ToDictionary(m=>m.Path,StringComparer.OrdinalIgnoreCase);
        var publication=System.Diagnostics.Stopwatch.StartNew();
        var progress = new Progress<ScanProgress>(p => {
            if(version!=scanVersion)return;
            ScanFraction=p.Discovered>0?Math.Clamp(p.Indexed/(double)p.Discovered,0,1):0;
            Status = dropped ? string.Format(Strings.Get("ImportProgress"), p.Discovered, p.Indexed, p.Skipped) : $"{Strings.Get("Scanning")}  {p.Indexed:N0} / {p.Discovered:N0}   ·   {p.Located:N0} {Strings.Get("Located")}   ·   {p.Errors} {Strings.Get("Errors")}";
            if(p.Committed!=null)foreach(var item in p.Committed)if(!excludedPaths.Contains(MediaInputs.NormalizePath(item.Path)))live[item.Path]=item;
            if(p.Committed!=null && publication.ElapsedMilliseconds>=2000){Items=live.Values.Where(m=>!excludedPaths.Contains(MediaInputs.NormalizePath(m.Path))).ToList();publication.Restart();Changed(nameof(IsEmpty));Changed(nameof(Counters));LibraryChanged?.Invoke();}
        });
        try
        {
            var result = await Task.Run(async () => { using var index = new MediaIndex(Path.Combine(dataDirectory, "index.db")); var service=new ScanService(PreviewService.Tool("ExifTool", "ExifTool.exe")){BatchSize=160};var scanned=await service.ScanAsync(paths,index,progress,token);LastScanMeasurements=service.Measurements;return scanned; });
            ScanFraction=1;
            LastImportChanged=result.Items.Count!=result.Reused;
            Errors = result.Errors;
            Status = dropped ? string.Format(Strings.Get("ImportComplete"), result.Discovered, result.Added, result.Reused, result.Skipped, result.Errors.Count) : (result.Items.Count == 0 ? Strings.Get("NoMedia") : Strings.Get("ScanComplete")) + $" · {result.Errors.Count} {Strings.Get("Errors")}";
        }
        catch (OperationCanceledException) { Status = Strings.Get("Cancelled"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Status = Strings.Get("ScanError") + ": " + ex.Message; Errors = [ex.Message]; }
        finally
        {
            try { if(LastImportChanged) {var current=await Task.Run(() => { using var index = new MediaIndex(Path.Combine(dataDirectory, "index.db")); return index.ReadAll().ToList(); });Items=current.Where(m=>!excludedPaths.Contains(MediaInputs.NormalizePath(m.Path))).ToList();} }
            finally { ++scanVersion; ScanCancellation.Dispose(); ScanCancellation = null; Changed(nameof(IsScanning)); Changed(nameof(CanScan)); }

            if(LastImportChanged){Changed(nameof(IsEmpty)); Changed(nameof(Counters)); LibraryChanged?.Invoke();}
        }
    }
    public Task RemoveFromLibraryAsync(MediaItem item) => RemoveManyFromLibraryAsync([item]);
    public async Task RemoveManyFromLibraryAsync(IReadOnlyList<MediaItem> items)
    {
        var paths=items.Select(m=>MediaInputs.NormalizePath(m.Path)).Where(p=>Items.Any(m=>string.Equals(MediaInputs.NormalizePath(m.Path),p,StringComparison.OrdinalIgnoreCase))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(paths.Count==0)return;
        IsRemoving=true;Changed(nameof(CanScan));
        try
        {
            var newlyExcluded=paths.Where(p=>!excludedPaths.Contains(p)).ToArray();
            excludedPaths.UnionWith(paths);
            try{await Task.Run(()=>{using var index=new MediaIndex(Path.Combine(dataDirectory,"index.db"));index.ExcludeMany(paths);});}
            catch{excludedPaths.ExceptWith(newlyExcluded);throw;}
            Items=Items.Where(m=>!paths.Contains(MediaInputs.NormalizePath(m.Path))).ToList();
            var remaining=selection.Where(m=>!excludedPaths.Contains(MediaInputs.NormalizePath(m.Path))).ToArray();
            var refresh=ShowItems(remaining,SidebarTitle,PageIndex);
            Changed(nameof(IsEmpty));Changed(nameof(Counters));LibraryChanged?.Invoke();
            await refresh;
        }
        finally{IsRemoving=false;Changed(nameof(CanScan));}
    }
    public event Action? PageChanging;
    public event Action? PagePublished;
    private int selectionVersion;
    private IReadOnlyList<MediaItem> selection = [];
    public IReadOnlyList<MediaItem> Selection => selection;
    public int PageIndex { get; private set; }
    public int PageCount => Math.Max(1, (selection.Count + 119) / 120);
    public string SelectionSummary => $"{Strings.Get("PhotoCountLabel")}: {selection.Count(m=>m.Kind=="Photo")} · {Strings.Get("VideoCountLabel")}: {selection.Count(m=>m.Kind=="Video")}";
    public string PageLabel => $"{PageIndex + 1} / {PageCount} · {selection.Count:N0} {Strings.Get("Count")}";
    public bool SortNewest { get; set; }
    public async Task ShowItems(IEnumerable<MediaItem> items, string title, int page = 0)
    {
        selection = (SortNewest ? items.OrderByDescending(m => m.Captured).ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase) : items.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)).ToArray(); PageIndex = Math.Clamp(page,0,PageCount-1);
        SidebarTitle = title; Changed(nameof(SidebarTitle)); Changed(nameof(SelectionSummary));
        await ShowPage(PageIndex);
    }
    public async Task ShowPage(int page)
    {
        var version = ++selectionVersion;
        PageIndex = Math.Clamp(page, 0, PageCount - 1); Changed(nameof(PageLabel));
        PageChanging?.Invoke();
        VisibleItems.Clear(); foreach (var item in selection.Skip(PageIndex * 120).Take(120)) VisibleItems.Add(new(item));
        PagePublished?.Invoke();
        // Small thumbnails and bounded workers keep selection responsive while previews are prepared.
        await Parallel.ForEachAsync(VisibleItems.Take(120).ToArray(), new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (item, _) => {
            if (version != selectionVersion) return;
            var image = await Previews.GetAsync(item.Item.Path, true);
            if (System.Windows.Application.Current is { } app) await app.Dispatcher.InvokeAsync(() => { if (!DiagnosticSuppressThumbnailDelivery && version == selectionVersion) item.Thumbnail = image; },System.Windows.Threading.DispatcherPriority.Background);
            else if (version == selectionVersion) item.Thumbnail = image;
        });
    }
}
