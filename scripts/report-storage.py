# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Read-only workspace inventory. Reparse entries are recorded and never followed."""
import os,json,gzip,subprocess,ctypes,time,collections
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
# This script lives in scripts; local records are intentionally excluded from Git.
OUT=ROOT/'.local/storage-cleanup';OUT.mkdir(parents=True,exist_ok=True)
def scan(root):
 start=time.monotonic();dirs=collections.defaultdict(lambda:[0,0]);files=[];links=[];errors=[];stack=[root]
 while stack:
  folder=stack.pop()
  try:
   with os.scandir(folder) as entries:
    for entry in entries:
     try:
      s=entry.stat(follow_symlinks=False);rel=os.path.relpath(entry.path,root).replace('\\','/')
      if getattr(s,'st_file_attributes',0)&0x400:links.append({'Path':rel,'Directory':entry.is_dir(follow_symlinks=False)});continue
      if entry.is_dir(follow_symlinks=False):stack.append(entry.path);continue
      if not entry.is_file(follow_symlinks=False):continue
      files.append({'Path':rel,'Bytes':s.st_size})
      parent=os.path.dirname(rel).replace('\\','/')
      while True:
       dirs[parent][0]+=s.st_size;dirs[parent][1]+=1
       if not parent:break
       parent=parent.rpartition('/')[0]
     except OSError as ex:errors.append({'Path':entry.path,'Error':str(ex)})
  except OSError as ex:errors.append({'Path':str(folder),'Error':str(ex)})
 tracked=subprocess.check_output(['git','-C',str(root),'ls-files','-z']).decode('utf-8').split('\0');tracked=set(tracked)
 paths=[f['Path'] for f in files if not f['Path'].startswith('.git/')]
 p=subprocess.run(['git','-C',str(root),'-c','core.quotePath=false','check-ignore','--stdin','-z'],input=('\0'.join(paths)+'\0').encode('utf-8'),stdout=subprocess.PIPE,stderr=subprocess.PIPE)
 ignored=set(p.stdout.decode('utf-8').split('\0'))
 kinds=collections.Counter();ext=collections.Counter()
 for f in files:
  n=f['Path'];f['Git']='metadata' if n.startswith('.git/') else 'tracked' if n in tracked else 'ignored' if n in ignored else 'untracked';kinds[f['Git']]+=f['Bytes'];ext[Path(n).suffix.lower()]+=f['Bytes']
 free=ctypes.c_ulonglong();total=ctypes.c_ulonglong();allfree=ctypes.c_ulonglong();ctypes.windll.kernel32.GetDiskFreeSpaceExW(str(root),ctypes.byref(free),ctypes.byref(total),ctypes.byref(allfree))
 result={'Root':str(root),'UnixTime':time.time(),'ElapsedSeconds':time.monotonic()-start,'TotalLogicalBytes':dirs[''][0],'Files':len(files),'CFreeBytes':free.value,'CVolumeBytes':total.value,'GitBytes':dict(kinds),'Directories':[{'Path':p or '.', 'Bytes':v[0],'Files':v[1]} for p,v in sorted(dirs.items(),key=lambda q:q[1][0],reverse=True)],'LargestFiles':sorted(files,key=lambda f:f['Bytes'],reverse=True)[:50],'Extensions':dict(ext.most_common()),'ReparseEntries':links,'Errors':errors}
 return result,files
if __name__=='__main__':
 import argparse
 ap=argparse.ArgumentParser();ap.add_argument('--output',default=str(OUT/'inventory-current.json'));args=ap.parse_args()
 target=Path(args.output).resolve()
 if not target.is_relative_to(OUT.resolve()):raise ValueError('Reports must remain inside .local/storage-cleanup')
 report,files=scan(ROOT);target.write_text(json.dumps(report,indent=2,ensure_ascii=True)+'\n',encoding='utf-8')
 # Compress metadata directly so repeated audits do not accumulate large plain-text inventories.
 with gzip.open(Path(args.output).with_suffix('.files.jsonl.gz'),'wt',encoding='utf-8') as records:
  for f in files:records.write(json.dumps(f,ensure_ascii=True)+'\n')
 print(json.dumps({k:report[k] for k in ['TotalLogicalBytes','Files','CFreeBytes','ElapsedSeconds','GitBytes']}));print(json.dumps(report['Directories'][:18],indent=2))
