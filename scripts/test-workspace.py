# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Create or clean only a marked test-run workspace. Cleanup is dry-run unless explicitly confirmed."""
from pathlib import Path
import argparse,importlib.util,json,os,uuid
ROOT=Path(__file__).resolve().parents[1]

def checked(run_id):
    if uuid.UUID(run_id).hex!=run_id:raise ValueError('A lowercase GUID without separators is required')
    path=ROOT/'.local/test-runs'/run_id
    for parent in [ROOT,ROOT/'.local',path.parent]:
        if parent.exists() and getattr(os.lstat(parent),'st_file_attributes',0)&0x400:raise ValueError('Reparse parent refused')
    if path.exists() and getattr(os.lstat(path),'st_file_attributes',0)&0x400:raise ValueError('Reparse run refused')
    return path

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['prepare','clean']);parser.add_argument('--id',required=True)
    parser.add_argument('--apply',action='store_true');parser.add_argument('--confirm',default='')
    args=parser.parse_args();path=checked(args.id);marker=path/'.owned-test-run.json'
    if args.action=='prepare':
        path.mkdir(parents=True,exist_ok=False)
        marker.write_text(json.dumps({'Owner':'GeoStack test.ps1','Run':args.id})+'\n',encoding='utf-8')
        print(path);return
    if getattr(os.lstat(marker),'st_file_attributes',0)&0x400:raise ValueError('Reparse marker refused')
    record=json.loads(marker.read_text(encoding='utf-8'))
    if record!={'Owner':'GeoStack test.ps1','Run':args.id}:raise ValueError('Ownership marker differs')
    if not args.apply:print('DRY_RUN: marked test run only:',path);return
    if args.confirm!=args.id:raise ValueError('Explicit confirmation must equal the run ID')
    spec=importlib.util.spec_from_file_location('workspace_cleanup',ROOT/'scripts/cleanup-workspace.py')
    cleanup=importlib.util.module_from_spec(spec);spec.loader.exec_module(cleanup)
    def remove(folder):
        with os.scandir(folder) as entries:
            children=list(entries)
        for child in children:
            info=child.stat(follow_symlinks=False)
            attributes=getattr(info,'st_file_attributes',0)
            if attributes&0x400:
                if attributes&0x10:os.rmdir(child.path)
                else:os.unlink(child.path)
            elif child.is_dir(follow_symlinks=False):remove(Path(child.path))
            else:cleanup.unlink_generated(Path(child.path))
        os.rmdir(folder)
    remove(path);print('Removed completed owned test run:',args.id)
if __name__=='__main__':main()
