// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GeoStack.Core;

namespace GeoStack.App;

internal sealed class MapDownloadsWindow : Window
{
    internal sealed class Row(MapDownloadArea area)
    {
        public MapDownloadArea Area=>area;
        public bool Completed {get;set;}
        public bool Selected {get;set;}=!area.Installed && area.Supported && !area.Isolated;
        public string Name=>Strings.Get(area.IsContext?"DownloadRegionalContext":"DownloadLocalDetail")+" · "+(area.Place==null?"":Strings.Get("DownloadAreaNear")+" "+area.Place.Display(App.Settings.Language)+" · ")+area.CoordinateLabel;
        public int Count=>area.MediaCount;
        public string Coverage=>area.Coverage;
        public string Storage=>area.Installed?Strings.Get("DownloadInstalled"):string.Format(Strings.Get("DownloadStorageBound"),area.MaximumInstalledBytes/1048576d);
        public string State=>!area.Installed && !Completed && area.MissingCount<area.MediaCount?string.Format(Strings.Get("DownloadPartial"),area.MissingCount,area.MediaCount):Strings.Get(area.Installed || Completed?"DownloadInstalled":!area.Supported?"DownloadUnsupported":area.Isolated?"DownloadIsolated":"DownloadMissing");
    }
    private readonly MainWindow main;
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar progressBar=new(){Height=4,Maximum=1,IsIndeterminate=true,Margin=new(0,6,0,6)};
    private readonly DataGrid grid=new(){AutoGenerateColumns=false,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single,EnableRowVirtualization=true};
    private readonly Button confirm;
    private readonly CancellationTokenSource cancellation=new();
    private bool running;
    internal IReadOnlyList<Row> Rows {get;private set;}=[];
    internal MapDownloadsWindow(MainWindow main,Window owner)
    {
        this.main=main;Owner=owner;Icon=owner.Icon;Title=Strings.Get("DownloadLibraryMaps");Width=1020;Height=700;SetResourceReference(StyleProperty,typeof(Window));WindowLayout.Attach(this,"MapManager");
        var root=new DockPanel{Margin=new(16)};Content=root;
        var heading=new TextBlock{Text=Strings.Get("DownloadPrivacy"),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,10)};var privacy=new ScrollViewer{Content=heading,MaxHeight=140,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};DockPanel.SetDock(privacy,Dock.Top);root.Children.Add(privacy);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        System.Windows.Automation.AutomationProperties.SetName(progressBar,Strings.Get("DownloadLibraryMaps"));footer.Children.Add(progressBar);
        footer.Children.Add(new ScrollViewer{Content=status,MaxHeight=120,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
        confirm=DialogLayout.Button("DownloadAllMissing",async()=>await DownloadSelected());confirm.IsEnabled=false;buttons.Children.Add(confirm);
        buttons.Children.Add(DialogLayout.Button("DownloadCancel",()=>{cancellation.Cancel();if(!running)Close();}));
        grid.Columns.Add(new DataGridCheckBoxColumn{Header=Strings.Get("DownloadSelect"),Binding=new Binding("Selected"),Width=70});
        foreach(var pair in new[]{("Name","Name",2d),("Count","Count",.5),("Coverage","Coverage",2d),("Status","State",1.5),("Bytes","Storage",1.2)})
        {
            var text=new Style(typeof(TextBlock));text.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));text.Setters.Add(new Setter(ToolTipProperty,new Binding(pair.Item2)));
            grid.Columns.Add(new DataGridTextColumn{Header=Strings.Get(pair.Item1),Binding=new Binding(pair.Item2),Width=new(pair.Item3,DataGridLengthUnitType.Star),IsReadOnly=true,ElementStyle=text});
        }
        root.Children.Add(grid);Closing+=(_,e)=>{if(running){e.Cancel=true;cancellation.Cancel();}};Closed+=(_,_)=>cancellation.Dispose();
        Loaded+=async(_,_)=>await Analyze();
    }
    private static string Failure(Exception ex)=>Strings.Get(ex.Message is "DownloadDiskSpace" or "DownloadSourceMissing" or "DownloadUnsupported"?ex.Message:ex is System.Net.Http.HttpRequestException?"DownloadFailureNetwork":ex is InvalidDataException or System.Text.Json.JsonException or FormatException or OverflowException?"DownloadFailureData":ex is IOException or UnauthorizedAccessException?"DownloadFailureStorage":"DownloadFailed");
    internal async Task Analyze()
    {
        running=true;status.Text=Strings.Get("DownloadAnalyzing");
        try
        {
            var items=main.ViewModel.Items.ToArray();var maps=main.Atlas.Tiles.Catalog.Packages.ToArray();
            var plan=await Task.Run(()=>RegionalContextPlanner.Extend(LibraryMapPlanner.Build(items,maps,App.LocationResolver.Resolve,cancellation.Token,validateTile:MapProbe.CanDecode),maps,cancellation.Token,MapProbe.CanDecode));
            Rows=plan.Areas.Select(a=>new Row(a)).ToArray();grid.ItemsSource=Rows;confirm.IsEnabled=Rows.Any(r=>!r.Area.Installed && r.Area.Supported);
            status.Text=string.Format(Strings.Get("DownloadPlanSummary"),Rows.Count,plan.WithoutGps,plan.IsolatedLocations,plan.UnavailablePackages)+Environment.NewLine+Strings.Get("DownloadProvider")+Environment.NewLine+string.Format(Strings.Get("DownloadStorageBound"),Rows.Where(r=>r.Selected).Sum(r=>r.Area.MaximumInstalledBytes)/1048576d)+Environment.NewLine+Path.Combine(App.DataDirectory,"Maps","Library");
        }
        catch(OperationCanceledException){status.Text=Strings.Get("Cancelled");}
        catch(Exception ex) when(ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException or ArgumentException){status.Text=Failure(ex);}
        finally{running=false;progressBar.IsIndeterminate=false;}
    }
    internal async Task DownloadSelected()
    {
        grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);
        var selected=Rows.Where(r=>r.Selected && !r.Completed && !r.Area.Installed && r.Area.Supported).ToArray();if(selected.Length==0){status.Text=Strings.Get("DownloadSelectArea");return;}
        if(selected.Sum(r=>r.Area.MaximumInstalledBytes)>1024L*1024*1024 && MessageBox.Show(this,Strings.Get("DownloadLargeWarning"),Title,MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        running=true;progressBar.IsIndeterminate=false;progressBar.Value=0;confirm.IsEnabled=false;grid.IsEnabled=false;var done=0;var failed=0;var failures=new List<string>();
        try
        {
            foreach(var row in selected)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var progress=new Progress<AcquisitionProgress>(p=>{progressBar.Value=(done+failed+p.Completed/(double)Math.Max(1,p.Total))/selected.Length;status.Text=row.Name+Environment.NewLine+string.Format(Strings.Get("DownloadProgress"),done,selected.Length,p.Completed,p.Total,p.Bytes/1048576d);});
                    var map=await MapAcquisition.Prepare(row.Area,Path.Combine(App.DataDirectory,"Maps","Library"),progress,cancellation.Token);
                    var catalog=main.Atlas.Tiles.Catalog;catalog.Packages.Add(map);var previous=catalog.Preferred;catalog.Preferred=map.Id;
                    try{catalog.Save();}catch{catalog.Packages.Remove(map);catalog.Preferred=previous;throw;}
                    done++;row.Completed=true;row.Selected=false;grid.Items.Refresh();main.Atlas.InvalidateVisual();
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or FormatException or OverflowException)
                {failed++;failures.Add(row.Name+": "+Failure(ex));status.Text=failures[^1];if(selected.Length==1)throw;}
            }
            status.Text=string.Format(Strings.Get("DownloadComplete"),done,failed)+Environment.NewLine+string.Join(Environment.NewLine,failures.Take(8));
        }
        catch(OperationCanceledException){status.Text=Strings.Get("Cancelled")+" · "+done;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or FormatException or OverflowException){status.Text=Failure(ex);}
        finally{running=false;grid.IsEnabled=true;confirm.IsEnabled=!cancellation.IsCancellationRequested && Rows.Any(r=>!r.Completed && !r.Area.Installed && r.Area.Supported); }
    }
}
