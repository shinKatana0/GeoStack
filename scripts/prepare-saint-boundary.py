# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Convert the pinned, already acquired RU-SPE polygon; no network or point-derived boundaries."""
from pathlib import Path
import argparse,json,hashlib,importlib.util,shutil
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('source',type=Path);parser.add_argument('output',type=Path);args=parser.parse_args()
root=Path(__file__).resolve().parent.parent
digest=hashlib.sha256(args.source.read_bytes()).hexdigest()
assert digest=='47179a0b1f30097226bc057921e674d83a9ac9e2a44023e687351355cdb7fc1b','Pinned source checksum mismatch'
data=json.loads(args.source.read_text(encoding='utf-8'));matches=[f for f in data['features'] if f['properties']['shapeISO']=='RU-SPE'];assert len(matches)==1
city=matches[0];assert city['properties']['shapeName']=='Saint Petersburg' and city['properties']['shapeID']=='28173009B8796515134902'
country=next(f for f in json.loads((root/'assets/geography/countries.geojson').read_text(encoding='utf-8'))['features'] if f['properties']['NAME']=='Russia')
country_id=str(country['properties']['NE_ID']);identifier='geoboundaries:RUS-ADM1-28173009:RU-SPE'
def feature(p,g):return {'type':'Feature','properties':p,'geometry':g}
source={'type':'FeatureCollection','features':[
 feature({'id':country_id,'name':'Russia','name_en':'Russia','name_ru':country['properties']['NAME_RU'],'level':'country','country_id':country_id,'semantics':'Natural Earth coarse country, public domain; retain uncertainty','warnings':['GeoCoarseCountry']},country['geometry']),
 feature({'id':identifier,'name':'Saint Petersburg','name_en':'Saint Petersburg','name_ru':'\u0421\u0430\u043d\u043a\u0442-\u041f\u0435\u0442\u0435\u0440\u0431\u0443\u0440\u0433','level':'city','parent_id':country_id,'country_id':country_id,'ancestors':[country_id],'semantics':'Reviewed federal-city administrative extent (2017), including subordinate areas; not a built-up footprint or present-day legal precision','warnings':['GeoDatasetOutdated']},city['geometry'])]}
args.output.mkdir(parents=True,exist_ok=True)
input_path=args.output/'conversion-source.geojson';input_path.write_text(json.dumps(source,ensure_ascii=True,separators=(',',':')),encoding='utf-8')
manifest={'Id':'geoboundaries-ru-spe-selected','Name':'Saint Petersburg selected federal-city containment','Version':'gb-9469f09-RU-SPE-review1','Source':'geoBoundaries gbOpen / OpenStreetMap Wambacher; Natural Earth country','SourceUrl':'https://www.geoboundaries.org/api/current/gbOpen/RUS/ADM1/','License':'ODbL-1.0; Natural Earth public domain','LicenseUrl':'https://opendatacommons.org/licenses/odbl/1-0/','Attribution':'OpenStreetMap contributors; geoBoundaries; Wambacher; Natural Earth','Coverage':'Russia: selected Saint Petersburg federal-city extent only; no other Russian cities or districts','DataDate':'2017-01-01','Semantics':'Federal-city role reviewed separately from numeric ADM1. Government statistical source: https://www.rosstat.gov.ru/storage/mediabank/sJb8E6SZ/status-gorodov.pdf . No filename/nearest-city inference. Historic boundary requires uncertainty warning.','BoundaryToleranceDegrees':1e-8,'SourceSHA256':digest,'SourceFeature':city['properties'],'SourceCommit':'9469f09'}
meta=args.output/'source-manifest.json';meta.write_text(json.dumps(manifest,indent=2)+'\n')
spec=importlib.util.spec_from_file_location('conversion',root/'scripts/convert-geographic-package.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
result=module.convert(input_path,meta,args.output/'ru-spe-selected.gsgeo')
(args.output/'package.json').write_text(json.dumps(result,indent=2)+'\n')
(args.output/'NOTICE.txt').write_text('ODbL 1.0: OpenStreetMap contributors, geoBoundaries, Wambacher. Natural Earth country is public domain. Changes: select RU-SPE source polygon, explicit federal-city semantic review and SQLite R-tree indexing. Historical 2017 extent, not current legal precision. Full derived geometry is in conversion-source.geojson; no original photograph was used.\n')
shutil.copy2(root/'.local/geographic-sources/license-texts/ODbL-1.0.txt',args.output/'ODbL-1.0.txt')
print(json.dumps(result,indent=2))
