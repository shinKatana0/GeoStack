# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Synthetic topology/package fixtures, distinct from real geographic correctness fixtures."""
import importlib.util
import json
from pathlib import Path

root=Path(__file__).resolve().parent.parent
spec=importlib.util.spec_from_file_location('conversion',root/'scripts/convert-geographic-package.py')
conversion=importlib.util.module_from_spec(spec);spec.loader.exec_module(conversion)
out=root/'.local/geographic-fixtures';out.mkdir(exist_ok=True)
def polygon(west,south,east,north):
    return {'type':'Polygon','coordinates':[[[west,south],[east,south],[east,north],[west,north],[west,south]]]}
def feature(identifier,level,parent,geometry,ancestors):
    return {'type':'Feature','properties':{'id':identifier,'level':level,'parent_id':parent,'country_id':'synthetic-country','name':identifier,'name_en':identifier+'-en','name_ru':identifier+'-ru','ancestors':ancestors,'semantics':'Synthetic topology fixture; not real geographic coverage'},'geometry':geometry}
base=[feature('synthetic-country','country',None,polygon(0,0,30,30),[]),feature('synthetic-region','region','synthetic-country',polygon(0,0,30,30),['synthetic-country']),feature('synthetic-west','city','synthetic-region',polygon(0,0,10,20),['synthetic-country','synthetic-region']),feature('synthetic-east','city','synthetic-region',polygon(10,0,20,20),['synthetic-country','synthetic-region'])]
hole=polygon(1,1,9,9);hole['coordinates'].append([[3,3],[7,3],[7,7],[3,7],[3,3]])
base.append(feature('synthetic-district','district','synthetic-west',hole,['synthetic-country','synthetic-region','synthetic-west']))
manifest={'Id':'synthetic-topology','Name':'Synthetic topology fixtures','Version':'1','Source':'GeoStack original synthetic fixture','SourceUrl':'https://example.invalid/synthetic','License':'CC0-1.0','LicenseUrl':'https://creativecommons.org/publicdomain/zero/1.0/','Attribution':'GeoStack synthetic fixtures; no real-world boundaries','Coverage':'Synthetic coordinates only','DataDate':'2026-11-11','Semantics':'Engine test only; no administrative truth'}
dateline=[feature('synthetic-country','country',None,polygon(170,-10,-170,10),[]),feature('synthetic-dateline-city','city','synthetic-country',polygon(175,-5,-175,5),['synthetic-country'])]
nested=base+[feature('synthetic-inner-district','district','synthetic-west',polygon(1.5,1.5,2.5,2.5),['synthetic-country','synthetic-region','synthetic-west','synthetic-district'])]
for name,features in [('topology',base),('nested',nested),('dateline',dateline),('overlap',base+[feature('synthetic-overlap','city','synthetic-region',polygon(5,0,15,20),['synthetic-country','synthetic-region'])]),('same-version-updated',base[:2]),('updated',base[:2])]:
    source=out/(name+'.geojson');source.write_text(json.dumps({'type':'FeatureCollection','features':features}),encoding='utf-8')
    current=dict(manifest);current['Version']='2' if name=='updated' else '1'
    metadata=out/(name+'-manifest.json');metadata.write_text(json.dumps(current),encoding='utf-8')
    package=out/(name+'.gsgeo')
    if package.exists():package.unlink() # Only fixed synthetic outputs in this dedicated directory.
    print(conversion.convert(source,metadata,package))
(out/'corrupt.gsgeo').write_bytes(b'Not a SQLite geographic package')
