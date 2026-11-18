// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GeoStack.Core;
using Microsoft.Win32;

namespace GeoStack.App;

public partial class MainWindow
{
    private MediaItem[] CurrentCopySelection() => selectedMedia.Resolve(ViewModel.Items).ToArray();
    private void Copy_Click(object sender, RoutedEventArgs e) { var items=CurrentCopySelection();if(ConfirmHiddenSelection(items.Length))OpenCopy(items); }
    private void CopyStack_Click(object sender, RoutedEventArgs e) => OpenCopy(ViewModel.Selection.ToArray());
    private void OpenCopy(MediaItem[] items)
    {
        if (items.Length == 0) { ShowMessage(Strings.Get("SelectMedia"), Strings.Get("CopyToLocation")); return; }
        new CopyWindow(this, items).ShowDialog();
    }
    private void Journal_Click(object sender, RoutedEventArgs e)
    {
        var entries = GeographicCopy.ReadJournal(Path.Combine(App.DataDirectory, "CopyJournal"));
        var window = DialogLayout.Window(this, "CopyHistory", 1000, 650, out var panel);
        var privacy = DialogLayout.Text("JournalPrivacy"); DockPanel.SetDock(privacy, Dock.Top); panel.Children.Add(privacy);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, ItemsSource = entries.OrderByDescending(e => e.Timestamp), Margin = new(0, 12, 0, 0) };
        foreach (var (key, property) in new[] { ("Timestamp", "Timestamp"), ("OperationMode", "Operation"), ("TransferPhase", "Phase"), ("Source", "Source"), ("Destination", "Destination"), ("Status", "Status"), ("Verification", "Sha256"), ("Failure", "Failure") }) grid.Columns.Add(new DataGridTextColumn { Header = Strings.Get(key), Binding = new Binding(property) { Converter = property is "Status" or "Operation" or "Phase" ? new CopyStateConverter() : null }, Width = new(1,DataGridLengthUnitType.Star) });
        var close = DialogLayout.Button("Close", () => window.Close()); close.IsCancel = true; close.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var recovery=DialogLayout.Button("ReviewMoves",async()=>await ReviewMoves());DockPanel.SetDock(recovery,Dock.Bottom);panel.Children.Add(recovery);
        panel.Children.Add(grid); window.ShowDialog();
    }
    internal async Task ReconcileOrganizedMedia(IReadOnlyList<CopyJournalEntry> results)
    {
        var mapping=results.Where(r=>r.Status==CopyState.Completed && r.SourceRemoved).ToDictionary(r=>r.Source,r=>r.Destination,StringComparer.OrdinalIgnoreCase);
        var mapSelection=Atlas.SelectedMediaId;var nextMapSelection=mapSelection==null?null:mapping.GetValueOrDefault(mapSelection,mapSelection);
        var ids=selectedMedia.Ids.Select(id=>mapping.GetValueOrDefault(id,id)).ToArray();
        await ViewModel.ReconcileMoves(results);selectedMedia.Clear();selectedMedia.All(ids);selectedMedia.Reconcile(ViewModel.Items.Select(m=>m.Path));
        sidebarPlace=null;sidebarKey="AllMedia";await RefreshLiveLibrary();RestoreGridSelection();
        places=null;outside=[];EdgeIndicators.Children.Clear();placesRefresh=RebuildPlaces();await placesRefresh;
        Atlas.Highlight(nextMapSelection==null?null:ViewModel.Items.FirstOrDefault(m=>m.Path.Equals(nextMapSelection,StringComparison.OrdinalIgnoreCase)));
    }
    private async Task ReviewMoves()
    {
        var directory=Path.Combine(App.DataDirectory,"CopyJournal");var pending=await Task.Run(()=>GeographicCopy.InspectInterruptedMoves(directory,CancellationToken.None));
        var dialog=DialogLayout.Window(this,"ReviewMoves",1000,620,out var panel);var explanation=DialogLayout.Text("MoveRecovery");DockPanel.SetDock(explanation,Dock.Top);panel.Children.Add(explanation);
        var rows=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,ItemsSource=pending,SelectionMode=DataGridSelectionMode.Single};
        foreach(var (key,path) in new[]{("Source","Checkpoint.Source"),("Destination","Checkpoint.Destination"),("SourceExists","SourceExists"),("SourceAbsenceConfirmed","SourceAbsenceConfirmed"),("Verification","DestinationMatches")})rows.Columns.Add(new DataGridTextColumn{Header=Strings.Get(key),Binding=new Binding(path){Converter=path.StartsWith("Checkpoint.",StringComparison.Ordinal)?null:new CopyStateConverter()},Width=new(1,DataGridLengthUnitType.Star)});
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(status,Dock.Bottom);panel.Children.Add(status);
        var action=DialogLayout.Button("ReconcileMove",async()=>{
            if(rows.SelectedItem is not MoveRecovery selected || !selected.SourceAbsenceConfirmed || !selected.DestinationMatches)return;
            if(MessageBox.Show(dialog,Strings.Get("ReconcileMoveConfirm"),dialog.Title,MessageBoxButton.OKCancel,MessageBoxImage.None,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
            if(!ViewModel.BeginOrganization()){status.Text=Strings.Get("LibraryBusy");return;}
            try{var entry=selected.Checkpoint with{Status=CopyState.Completed,SourceRemoved=true,DestinationVerified=true};await ReconcileOrganizedMedia([entry]);await Task.Run(()=>GeographicCopy.AcknowledgeReconciledMove(entry,directory,CancellationToken.None));status.Text=Strings.Get("Completed");rows.ItemsSource=await Task.Run(()=>GeographicCopy.InspectInterruptedMoves(directory,CancellationToken.None));}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){status.Text=ex.Message;}
            finally{ViewModel.EndOrganization();}
        });action.IsEnabled=false;rows.SelectionChanged+=(_,_)=>action.IsEnabled=rows.SelectedItem is MoveRecovery{SourceAbsenceConfirmed:true,DestinationMatches:true};DockPanel.SetDock(action,Dock.Bottom);panel.Children.Add(action);panel.Children.Add(rows);dialog.ShowDialog();
    }
}

public static class DialogLayout
{
    public static TextBlock Text(string key) => new() { Text = Strings.Get(key), TextWrapping = TextWrapping.Wrap, Margin = new(4, 6, 4, 6) };
    public static Button Button(string key, Action action)
    {
        var button = new Button { Content = Strings.Get(key) }; button.Click += (_, _) => action(); return button;
    }
    public static Window Window(Window owner, string title, double width, double height, out DockPanel panel)
    {
        panel = new DockPanel { Margin = new(20) };
        var bounds = WindowBounds.Fit(SystemParameters.WorkArea, new(width, height));
        var window = new Window { Owner = owner, Icon = owner.Icon, Title = Strings.Get(title), Width = bounds.Width, Height = bounds.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        WindowLayout.Attach(window, title); window.SourceInitialized += (_, _) => App.ApplyTheme(); return window;
    }
}

public sealed class CopyWindow : Window
{
    private readonly MediaItem[] selection;
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, Margin = new(0, 10, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4) };
    private readonly TextBox root = new() { IsReadOnly = true, MinWidth = 280, Margin = new(4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox language, hierarchy, missing, operation;
    private readonly Button preview, confirm;
    private readonly StackPanel options = new();
    private List<CopyProposal> plan = [];
    private CancellationTokenSource? work;
    private bool completed;
    public CopyWindow(Window owner, MediaItem[] selection)
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/GeoStack;component/OrganizationDialogStyles.xaml", UriKind.Relative) });
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SetResourceReference(StyleProperty, typeof(Window));
        this.selection = selection; Owner = owner; Icon = owner.Icon; Title = Strings.Get("CopyToLocation"); Width = Math.Min(1160, SystemParameters.WorkArea.Width); Height = Math.Min(780, SystemParameters.WorkArea.Height); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new(12) }; Content = panel;
        WindowLayout.Attach(this, "CopyToLocation");
        var optionScroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(optionScroll, Dock.Top); panel.Children.Add(optionScroll);
        SizeChanged += (_, _) => optionScroll.MaxHeight = Math.Max(90, ActualHeight * .35);
        var copySafety=DialogLayout.Text("CopySafety");options.Children.Add(copySafety);
        TextBlock OptionLabel(string key) { var text=DialogLayout.Text(key);text.VerticalAlignment=VerticalAlignment.Center;return text; }
        var modes=new WrapPanel();modes.Children.Add(OptionLabel("OperationMode"));
        operation=new ComboBox{ItemsSource=new[]{Strings.Get("CopyToLocation"),Strings.Get("MoveToLocation")},SelectedIndex=0};modes.Children.Add(operation);options.Children.Add(modes);
        var safety=DialogLayout.Text("MoveSafety");safety.Visibility=Visibility.Collapsed;options.Children.Add(safety);
        operation.SelectionChanged+=(_,_)=>{var move=operation.SelectedIndex==1;Title=Strings.Get(move?"MoveToLocation":"CopyToLocation");copySafety.Visibility=move?Visibility.Collapsed:Visibility.Visible;safety.Visibility=move?Visibility.Visible:Visibility.Collapsed;InvalidatePlan();if(confirm!=null)confirm.Content=Strings.Get(move?"ConfirmMove":"ConfirmCopy");};
        options.Children.Add(DialogLayout.Button("GeographicData",()=>{GeographicDataWindow.Show(this);InvalidatePlan();}));
        var destination = new DockPanel();
        destination.Children.Add(DialogLayout.Button("DestinationRoot", () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) { root.Text = picker.FolderName; InvalidatePlan(); } })); destination.Children.Add(root); options.Children.Add(destination);
        var settings = new WrapPanel(); options.Children.Add(settings);
        settings.Children.Add(OptionLabel("FolderLanguage"));
        language = new ComboBox { ItemsSource = new[] { Strings.Get("EnglishLanguage"), Strings.Get("RussianLanguage") }, SelectedIndex = App.Settings.FolderLanguage == "ru-RU" ? 1 : 0 }; settings.Children.Add(language);
        hierarchy = new ComboBox { ItemsSource = new[] { Strings.Get("CountryCity"), Strings.Get("CountryCityDistrict"), Strings.Get("CountryRegionCity") }, SelectedIndex = Array.IndexOf(new[] { "CountryCity", "CountryCityDistrict", "CountryRegionCity" }, App.Settings.FolderHierarchy) }; settings.Children.Add(hierarchy);
        missing = new ComboBox { ItemsSource = new[] { Strings.Get("SkipMissingGps"), Strings.Get("CopyUnknownGps") }, SelectedIndex = 0 }; settings.Children.Add(missing);
        options.Children.Add(DialogLayout.Text("BoundaryWarning"));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom); bottom.Children.Add(status);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(actions);
        preview = DialogLayout.Button("PreviewCopy", async () => await Preview()); actions.Children.Add(preview);
        confirm = DialogLayout.Button("ConfirmCopy", async () => await Copy()); confirm.IsEnabled = false; actions.Children.Add(confirm);
        actions.Children.Add(DialogLayout.Button("CancelCopy", () => { if (work != null) work.Cancel(); else Close(); }));
        actions.Children.Add(DialogLayout.Button("OpenDestination", () => { if (completed && Directory.Exists(root.Text)) Process.Start(new ProcessStartInfo(root.Text) { UseShellExecute = true }); }));
        grid.ColumnHeaderHeight=double.NaN;
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = Strings.Get("Include"), Binding = new Binding("Include"), MinWidth=90 });
        foreach (var (key, property, editable) in new[] { ("Source", "SourceName", false), ("Location", "Location", false), ("DestinationFolder", "RelativeDirectory", true), ("Destination", "PreviewDestination", false), ("Bytes", "Size", false), ("Status", "State", false), ("GeoWarnings", "GeographicWarnings", false), ("MissingGps", "MissingGps", false) })
        {
            var column = new DataGridTextColumn { Header = Strings.Get(key), Binding = new Binding(property) { Converter = property == "GeographicWarnings" ? new GeographicWarningConverter() : property is "State" or "MissingGps" ? new CopyStateConverter() : null, Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay }, IsReadOnly = !editable, Width = new(1, DataGridLengthUnitType.Star), MinWidth = 120 };
            var textStyle = new Style(typeof(TextBlock), (Style)FindResource(typeof(TextBlock)));
            textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            textStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6,0,6,0)));
            if (property is "SourceName" or "PreviewDestination") textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property == "SourceName" ? "Source" : "Destination")));
            else if (property is "Location" or "RelativeDirectory" or "GeographicWarnings") textStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property) { Converter = property == "GeographicWarnings" ? new GeographicWarningConverter() : null }));
            column.ElementStyle = textStyle; grid.Columns.Add(column);
        }
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto); grid.FrozenColumnCount = 2;
        panel.Children.Add(grid);
        foreach (var combo in new[] { language, hierarchy, missing }) combo.SelectionChanged += (_, _) => InvalidatePlan();
        Closing += (_, e) => { if (work != null) { work.Cancel(); e.Cancel = true; } };
        SourceInitialized += (_, _) => App.ApplyTheme();
    }
    private void InvalidatePlan() { confirm.IsEnabled = false; completed = false; plan = []; grid.ItemsSource = null; }
    internal async Task PrepareVerification(string destination) { Directory.CreateDirectory(destination); root.Text = destination; await Preview(); }
    internal IReadOnlyList<CopyProposal> PreviewRows => plan;
    internal void CaptureReleaseAlignment(string directory,string label)
    {
        var width=Width;var height=Height;var state=WindowState;
        try
        {
            WindowState=WindowState.Normal;Width=700;Height=500;UpdateLayout();
            foreach(var combo in new[]{operation,language,hierarchy,missing})
                if(combo.ActualHeight<38 || combo.VerticalAlignment!=VerticalAlignment.Center)throw new InvalidOperationException("Organization selector alignment failed");
            hierarchy.Focus();
            foreach(var scale in new[]{1.0,1.25,1.5})FeatureVerification.Snapshot(this,Path.Combine(directory,$"alignment-{label}-minimum-{scale:0.00}.png"),scale);
            WindowState=WindowState.Maximized;UpdateLayout();FeatureVerification.Snapshot(this,Path.Combine(directory,$"alignment-{label}-maximized.png"));
        }
        finally{WindowState=state;Width=width;Height=height;UpdateLayout();}
    }
    internal void SelectOperationForVerification(OrganizationOperation mode)=>operation.SelectedIndex=mode==OrganizationOperation.Move?1:0;
    internal async Task<IReadOnlyList<CopyJournalEntry>> ExecutePreparedForVerification()
    {
        if(!App.DataDirectory.StartsWith(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.local/owner055"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned organization profile required");
        var move=operation.SelectedIndex==1;
        if(move && plan.Any(p=>!p.Source.StartsWith(Path.Combine(App.DataDirectory,"inputs")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Only disposable owned input copies may be moved by verification");
        var main=(MainWindow)Owner;if(move && !main.ViewModel.BeginOrganization())throw new InvalidOperationException("Library busy");
        try{return await ExecutePrepared(move,null,CancellationToken.None);}finally{if(move)main.ViewModel.EndOrganization();}
    }
    private async Task<IReadOnlyList<CopyJournalEntry>> ExecutePrepared(bool move,IProgress<(int Done,int Total)>? progress,CancellationToken token)
    {
        var destination=root.Text;
        var results=await Task.Run(async()=>move?await GeographicCopy.ExecuteMove(plan,destination,Path.Combine(App.DataDirectory,"CopyJournal"),progress,token):await GeographicCopy.Execute(plan,destination,Path.Combine(App.DataDirectory,"CopyJournal"),progress,token));
        if(move)
        {
            await ((MainWindow)Owner).ReconcileOrganizedMedia(results);
            foreach(var result in results.Where(r=>r.Status==CopyState.Completed))await Task.Run(()=>GeographicCopy.AcknowledgeReconciledMove(result,Path.Combine(App.DataDirectory,"CopyJournal"),CancellationToken.None));
        }
        return results;
    }
    private void Busy(bool value) { options.IsEnabled = !value; preview.IsEnabled = !value; grid.IsEnabled = !value; confirm.IsEnabled = !value && plan.Any(p => p.Include); }
    private async Task Preview()
    {
        if (!Directory.Exists(root.Text)) return;
        plan = []; grid.ItemsSource = null; work = new(); Busy(true); var destination = root.Text; var lang = language.SelectedIndex == 1 ? "ru-RU" : "en-US"; var mode = new[] { "CountryCity", "CountryCityDistrict", "CountryRegionCity" }[Math.Max(0, hierarchy.SelectedIndex)]; var skip = missing.SelectedIndex == 0; var unknown = Strings.GetForLanguage("UnknownLocation", lang); var displayLanguage = App.Settings.Language;
        try
        {
            App.Settings = App.Settings with { FolderLanguage = lang, FolderHierarchy = mode }; App.Settings.Save(App.SettingsPath);
            plan = await Task.Run(async () => await GeographicCopy.Plan(selection, App.LocationResolver, destination, lang, mode, skip, work.Token, unknown, displayLanguage));
            grid.ItemsSource = plan; status.Text = $"{Strings.Get("Files")}: {plan.Count} · {Strings.Get("Bytes")}: {plan.Sum(p => p.Size):N0} · {Strings.Get("Operations")}: {plan.Count(p => p.Include)} · {Strings.Get("MissingGps")}: {plan.Count(p => p.MissingGps)} · {Strings.Get("Identical")}: {plan.Count(p => p.State == CopyState.Identical)}";
            if(plan.Any(p=>p.GeographicWarnings.Any(w=>w is "GeoCountryFallback" or "GeoRegionFallback")))status.Text+="\n"+Strings.Get("CityDataRequired");
        }
        catch (OperationCanceledException) { status.Text = Strings.Get("Cancelled"); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { status.Text = Strings.Get(operation.SelectedIndex==1?"MoveFailed":"CopyFailed") + ": " + ex.Message; }
        finally { work.Dispose(); work = null; Busy(false); }
    }
    private async Task Copy()
    {
        if (work != null) return;
        grid.CommitEdit(); grid.CommitEdit(DataGridEditingUnit.Row, true);
        var move=operation.SelectedIndex==1;var main=Owner as MainWindow;var organizing=false;
        if(move && (main==null || !(organizing=main.ViewModel.BeginOrganization()))){status.Text=Strings.Get("LibraryBusy");return;}
        work = new(); Busy(true); var destination = root.Text;
        try
        {
            await Task.Run(() => GeographicCopy.Recheck(plan, destination, work.Token)); grid.Items.Refresh();
            status.Text = $"{Strings.Get("Files")}: {plan.Count} ? {Strings.Get("Operations")}: {plan.Count(p => p.Include)} ? {Strings.Get("Bytes")}: {plan.Where(p => p.Include).Sum(p => p.Size):N0}";
            if (MessageBox.Show(this, Strings.Get(move?"ConfirmMoveText":"ConfirmCopyText") + $"\n{Strings.Get("Operations")}: {plan.Count(p => p.Include)}", Title, MessageBoxButton.OKCancel,MessageBoxImage.None,MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var progress = new Progress<(int Done, int Total)>(p => status.Text = $"{Strings.Get(move?"Moving":"Copying")} {p.Done} / {p.Total}");
            var results = await ExecutePrepared(move,progress,work.Token);
            completed = results.Any(r => r.Status == CopyState.Completed) || move && results.Any(r=>r.DestinationVerified);
            status.Text = $"{Strings.Get("Completed")}: {results.Count(r => r.Status == CopyState.Completed)} · {Strings.Get("Errors")}: {results.Count(r => r.Status is CopyState.Failed or CopyState.NeedsReview)} · {Strings.Get("Cancelled")}: {results.Count(r => r.Status == CopyState.Cancelled)} · {Strings.Get("SourceRetained")}: {results.Count(r=>r.Status==CopyState.SourceRetained)}";
            foreach (var (row, result) in plan.Zip(results)) { row.State = result.Status; row.Destination = result.Destination; }
            grid.Items.Refresh();
        }
        catch (OperationCanceledException) { status.Text = Strings.Get("Cancelled"); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { status.Text = Strings.Get(move?"MoveFailed":"CopyFailed") + ": " + ex.Message; }
        finally { work.Dispose(); work = null; plan = []; Busy(false);if(organizing)main!.ViewModel.EndOrganization(); }
    }
}
