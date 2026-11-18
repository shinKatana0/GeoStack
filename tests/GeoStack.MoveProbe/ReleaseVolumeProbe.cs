// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text.Json;
using GeoStack.Core;

// Explicitly invoked release diagnostic. Never runs during normal browsing or ordinary tests.
internal static class ReleaseVolumeProbe
{
    internal static async Task Run(string[] args)
    {
        if(args.Length is not (3 or 4))throw new ArgumentException("Two marked disposable roots required");
        var sourceRoot=Path.GetFullPath(args[1]);var destinationRoot=Path.GetFullPath(args[2]);
        if(!sourceRoot.Contains(Path.Combine(".local","release055","volume-"),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(destinationRoot).StartsWith("GeoStack-release-safety-",StringComparison.Ordinal))throw new IOException("Owned test roots required");
        var marker=File.ReadAllText(Path.Combine(sourceRoot,".geostack-test-owner"));
        if(!Guid.TryParse(marker,out _) || marker!=File.ReadAllText(Path.Combine(destinationRoot,".geostack-test-owner")) || Path.GetPathRoot(sourceRoot)==Path.GetPathRoot(destinationRoot))throw new IOException("Distinct real volumes and matching markers required");
        var results=new List<object>();
        async Task<CopyProposal> Row(string name)
        {
            var source=Path.Combine(sourceRoot,name+".jpg");
            if(!File.Exists(source))await File.WriteAllTextAsync(source,"Disposable GeoStack cross-volume release fixture: "+name);
            return new(){Source=source,Location="Disposable cross-volume fixture",RelativeDirectory=name,Destination=GeographicCopy.Destination(destinationRoot,name,name+".jpg"),Size=new FileInfo(source).Length,SourceHash=await GeographicCopy.Hash(source,default)};
        }
        void Require(bool condition,string reason){if(!condition)throw new IOException(reason);}
        var journal=Path.Combine(sourceRoot,"journal");
        if(args.Length==4)
        {
            var row=await Row("interrupt");
            await GeographicCopy.ExecuteMoveCore([row],destinationRoot,journal,null,default,new(false,phase=>{
                if(phase!="DestinationVerified")return;
                File.WriteAllText(Path.Combine(sourceRoot,"checkpoint"),phase);
                using var pause=new ManualResetEvent(false);pause.WaitOne();
            }));return;
        }
        foreach(var name in new[]{"success","collision","cancel","delete-failure"})
        {
            var row=await Row(name);var original=row.SourceHash;using var token=new CancellationTokenSource();
            if(name=="collision"){Directory.CreateDirectory(Path.GetDirectoryName(row.Destination)!);await File.WriteAllTextAsync(row.Destination,"Keep existing destination");}
            var result=(await GeographicCopy.ExecuteMoveCore([row],destinationRoot,journal,null,token.Token,new(false,phase=>{
                if(phase!="DestinationVerified")return;
                Require(File.Exists(row.Source),"Source vanished before verified checkpoint");
                if(name=="cancel")token.Cancel();
                if(name=="delete-failure")throw new IOException("Injected removal failure on actual cross-volume transfer");
            }))).Single();
            Require(result.TransferMethod=="VerifiedStagedTransfer" && result.DestinationVerified,"Real staged path not verified");
            Require(await GeographicCopy.Hash(result.Destination,default)==original,"Destination mismatch");
            var completed=name is "success" or "collision";
            Require(result.Status==(completed?CopyState.Completed:CopyState.SourceRetained),"Unexpected transfer state");
            Require(File.Exists(row.Source)!=completed,"Incorrect source disposition");
            if(!completed)Require(await GeographicCopy.Hash(row.Source,default)==original,"Retained source changed");
            if(name=="collision")Require(await File.ReadAllTextAsync(Path.Combine(destinationRoot,name,name+".jpg"))=="Keep existing destination","Destination overwritten");
            results.Add(new{Case=name,Status="PASS",result,RemovalFailureInjected=name=="delete-failure"});
        }
        var child=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        foreach(var value in new[]{"--cross-volume",sourceRoot,destinationRoot,"child"})child.ArgumentList.Add(value);
        using(var process=Process.Start(child)!)
        {
            var timeout=Stopwatch.StartNew();while(!File.Exists(Path.Combine(sourceRoot,"checkpoint")) && timeout.Elapsed<TimeSpan.FromSeconds(20) && !process.HasExited)await Task.Delay(20);
            Require(File.Exists(Path.Combine(sourceRoot,"checkpoint")),"Child missed durable checkpoint");process.Kill();await process.WaitForExitAsync();
        }
        var recovery=(await GeographicCopy.InspectInterruptedMoves(journal,default)).Single(r=>Path.GetFileName(r.Checkpoint.Source)=="interrupt.jpg");
        Require(recovery.SourceExists && recovery.DestinationMatches,"Killed transfer did not preserve both valid files");
        results.Add(new{Case="actual-child-termination-after-DestinationVerified",Status="PASS",recovery});
        await File.WriteAllTextAsync(Path.Combine(sourceRoot,"cross-volume-results.json"),JsonSerializer.Serialize(new{Status="PASS",SourceRoot=sourceRoot,DestinationRoot=destinationRoot,Core=typeof(GeographicCopy).Assembly.Location,Results=results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: real cross-volume transfer, collision, cancellation, injected removal failure and actual process interruption");
    }
}

