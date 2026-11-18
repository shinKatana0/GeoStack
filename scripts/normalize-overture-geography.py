# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Review Overture polygon roles with source hierarchy and Wikidata entity types.

Development only. No place names, distances or target coordinates determine classification.
All retained city/district geometry comes from actual source areas.
"""
import argparse
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import sys
import urllib.request

root=Path(__file__).resolve().parent.parent
development=root/'.local/geographic-dev-pinned'
if development.exists():sys.path.insert(0,str(development))
from shapely.geometry import shape,mapping,Point
from shapely.ops import unary_union

def records(path):
    return [json.loads(line.lstrip('\x1e')) for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]

def as_map(value):
    return dict(value or {}) if isinstance(value,list) else value or {}

def normalize(areas_path,divisions_path,output,release,tolerance,targets_path):
    areas=records(areas_path);divisions={f['properties']['id']:f['properties'] for f in records(divisions_path)}
    targets=json.loads(targets_path.read_text(encoding='utf-8'))
    selected_points=[Point(p['longitude'],p['latitude']) for p in targets]
    areas=[feature for feature in areas if any(shape(feature['geometry']).covers(point) for point in selected_points)]
    rules=json.loads((root/'scripts/geographic-semantic-rules.json').read_text(encoding='utf-8'))
    wiki_dir=root/'.local/geographic-sources/wikidata';wiki_dir.mkdir(exist_ok=True)
    wiki_revisions={};memo={}
    def types(division):
        q=division.get('wikidata')
        if not q:return set()
        if q not in memo:
            path=wiki_dir/(q+'.json')
            if not path.exists():
                request=urllib.request.Request(f'https://www.wikidata.org/wiki/Special:EntityData/{q}.json',headers={'User-Agent':'GeoStack-development-data-review/0.4'})
                path.write_bytes(urllib.request.urlopen(request,timeout=30).read())
            entity=json.loads(path.read_text(encoding='utf-8'))['entities'][q]
            memo[q]={claim['mainsnak'].get('datavalue',{}).get('value',{}).get('id') for claim in entity.get('claims',{}).get('P31',[]) if claim.get('rank')!='deprecated'}
            wiki_revisions[q]={'revision':entity.get('lastrevid'),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
        return memo[q]
    def ancestors(division):
        return list(dict.fromkeys(item['division_id'] for hierarchy in division.get('hierarchies') or [] for item in hierarchy if item['division_id']!=division.get('id')))
    def role(division):
        if types(division)&set(rules['districtInstanceTypes']):return 'district'
        if division.get('class')=='city' and division.get('subtype')=='locality':return 'city'
        if division.get('subtype') in ('borough','macrohood','neighborhood'):return 'district'
        return None
    groups=defaultdict(list)
    for feature in areas:
        p=feature['properties']
        if p.get('is_territorial') is False:continue
        if p.get('class')=='maritime' and p.get('is_territorial') is not True:continue
        groups[p['division_id']].append(feature)
    countries={};regions={}
    for identifier,features in groups.items():
        p=features[0]['properties']
        if p['subtype']=='country':countries[p['country']]=identifier
        if p['subtype']=='region' and p.get('region'):regions[p['region']]=identifier
    city_nodes=[d for d in divisions.values() if d.get('subtype')=='locality' and d.get('class')=='city']
    plans={};city_aliases={};source_aliases={};omitted=[]
    def plan(output_id,source_id,level,naming,description,warning=False):
        plans[output_id]={'source_id':source_id,'level':level,'division':naming,'description':description,'warnings':['GeoAdministrativeCityExtent'] if warning else []}
        source_aliases[source_id]=output_id
        if level=='city':
            city_aliases[source_id]=output_id;city_aliases[output_id]=output_id
    # A direct-administered municipality has a documented city jurisdiction.
    # Metropolitan prefectures remain regions; their independent municipalities are not invented as one city.
    for identifier in regions.values():
        d=divisions.get(identifier,{})
        if not types(d)&set(rules['administrativeLocalityRegionTypes']):continue
        linked=[n for n in city_nodes if d.get('wikidata') and n.get('wikidata')==d['wikidata']]
        if len(linked)==1:
            plan(linked[0]['id'],identifier,'city',linked[0],'Administrative metropolitan/municipal extent; identical source-linked Wikidata entity',True)
    # Local administrative cities and city-equivalent county records must carry
    # explicit source identity/type or a unique direct city child, never just ADM level.
    for identifier,features in groups.items():
        p=features[0]['properties'];d=divisions.get(identifier,{})
        if p['subtype'] not in ('localadmin','county'):continue
        same_entity=[n for n in city_nodes if d.get('wikidata') and n.get('wikidata')==d['wikidata']]
        direct=[n for n in city_nodes if n.get('parent_division_id')==identifier]
        linked=same_entity if len(same_entity)==1 else direct if as_map(d.get('local_type')).get('en')=='city' and len(direct)==1 else []
        if len(linked)!=1:continue
        child=linked[0];kind=role(child)
        if child['id'] in groups and groups[child['id']][0]['properties']['subtype']=='locality':continue
        if kind in ('city','district') and child['id'] not in plans:
            plan(child['id'],identifier,kind,child,'Administrative source polygon linked to a unique source locality by entity identity or explicit city-child hierarchy',kind=='city')
    # Retain genuine locality polygons only when the source class is a reviewed city.
    # Place nodes without polygons cannot create coverage.
    for identifier,features in groups.items():
        p=features[0]['properties'];d=divisions.get(identifier,{})
        if p['subtype']!='locality' or identifier in plans:continue
        kind=role(d)
        if kind in ('city','district'):
            if kind=='city' and d.get('wikidata'):
                equivalents=[i for i,v in plans.items() if v['level']=='city' and v['division'].get('wikidata')==d['wikidata']]
                if len(equivalents)==1:city_aliases[identifier]=equivalents[0];continue
            plan(identifier,identifier,kind,d,'Actual source locality polygon; Wikidata district types take precedence over generic city classification')
    # Countries and regions keep their original identifiers and geometries.
    for identifier,features in groups.items():
        p=features[0]['properties']
        if p['subtype'] in ('country','region'):
            plan(identifier,identifier,p['subtype'],divisions.get(identifier,p),'Original Overture administrative area subtype')
        elif p['subtype'] in ('borough','macrohood','neighborhood') and identifier not in plans:
            d=divisions.get(identifier,p)
            plan(identifier,identifier,'district',d,'Original Overture borough/super-neighborhood/neighborhood area with verified city hierarchy')
            if not types(d)&set(rules['districtInstanceTypes']):plans[identifier]['warnings'].append('GeoNeighborhoodExtent')
    normalized=[]
    for identifier,item in sorted(plans.items()):
        source_id=item['source_id'];features=groups[source_id];p=features[0]['properties'];d=item['division'];level=item['level'];country=countries.get(p['country'])
        if country is None:omitted.append({'id':identifier,'reason':'No included country polygon'});continue
        region=regions.get(p.get('region'));history=ancestors(divisions.get(source_id,d))
        original_history=ancestors(d)
        history=list(dict.fromkeys(history if level=='city' and source_id!=identifier else history+original_history))
        history=[source_aliases.get(a,a) if source_aliases.get(a,a)!=identifier else a for a in history]
        if country!=identifier and country not in history:history.append(country)
        if region and region!=identifier and region not in history:history.append(region)
        if level=='district':
            parents=set(city_aliases[a] for a in history+[source_id] if a in city_aliases and city_aliases[a] in plans)
            if len(parents)==1:parent=parents.pop()
            elif not parents and region and types(d)&set(rules['districtInstanceTypes']):parent=region
            else:omitted.append({'id':identifier,'reason':'Missing or ambiguous verified city/administrative region hierarchy'});continue
            if parent not in history:history.append(parent)
        elif level=='city':parent=region or country
        elif level=='region':parent=country
        else:parent=None
        geometry=unary_union([shape(f['geometry']) for f in features])
        if not geometry.is_valid or geometry.geom_type not in ('Polygon','MultiPolygon'):
            omitted.append({'id':identifier,'reason':'Invalid or nonpolygonal source geometry'});continue
        geometry=geometry.simplify(tolerance,preserve_topology=True) if tolerance else geometry
        names=d.get('names') or p.get('names') or {};common=as_map(names.get('common'))
        properties={'id':identifier,'level':level,'parent_id':parent,'country_id':country,'country_code':p['country'],'name':names.get('primary') or common.get('en'),'name_en':common.get('en'),'name_ru':common.get('ru'),'ancestors':list(dict.fromkeys(history)),'semantics':item['description'],'warnings':item['warnings'],'source_division_id':source_id,'source_area_ids':[f['properties']['id'] for f in features],'wikidata':d.get('wikidata'),'sources':[s for f in features for s in f['properties'].get('sources') or []]}
        if not properties['name']:omitted.append({'id':identifier,'reason':'No verified source name'});continue
        normalized.append({'type':'Feature','properties':properties,'geometry':mapping(geometry)})
    # Omitted source subtypes remain explicitly reported for coverage review.
    used_sources={v['source_id'] for v in plans.values()}
    for identifier,features in groups.items():
        if identifier not in used_sources:omitted.append({'id':identifier,'subtype':features[0]['properties']['subtype'],'reason':'No reviewed city/district role; original point-only or intermediate semantics retained in raw source'})
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('w',encoding='utf-8') as file:
        for feature in normalized:file.write(json.dumps(feature,ensure_ascii=False,separators=(',',':'))+'\n')
    report={'release':release,'inputAreasSha256':hashlib.sha256(areas_path.read_bytes()).hexdigest(),'inputDivisionsSha256':hashlib.sha256(divisions_path.read_bytes()).hexdigest(),'semanticRulesSha256':hashlib.sha256((root/'scripts/geographic-semantic-rules.json').read_bytes()).hexdigest(),'wikidataRevisions':wiki_revisions,'simplificationDegrees':tolerance,'included':len(normalized),'omitted':omitted,'countries':sorted(countries),'levels':{level:sum(f['properties']['level']==level for f in normalized) for level in ['country','region','city','district']}}
    output.with_suffix('.review.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('areas',type=Path);parser.add_argument('divisions',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--release',default='2026-10-21.0');parser.add_argument('--simplify-degrees',type=float,default=0.00001)
    parser.add_argument('--targets',type=Path,default=root/'scripts/geographic-validation-targets.json')
    args=parser.parse_args();normalize(args.areas,args.divisions,args.output,args.release,args.simplify_degrees,args.targets)
