// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace GeoStack.Core;

public sealed record MoveRecovery(CopyJournalEntry Checkpoint, bool SourceExists, bool DestinationExists, bool DestinationMatches, string Advice, bool SourceAbsenceConfirmed=false);

public static partial class GeographicCopy
{
    // Fault injection is per operation, never global. Tests exercise the staged path on C: without claiming a second volume.
    internal sealed record MoveTestOptions(bool ForceStaged = false, Action<string>? Checkpoint = null, Func<long>? AvailableSpace = null);
    public static Task<IReadOnlyList<CopyJournalEntry>> ExecuteMove(IReadOnlyList<CopyProposal> plan, string root, string journalDirectory, IProgress<(int Done,int Total)>? progress, CancellationToken token)
        => ExecuteMoveCore(plan, root, journalDirectory, progress, token, null);

    internal static async Task<IReadOnlyList<CopyJournalEntry>> ExecuteMoveCore(IReadOnlyList<CopyProposal> plan, string root, string journalDirectory, IProgress<(int Done,int Total)>? progress, CancellationToken token, MoveTestOptions? test)
    {
        var operation=Guid.NewGuid(); ValidateAncestors(journalDirectory); Directory.CreateDirectory(journalDirectory);
        using var journalLease=new DestinationLease(MoveFileHandle.NativePath(journalDirectory));
        var journal=Path.Combine(journalDirectory,operation.ToString("N")+".jsonl");
        var results=new List<CopyJournalEntry>();
        void Write(CopyJournalEntry entry)
        {
            using(var log=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
            {log.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry)+"\n"));log.Flush(true);}
            test?.Checkpoint?.Invoke(entry.Phase);
        }
        foreach(var row in plan)
        {
            string? temporary=null,checksum=null;FileStream? committedDestination=null;var verified=false;var removed=false;var method="Unstarted";var phase="Planned";
            var state=row.Include?CopyState.Started:row.State is CopyState.Ready or CopyState.Rename?CopyState.Skipped:row.State;string? failure=null;
            CopyJournalEntry Entry()=>new(operation,DateTime.UtcNow,row.Source,row.Destination,row.Size,state,checksum,failure,OrganizationOperation.Move,phase,verified,removed,method);
            try
            {
                if(row.Include)
                {
                    token.ThrowIfCancellationRequested();
                    var source=Path.GetFullPath(row.Source);var destination=Destination(root,row.RelativeDirectory,Path.GetFileName(row.Destination));
                    var filename=Path.GetFileName(destination);
                    if(filename.TrimEnd(' ','.')!=filename || System.Text.RegularExpressions.Regex.IsMatch(filename.Split('.')[0].TrimEnd(),@"^(CON|PRN|AUX|NUL|COM[1-9\u00B9\u00B2\u00B3]|LPT[1-9\u00B9\u00B2\u00B3])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant))throw new IOException("Unsupported destination filename");
                    if(source.Equals(destination,StringComparison.OrdinalIgnoreCase))throw new IOException("Source and destination overlap");
                    ValidateAncestors(source);ValidateAncestors(destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var sourceLease=new DestinationLease(MoveFileHandle.NativePath(Path.GetDirectoryName(source)!));
                    using var destinationLease=new DestinationLease(MoveFileHandle.NativePath(Path.GetDirectoryName(destination)!));
                    using(var handle=MoveFileHandle.Open(source))
                    {
                        using var stream=new FileStream(new SafeFileHandle(handle.DangerousGetHandle(),false),FileAccess.Read,131072,false);
                        if(stream.Length!=row.Size)throw new IOException("Source size changed since preview");
                        checksum=Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
                        if(row.SourceHash==null || checksum!=row.SourceHash)throw new IOException("Source changed since preview");
                        var sameVolume=MoveFileHandle.Volume(source)==MoveFileHandle.Volume(Path.GetDirectoryName(destination)!);
                        method=sameVolume && test?.ForceStaged!=true?"AtomicRename":"VerifiedStagedTransfer";
                        phase="SourceVerified";Write(Entry());
                        var original=destination;var suffix=1;
                        string Next()=>Destination(root,row.RelativeDirectory,Path.GetFileNameWithoutExtension(original)+$" ({suffix++})"+Path.GetExtension(original));
                        while(File.Exists(destination) || Directory.Exists(destination))destination=Next();
                        row.Destination=destination;phase="TransferIntent";Write(Entry());token.ThrowIfCancellationRequested();
                        if(method=="AtomicRename")
                        {
                            while(true)
                            {
                                try{MoveFileHandle.Rename(handle,destination);break;}
                                catch(IOException) when(File.Exists(destination)||Directory.Exists(destination)){destination=Next();row.Destination=destination;phase="TransferIntent";Write(Entry());token.ThrowIfCancellationRequested();}
                            }
                            // The verified open file itself was renamed without replacement. No path-based source deletion occurs.
                            row.Destination=destination;verified=true;removed=true;phase="DestinationCommitted";Write(Entry());
                        }
                        else
                        {
                            if((test?.AvailableSpace?.Invoke()??new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace)<row.Size)throw new IOException("Insufficient disk space");
                            var written=File.GetLastWriteTimeUtc(source);var created=File.GetCreationTimeUtc(source);
                            temporary=Path.Combine(Path.GetDirectoryName(destination)!,".geostack-"+Guid.NewGuid().ToString("N")+".partial");
                            stream.Position=0;
                            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,FileOptions.Asynchronous|FileOptions.WriteThrough))
                            {await stream.CopyToAsync(output,131072,token);await output.FlushAsync(token);output.Flush(true);}
                            File.SetLastWriteTimeUtc(temporary,written);File.SetCreationTimeUtc(temporary,created);
                            if(new FileInfo(temporary).Length!=row.Size || await Hash(temporary,token)!=checksum)throw new IOException("Content verification failed");
                            token.ThrowIfCancellationRequested();
                            while(true)
                            {
                                try{File.Move(temporary,destination,false);temporary=null;break;}
                                catch(IOException) when(File.Exists(destination)||Directory.Exists(destination)){destination=Next();row.Destination=destination;phase="TransferIntent";Write(Entry());token.ThrowIfCancellationRequested();}
                            }
                            row.Destination=destination;
                            // Keep the committed destination pinned against replacement/write until source disposition and checkpoint finish.
                            committedDestination=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.SequentialScan);
                            if(committedDestination.Length!=row.Size || Convert.ToHexString(await SHA256.HashDataAsync(committedDestination,token))!=checksum)throw new IOException("Committed destination verification failed");
                            verified=true;phase="DestinationVerified";Write(Entry());
                            token.ThrowIfCancellationRequested();
                            MoveFileHandle.Remove(handle);removed=true;phase="SourceRemovalRequested";Write(Entry());
                        }
                    }
                    // Disposition closes above. An unexpected remaining source is a partial outcome, never a completed Move.
                    if(!SourceAbsent(source)){removed=false;state=File.Exists(source)||Directory.Exists(source)?CopyState.SourceRetained:CopyState.NeedsReview;failure="Verified destination retained; source absence is not confirmed";}
                    else
                    {
                        if(!File.Exists(row.Destination) || new FileInfo(row.Destination).Length!=row.Size || await Hash(row.Destination,CancellationToken.None)!=checksum)throw new IOException("Final move destination requires review");
                        state=verified && removed?CopyState.Completed:CopyState.Failed;
                    }
                }
            }
            catch(OperationCanceledException){state=verified?CopyState.SourceRetained:CopyState.Cancelled;failure=verified?"Cancelled after destination commitment; review both paths":null;}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {state=verified?(removed?CopyState.NeedsReview:CopyState.SourceRetained):CopyState.Failed;failure=ex.Message;}
            finally
            {
                // Keep the destination pinned until the original source handle has closed and its disposition has completed.
                committedDestination?.Dispose();
                if(temporary!=null)try{ValidateAncestors(temporary);File.Delete(temporary);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failure=(failure??"")+" Partial cleanup requires review: "+ex.Message;}
            }
            phase=state==CopyState.Completed?"FilesystemCompleted":"Result";var result=Entry();Write(result);results.Add(result);progress?.Report((results.Count,plan.Count));
        }
        return results;
    }

    public static async Task<IReadOnlyList<MoveRecovery>> InspectInterruptedMoves(string journalDirectory,CancellationToken token)
    {
        var result=new List<MoveRecovery>();
        foreach(var entry in ReadJournal(journalDirectory).Where(e=>e.Operation==OrganizationOperation.Move).GroupBy(e=>(e.OperationId,e.Source)).Select(g=>g.Last()).Where(e=>e.Phase!="CatalogReconciled" && (e.DestinationVerified || e.Phase!="Result")))
        {
            token.ThrowIfCancellationRequested();var source=File.Exists(entry.Source)||Directory.Exists(entry.Source);var absent=SourceAbsent(entry.Source);var destination=File.Exists(entry.Destination);var matches=false;
            try{matches=destination && entry.Sha256!=null && new FileInfo(entry.Destination).Length==entry.Size && await Hash(entry.Destination,token)==entry.Sha256;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ }
            result.Add(new(entry,source,destination,matches,"Review both paths; inaccessible source volumes are not treated as deletion. No automatic file changes",absent));
        }
        return result;
    }
    public static async Task AcknowledgeReconciledMove(CopyJournalEntry entry,string journalDirectory,CancellationToken token)
    {
        if(entry.Operation!=OrganizationOperation.Move || !SourceAbsent(entry.Source) || !File.Exists(entry.Destination) || new FileInfo(entry.Destination).Length!=entry.Size || await Hash(entry.Destination,token)!=entry.Sha256)throw new IOException("Recovery requires confirmed source absence and a verified destination; no files changed");
        ValidateAncestors(journalDirectory);using var lease=new DestinationLease(MoveFileHandle.NativePath(journalDirectory));
        var completed=entry with{Timestamp=DateTime.UtcNow,Status=CopyState.Completed,Phase="CatalogReconciled",DestinationVerified=true,SourceRemoved=true,Failure=null};
        using var stream=new FileStream(Path.Combine(journalDirectory,entry.OperationId.ToString("N")+".jsonl"),FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough);stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(completed)+"\n"));stream.Flush(true);
    }
    private static bool SourceAbsent(string path)
    {
        try
        {
            var source=Path.GetFullPath(path);var parent=Path.GetDirectoryName(source)!;ValidateAncestors(parent);
            // File.Exists also returns false for unavailable drives and access errors. Confirm absence in an accessible parent instead.
            using var entries=Directory.EnumerateFileSystemEntries(parent,Path.GetFileName(source),SearchOption.TopDirectoryOnly).GetEnumerator();return !entries.MoveNext();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return false;}
    }

    private static class MoveFileHandle
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int information,IntPtr buffer,uint size);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetVolumePathNameW(string path,StringBuilder volume,uint size);
        internal static SafeFileHandle Open(string path)
        {
            var handle=CreateFileW(NativePath(path),0x80010000,1,IntPtr.Zero,3,0x08200000,IntPtr.Zero);
            if(handle.IsInvalid){handle.Dispose();throw new IOException("Cannot lock source for verified move",new Win32Exception(Marshal.GetLastWin32Error()));}return handle;
        }
        internal static string Volume(string path)
        {var value=new StringBuilder(32768);if(!GetVolumePathNameW(NativePath(path),value,(uint)value.Capacity))throw new IOException("Cannot identify volume",new Win32Exception(Marshal.GetLastWin32Error()));return value.ToString().Replace(@"\\?\UNC\",@"\\").Replace(@"\\?\","").ToUpperInvariant();}
        internal static string NativePath(string path)
        {var full=Path.GetFullPath(path);return full.Length<240 || full.StartsWith(@"\\?\",StringComparison.Ordinal)?full:full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;}
        internal static void Rename(SafeFileHandle handle,string destination)
        {
            // Win32 requires a terminated FILE_RENAME_INFO name even though FileNameLength excludes that terminator.
            var name=Encoding.Unicode.GetBytes(NativePath(destination));var offset=IntPtr.Size==8?20:12;var size=offset+name.Length+2;
            var memory=Marshal.AllocHGlobal(size);
            try{Marshal.Copy(new byte[size],0,memory,size);Marshal.WriteInt32(memory,IntPtr.Size==8?16:8,name.Length);Marshal.Copy(name,0,IntPtr.Add(memory,offset),name.Length);if(!SetFileInformationByHandle(handle,3,memory,(uint)size))throw new IOException("Atomic rename failed",new Win32Exception(Marshal.GetLastWin32Error()));}
            finally{Marshal.FreeHGlobal(memory);}
        }
        internal static void Remove(SafeFileHandle handle)
        {
            var memory=Marshal.AllocHGlobal(4);try{Marshal.WriteInt32(memory,1);if(!SetFileInformationByHandle(handle,4,memory,4))throw new IOException("Source retained after verified transfer",new Win32Exception(Marshal.GetLastWin32Error()));}finally{Marshal.FreeHGlobal(memory);}
        }
    }
}
