// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows;
using System.Windows.Automation;

namespace GeoStack.App;

public partial class MainWindow
{
    private bool dropImportActive;
    private bool closeAfterDrop;
    internal bool ImportOverlayVisible => ImportOverlay.Visibility == Visibility.Visible;
    internal void HideImportOverlay() => ImportOverlay.Visibility = Visibility.Collapsed;
    internal bool ShowImportOverlay(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop, false)) { HideImportOverlay(); return false; }
        ImportHint.Text = Strings.Get(ViewModel.IsRemoving || ViewModel.IsOrganizing?"LibraryBusy":ViewModel.IsScanning || dropImportActive ? "ImportBusy" : "DropToAdd");
        AutomationProperties.SetName(ImportOverlay, ImportHint.Text);
        ImportOverlay.Visibility = Visibility.Visible;
        return ViewModel.CanScan && !dropImportActive;
    }
    private void Import_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) { HideImportOverlay(); return; }
        e.Effects = ShowImportOverlay(e.Data) && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Import_DragLeave(object sender, DragEventArgs e) => HideImportOverlay();
    private async void Import_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return;
        e.Handled = true; e.Effects = DragDropEffects.None; HideImportOverlay();
        if ((e.AllowedEffects & DragDropEffects.Copy) == 0) return;
        if (!ViewModel.CanScan || dropImportActive) { ViewModel.Status = Strings.Get("LibraryBusy"); return; }
        e.Effects = DragDropEffects.Copy;
        try { await ImportDroppedData(e.Data); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        { ViewModel.Status = Strings.Get("ImportFailed"); }
    }
    internal async Task<bool> ImportDroppedData(IDataObject data)
    {
        HideImportOverlay();
        if (!ViewModel.CanScan || dropImportActive) { ViewModel.Status = Strings.Get("LibraryBusy"); return false; }
        if (!data.GetDataPresent(DataFormats.FileDrop, false) || data.GetData(DataFormats.FileDrop, false) is not string[] { Length: > 0 and <= 65536 } paths) return false;
        dropImportActive = true;
        try
        {
            var selected = Atlas.SelectedMediaId;
            await ViewModel.ImportAsync(paths);
            if (closeAfterDrop || !ViewModel.LastImportChanged) return true;
            IEnumerable<GeoStack.Core.MediaItem> visible = FilteredLibrary();
            if (sidebarPlace != null)
            {
                var anchor = selected ?? sidebarPlace.Items[0].Path;
                sidebarPlace = GeoStack.Core.Geo.Cluster(ViewModel.Items, Atlas.Camera.World).FirstOrDefault(c => c.Items.Any(m => m.Path.Equals(anchor, StringComparison.OrdinalIgnoreCase)));
                visible = sidebarPlace?.Items ?? ViewModel.Items;
            }
            await ShowSelection(visible, sidebarPlace == null ? Strings.Get(sidebarKey) : Atlas.Describe(sidebarPlace.Latitude, sidebarPlace.Longitude), selected);
            await placesRefresh;
            return true;
        }
        finally { dropImportActive = false; HideImportOverlay(); if (closeAfterDrop) _ = Dispatcher.BeginInvoke(Close); }
    }
}
