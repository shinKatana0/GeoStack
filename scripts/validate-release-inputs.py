# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Validate localization, pinned native inputs and compact runtime geography without network."""
import argparse, json, xml.etree.ElementTree as ET
from pathlib import Path
from release_audit_compat import digest
ROOT=Path(__file__).resolve().parent.parent
p=argparse.ArgumentParser();p.add_argument('--without-tools',action='store_true');a=p.parse_args()
resources=ROOT/'src/GeoStack.App/Resources'
def strings(name):return {e.attrib['name']:e.findtext('value') for e in ET.parse(resources/name).getroot().findall('data')}
english=strings('Strings.en-US.resx');russian=strings('Strings.ru-RU.resx');assert english.keys()==russian.keys(),'Localization keys differ';assert all(english.values()) and all(russian.values()),'Empty localized value'
for name in ['world','urban-roads']:
 manifest=json.loads((ROOT/'assets/geography'/f'{name}.manifest.json').read_text());file='world.gsbase' if name=='world' else 'urban-roads.gslite';assert digest(ROOT/'assets/geography'/file)==manifest['SHA256'].upper(),file
folder=ROOT/'offline-maps/atlas-pilot-compact';atlas=json.loads((folder/'MANIFEST.json').read_text())
for entry in atlas['Packages']:assert digest(folder/entry['File'])==entry['SHA256'].upper(),entry['File']
if not a.without_tools:
 lock=json.loads((ROOT/'release/dependencies.lock.json').read_text())
 for entry in lock['Files']:assert digest(ROOT/entry['Path'])==entry['SHA256'],entry['Path']
print('PASS: localization and exact compact geographic inputs'+(' (native tools not checked)' if a.without_tools else ', including pinned native tool bytes'))
