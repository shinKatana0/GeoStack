"""Verify and optionally compile the archived RC4 application inputs; never substitutes a release."""
# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
import argparse,hashlib,json,os,pathlib,re,subprocess,zipfile
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--inputs',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--build',action='store_true');a=p.parse_args()
r=pathlib.Path(__file__).resolve().parent.parent;out=a.output.resolve();assert out.is_relative_to(r/'.local') and not out.exists(),'Use a fresh explicit directory in this checkout .local'
release=json.loads((r/'release/windows-rc4.json').read_text());archive=a.inputs.resolve();assert hashlib.sha256(archive.read_bytes()).hexdigest().upper()==release['compiler_inputs']['sha256']
def normalize(b):
 text=b.decode('utf-8-sig').replace('\r\n','\n')
 text=re.sub(r'\A(?://|#) Copyright 2026 shinKatana0\n(?://|#) SPDX-License-Identifier: Apache-2.0\n','',text)
 return re.sub(r'\A<!-- Copyright 2026 shinKatana0 \| SPDX-License-Identifier: Apache-2.0 -->\n','',text)
with zipfile.ZipFile(archive) as z:
 manifest=json.loads(z.read('build-inputs.json'));assert manifest['original_build_revision']==release['original_build_revision']
 checked=0
 for name,row in manifest['files'].items():
  data=z.read(name);assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest().upper()==row['sha256']
  if name.startswith('src/'):
   assert normalize((r/name).read_bytes())==normalize(data),'Public source mismatch: '+name;checked+=1
 for name in z.namelist():assert not pathlib.PurePosixPath(name).is_absolute() and '..' not in pathlib.PurePosixPath(name).parts and ':' not in name
 z.extractall(out/'inputs')
report={'status':'PASS','public_product_sources_checked':checked,'input_files_checked':len(manifest['files']),'original_build_revision':release['original_build_revision'],'archive_sha256':release['compiler_inputs']['sha256'],'private_git_history_included':False,'build':'NOT_REQUESTED','release_substitution':False}
if a.build:
 source=out/'inputs';publish=out/'application-rebuild';env=os.environ.copy();env.update(DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_CLI_UI_LANGUAGE='en-US',TEMP=str(out/'temp'),TMP=str(out/'temp'))
 (out/'temp').mkdir();env.setdefault('NUGET_PACKAGES',str(r/'.local/packages'))
 command=['dotnet','publish',str(source/'src/GeoStack.App/GeoStack.App.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:RuntimeFrameworkVersion=10.0.14','-p:GeoStackChannel=Production','-p:PublishSingleFile=false','-p:PublishReadyToRun=false','-p:DebugType=None','-p:DebugSymbols=false','-p:SourceRevisionId='+release['original_build_revision'],'-p:EnableSourceControlManagerQueries=false','-p:PathMap='+str(source)+'=/_/','-p:NuGetAudit=false','-o',str(publish),'--ignore-failed-sources']
 result=subprocess.run(command,cwd=source,env=env,capture_output=True);(out/'build.log').write_bytes(result.stdout+result.stderr);assert result.returncode==0,'Rebuild failed; inspect build.log'
 binaries=[]
 for name,expected in release['application_binaries'].items():
  f=publish/name;actual=hashlib.sha256(f.read_bytes()).hexdigest().upper();binaries.append({'file':name,'sha256':actual,'original_sha256':expected,'byte_identical':actual==expected})
 report.update(build='PASS',binaries=binaries,byte_identical=all(x['byte_identical'] for x in binaries),artifact_use='Reproducibility experiment only. Original RC4 asset unchanged. A different artifact needs separate validation and owner selection.')
(out/'source-correspondence.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
