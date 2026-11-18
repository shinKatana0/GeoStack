// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO;
using GeoStack.Core;
using Xunit;
namespace GeoStack.Tests;
public sealed class WindowsReleaseTests
{
    [Fact]
    public async Task ActualFfmpegCancellationReapsItsProcess()
    {
        var root=Environment.GetEnvironmentVariable("GEOSTACK_ROOT") ?? Path.GetFullPath("../../../../../",AppContext.BaseDirectory);
        var directory=TestStorage.Create(root); Directory.CreateDirectory(directory);
        var executable=Path.GetFullPath(Path.Combine(directory,"ffmpeg.exe"));
        File.Copy(TestMediaTools.FFmpeg,executable);
        var observed=new HashSet<int>();
        using var cancellation=new CancellationTokenSource();
        var task=ToolProcess.Run(executable,["-nostdin","-v","error","-re","-stream_loop","-1","-i",Path.Combine(root,".local/format-fixtures/Generated.MP4"),"-f","null","-"],cancellation.Token);
        try
        {
            for(var i=0;i<40 && observed.Count==0;i++)
            {
                foreach(var p in Process.GetProcessesByName("ffmpeg"))
                    using(p) { try { if(string.Equals(p.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase)) observed.Add(p.Id); } catch(InvalidOperationException) {} }
                if(observed.Count==0) await Task.Delay(50);
            }
            Assert.NotEmpty(observed);
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
        for(var i=0;i<40;i++)
        {
            var alive=Process.GetProcessesByName("ffmpeg");
            var remaining=alive.Any(p=>observed.Contains(p.Id));
            foreach(var p in alive) p.Dispose();
            if(!remaining) return;
            await Task.Delay(50);
        }
        Assert.Fail("Cancelled FFmpeg process survived the cleanup deadline");
    }
}
