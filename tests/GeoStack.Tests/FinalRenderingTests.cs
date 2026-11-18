// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeoStack.App;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class FinalRenderingTests
{
    private static MapPackage Map(string id,int max,double west,double east,double south,double north)=>new(id,id,"unused",1,"png",0,max,west,south,east,north,"test","test");
    [Theory] [InlineData("ru-RU","ru-RU")] [InlineData("ru","ru-RU")] [InlineData("en-GB","en-US")] [InlineData("pl-PL","en-US")] [InlineData("ja-JP","en-US")]
    public void CleanProfileUsesSupportedSystemLanguageAndSystemTheme(string culture,string expected)
    {
        var path=Path.Combine(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory),".local",Guid.NewGuid()+".json");var settings=Preferences.Load(path,culture);Assert.Equal(expected,settings.Language);Assert.Equal("System",settings.Theme);
    }
    [Theory] [InlineData("System",true,true)] [InlineData("System",false,false)] [InlineData("Light",true,false)] [InlineData("Dark",false,true)]
    public void ThemeSelectionResolvesSystemAndExplicitChoices(string theme,bool osDark,bool expected)=>Assert.Equal(expected,Preferences.IsDarkTheme(theme,osDark));
    [Fact] public void PartialProfileUsesSystemLanguageWhileExplicitPreferencesWin()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory);var folder=TestStorage.Create(root);Directory.CreateDirectory(folder);var path=Path.Combine(folder,"preferences.json");
        File.WriteAllText(path,"{\"Theme\":\"Dark\"}");Assert.Equal("ru-RU",Preferences.Load(path,"ru-RU").Language);Assert.Equal("Dark",Preferences.Load(path,"ru-RU").Theme);
        new Preferences("en-US","Light","ru-RU","CountryCityDistrict").Save(path);var loaded=Preferences.Load(path,"ru-RU");Assert.Equal("en-US",loaded.Language);Assert.Equal("Light",loaded.Theme);Assert.Equal("ru-RU",loaded.FolderLanguage);
    }
    [Fact] public void CrossingDetailBoundarySelectsOneCompleteContextSurface()
    {
        var detail=Map("detail",15,20.9372,21.0872,52.1747,52.2847);var context=Map("context",12,20.4122,21.6122,51.7297,52.7297);var p=Geo.Project(52.2297,21.09);
        var plans=RasterComposition.Plans([detail,context],detail.Id,8388608,950,700,new(p.X,p.Y));Assert.Single(plans);Assert.Equal(context.Id,plans[0].Package.Id);
        p=Geo.Project(52.2297,21.0122);plans=RasterComposition.Plans([detail,context],detail.Id,8388608,950,700,new(p.X,p.Y));Assert.Equal(detail.Id,plans[0].Package.Id);Assert.Equal(context.Id,plans[1].Package.Id);
        p=Geo.Project(52.2297,24);Assert.Empty(RasterComposition.Plans([detail,context],detail.Id,8388608,950,700,new(p.X,p.Y)));
    }
    [Fact] public void ZoomCeilingUsesActualLocalNativeDataInsteadOfHidingLayers()
    {
        var detail=Map("detail",15,20.9372,21.0872,52.1747,52.2847);var context=Map("context",12,20.4122,21.6122,51.7297,52.7297);
        Assert.Equal(15,RasterComposition.NativeZoomAt([detail,context],detail.Id,52.2297,21.0122));Assert.Equal(12,RasterComposition.NativeZoomAt([detail,context],detail.Id,52.2297,21.3));Assert.Equal(12,RasterComposition.NativeZoomAt([],"world",52,21));
        var p=Geo.Project(52.2297,21.0122);Assert.NotEmpty(RasterComposition.Plans([detail,context],detail.Id,256*Math.Pow(2,15)*1.01,950,700,new(p.X,p.Y)));
    }
    [Fact] public void MosaicCopiesAdjacentPixelsExactlyAndPanChangesOnlyItsTransform()
    {
        var map=Map("raster-global",2,-180,180,-85,85);var plan=RasterComposition.Plans([map],map.Id,1024,500,200,new(.5,.5)).Single();
        var images=plan.Tiles.Select(t=>{var bytes=Enumerable.Repeat((byte)(t.Key.X*30+t.Key.Y),256*256*4).ToArray();var image=BitmapSource.Create(256,256,96,96,PixelFormats.Pbgra32,null,bytes,1024);image.Freeze();return image;}).ToArray();
        var frame=RasterComposition.Assemble(plan,images,TestContext.Current.CancellationToken);var pixels=new byte[plan.Columns*256*plan.Rows*256*4];frame.Image.CopyPixels(pixels,plan.Columns*1024,0);
        foreach(var tile in plan.Tiles){var x=(tile.Key.X-plan.Left)*256;var y=(tile.Key.Y-plan.Top)*256;Assert.Equal((byte)(tile.Key.X*30+tile.Key.Y),pixels[(y*plan.Columns*256+x)*4]);}
        var before=frame.Screen(1024,500,200,new(.5,.5));var after=frame.Screen(1024,500,200,new(.5-10d/1024,.5));Assert.Equal(10,after.X-before.X,5);Assert.Equal(before.Width,after.Width);
    }
}
