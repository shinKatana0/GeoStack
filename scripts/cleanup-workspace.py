# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Apply only a reviewed workspace cleanup manifest. Default is a dry run; no recursive deletion or ACL changes."""
from pathlib import Path
import argparse,ctypes,gzip,hashlib,json,os,stat,time,collections,subprocess
from ctypes import wintypes
ROOT=Path(__file__).resolve().parent.parent
PROTECTED=('src/','tests/','scripts/','docs/','assets/','licenses/','.git/','tools/','dist/GeoStack-portable-win-x64/','dist/GeoStack-0.5-consolidated-rc-win-x64/','dist/GeoStack-geographic-data-0.4/','.local/production-backups/pre05-20261114-001140/','.local/storage-cleanup/')
def safe_path(relative):
 if not relative or Path(relative).is_absolute() or '..' in Path(relative).parts:raise ValueError('Invalid relative target')
 name=relative.replace('\\','/')
 build_output=any(name.startswith(p+'/'+kind+'/') for p in ['src/GeoStack.App','src/GeoStack.Core','tests/GeoStack.Tests'] for kind in ['bin','obj'])
 if any(name.casefold().startswith(p.casefold()) for p in PROTECTED) and not build_output:raise ValueError('Protected target')
 path=ROOT/name
 if os.path.commonpath([str(ROOT),os.path.abspath(path)]).casefold()!=str(ROOT).casefold():raise ValueError('Target escaped workspace')
 current=path
 while current!=ROOT:
  if getattr(os.lstat(current),'st_file_attributes',0)&0x400:raise ValueError('Reparse target/ancestor refused')
  current=current.parent
 return path

def unlink_generated(path):
 try:os.unlink(path);return
 except PermissionError:
  if not (os.stat(path,follow_symlinks=False).st_file_attributes&1):raise
 # Windows 11 documented disposition flag deletes this name without changing the read-only
 # attribute on retained hard-linked seed files. It does not modify an ACL or security policy.
 k=ctypes.WinDLL('kernel32',use_last_error=True)
 k.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE];k.CreateFileW.restype=wintypes.HANDLE
 k.SetFileInformationByHandle.argtypes=[wintypes.HANDLE,ctypes.c_int,ctypes.c_void_p,wintypes.DWORD];k.SetFileInformationByHandle.restype=wintypes.BOOL
 k.CloseHandle.argtypes=[wintypes.HANDLE]
 handle=k.CreateFileW(str(path),0x10000|0x80,7,None,3,0x200000,None)
 if handle==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
 try:
  flags=wintypes.DWORD(0x1|0x2|0x10)
  if not k.SetFileInformationByHandle(handle,21,ctypes.byref(flags),ctypes.sizeof(flags)):raise ctypes.WinError(ctypes.get_last_error())
 finally:k.CloseHandle(handle)

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--manifest',type=Path,default=ROOT/'.local/storage-cleanup/candidates.jsonl.gz');ap.add_argument('--apply',action='store_true');ap.add_argument('--confirm',default='');args=ap.parse_args()
 if not args.manifest.resolve().is_relative_to(ROOT/'.local/storage-cleanup'):raise ValueError('Manifest must be in the workspace audit directory')
 digest=hashlib.sha256(args.manifest.read_bytes()).hexdigest();header=json.loads(args.manifest.with_suffix('').with_suffix('.summary.json').read_text(encoding='utf-8'))
 if header['ManifestSHA256']!=digest:raise ValueError('Manifest hash differs')
 print(json.dumps({'Mode':'APPLY' if args.apply else 'DRY_RUN','Files':header['Files'],'LogicalBytes':header['LogicalBytes'],'EstimatedPayloadReclaimBytes':header['EstimatedPayloadReclaimBytes'],'ManifestSHA256':digest}),flush=True)
 if not args.apply:return
 if args.confirm!=digest:raise ValueError('Explicit --confirm must equal the reviewed manifest SHA-256')
 tracked=set(subprocess.check_output(['git','-C',str(ROOT),'ls-files','-z']).decode('utf-8').split('\0'))
 out=ROOT/'.local/storage-cleanup';log=out/'deletion-results.jsonl';counts=collections.Counter();sizes=collections.Counter();parents=set();errors=[];start=time.monotonic();reported=0
 if (out/'deletion-summary.json').exists():raise ValueError('A completed audit already exists; preserve its records and prepare a new reviewed audit before another apply')
 with gzip.open(args.manifest,'rt',encoding='utf-8') as records,log.open('a',encoding='utf-8') as writer:
  for line in records:
   row=json.loads(line);path=None
   try:
    if row['Path'] in tracked:raise ValueError('Git-tracked file refused')
    if row.get('Git')!='ignored':raise ValueError('Only reviewed ignored generated files are eligible')
    path=safe_path(row['Path']);s=os.stat(path,follow_symlinks=False)
    if s.st_size!=row['Bytes'] or s.st_mtime_ns!=row['MtimeNs'] or s.st_ino!=row['FileId']:raise ValueError('File changed since dry run')
    unlink_generated(path);counts[row['Category']]+=1;sizes[row['Category']]+=row['Bytes'];parents.add(path.parent)
   except Exception as ex:
    error={'Path':row['Path'],'Result':'ERROR','Error':str(ex)};errors.append(error);writer.write(json.dumps(error,ensure_ascii=True)+'\n')
   completed=sum(counts.values())
   if completed-reported>=20000:print('Deleted',completed,'files',flush=True);reported=completed
 # Remove only newly empty directories below reviewed generated roots, never a protected/root directory.
 boundaries={ROOT/p for p in header['Roots']}
 for directory in sorted(parents,key=lambda p:len(p.parts),reverse=True):
  current=directory
  while current!=ROOT and any(current==b or current.is_relative_to(b) for b in boundaries):
   try:
    if getattr(os.lstat(current),'st_file_attributes',0)&0x400:break
    os.rmdir(current)
   except OSError:break
   current=current.parent
 result={'Counts':dict(counts),'LogicalBytesByCategory':dict(sizes),'DeletedFiles':sum(counts.values()),'LogicalBytesRemoved':sum(sizes.values()),'Errors':errors,'ElapsedSeconds':time.monotonic()-start,'Scope':'Unlinked exact reviewed generated files; no source/ACL/user-state changes. Free-space delta is measured separately.'}
 (out/'deletion-summary.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print(json.dumps({k:v for k,v in result.items() if k!='Errors'}),flush=True)
if __name__=='__main__':main()
