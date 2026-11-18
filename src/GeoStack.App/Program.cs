// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
namespace GeoStack.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        StartupTrace.Begin(args);
        var application = new App();
        StartupTrace.Mark("ApplicationConstructed");
        application.InitializeComponent();
        StartupTrace.Mark("ApplicationResourcesLoaded");
        application.Run();
    }
}
