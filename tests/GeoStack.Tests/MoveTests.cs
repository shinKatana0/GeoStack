// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using GeoStack.Core;
using Xunit;

namespace GeoStack.Tests;

public sealed class MoveTests
{
    private static string Temporary()=>TestStorage.Create(Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory));
    private static async Task<(CopyProposal Row,string Root,string Journal)> Prepare(string name="source.jpg")
    {
        var source=Path.Combine(Temporary(),name);await File.WriteAllTextAsync(source,"Disposable move fixture with verified bytes",TestContext.Current.CancellationToken);
        var root=Temporary();var row=new CopyProposal{Source=source,RelativeDirectory="Country/City",Location="Verified test geography",Destination=GeographicCopy.Destination(root,"Country/City",name),Size=new FileInfo(source).Length,SourceHash=await GeographicCopy.Hash(source,TestContext.Current.CancellationToken)};
        return(row,root,Temporary());
    }
    [Fact] public async Task SameVolumeMoveRenamesVerifiedOpenFileWithoutCopying()
    {
        var (row,root,journal)=await Prepare();var hash=row.SourceHash;
        var result=Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken));
        Assert.Equal(CopyState.Completed,result.Status);Assert.Equal("AtomicRename",result.TransferMethod);Assert.True(result.SourceRemoved);Assert.True(result.DestinationVerified);Assert.False(File.Exists(row.Source));Assert.Equal(hash,await GeographicCopy.Hash(result.Destination,TestContext.Current.CancellationToken));
        Assert.Single(await GeographicCopy.InspectInterruptedMoves(journal,TestContext.Current.CancellationToken));
        await GeographicCopy.AcknowledgeReconciledMove(result,journal,TestContext.Current.CancellationToken);
        Assert.Empty(await GeographicCopy.InspectInterruptedMoves(journal,TestContext.Current.CancellationToken));
    }
    [Fact] public async Task StagedPathVerifiesAndDurablyCheckpointsBeforeSourceRemoval()
    {
        var (row,root,journal)=await Prepare();var saw=false;
        var result=Assert.Single(await GeographicCopy.ExecuteMoveCore([row],root,journal,null,TestContext.Current.CancellationToken,new(true,phase=>{
            if(phase!="DestinationVerified")return;saw=true;Assert.True(File.Exists(row.Source));Assert.Equal(row.SourceHash,GeographicCopy.Hash(row.Destination,default).GetAwaiter().GetResult());Assert.Contains(GeographicCopy.ReadJournal(journal),e=>e.Phase==phase && e.DestinationVerified);
        })));
        Assert.True(saw);Assert.Equal(CopyState.Completed,result.Status);Assert.Equal("VerifiedStagedTransfer",result.TransferMethod);Assert.False(File.Exists(row.Source));
    }
    [Fact] public async Task CancellationAfterCommitPreservesSourceAndVerifiedDestination()
    {
        var (row,root,journal)=await Prepare();using var cancellation=new CancellationTokenSource();
        var result=Assert.Single(await GeographicCopy.ExecuteMoveCore([row],root,journal,null,cancellation.Token,new(true,phase=>{if(phase=="DestinationVerified")cancellation.Cancel();})));
        Assert.Equal(CopyState.SourceRetained,result.Status);Assert.True(File.Exists(row.Source));Assert.Equal(row.SourceHash,await GeographicCopy.Hash(result.Destination,TestContext.Current.CancellationToken));Assert.False(result.SourceRemoved);
    }
    [Fact] public async Task SimulatedCrashAfterCheckpointRequiresReviewAndNeverDeletesDuringRecovery()
    {
        var (row,root,journal)=await Prepare();
        await Assert.ThrowsAsync<SimulatedCrash>(()=>GeographicCopy.ExecuteMoveCore([row],root,journal,null,TestContext.Current.CancellationToken,new(true,phase=>{if(phase=="DestinationVerified")throw new SimulatedCrash();})));
        var recovery=Assert.Single(await GeographicCopy.InspectInterruptedMoves(journal,TestContext.Current.CancellationToken));Assert.True(recovery.SourceExists);Assert.True(recovery.DestinationMatches);Assert.True(File.Exists(row.Source));Assert.True(File.Exists(row.Destination));
    }
    private sealed class SimulatedCrash:Exception;
    [Fact] public async Task DeletionFailureIsPartialAndPreservesBothFiles()
    {
        var (row,root,journal)=await Prepare();
        var result=Assert.Single(await GeographicCopy.ExecuteMoveCore([row],root,journal,null,TestContext.Current.CancellationToken,new(true,phase=>{if(phase=="DestinationVerified")throw new IOException("Injected source-removal failure");})));
        Assert.Equal(CopyState.SourceRetained,result.Status);Assert.True(File.Exists(row.Source));Assert.True(File.Exists(row.Destination));
    }
    [Fact] public async Task OccupiedDestinationAfterPreviewIsNeverOverwritten()
    {
        var (row,root,journal)=await Prepare();Directory.CreateDirectory(Path.GetDirectoryName(row.Destination)!);await File.WriteAllTextAsync(row.Destination,"Other file",TestContext.Current.CancellationToken);var occupied=row.Destination;
        var result=Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken));
        Assert.Equal(CopyState.Completed,result.Status);Assert.NotEqual(occupied,result.Destination);Assert.Equal("Other file",await File.ReadAllTextAsync(occupied,TestContext.Current.CancellationToken));
    }
    [Fact] public async Task ChangedSourceAndLockedSourceRemainUntouched()
    {
        var (row,root,journal)=await Prepare();await File.AppendAllTextAsync(row.Source,"changed",TestContext.Current.CancellationToken);
        Assert.Equal(CopyState.Failed,Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken)).Status);Assert.True(File.Exists(row.Source));Assert.False(File.Exists(row.Destination));
        using var locked=new FileStream(row.Source,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Assert.Equal(CopyState.Failed,Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken)).Status);
    }
    [Fact] public async Task UnicodeBatchRelocationPersistsExclusionsAndExactNewPaths()
    {
        var (a,root,journal)=await Prepare("travel-\u65e5\u672c.jpg");a.RelativeDirectory="Country/\u57ce\u5e02";a.Destination=GeographicCopy.Destination(root,a.RelativeDirectory,Path.GetFileName(a.Source));var (b,_,_)=await Prepare("b.mov");b.RelativeDirectory="Country/Other City";b.Destination=GeographicCopy.Destination(root,b.RelativeDirectory,"b.mov");
        using var index=new MediaIndex(Path.Combine(Temporary(),"index.db"));MediaItem[] items=[new(a.Source,35.68,139.76,"Photo"),new(b.Source,47.37,8.54,"Video")];foreach(var item in items)index.Upsert(item);
        var results=await GeographicCopy.ExecuteMove([a,b],root,journal,null,TestContext.Current.CancellationToken);Assert.All(results,r=>Assert.Equal(CopyState.Completed,r.Status));
        index.RelocateMany(items.Zip(results).Select(p=>(p.First,p.Second.Destination)).ToArray());Assert.Equal(2,index.ReadAll().Count);Assert.All(items,m=>Assert.True(index.Exclusions().Contains(MediaInputs.NormalizePath(m.Path))));Assert.Equal(results.Select(r=>r.Destination).Order(),index.ReadAll().Select(m=>m.Path).Order());
        foreach(var item in items)index.Upsert(item);Assert.Equal(2,index.ReadAll().Count);
    }
    [Fact] public async Task SameSourceDestinationAndPreCancelledMovePreserveSource()
    {
        var (row,root,journal)=await Prepare();row.RelativeDirectory="Country";root=Path.GetDirectoryName(Path.GetDirectoryName(row.Source))!;
        using var token=new CancellationTokenSource();token.Cancel();Assert.Equal(CopyState.Cancelled,Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,token.Token)).Status);Assert.True(File.Exists(row.Source));
        row.Include=false;Assert.Equal(CopyState.Skipped,Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken)).Status);Assert.True(File.Exists(row.Source));
    }
    [Theory][InlineData("TransferIntent",false)][InlineData("DestinationVerified",true)]
    public async Task ActualOwnedChildTerminationAtDurableCheckpointPreservesData(string checkpoint,bool destinationExists)
    {
        var repository=Environment.GetEnvironmentVariable("GEOSTACK_ROOT")??Path.GetFullPath("../../../../../",AppContext.BaseDirectory);
        var directory=Path.GetFullPath(Path.Combine(repository,".local","owner055","probe-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(directory);
        var source=Path.Combine(directory,"source.jpg");await File.WriteAllTextAsync(source,"Disposable process-interruption fixture",TestContext.Current.CancellationToken);var hash=await GeographicCopy.Hash(source,TestContext.Current.CancellationToken);
        var executable=Path.Combine(repository,"tests/GeoStack.MoveProbe/bin/Release/net10.0/GeoStack.MoveProbe.exe");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};foreach(var argument in new[]{repository,directory,checkpoint})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Move probe did not start");
        try
        {
            var ready=Path.Combine(directory,"checkpoint.txt");var clock=Stopwatch.StartNew();while(!File.Exists(ready) && !process.HasExited && clock.Elapsed.TotalSeconds<20)await Task.Delay(25,TestContext.Current.CancellationToken);
            Assert.True(File.Exists(ready),"Native child did not reach checkpoint; execution is not PASS");
            process.Kill();await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            var recovery=Assert.Single(await GeographicCopy.InspectInterruptedMoves(Path.Combine(directory,"journal"),TestContext.Current.CancellationToken));Assert.True(recovery.SourceExists);Assert.Equal(destinationExists,recovery.DestinationExists);Assert.Equal(destinationExists,recovery.DestinationMatches);
            // Windows can still be releasing the terminated helper's DELETE handle. Share delete, but not writes, for this read-only byte assertion.
            await using var preserved=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,131072,FileOptions.Asynchronous);
            Assert.Equal(hash,Convert.ToHexString(await SHA256.HashDataAsync(preserved,TestContext.Current.CancellationToken)));
        }
        finally{if(!process.HasExited){process.Kill();await process.WaitForExitAsync(TestContext.Current.CancellationToken);}}
    }
    [Fact] public async Task InsufficientStagedSpaceAndSamePathNeverRemoveSource()
    {
        var (row,root,journal)=await Prepare();var result=Assert.Single(await GeographicCopy.ExecuteMoveCore([row],root,journal,null,TestContext.Current.CancellationToken,new(true,AvailableSpace:()=>0)));
        Assert.Equal(CopyState.Failed,result.Status);Assert.True(File.Exists(row.Source));Assert.False(File.Exists(row.Destination));
        var sourceDirectory=Path.GetDirectoryName(Path.GetFullPath(row.Source))!;root=Path.GetDirectoryName(sourceDirectory)!;row.RelativeDirectory=Path.GetFileName(sourceDirectory);row.Destination=Path.GetFullPath(row.Source);
        result=Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken));Assert.Equal(CopyState.Failed,result.Status);Assert.True(File.Exists(row.Source));
    }
    [Fact] public async Task LongNativeDestinationRemainsCorrectAndVerified()
    {
        var (row,root,journal)=await Prepare();row.RelativeDirectory=string.Join('/',Enumerable.Range(0,6).Select(i=>"segment-"+i+new string('x',38)));row.Destination=GeographicCopy.Destination(root,row.RelativeDirectory,"source.jpg");
        Assert.True(row.Destination.Length>260);var result=Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken));Assert.True(result.Status==CopyState.Completed,result.Failure);Assert.Equal(row.SourceHash,await GeographicCopy.Hash(result.Destination,TestContext.Current.CancellationToken));Assert.False(File.Exists(row.Source));
    }
    [Fact] public async Task ReadOnlyStagedSourceIsNeverSilentlyDeleted()
    {
        var (row,root,journal)=await Prepare();File.SetAttributes(row.Source,FileAttributes.ReadOnly);
        try
        {
            var result=Assert.Single(await GeographicCopy.ExecuteMoveCore([row],root,journal,null,TestContext.Current.CancellationToken,new(true)));
            Assert.True(File.Exists(row.Source));Assert.Equal(row.SourceHash,await GeographicCopy.Hash(row.Source,TestContext.Current.CancellationToken));Assert.Equal(File.Exists(row.Destination)?CopyState.SourceRetained:CopyState.Failed,result.Status);
        }
        finally{File.SetAttributes(row.Source,FileAttributes.Normal);}
    }
    [Fact] public async Task ReservedDestinationNameIsRejectedBeforeAnyRename()
    {
        var (row,root,journal)=await Prepare();row.RelativeDirectory="Country";row.Destination=Path.Combine(root,"Country","CON.jpg");
        var result=Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken));Assert.Equal(CopyState.Failed,result.Status);Assert.Equal(row.SourceHash,await GeographicCopy.Hash(row.Source,TestContext.Current.CancellationToken));Assert.False(result.SourceRemoved);
    }
    [Fact] public async Task CopyRecordsVerifiedDestinationWithoutRemovingSource()
    {
        var (row,root,journal)=await Prepare();var result=Assert.Single(await GeographicCopy.Execute([row],root,journal,null,TestContext.Current.CancellationToken));Assert.Equal(OrganizationOperation.Copy,result.Operation);Assert.Equal(CopyState.Completed,result.Status);Assert.True(result.DestinationVerified);Assert.False(result.SourceRemoved);Assert.Equal(row.SourceHash,await GeographicCopy.Hash(row.Source,TestContext.Current.CancellationToken));Assert.Equal(row.SourceHash,await GeographicCopy.Hash(result.Destination,TestContext.Current.CancellationToken));
    }
    [Fact] public async Task InaccessibleOriginalParentDoesNotBecomeConfirmedDeletionDuringRecovery()
    {
        var (row,root,journal)=await Prepare();var moved=Assert.Single(await GeographicCopy.ExecuteMove([row],root,journal,null,TestContext.Current.CancellationToken));Assert.Equal(CopyState.Completed,moved.Status);
        var parent=Path.GetDirectoryName(Path.GetFullPath(row.Source))!;GeographicCopy.ValidateAncestors(parent);Directory.Delete(parent);
        var pending=Assert.Single(await GeographicCopy.InspectInterruptedMoves(journal,TestContext.Current.CancellationToken));Assert.False(pending.SourceAbsenceConfirmed);Assert.True(pending.DestinationMatches);
        await Assert.ThrowsAsync<IOException>(()=>GeographicCopy.AcknowledgeReconciledMove(moved,journal,TestContext.Current.CancellationToken));Assert.Equal(row.SourceHash,await GeographicCopy.Hash(moved.Destination,TestContext.Current.CancellationToken));
    }
}
