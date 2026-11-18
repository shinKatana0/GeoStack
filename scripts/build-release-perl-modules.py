"""Rebuild the two previously unmatched modules using the pinned standard Strawberry toolchain."""
# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
import pathlib,subprocess,os,json,hashlib,zipfile,tarfile

r=pathlib.Path(__file__).resolve().parent.parent;stage=r/'.local/winrelease'; sp=stage/'native-build/strawberry'; env=os.environ.copy();env['PATH']=str(sp/'perl/bin')+';'+str(sp/'c/bin')+';'+env['PATH']
for key in ['LC_ALL','LC_CTYPE','LANG']:env[key]='C'
env['TEMP']=str(stage/'temp');env['TMP']=env['TEMP'];env.pop('PERL5LIB',None)
# The standard compiler is a separately acquired build tool, never a release asset.
inputs=stage/'sources'
expected={'strawberry-perl-5.32.1.1-64bit-portable.zip':'692646105b0f5e058198a852dc52a48f1cebcaf676d63bbdeae12f4eaee9bf5c','Win32-FindFile-0.15.tar.gz':'bfd4ef029f0a0e0d0c952d8bdc1bbfdf1ca63e4bb120752d35597e4e9a6e9578','IO-Compress-Brotli-0.004001.tar.gz':'8ba5c0167e966f487bde159c18bc1b3486528013b3235d39f2fcb375ca4bf410'}
for name,value in expected.items():
 assert hashlib.sha256((inputs/name).read_bytes()).hexdigest()==value,'Pinned input mismatch: '+name
if not (sp/'perl/bin/perl.exe').exists():
 with zipfile.ZipFile(inputs/'strawberry-perl-5.32.1.1-64bit-portable.zip') as z:
  assert sum(x.file_size for x in z.infolist())<900*1024*1024
  assert all(not pathlib.Path(x.filename).is_absolute() and '..' not in pathlib.Path(x.filename).parts and ':' not in x.filename for x in z.infolist())
  z.extractall(sp)
for name in ['Win32-FindFile-0.15','IO-Compress-Brotli-0.004001']:
 dest=stage/'native-build/modules';dest.mkdir(parents=True,exist_ok=True)
 if not (dest/name/'Makefile.PL').exists():
  with tarfile.open(inputs/(name+'.tar.gz')) as t:
   assert all((dest/x.name).resolve().is_relative_to(dest.resolve()) and not x.issym() and not x.islnk() for x in t)
   t.extractall(dest)
commands=[]
def run(args,cwd):
 commands.append({'cwd':str(cwd.relative_to(stage)),'args':[str(x).replace(str(stage),'${STAGE}') for x in args]})
 p=subprocess.run(list(map(str,args)),cwd=cwd,env=env,capture_output=True)
 with (stage/'native-build.log').open('ab') as log:log.write(p.stdout+p.stderr)
 (stage/'native-build-commands.json').write_text(json.dumps(commands,indent=2))
 if p.returncode: raise RuntimeError('Build failed '+str(args[0])+' exit '+str(p.returncode))
perl=sp/'perl/bin/perl.exe'; gcc=sp/'c/bin/gcc.exe';ar=sp/'c/bin/ar.exe';make=sp/'c/bin/gmake.exe'
for name in ['Win32-FindFile-0.15','IO-Compress-Brotli-0.004001']:
 folder=stage/'native-build/modules'/name
 if 'Brotli' in name:
  objects=[]
  for source in sorted((folder/'brotli/c').glob('*/*.c')):
   if source.parent.name not in ['common','enc','dec']:continue
   output=folder/'brotli'/('native-'+source.parent.name+'-'+source.stem+'.o')
   run([gcc,'-O2','-I'+str(folder/'brotli/c/include'),'-c',source,'-o',output],folder); objects.append(output)
  run([ar,'-crs',folder/'brotli/libbrotli.a',*objects],folder)
 run([perl,'Makefile.PL'],folder)
 run([make],folder)
 if 'Brotli' in name:
  test=folder/'t/01-uncompress.t';original=test.read_text(encoding='utf-8')
  if 'binmode FH;' not in original:
   test.write_text(original.replace("open FH, '<', $test;","open FH, '<', $test;\n\tbinmode FH; # Windows: compressed test inputs require binary mode."),encoding='utf-8')
 run([make,'test'],folder)
 print('Built '+name,flush=True)
 results=list((folder/'blib/arch/auto').rglob('*.dll'));assert len(results)==1
 print(results[0].relative_to(stage),hashlib.sha256(results[0].read_bytes()).hexdigest(),flush=True)
(stage/'native-build-commands.json').write_text(json.dumps(commands,indent=2))
