// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GeoStack.Core;
using Microsoft.Win32;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifyFinalRendering(string root)
    {
        await libraryInitialized.Task.WaitAsync(TimeSpan.FromSeconds(45)); await AtlasReady;
        var evidence=Path.Combine(App.DataDirectory,"evidence");Directory.CreateDirectory(evidence);var initial=App.Settings;
        var clean=!File.Exists(App.SettingsPath);var culture=App.StartupUICulture;
        if(!clean || initial.Language!=Preferences.DetectLanguage(culture) || initial.Theme!="System")throw new InvalidOperationException("Clean profile did not use system defaults");
        var osDark=(int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1)==0;
        var actualDark=((SolidColorBrush)Application.Current.Resources["Background"]).Color.R<100;
        if(osDark!=actualDark)throw new InvalidOperationException("System theme mismatch");
        await SettleBrowsing();FeatureVerification.Snapshot(this,Path.Combine(evidence,"clean-system-startup.png"));
        var settings=new SettingsWindow(this);settings.Show();await Task.Delay(100);
        if(settings.ThemePicker.SelectedIndex!=0)throw new InvalidOperationException("System theme not selected");FeatureVerification.Snapshot(settings,Path.Combine(evidence,"clean-system-settings.png"));settings.Close();
        var observations=new List<object>();
        foreach(var theme in new[]{"Light","Dark"})
        {
            App.Settings=initial with {Theme=theme};App.ApplyTheme();
            foreach(var city in new[]{(Id:"warsaw",Lat:52.2297,Lon:21.0122),(Id:"kyoto",Lat:34.9858,Lon:135.7588)})
            {
                var map=Atlas.Tiles.Catalog.Packages.Single(m=>Path.GetFileName(m.Path)==city.Id+"-city.mbtiles");Atlas.Tiles.Catalog.Preferred=map.Id;Atlas.Tiles.Clear();
                Atlas.SetProbeCamera(city.Lat,city.Lon,15);await SettleBrowsing();
                if(Atlas.ActiveTiles.RenderedMapIds.Count!=1 || !Atlas.ActiveTiles.RenderedMapIds.Contains(map.Id) || Atlas.Tiles.VisibleNativeZoom!=15)throw new InvalidOperationException("Detailed whole-frame composition unavailable");
                var positions=Atlas.MediaPositionsForVerification();
                FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-detail-"+theme+".png"));
                for(var i=0;i<40;i++)Atlas.ZoomBy(1.5);await SettleBrowsing();
                if(Atlas.TileZoom>15 || Atlas.ActiveTiles.DrawnTileCount==0 || Atlas.Tiles.VisibleNativeZoom!=15)throw new InvalidOperationException("High zoom lost raster coverage");
                FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-maximum-zoom-"+theme+".png"),1.5);
                Atlas.SetProbeCamera(city.Lat,city.Lon+.07,15);await SettleBrowsing();
                var context=Atlas.Tiles.Catalog.Packages.Single(m=>Path.GetFileName(m.Path)==city.Id+"-context-city.mbtiles");
                if(Atlas.ActiveTiles.RenderedMapIds.Count!=1 || !Atlas.ActiveTiles.RenderedMapIds.Contains(context.Id))throw new InvalidOperationException("Boundary did not select coherent full context");
                FeatureVerification.Snapshot(this,Path.Combine(evidence,city.Id+"-boundary-context-"+theme+".png"));
                for(var i=0;i<100;i++){Atlas.PanPixels(new Vector(i%2==0?35:-35,0));await Task.Delay(12);if(Atlas.ActiveTiles.RenderedMapIds.Count>1)throw new InvalidOperationException("Mixed raster surfaces during pan");}
                await SettleBrowsing();Atlas.SetProbeCamera(city.Lat,city.Lon,15);await SettleBrowsing();
                var after=Atlas.MediaPositionsForVerification();if(positions.Any(p=>!after.TryGetValue(p.Key,out var value)||(value-p.Value).Length>.01))throw new InvalidOperationException("Marker coordinates changed");
                observations.Add(new{City=city.Id,Theme=theme,Detail=true,MaximumNativeZoom=Atlas.Tiles.VisibleNativeZoom,BoundaryUsesContextOnly=true,MarkerAlignment=true,PanIterations=100});
            }
        }
        App.Settings=initial;Strings.SetLanguage(initial.Language);App.ApplyTheme();Localize();
        if(File.Exists(App.SettingsPath))throw new InvalidOperationException("Verification overwrote clean-profile preferences");
        File.WriteAllText(Path.Combine(App.DataDirectory,"final-render.json"),JsonSerializer.Serialize(new{Status="PASS",Scope="Actual packaged WPF surfaces; physical mouse/DPI NOT_TESTED",CleanProfile=clean,HostUICulture=culture,Language=initial.Language,Theme=initial.Theme,SystemDark=osDark,ResolvedDark=actualDark,PreferencesNotOverwritten=true,Observations=observations},new JsonSerializerOptions{WriteIndented=true}));
    }
}
