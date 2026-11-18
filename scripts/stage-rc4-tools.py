"""Stage only hash-pinned RC4 tools from an explicitly identified local tool source."""
# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
import argparse,hashlib,json,pathlib,shutil
r=pathlib.Path(__file__).resolve().parent.parent
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--destination',type=pathlib.Path,required=True);a=p.parse_args()
source=a.source.resolve();dest=a.destination.resolve();assert source.is_relative_to(r) and dest.is_relative_to(r/'.local') and source!=dest
lock=json.loads((r/'release/windows/rc4/tools.lock.json').read_text())
def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest().upper()
for name,row in lock['files'].items():
 rel=pathlib.PurePosixPath(name).relative_to('tools');f=source/rel;assert f.is_file() and sha(f)==row['sha256'],str(rel)
if dest.exists():
 expected={str(pathlib.Path(n).relative_to('tools')) for n in lock['files']};actual={str(f.relative_to(dest)) for f in dest.rglob('*') if f.is_file()};assert actual==expected,'Existing stage differs; preserved'
for name,row in lock['files'].items():
 rel=pathlib.PurePosixPath(name).relative_to('tools');f=dest/rel
 if f.exists():assert sha(f)==row['sha256'],'Existing bytes differ; preserved'
 else:f.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source/rel,f)
print(json.dumps({'status':'PASS','files':len(lock['files']),'removed_files':len(lock['removed_files'])}))
