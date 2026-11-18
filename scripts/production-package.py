# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Versioned local production replacement with checksum-verified backup and user-state preservation."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,shutil,subprocess,uuid
ROOT=Path(__file__).resolve().parent.parent
PRODUCTION=ROOT/'dist/GeoStack-portable-win-x64'
def checked(value):
 p=Path(value).resolve();assert p.is_relative_to(ROOT) and p!=ROOT,'Target must be inside the repository';return p
def files(folder):
 result={}
 for p in folder.rglob('*'):
  assert not p.is_symlink() and not (p.stat().st_file_attributes & 0x400),'Reparse points require manual review'
  if p.is_file():
   h=hashlib.sha256()
   with p.open('rb') as f:
    for b in iter(lambda:f.read(1048576),b''):h.update(b)
   result[p.relative_to(folder).as_posix()]=h.hexdigest().upper()
 return result
def idle():
 command='if(Get-Process GeoStack -ErrorAction SilentlyContinue){exit 23}'
 assert subprocess.run(['powershell.exe','-NoProfile','-Command',command],capture_output=True).returncode==0,'Close GeoStack before replacing or restoring production'
def user_file(name,runtime):
 return name not in runtime or name.split('/')[0].lower() in {'data','maps','geographicdata','cache','copyjournal'}
def swap(source,runtime,current_runtime):
 idle();source=checked(source)
 stage=ROOT/'.local/production-staging'/uuid.uuid4().hex;stage.parent.mkdir(exist_ok=True)
 shutil.copytree(source,stage);assert files(stage)==runtime,'Staging copy mismatch'
 if PRODUCTION.exists():
  for rel in files(PRODUCTION):
   if user_file(rel,current_runtime):
    target=stage/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(PRODUCTION/rel,target)
 history=ROOT/'.local/production-history'/datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f');history.parent.mkdir(exist_ok=True)
 idle();
 if PRODUCTION.exists():os.replace(PRODUCTION,history)
 try:os.replace(stage,PRODUCTION)
 except BaseException:
  if history.exists():os.replace(history,PRODUCTION)
  raise
 return history
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('action',choices=['promote','rollback']);parser.add_argument('--candidate');parser.add_argument('--validation');parser.add_argument('--backup',default='.local/production-backup.json');parser.add_argument('--record',default='.local/production-release.json');parser.add_argument('--rehearsal',action='store_true');parser.add_argument('--rehearsal-root');args=parser.parse_args()
if args.rehearsal:
 rehearsal=checked(ROOT/args.rehearsal_root) if args.rehearsal_root else ROOT/'.local/rollback-rehearsal'
 assert rehearsal.is_relative_to(ROOT/'.local'),'Rehearsal must remain in local scratch space'
 PRODUCTION=rehearsal/'production'
elif args.rehearsal_root:raise ValueError('Rehearsal root requires rehearsal mode')
backup=json.loads(checked(ROOT/args.backup).read_text(encoding='utf-8-sig'));location=checked(backup['Backup']);assert files(location)==backup['Files'],'Backup verification failed'
record=checked(ROOT/args.record)
if args.action=='promote':
 candidate=checked(ROOT/args.candidate);assert (candidate.is_relative_to(ROOT/'dist') or args.rehearsal and candidate.is_relative_to(rehearsal)) and candidate!=PRODUCTION
 runtime=files(candidate);assert 'atlas/world.gsatlas' in runtime,'Candidate does not contain the default atlas'
 if not args.rehearsal:
  gate=json.loads(checked(ROOT/args.validation).read_text(encoding='utf-8'))
  identity=hashlib.sha256(json.dumps(runtime,sort_keys=True,separators=(',',':')).encode()).hexdigest().upper()
  assert gate['Status']=='PASS' and gate['OwnerVisualApproval'] in ('HYBRID_ATLAS_OWNER_VISUAL_ACCEPTED','URBAN_LITE_OWNER_VISUAL_ACCEPTED','GEOSTACK_0_5_5_OWNER_ACCEPTED') and gate['RuntimeManifestSHA256']==identity,'Candidate validation/provenance mismatch'
  if gate['OwnerVisualApproval']=='URBAN_LITE_OWNER_VISUAL_ACCEPTED':
   assert all(p in runtime for p in ('geography/world.gsbase','geography/urban-roads.gslite','licenses/Urban-Lite-OSM.txt')),'Accepted Urban Lite runtime assets missing'
   assert gate['MoveIncluded'] is False and gate['DebtStatus']=='URBAN_LITE_OWNER_ACCEPTED_WITH_DEBT','Atlas-only promotion must exclude unaccepted move changes and preserve performance debt'
  if gate['OwnerVisualApproval']=='GEOSTACK_0_5_5_OWNER_ACCEPTED':
   assert gate['AcceptedCandidateSource']=='8a4cc2db2c185815323340157100c4b97fc1bc7b','Accepted candidate identity required'
   assert all(gate['CriticalChecks'][name]=='PASS' for name in ('FullSuite','PackagedCopy','PackagedMove','CatalogRestart','Atlas','Rollback','Alignment')),'Required packaged release checks failed'
   if gate['MoveIncluded']:
    proof_path=checked(ROOT/gate['CrossVolumeEvidence']);assert hashlib.sha256(proof_path.read_bytes()).hexdigest().upper()==gate['CrossVolumeEvidenceSHA256'],'Move safety evidence changed'
    proof=json.loads(proof_path.read_text(encoding='utf-8'));assert proof['Status']=='PASS' and Path(proof['SourceRoot']).anchor.upper()!=Path(proof['DestinationRoot']).anchor.upper(),'Real distinct-volume verification required'
 current=files(PRODUCTION)
 for rel,digest in backup['Files'].items():
  if not user_file(rel,backup['Files']):assert current.get(rel)==digest,'Production changed since backup'
 history=swap(candidate,runtime,backup['Files'])
 result={'BackupRecord':str(checked(ROOT/args.backup)),'Candidate':str(candidate),'PreviousDirectory':str(history),'PromotedUTC':datetime.datetime.now(datetime.timezone.utc).isoformat(),'RuntimeFiles':runtime,'PostPromotionAcceptance':'NOT_TESTED'}
 record.write_text(json.dumps(result,indent=2),encoding='utf-8');deployed=files(PRODUCTION);assert all(deployed.get(p)==h for p,h in runtime.items())
 print('Promoted verified runtime; post-promotion acceptance remains required:',PRODUCTION)
else:
 release=json.loads(record.read_text(encoding='utf-8'));history=swap(location,backup['Files'],release['RuntimeFiles']);print('Restored previous production, preserving current user state; replaced runtime retained:',history)
