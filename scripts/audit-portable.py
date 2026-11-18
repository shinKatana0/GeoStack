# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Inspect package completeness, hashes and size without loading native executables."""
from pathlib import Path
import argparse
import hashlib
import json
import re

root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--package',type=Path,default=root/'dist/GeoStack-portable-win-x64')
parser.add_argument('--report-name',default='package')
args=parser.parse_args()
assert re.fullmatch('[a-zA-Z0-9-]+',args.report_name), 'Invalid report name'
package=args.package.resolve()
assert package.is_relative_to((root/'dist').resolve()), 'Audit only repository distributions'
required=['GeoStack.exe','GeoStack.dll','GeoStack.Core.dll','coreclr.dll','PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','GeoStack.runtimeconfig.json','geography/countries.geojson','geography/cities.geojson','tools/ExifTool/ExifTool.exe','tools/ExifTool/exiftool_files/perl532.dll','tools/FFmpeg/ffmpeg.exe','branding/GeoStack.ico','branding/GeoStack-256.png','licenses/ExifTool.txt','licenses/FFmpeg-GPLv3.txt','licenses/THIRD-PARTY-NOTICES.TXT']
missing=[name for name in required if not (package/name).is_file()]
runtime=json.loads((package/'GeoStack.runtimeconfig.json').read_text())['runtimeOptions']
assert not missing,missing
assert all(f['version']=='10.0.14' for f in runtime['includedFrameworks'])
assert not list(package.glob('*.pdb'))
assert not (package/'data').exists(), 'Release package contains mutable application data'
files=[p for p in package.rglob('*') if p.is_file()]
assert not (package/'docs/evidence').exists(), 'Engineering evidence must remain outside the portable runtime'
assert not any((package/name).exists() for name in ('TestResults','.local','offline-map-workspace','offline-maps','acceptance-local','tests')), 'Development workspace or fixtures in runtime package'
assert not [p for p in files if p.suffix.lower() in ('.trx','.nettrace','.gcdump','.dmp','.mbtiles','.gsgeo','.db','.partial')], 'Development, private or optional data in runtime package'
assert not any(p.name.lower().startswith('geostack.tests') for p in files), 'Test assembly in runtime package'
sizes=sorted(files,key=lambda p:p.stat().st_size,reverse=True)
report={'Status':'PASS','Package':str(package),'Files':len(files),'Bytes':sum(p.stat().st_size for p in files),'IncludedFrameworks':runtime['includedFrameworks'],'RequiredFiles':required,'LargestFiles':[{'Path':str(p.relative_to(package)).replace('\\','/'),'Bytes':p.stat().st_size} for p in sizes[:15]]}
def digest(path):
    value=hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda:source.read(65536),b''): value.update(block)
    return value.hexdigest()
assert all(p.parent == package/"atlas" for p in package.rglob("*.gsatlas")), "Unexpected optional atlas data outside runtime atlas"
if (package/'atlas').exists():
    atlas=json.loads((package/'atlas/MANIFEST.json').read_text(encoding='utf-8'))
    assert {p['File'] for p in atlas['Packages']}=={'world.gsatlas','zurich.gsatlas','warsaw.gsatlas'}
    assert all(digest(package/'atlas'/p['File'])==p['SHA256'].lower() for p in atlas['Packages']), 'Bundled atlas data checksum mismatch'

dependencies=json.loads((package/'dependencies.json').read_text(encoding='utf-8-sig'))
expected_dependencies={'tools/ExifTool/ExifTool.exe','tools/FFmpeg/ffmpeg.exe','geography/countries.geojson','geography/cities.geojson'}
assert {entry['File'].replace('\\','/') for entry in dependencies}==expected_dependencies, 'Invalid dependency manifest paths'
assert all(digest(package/entry['File'])==entry['Hash'].lower() for entry in dependencies), 'Dependency manifest hash mismatch'
manifest={str(p.relative_to(package)).replace('\\','/'):digest(p) for p in files if p.suffix.lower() in ('.exe','.dll','.ico','.geojson')}
(root/f'.local/{args.report_name}-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
(root/f'.local/{args.report_name}-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
