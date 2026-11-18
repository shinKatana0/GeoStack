// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace GeoStack.App;

public static class ApplicationInformation
{
    public static string InstalledVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString() ?? Strings.Get("VersionUnavailable");

    public static IReadOnlyList<(string Label, string RelativePath)> HelpDocuments { get; } = new[] {
        ("UserGuide", "README.md"), ("GeographicOrganizationGuide", "docs/GEOGRAPHIC_ORGANIZATION.md"),
        ("OfflineMapsGuide", "docs/OFFLINE_MAPS.md"), ("PhysicalAcceptanceGuide", "docs/PHYSICAL_ACCEPTANCE.md")
    };
}

/// <summary>Preferences and secondary information share one scrollable, keyboard-accessible page.</summary>
public sealed class SettingsWindow : Window
{
    internal ComboBox LanguagePicker { get; }
    internal ComboBox ThemePicker { get; }
    internal ScrollViewer ContentScroller { get; }
    internal Button CloseButton { get; }
    public string SelectedLanguage => LanguagePicker.SelectedIndex == 1 ? "ru-RU" : "en-US";
    public string SelectedTheme => new[] { "System", "Light", "Dark" }[Math.Max(0, ThemePicker.SelectedIndex)];

    public SettingsWindow(Window owner)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner; Icon = owner.Icon; Title = Strings.Get("Settings");
        Width = 640; Height = 760; MinWidth = 400; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new(24) }; Content = panel;
        CloseButton = new Button { Content = Strings.Get("Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(4, 12, 4, 0) };
        CloseButton.Click += (_, _) => Close();
        DockPanel.SetDock(CloseButton, Dock.Bottom); panel.Children.Add(CloseButton);
        var body = new StackPanel();
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Continue);
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        ContentScroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        panel.Children.Add(ContentScroller);

        LanguagePicker = new ComboBox { ItemsSource = new[] { Strings.Get("EnglishLanguage"), Strings.Get("RussianLanguage") }, SelectedIndex = App.Settings.Language == "ru-RU" ? 1 : 0 };
        ThemePicker = new ComboBox { ItemsSource = new[] { Strings.Get("System"), Strings.Get("Light"), Strings.Get("Dark") }, SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "System", "Light", "Dark" }, App.Settings.Theme)) };
        AddPreference(body, "SettingsLanguageLabel", "Language", LanguagePicker);
        AddPreference(body, "SettingsThemeLabel", "Theme", ThemePicker);
        body.Children.Add(Text("InitialPreferencesHint", 11));
        body.Children.Add(Text("Privacy", 11));

        body.Children.Add(Heading("About"));
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(4, 6, 4, 8) };
        identity.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/GeoStack;component/Branding/GeoStack.png")), Width = 48, Height = 48, Margin = new(0, 0, 12, 0) });
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = "GeoStack", FontSize = 20, FontWeight = FontWeights.SemiBold });
        name.Children.Add(new TextBlock { Text = string.Format(Strings.Get("InstalledVersion"), ApplicationInformation.InstalledVersion) });
        identity.Children.Add(name); body.Children.Add(identity);
        if(owner is MainWindow main)
        {
            var diagnostics=new TextBox{Text=RuntimeIdentity.Describe(main),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Margin=new Thickness(4,6,4,6)};
            diagnostics.SetResourceReference(BackgroundProperty,"Surface");diagnostics.SetResourceReference(ForegroundProperty,"Text");body.Children.Add(diagnostics);
            var copy=new Button{Content=Strings.Get("CopyDiagnostics"),HorizontalAlignment=HorizontalAlignment.Left};
            copy.Click+=(_,_)=>{try{Clipboard.SetText(RuntimeIdentity.Describe(main));}catch(System.Runtime.InteropServices.ExternalException){MessageBox.Show(this,Strings.Get("ClipboardUnavailable"));}};body.Children.Add(copy);
        }
        body.Children.Add(Text("AboutText"));
        body.Children.Add(Text("SettingsAttribution", 12));
        var legal = new WrapPanel(); body.Children.Add(legal);
        AddDocument(legal, "ThirdPartyLicenses", "licenses");
        AddDocument(legal, "RedistributionNotes", "docs/REDISTRIBUTION_STATUS.md");

        body.Children.Add(Heading("Help"));
        body.Children.Add(Text("HelpText"));
        var help = new WrapPanel(); body.Children.Add(help);
        foreach (var document in ApplicationInformation.HelpDocuments) AddDocument(help, document.Label, document.RelativePath);

        foreach (var button in legal.Children.Cast<Button>().Concat(help.Children.Cast<Button>()).Append(CloseButton))
        {
            var focus = new Style(typeof(Button), (Style)FindResource(typeof(Button)));
            var trigger = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
            trigger.Setters.Add(new Setter(BorderBrushProperty, FindResource("Accent"))); focus.Triggers.Add(trigger); button.Style = focus;
        }
        Loaded += (_, _) => LanguagePicker.Focus();
        WindowLayout.Attach(this, "Settings");
        SourceInitialized += (_, _) => App.ApplyTheme();
    }

    private static TextBlock Text(string key, double size = 13) => new() { Text = Strings.Get(key), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(4, 6, 12, 6) };
    private static TextBlock Heading(string key) => new() { Text = Strings.Get(key), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new(4, 18, 4, 6) };
    private static void AddPreference(Panel panel, string label, string name, ComboBox picker)
    {
        var heading = new Label { Content = Strings.Get(label), Target = picker, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 0) };
        heading.SetResourceReference(ForegroundProperty, "Text"); panel.Children.Add(heading);
        AutomationProperties.SetName(picker, Strings.Get(name)); panel.Children.Add(picker);
    }
    private void AddDocument(Panel panel, string key, string relative)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relative);
        var available = File.Exists(path) || Directory.Exists(path);
        var button = new Button { Content = Strings.Get(key), IsEnabled = available };
        AutomationProperties.SetName(button, Strings.Get(key));
        button.ToolTip = available ? path : Strings.Get("DocumentationUnavailable");
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += (_, _) => {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            { MessageBox.Show(this, Strings.Get("OpenDocumentationFailed") + Environment.NewLine + path, Title, MessageBoxButton.OK, MessageBoxImage.Information); }
        };
        panel.Children.Add(button);
    }
}
