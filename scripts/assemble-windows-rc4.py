"""Reproduce RC4 application bytes and assemble only its verified reference runtime."""
# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
import argparse,hashlib,json,pathlib,subprocess,sys,uuid,zipfile
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--inputs',type=pathlib.Path,required=True);p.add_argument('--reference',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
r=pathlib.Path(__file__).resolve().parent.parent;out=a.output.resolve();assert out.is_relative_to(r/'dist') and not out.exists(),'Use a new dist destination'
release=json.loads((r/'release/windows-rc4.json').read_text());expected=release['assets'][0];assert hashlib.sha256(a.reference.read_bytes()).hexdigest().upper()==expected['sha256']
work=r/'.local'/('rc4-reproduce-'+uuid.uuid4().hex)
subprocess.run([sys.executable,str(r/'scripts/reproduce-windows-rc4.py'),'--inputs',str(a.inputs.resolve()),'--output',str(work),'--build'],check=True)
result=json.loads((work/'source-correspondence.json').read_text());assert result.get('byte_identical'),'Rebuild differs; keep it as a separate experiment, record hashes, and validate before selecting a new release. Reference RC4 is unchanged.'
with zipfile.ZipFile(a.reference) as z:
 manifest=json.loads(z.read('file-manifest.json'));assert set(z.namelist())==set(manifest)|{'file-manifest.json'}
 for name,row in manifest.items():
  rel=pathlib.PurePosixPath(name);assert rel.parts[0]=='GeoStack' and '..' not in rel.parts and ':' not in name
  data=z.read(name);assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest().upper()==row['sha256']
 out.mkdir(parents=True)
 for name in manifest:
  dest=out/pathlib.PurePosixPath(name).relative_to('GeoStack');dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(z.read(name))
for name in release['application_binaries']:
 data=(work/'application-rebuild'/name).read_bytes();assert hashlib.sha256(data).hexdigest().upper()==release['application_binaries'][name];(out/name).write_bytes(data)
report={'status':'PASS','application_reproduced_byte_identically':True,'runtime_reference_sha256':expected['sha256'],'source_tag_counterpart_verified':True,'publication_authorized':False,'files':len(manifest),'output':'New isolated dist directory','reproduction':result}
(work/'portable-correspondence.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
