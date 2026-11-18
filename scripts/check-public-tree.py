# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Local English/localization, bilingual-documentation and privacy gate.

Uses the standard library, never uploads content or prints finding values.
Semantic translation quality and ownership also require human review.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import posixpath
import re
import subprocess
import urllib.parse
import xml.etree.ElementTree as ET

CYRILLIC = re.compile('[\u0400-\u052f]')
TEXT = {'.cs','.xaml','.csproj','.props','.targets','.json','.yaml','.yml','.xml','.ps1','.cmd','.bat','.sh','.py','.js','.ts','.config','.editorconfig','.gitignore','.gitattributes','.md','.resx','.txt','.toml','.sln','.pod'}


def hash_bytes(raw):
    return hashlib.sha256(raw).hexdigest()


def sections(text):
    return re.findall(r'<!-- section:([a-z0-9-]+) -->', text)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    policy = json.loads((root/'publication-policy.json').read_text(encoding='utf-8'))
    files = {}
    failures = []
    for parent, dirs, names in os.walk(root, followlinks=False):
        dirs[:] = [d for d in dirs if d not in {'.git','.local','bin','obj','TestResults','__pycache__'}]
        for name in names:
            path = Path(parent)/name
            if path.is_symlink() or getattr(path.lstat(),'st_file_attributes',0) & 0x400:
                failures.append({'File':path.relative_to(root).as_posix(),'Rule':'ReparsePoint'})
                continue
            files[path.relative_to(root).as_posix()] = path
    pairs = policy['DocumentPairs']
    public_docs = {p[k] for p in pairs for k in ['English','Russian']}
    markdown = {n for n in files if n.endswith('.md')}
    if markdown != public_docs | set(policy['MachineMarkdown']):
        failures.append({'Rule':'UnclassifiedMarkdown','Files':sorted(markdown.symmetric_difference(public_docs | set(policy['MachineMarkdown'])))})
    unauthorized = []
    approved = 0
    external = 0
    code_files = 0
    texts = {}
    for name,path in files.items():
        if path.suffix.lower() not in TEXT and path.name not in {'LICENSE','NOTICE','AGENTS.md','.gitignore','.gitattributes'}:
            continue
        try: text = path.read_text(encoding='utf-8-sig')
        except UnicodeError:
            failures.append({'File':name,'Rule':'UnexpectedTextEncoding'})
            continue
        texts[name] = text
        if not name.endswith('.md'): code_files += 1
        hits = [(i,line) for i,line in enumerate(text.splitlines(),1) if CYRILLIC.search(line)]
        if not hits: continue
        if name in {p['Russian'] for p in pairs}:
            approved += len(hits)
            continue
        if name in policy['ExternalCyrillic']:
            if hash_bytes(path.read_bytes()) == policy['ExternalCyrillic'][name]['SHA256']:
                external += len(hits)
                continue
        if name in policy['LocalizationArrays']:
            data = json.loads(text)
            if all(isinstance(v,list) and len(v)==2 and all(isinstance(s,str) for s in v) and not CYRILLIC.search(k+v[0]) for k,v in data.items()):
                approved += len(hits)
                continue
        if name in policy['RussianResources']:
            if name.endswith('.resx'):
                tree=ET.fromstring(text)
                allowed={v.text or '' for v in tree.findall('./data/value')}
                # Remove only resource values; comments, names and schema stay English.
                for v in tree.findall('./data/value'):v.text=''
                if not CYRILLIC.search(ET.tostring(tree,encoding='unicode')):
                    approved += len(hits)
                    continue
            else:
                data=json.loads(text)
                if all(isinstance(v,str) and not CYRILLIC.search(k) for k,v in data.items()):
                    approved += len(hits)
                    continue
        allowed = policy['LocalizedTestLines'].get(name,{}).get('LineSHA256',[])
        for number,line in hits:
            if name=='README.md' and number==1 and line.startswith('English | [') and '(README.ru.md)' in line:
                approved += 1
            elif hash_bytes(line.encode()) in allowed:
                approved += 1
            else: unauthorized.append({'File':name,'Line':number,'Rule':'UnauthorizedCyrillic'})
    failures += unauthorized

    link_count = 0
    parity = []
    stable_tokens = ['0.5.5','Windows 11 x64','.NET 10','JPEG','HEIC','HEIF','RAW','MOV','MP4','SHA-256','370 ms','BLOCKED','NOT_TESTED']
    for pair in pairs:
        en=pair['English'];ru=pair['Russian']
        if en not in texts or ru not in texts:
            failures.append({'File':en,'Rule':'MissingTranslation'});continue
        a,b=texts[en],texts[ru]
        same=sections(a)==sections(b) and bool(sections(a))
        same &= len(re.findall(r'^## ',a,re.M))==len(re.findall(r'^## ',b,re.M))==len(sections(a))
        same &= re.findall(r'```[^\n]*\n(.*?)```',a,re.S)==re.findall(r'```[^\n]*\n(.*?)```',b,re.S)
        same &= all((token in a)==(token in b) for token in stable_tokens)
        same &= all(len(part.strip())>=30 for part in re.split(r'<!-- section:[^>]+ -->',b)[1:])
        if not same: failures.append({'File':en,'Rule':'DocumentationParity'})
        if Path(ru).name not in a.splitlines()[0] or Path(en).name not in b.splitlines()[0]:
            failures.append({'File':en,'Rule':'LanguageNavigation'})
        parity.append({'English':en,'Russian':ru,'Sections':sections(a),'Result':'PASS' if same else 'FAIL'})
    for name in public_docs:
        text=texts.get(name,'')
        refs=re.findall(r'!?\[[^\]]*\]\(([^)]+)\)',text)+re.findall(r'<img[^>]+src="([^"]+)"',text)
        for ref in refs:
            if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:',ref) or ref.startswith('//'):continue
            target=urllib.parse.unquote(ref.split('#')[0])
            if not target:continue
            target=posixpath.normpath(posixpath.join(posixpath.dirname(name),target))
            link_count += 1
            if target not in files:failures.append({'File':name,'Rule':'BrokenLocalLink','Target':target})
    contracts=json.loads((root/'tests/DocumentationLocalization.json').read_text(encoding='utf-8'))
    for name,ids in policy.get('DocumentContracts',{}).items():
        russian=next(p['Russian'] for p in pairs if p['English']==name)
        for key in ids:
            expected=contracts[key]
            if expected[0].casefold() not in texts[name].casefold() or expected[1].casefold() not in texts[russian].casefold():
                failures.append({'File':name,'Rule':'MissingSubstantiveContract','Contract':key})

    patterns={
        'PrivateKey':r'-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----',
        'PGPPrivateKey':r'-----BEGIN\s+PGP\s+PRIVATE\s+KEY\s+BLOCK-----',
        'OpenAIKey':r'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{32,}',
        'AnthropicKey':r'\bsk-ant-[A-Za-z0-9_-]{25,}',
        'GitHubToken':r'\bgh[pousr]_[A-Za-z0-9]{30,}|\bgithub_pat_[A-Za-z0-9_]{40,}',
        'AWSKey':r'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b',
        'GoogleKey':r'\bAIza[0-9A-Za-z_-]{35}',
        'BearerOrCookie':r'(?i)(?:Authorization[\"\s:=]+Bearer|Cookie[\"\s:=]+)[A-Za-z0-9_/+=.-]{18,}',
        'CredentialURL':r'https?://[^\s/@\"]+:[^\s/@\"]+@',
        'PasswordAssignment':r'(?i)(?:password|client_secret|webhook_secret|accountkey)[\"\s]*[:=][\s\"]*[A-Za-z0-9_+/=-]{8,}',
        'OAuthToken':r'(?i)(?:access_token|refresh_token|id_token)[\"\s]*[:=][\s\"]*[A-Za-z0-9_+/=.-]{20,}',
        'AzureCredential':r'(?i)(?:AccountKey|SharedAccessSignature)\s*=\s*[A-Za-z0-9_+/=.-]{20,}',
        'MACAddress':r'\b(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\b',
        'SID':r'\bS-1-5-21-(?:\d+-){2}\d+',
        'MachinePath':r'(?i)\b[A-Z]:[\\/](?:Users|repo|Projects|OneDrive)[\\/]|/(?:Users|home)/[A-Za-z0-9._-]+',
        'PrivateNetwork':r'\b(?:192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3})\b',
    }
    privacy=[]
    for name,text in texts.items():
        for i,line in enumerate(text.splitlines(),1):
            subject=re.sub(r'https?://[^\s<>\"\']+','',line)
            for kind,pattern in patterns.items():
                if re.search(pattern,subject if kind=='MachinePath' else line):
                    fingerprint=hash_bytes(line.encode())
                    if fingerprint in policy.get('PrivacyTestLines',{}).get(name,{}).get('LineSHA256',[]):continue
                    privacy.append({'File':name,'Line':i,'Rule':kind,'Value':'MASKED'})
    failures += privacy
    for name,path in files.items():
        if path.suffix.lower() in {'.exe','.dll','.pfx','.p12','.dmp','.nettrace','.sqlite','.db','.ttf','.otf','.woff','.woff2'}:
            failures.append({'File':name,'Rule':'ForbiddenPublicationPayload'})
        if path.name in {'.bash_history','.python_history','ConsoleHost_history.txt'} or path.suffix.lower() in {'.ovpn','.key','.pfx','.p12'} or path.name.startswith('.env') and path.name!='.env.example':
            failures.append({'File':name,'Rule':'PrivateConfigurationOrHistory'})
    project=ET.parse(root/'src/GeoStack.App/GeoStack.App.csproj')
    if project.findtext('.//Version')!=policy['Version']:
        failures.append({'Rule':'ProjectDocumentationVersionMismatch'})
    git_command=['git','-c','safe.directory='+str(root)]
    git_env=os.environ.copy()
    git_env['GIT_CEILING_DIRECTORIES']=str(root.parent)
    git_probe=subprocess.run(git_command+['-C',str(root),'rev-parse','--show-toplevel'],capture_output=True,text=True,env=git_env)
    git_state='NOT_INITIALIZED'
    if (root/'.git').exists() and git_probe.returncode!=0:
        failures.append({'Rule':'PublicGitInspectionFailed'})
    if git_probe.returncode==0:
        if Path(git_probe.stdout.strip()).resolve()!=root:
            failures.append({'Rule':'InheritedPrivateGitRepository'})
        else:
            git_state='OWN_REPOSITORY'
            tracked=subprocess.check_output(git_command+['-C',str(root),'ls-files','-z'],env=git_env).decode().split('\0')
            for name in tracked:
                if any(part in {'.local','bin','obj','TestResults'} for part in Path(name).parts):
                    failures.append({'File':name,'Rule':'TrackedPrivateBuildOutput'})
            messages=subprocess.run(git_command+['-C',str(root),'log','--all','--format=%B'],capture_output=True,text=True,env=git_env)
            if messages.returncode==0 and CYRILLIC.search(messages.stdout):
                failures.append({'Rule':'NonEnglishPublicCommitMessage'})
    gate=json.loads((root/'release/publication-gate.json').read_text(encoding='utf-8'))
    license_state='APACHE_2_0_APPLIED' if gate.get('OwnerLicenseApproved') else 'PENDING'
    if gate.get('OwnerLicenseApproved'):
        if hash_bytes((root/'LICENSE').read_bytes())!='cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30':
            failures.append({'Rule':'OfficialLicenseMismatch'})
        if 'Copyright 2026 shinKatana0' not in (root/'NOTICE').read_text(encoding='utf-8'):
            failures.append({'Rule':'ApprovedCopyrightMissing'})
        header_policy=json.loads((root/'release/source-license.json').read_text(encoding='utf-8'))
        for name in header_policy['FirstPartyHeaderFiles']:
            if 'SPDX-License-Identifier: Apache-2.0' not in (root/name).read_text(encoding='utf-8-sig'):
                failures.append({'File':name,'Rule':'FirstPartySpdxMissing'})
    report={'FilesScanned':len(files),'CodeConfigTextFiles':code_files,'TextFilesScanned':len(texts),
            'UnauthorizedCyrillic':unauthorized,'ApprovedLocalizationLines':approved,'VerbatimExternalCyrillicLines':external,
            'DocumentPairs':parity,'LocalLinksChecked':link_count,'PrivacyFindings':privacy,
            'FailureCount':len(failures),'Failures':failures,'PublicLicense':license_state,'PublicIdentity':gate.get('PublicEmail','PENDING_OWNER_APPROVAL'),
            'GitState':git_state,'ParentGitDiscoveryPrevented':True,
            'Status':'PASS' if not failures else 'FAIL',
            'Limits':'Finite local rules and substantive documentation contracts supplement human review; they do not prove universal secrecy or translation correctness.'}
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in {'DocumentPairs','Failures','PrivacyFindings'}},indent=2))
    raise SystemExit(1 if failures else 0)


if __name__=='__main__':main()
