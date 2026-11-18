// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GeoStack.Core;
using Microsoft.Win32;

namespace GeoStack.App;

public sealed partial class NativeAtlasPilot
{
    public GlobalVectorAtlas? GlobalAtlas {get;private set;}
    private string? globalProfile;
    private static string GlobalCatalog(string profile)=>Path.Combine(profile,"Maps/global-atlas.json");
    private async Task LoadGlobalAtlas(string profile)
    {
        globalProfile=profile;var catalog=GlobalCatalog(profile);if(!App.ExperimentalGlobalAtlasEnabled || !File.Exists(catalog))return;
        try
        {
            var selection=JsonSerializer.Deserialize<GlobalAtlasSelection>(await File.ReadAllTextAsync(catalog));
            if(selection is not {Enabled:true} || !File.Exists(selection.Path))return;
            var dispatcher=Dispatcher.CurrentDispatcher;
            GlobalAtlas=await Task.Run(()=>new GlobalVectorAtlas(selection.Path,dispatcher));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or FormatException or OverflowException or JsonException or InvalidDataException or ArgumentException){ }
    }
    public async Task InstallGlobalAtlas(string path)
    {
        if(!App.ExperimentalGlobalAtlasEnabled)throw new InvalidOperationException("Experimental worldwide atlas is disabled in the consolidated candidate");
        var dispatcher=Dispatcher.CurrentDispatcher;var candidate=await Task.Run(()=>new GlobalVectorAtlas(path,dispatcher));
        try
        {
            var catalog=GlobalCatalog(globalProfile!);Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
            var temporary=catalog+"."+Guid.NewGuid().ToString("N")+".tmp";
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new GlobalAtlasSelection(Path.GetFullPath(path))));File.Move(temporary,catalog,true);
        }
        catch{await candidate.DisposeAsync();throw;}
        var previous=GlobalAtlas;GlobalAtlas=candidate;if(previous!=null)await previous.DisposeAsync();
    }
    public async Task RemoveGlobalAtlasReference()
    {
        var catalog=GlobalCatalog(globalProfile!);Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
        await File.WriteAllTextAsync(catalog,JsonSerializer.Serialize(new GlobalAtlasSelection("",false)));
        var previous=GlobalAtlas;GlobalAtlas=null;if(previous!=null)await previous.DisposeAsync();
    }
}

public partial class MainWindow
{
    internal void OpenGlobalAtlasManager(Window owner)
    {
        var dialog=DialogLayout.Window(owner,"GlobalAtlas",700,470,out var panel);
        var detail=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,8,4,16)};
        void Refresh(){var global=Atlas.AtlasPilot?.GlobalAtlas;detail.Text=Strings.Get("GlobalAtlasInfo")+Environment.NewLine+Environment.NewLine+(global==null || !File.Exists(global.Path)?Strings.Get("GlobalAtlasMissing"):global.Path+Environment.NewLine+new FileInfo(global.Path).Length.ToString("N0")+" "+Strings.Get("Bytes")+Environment.NewLine+global.Manifest.DataDate+Environment.NewLine+global.Manifest.Attribution);}
        var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
        var import=DialogLayout.Button("InstallGlobalAtlas",async()=>{
            var picker=new OpenFileDialog{Filter=Strings.Get("GlobalAtlasFilter"),CheckFileExists=true};if(picker.ShowDialog(dialog)!=true)return;
            actions.IsEnabled=false;
            try{await Atlas.AtlasPilot!.InstallGlobalAtlas(picker.FileName);Atlas.InvalidateVisual();Refresh();}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or FormatException or OverflowException or InvalidDataException or JsonException or ArgumentException){detail.Text=Strings.Get("GlobalAtlasInvalid");}
            finally{actions.IsEnabled=true;}
        });actions.Children.Add(import);
        actions.Children.Add(DialogLayout.Button("RemoveReference",async()=>{if(Atlas.AtlasPilot!=null)await Atlas.AtlasPilot.RemoveGlobalAtlasReference();Atlas.InvalidateVisual();Refresh();}));
        actions.Children.Add(DialogLayout.Button("Close",dialog.Close));panel.Children.Add(new ScrollViewer{Content=detail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Refresh();dialog.ShowDialog();
    }
}
