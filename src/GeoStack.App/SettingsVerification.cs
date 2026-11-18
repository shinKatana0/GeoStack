// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GeoStack.Core;

namespace GeoStack.App;

public partial class MainWindow
{
    internal async Task VerifySettingsNavigation()
    {
        var output = App.DataDirectory; var original = App.Settings;
        var checks = new List<object>();
        if (FindName("AboutButton") != null || FindName("HelpButton") != null) throw new InvalidOperationException("Secondary information remains on the toolbar");
        foreach (var language in new[] { "en-US", "ru-RU" }) foreach (var theme in new[] { "System", "Light", "Dark" })
        {
            ApplySettings(language, theme);
            var persisted = Preferences.Load(App.SettingsPath);
            if (persisted.Language != language || persisted.Theme != theme || persisted.FolderLanguage != original.FolderLanguage || persisted.FolderHierarchy != original.FolderHierarchy) throw new InvalidOperationException("Settings persistence regression");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            FeatureVerification.Snapshot(this, Path.Combine(output, $"toolbar-{language}-{theme}.png"));
            if (SettingsButton.Content is not TextBlock { Text: "\u2699" }) throw new InvalidOperationException("Settings gear content mismatch: " + SettingsButton.Content?.GetType().FullName + "/" + (SettingsButton.Content as TextBlock)?.Text);
            if (AutomationProperties.GetName(SettingsButton) != Strings.Get("Settings")) throw new InvalidOperationException("Settings gear accessibility name mismatch");
            if (SettingsButton.ToolTip as string != Strings.Get("Settings")) throw new InvalidOperationException("Settings gear tooltip mismatch");
            foreach (var constrained in new[] { false, true })
            {
                var settings = new SettingsWindow(this); settings.Show();
                // Effective DIP work area and 1.5x rasterization test layout; this does not change Windows DPI.
                settings.Width = 640; settings.Height = constrained ? 600 : 760;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var texts = Descendants(settings).OfType<TextBlock>().Select(t => t.Text).ToArray();
                var preferenceLabels = Descendants(settings).OfType<Label>().ToArray();
                var themeText = ((SolidColorBrush)App.Current.Resources["Text"]).Color;
                if (preferenceLabels.Length != 2 || preferenceLabels.Any(l => l.Foreground is not SolidColorBrush brush || brush.Color != themeText)) throw new InvalidOperationException("Preference label theme contrast regression");
                if (!texts.Contains(Strings.Get("About")) || !texts.Contains(Strings.Get("Help")) || !texts.Contains(Strings.Get("AboutText")) || !texts.Contains(string.Format(Strings.Get("InstalledVersion"), ApplicationInformation.InstalledVersion))) throw new InvalidOperationException("Missing Settings information");
                var documentNames = ApplicationInformation.HelpDocuments.Select(d => Strings.Get(d.Label))
                    .Concat(new[] { Strings.Get("ThirdPartyLicenses"), Strings.Get("RedistributionNotes") }).ToHashSet();
                var links = Descendants(settings).OfType<Button>().Where(b => documentNames.Contains(AutomationProperties.GetName(b))).ToArray();
                if (links.Length != 6 || links.Any(b => !b.IsEnabled || string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)))) throw new InvalidOperationException("Unavailable Settings documentation link");
                if (settings.SelectedLanguage != language || settings.SelectedTheme != theme || !settings.CloseButton.IsCancel) throw new InvalidOperationException("Preferences/keyboard close regression");
                var focusable = new[] { settings.LanguagePicker, settings.ThemePicker }.Cast<Control>().Concat(links).Append(settings.CloseButton).ToArray();
                if (focusable.Any(c => !c.Focusable || !c.IsTabStop)) throw new InvalidOperationException("Settings controls are not keyboard accessible");
                var suffix = constrained ? "150-effective-layout" : "100-layout";
                var scale = constrained ? 1.5 : 1;
                settings.ContentScroller.ScrollToTop(); settings.UpdateLayout();
                FeatureVerification.Snapshot(settings, Path.Combine(output, $"settings-{language}-{theme}-{suffix}-top.png"), scale);
                settings.ContentScroller.ScrollToBottom(); settings.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (settings.CloseButton.ActualHeight <= 0 || settings.CloseButton.TranslatePoint(new Point(), settings).Y + settings.CloseButton.ActualHeight > settings.ActualHeight) throw new InvalidOperationException("Close action is clipped");
                FeatureVerification.Snapshot(settings, Path.Combine(output, $"settings-{language}-{theme}-{suffix}-bottom.png"), scale);
                checks.Add(new { language, theme, EffectiveHeight = settings.Height, RasterScale = scale, AboutAndHelp = true, PreferenceLabelThemeColor = themeText.ToString(), LocalDocumentLinks = links.Length, Scrollable = settings.ContentScroller.ScrollableHeight > 0, FixedCloseVisible = true });
                settings.Close();
            }
        }
        ApplySettings(original.Language, original.Theme);
        File.WriteAllText(Path.Combine(output, "settings-verification.json"), JsonSerializer.Serialize(new { Status = "PASS", ApplicationInformation.InstalledVersion, Scope = "Packaged WPF controls, localized resources, persisted preferences and constrained layout; physical input and Windows 150% DPI NOT_TESTED", PriorHelpAction = "No dedicated Help action existed; existing About content and map hints retained", Settings = checks, PhysicalDpi = "NOT_TESTED", DocumentShellAssociation = "NOT_TESTED; paths verified without opening external applications" }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
