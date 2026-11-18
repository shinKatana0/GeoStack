// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using GeoStack.Core;

if(args.Length>=1 && args[0]=="--cross-volume") { await ReleaseVolumeProbe.Run(args); return; }

if(args.Length!=3)throw new ArgumentException("Owned root, test directory and checkpoint required");
var root=Path.GetFullPath(args[0]);var directory=Path.GetFullPath(args[1]);var owner=Path.Combine(root,".local","owner055")+Path.DirectorySeparatorChar;
if(!directory.StartsWith(owner,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(directory).StartsWith("probe-",StringComparison.Ordinal))throw new InvalidOperationException("Disposable move probe directory required");
var source=Path.Combine(directory,"source.jpg");var destinationRoot=Path.Combine(directory,"destination");
var hash=await GeographicCopy.Hash(source,default);var row=new CopyProposal{Source=source,Destination=GeographicCopy.Destination(destinationRoot,"Country/City","source.jpg"),RelativeDirectory="Country/City",Location="Synthetic test only",Size=new FileInfo(source).Length,SourceHash=hash};
await GeographicCopy.ExecuteMoveCore([row],destinationRoot,Path.Combine(directory,"journal"),null,default,new(args[2]=="DestinationVerified",phase=>{
    if(phase!=args[2])return;
    File.WriteAllText(Path.Combine(directory,"checkpoint.txt"),phase);
    // Intentionally stop at a durable checkpoint until the test parent terminates only this disposable helper.
    using var wait=new ManualResetEvent(false);wait.WaitOne();
}));
