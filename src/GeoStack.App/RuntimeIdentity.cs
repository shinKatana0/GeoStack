// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GeoStack.App;

public static class RuntimeIdentity
{
    public static string ProductVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version!.ToString();
    public static string Version => ProductVersion.Split('+')[0];
    public static string BuildCommit => ProductVersion.Contains('+') ? ProductVersion.Split('+')[1] : "Unavailable";
    public static string Channel => typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a=>a.Key=="GeoStackChannel")?.Value ?? "Production";
    public static string WindowTitle => Channel=="Preview" ? "GeoStack \u2014 PREVIEW "+Version : "GeoStack";
    public static string Executable => Path.GetFullPath(Environment.ProcessPath!);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    private static extern IntPtr GetCommandLineW();
    public static string NativeCommandLine => Marshal.PtrToStringUni(GetCommandLineW()) ?? string.Empty;
    public static string MapEngine(MainWindow window) => window.Atlas.AtlasPilot is {Enabled:true} ? "Hybrid Atlas (native WPF)" : "Offline raster/world fallback";
    public static string Hash(string path) { using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input)); }
    public static string Describe(MainWindow window) => string.Join(Environment.NewLine,new[]{
        ("RuntimeVersion",ProductVersion),("RuntimeCommit",BuildCommit),("RuntimeChannel",Channel),
        ("RuntimeExecutable",Executable),("RuntimeProfile",App.DataDirectory),("RuntimeEngine",MapEngine(window)),
        ("RuntimeCatalog",Path.Combine(App.DataDirectory,"Maps","catalog.json"))
    }.Select(p=>Strings.Get(p.Item1)+": "+p.Item2));
    public static object[] Assemblies() => new[]{typeof(App).Assembly,typeof(GeoStack.Core.MediaItem).Assembly}.Select(a=>(object)new{
        Name=a.GetName().Name,Path=System.IO.Path.GetFullPath(a.Location),SHA256=Hash(a.Location),Mvid=a.ManifestModule.ModuleVersionId
    }).ToArray();
}
