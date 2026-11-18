# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Refresh the Russian resx using UTF-8 source without shell code-page conversion."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parent.parent
translations=json.loads((root/'src/GeoStack.App/Resources/RussianSource.json').read_text(encoding='utf-8'))
path=root/'src/GeoStack.App/Resources/Strings.ru-RU.resx'
doc=ET.parse(path)
for data in doc.getroot().findall('data'):
    if data.attrib['name'] in translations: data.find('value').text=translations[data.attrib['name']]
ET.indent(doc)
doc.write(path,encoding='utf-8',xml_declaration=True)
extra=json.loads((root/'src/GeoStack.App/Resources/AdditionalStrings.json').read_text(encoding='utf-8'))
extra.update(json.loads((root/'src/GeoStack.App/Resources/OrganizationStrings.json').read_text(encoding='utf-8')))
extra.update(json.loads((root/'src/GeoStack.App/Resources/GeographicDataStrings.json').read_text(encoding='utf-8')))
extra.update(json.loads((root/'src/GeoStack.App/Resources/MapRecoveryStrings.json').read_text(encoding='utf-8')))
extra.update(json.loads((root/'src/GeoStack.App/Resources/BrowsingStrings.json').read_text(encoding='utf-8')))
extra.update(json.loads((root/'src/GeoStack.App/Resources/ImportStrings.json').read_text(encoding='utf-8')))
extra.update(json.loads((root/'src/GeoStack.App/Resources/DownloadStrings.json').read_text(encoding='utf-8')))
for filename, language in [('Strings',0),('Strings.en-US',0),('Strings.ru-RU',1)]:
    path=root/'src/GeoStack.App/Resources'/(filename+'.resx')
    doc=ET.parse(path)
    for key, values in extra.items():
        entry=next((e for e in doc.getroot().findall('data') if e.attrib['name']==key),None)
        if entry is None: entry=ET.SubElement(doc.getroot(),'data',name=key); ET.SubElement(entry,'value')
        entry.find('value').text=values[language]
    ET.indent(doc); doc.write(path,encoding='utf-8',xml_declaration=True)
