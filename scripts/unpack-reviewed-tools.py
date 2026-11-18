# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Unpack an explicitly supplied reviewed tool ZIP after traversal/lock verification."""
import json, stat, sys, zipfile
from pathlib import Path, PurePosixPath
from release_audit_compat import digest
ROOT=Path(__file__).resolve().parent.parent
expected={row['Path'] for row in json.loads((ROOT/'release/dependencies.lock.json').read_text())['Files']}
with zipfile.ZipFile(sys.argv[1]) as z:
 assert sum(i.file_size for i in z.infolist())<600*1024**2,'Tool archive expanded budget exceeded'
 assert {i.filename for i in z.infolist() if not i.is_dir()}==expected,'Exact locked tool file set required'
 assert len([i for i in z.infolist() if not i.is_dir()])==len(expected),'Duplicate tool entries refused'
 for item in z.infolist():
  path=PurePosixPath(item.filename)
  assert not path.is_absolute() and '..' not in path.parts and '\\' not in item.filename and ':' not in item.filename and path.parts[0]=='tools' and not stat.S_ISLNK(item.external_attr>>16),'Unsafe entry'
  assert not (ROOT/Path(*path.parts)).exists(),'Do not overwrite existing tool inputs'
 z.extractall(ROOT)
for row in json.loads((ROOT/'release/dependencies.lock.json').read_text())['Files']:assert digest(ROOT/row['Path'])==row['SHA256'],row['Path']
print('PASS: pinned tool files extracted; license clearance is independent')
