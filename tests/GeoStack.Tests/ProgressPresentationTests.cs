// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace GeoStack.Tests;

public sealed class ProgressPresentationTests
{
    [Theory]
    [InlineData("#DFE6EA")]
    [InlineData("#364852")]
    public async Task ProgressShowsMeasuredFractionsWithoutAnimationClocks(string lineColor)
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            try
            {
                var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")!;XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
                var app=XDocument.Load(Path.Combine(root,"src/GeoStack.App/App.xaml"));
                var style=app.Descendants(ns+"Style").Single(e=>(string?)e.Attribute(x+"Key")=="QuietProgress");
                var dictionary=new XElement(ns+"ResourceDictionary",new XAttribute(XNamespace.Xmlns+"x",x),new XElement(style));
                var resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString());
                var progress=new ProgressBar{Width=200,Height=4,Maximum=1,Value=.25,Style=(Style)resources["QuietProgress"]};
                progress.Resources["Line"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(lineColor));progress.Resources["Accent"]=Brushes.Teal;
                void Layout(){progress.Measure(new Size(200,4));progress.Arrange(new Rect(0,0,200,4));progress.UpdateLayout();}
                progress.ApplyTemplate();Layout();
                var window=XDocument.Load(Path.Combine(root,"src/GeoStack.App/MainWindow.xaml"));
                var scan=window.Descendants(ns+"ProgressBar").Single(e=>((string?)e.Attribute("Value"))?.Contains("ScanFraction")==true);
                var bindingControl=(ProgressBar)XamlReader.Parse(new XElement(ns+"ProgressBar",new XAttribute("Value",scan.Attribute("Value")!.Value)).ToString());
                Assert.Equal(BindingMode.OneWay,BindingOperations.GetBinding(bindingControl,System.Windows.Controls.Primitives.RangeBase.ValueProperty)!.Mode);
                var indicator=(Border)progress.Template.FindName("PART_Indicator",progress);
                Assert.InRange(indicator.ActualWidth,49.9,50.1);
                progress.Value=.75;Layout();Assert.InRange(indicator.ActualWidth,149.9,150.1);
                progress.IsIndeterminate=true;Layout();Assert.False(indicator.HasAnimatedProperties);Assert.False(progress.HasAnimatedProperties);Assert.Null(progress.Template.FindName("PART_GlowRect",progress));
                completion.SetResult();
            }
            catch(Exception ex){completion.SetException(ex);}
            finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}
        }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
