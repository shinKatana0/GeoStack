// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GeoStack.App;

public partial class MainWindow
{
    private TextBlock? copyActionLabel;
    private void RefreshZoomActions()
    {
        ZoomInButton.IsEnabled=Atlas.CanZoomIn;ZoomOutButton.IsEnabled=Atlas.CanZoomOut;
    }
    private void RefreshCopyActions()
    {
        if(copyActionLabel==null)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            row.Children.Add(new TextBlock{Text="\uE8C8",FontFamily=new FontFamily("Segoe MDL2 Assets"),Foreground=Brushes.White,Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center});
            copyActionLabel=new TextBlock{Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(copyActionLabel);CopyButton.Content=row;
        }
        var count=selectedMedia.Ids.Count;
        RefreshSelectionSummary();
        var label=Strings.Get("CopyToLocation");
        copyActionLabel.Text=count>0?$"{label} ({count})":label;CopyButton.IsEnabled=count>0;
        AutomationProperties.SetName(CopyButton,copyActionLabel.Text);
        CopyStackButton.Visibility=sidebarPlace==null?Visibility.Collapsed:Visibility.Visible;
        CopyStackButton.IsEnabled=sidebarPlace!=null && ViewModel.Selection.Count>0;
    }
}
