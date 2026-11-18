# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Build optional native atlas geometry from cached licensed tiles and bounded Natural Earth downloads.

Development dependencies reuse the pinned map-preparation workspace. Runtime needs only WPF/.NET.
OSM archive access is deliberately cache-only: this script cannot fetch regional source data.
"""
from pathlib import Path
import collections, gzip, hashlib, importlib.util, importlib.metadata, json, math, shutil, sys, urllib.request

ROOT=Path(__file__).resolve().parent.parent
sys.path[:0]=[str(ROOT/'offline-map-workspace/development'),str(ROOT/'.local/geographic-dev-pinned')]
from shapely.geometry import shape,box
from shapely.ops import unary_union
from shapely import transform

spec=importlib.util.spec_from_file_location('cityprep',ROOT/'scripts/prepare-city-maps.py')
cityprep=importlib.util.module_from_spec(spec);spec.loader.exec_module(cityprep)
WORK=ROOT/'offline-map-workspace/atlas-pilot';OUT=ROOT/'offline-maps/atlas-pilot'
WORK.mkdir(exist_ok=True);OUT.mkdir(exist_ok=True)
if shutil.disk_usage(ROOT).free<2*1024**3:raise RuntimeError('At least 2 GiB free required')
downloaded=0;sources=[]

def get(url,path,limit):
    global downloaded
    if path.exists():return path.read_bytes()
    with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'GeoStack-atlas-pilot'}),timeout=90) as response:
        data=response.read(limit+1)
    if len(data)>limit or downloaded+len(data)>64*1024**2:raise RuntimeError('64 MiB total source budget exceeded')
    downloaded+=len(data);path.write_bytes(data);return data

def project(lon,lat):
    lat=max(-85.05112878,min(85.05112878,lat))
    return [(lon+180)/360,(1-math.asinh(math.tan(math.radians(lat)))/math.pi)/2]

def paths(g,convert):
    if g.is_empty:return []
    if g.geom_type=='Polygon':return [list(map(convert,g.exterior.coords))]+[list(map(convert,r.coords)) for r in g.interiors]
    if g.geom_type=='LineString':return [list(map(convert,g.coords))]
    return [p for c in getattr(g,'geoms',[]) for p in paths(c,convert)]

def save(slug,features,labels,coverage,source):
    payload={'Schema':1,'Id':slug,'Source':source,'Coverage':coverage,'Features':features,'Labels':labels}
    raw=json.dumps(payload,separators=(',',':'),ensure_ascii=False).encode();target=OUT/(slug+'.gsatlas')
    if len(raw)>64*1024**2:raise RuntimeError('Pilot expanded package limit exceeded')
    data=gzip.compress(raw,compresslevel=9,mtime=0);target.write_bytes(data)
    report={'Id':slug,'File':target.name,'Bytes':len(data),'ExpandedBytes':len(raw),'SHA256':hashlib.sha256(data).hexdigest(),'Features':len(features),'Vertices':sum(len(p) for f in features for p in f['Paths']),'Labels':len(labels),'Coverage':coverage,'Source':source}
    print(json.dumps(report),flush=True);return report

def global_package():
    commit_path=WORK/'natural-earth-commit.json'
    commit=json.loads(get('https://api.github.com/repos/nvkelso/natural-earth-vector/commits/master',commit_path,131072))['sha']
    features=[]
    layers=[('ocean','ne_50m_ocean'),('land','ne_50m_land'),('water','ne_50m_lakes'),('river','ne_50m_rivers_lake_centerlines'),('boundary','ne_50m_admin_0_boundary_lines_land'),('region','ne_50m_admin_1_states_provinces_lines')]
    for kind,name in layers:
        url=f'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/{commit}/geojson/{name}.geojson';data=get(url,WORK/(name+'.geojson'),16*1024**2)
        sources.append({'URL':url,'Bytes':len(data),'SHA256':hashlib.sha256(data).hexdigest(),'License':'Natural Earth public domain'})
        for f in json.loads(data)['features']:
            geom=shape(f['geometry']).simplify(.015,preserve_topology=True)
            pp=paths(geom,lambda c:[round(v,9) for v in project(*c[:2])])
            # Split lines at the Mercator wrap instead of drawing across the world.
            if not pp:continue
            if geom.geom_type in ['LineString','MultiLineString']:
                pp=[p for p in pp if all(abs(a[0]-b[0])<.5 for a,b in zip(p,p[1:]))]
            if pp:features.append({'Lod':0,'Kind':kind,'Polygon':geom.geom_type in ['Polygon','MultiPolygon'],'Paths':pp})
    labels=[]
    for file,rank in [('countries.geojson',3),('cities.geojson',6)]:
        for f in json.loads((ROOT/'assets/geography'/file).read_text(encoding='utf-8'))['features']:
            p=f['properties'];city=file.startswith('cities');lon=p.get('LONGITUDE' if city else 'LABEL_X',p.get('longitude'));lat=p.get('LATITUDE' if city else 'LABEL_Y',p.get('latitude'))
            if lon is None or lat is None:continue
            source=p.get('name' if city else 'NAME','');en=p.get('NAME_EN',p.get('name_en',source));ru=p.get('NAME_RU',p.get('name_ru',source));xy=project(lon,lat)
            labels.append({'Id':str(p.get('NE_ID',p.get('ne_id',source))),'X':xy[0],'Y':xy[1],'Name':source,'English':en,'Russian':ru,'MinZoom':max(3,p.get('scalerank',6)+3) if city else 0,'Country':not city})
    return save('world',features,labels,[-180,-85.05113,180,85.05113],{'Name':'Natural Earth','Version':commit,'License':'Public domain','URL':'https://www.naturalearthdata.com/about/terms-of-use/'})

def regional(slug,lat,lon):
    archive=cityprep.Archive();used={}
    def read(offset,length):
        path=archive.cache/f'{offset}-{length}.bin'
        if not path.exists():raise RuntimeError('Required cached range missing; no regional download permitted: '+str(path))
        return path.read_bytes()
    reader=cityprep.Reader(read);features=[];labels={};counts=collections.Counter()
    for z in [8,10]:
        checkpoint=WORK/f'{slug}-v2-lod-{z}.json.gz'
        if checkpoint.exists():
            saved=json.loads(gzip.decompress(checkpoint.read_bytes()));features.extend(saved['Features']);labels.update(saved['Labels']);used.update(saved['Tiles']);counts=collections.Counter(saved['Counts']);print(slug,'cached LOD',z,flush=True);continue
        feature_start=len(features)
        halfx,halfy=(.025,.020) if z==15 else (.6,.5)
        x0,y1=cityprep.tile_xy(lat-halfy,lon-halfx,z);x1,y0=cityprep.tile_xy(lat+halfy,lon+halfx,z)
        for x in range(int(x0),int(x1)+1):
            for y in range(int(y0),int(y1)+1):
                source=reader.get(z,x,y)
                if source is None:raise RuntimeError('Missing geographic source tile')
                used[f'{z}/{x}/{y}']=hashlib.sha256(source).hexdigest()
                layers=cityprep.decode(gzip.decompress(source),default_options={'y_coord_down':True});groups=collections.defaultdict(list)
                for layer in ['earth','landuse','water','buildings','boundaries','roads']:
                    data=layers.get(layer,{});extent=data.get('extent',4096)
                    for f in data.get('features',[]):
                        p=f['properties'];kind=p.get('kind','');detail=p.get('kind_detail','');style=None
                        if layer=='earth':style='land'
                        elif layer=='water':style='water' if 'Polygon' in f['geometry']['type'] else 'river'
                        elif layer=='landuse':style='park' if kind in ['park','forest','wood','grass','nature_reserve','recreation_ground','golf_course','cemetery'] else 'urban' if kind in ['urban','urban_area','residential','industrial','commercial','retail','university','hospital'] else None
                        elif layer=='buildings' and z==15:style='building'
                        elif layer=='boundaries':style='boundary'
                        elif layer=='roads':style={'highway':'road-major','major_road':'road-secondary','minor_road':'road-local','rail':'rail'}.get(kind)
                        if style is None:continue
                        if z<12 and style in ['road-local','building']:continue
                        try:
                            g=shape(f['geometry']);g=g.intersection(box(0,0,extent,extent))
                            tolerance={8:2.5,10:2.5,12:3.5,15:3.0}[z];g=g.simplify(tolerance,preserve_topology=True)
                            if g.is_empty:continue
                            groups[(style,extent,'Polygon' in g.geom_type)].append(g);counts[style]+=1
                        except Exception as ex:raise RuntimeError(f'Invalid source geometry {slug}/{z}/{x}/{y}') from ex
                for (style,extent,polygon),geometries in groups.items():
                    g=unary_union(geometries);pp=paths(g,lambda c:[round((x+c[0]/extent)/2**z,9),round((y+c[1]/extent)/2**z,9)])
                    if pp:features.append({'Lod':z,'Kind':style,'Polygon':polygon,'Paths':pp})
                data=layers.get('places',{});extent=data.get('extent',4096)
                for f in data.get('features',[]):
                    p=f['properties'];c=f['geometry'];name=p.get('name');kind=p.get('kind_detail','')
                    if not name or c['type']!='Point' or kind not in ['city','town','village','suburb','quarter','neighbourhood']:continue
                    px,py=c['coordinates'];px=(x+px/extent)/2**z;py=(y+py/extent)/2**z
                    ident=str(p.get('wikidata') or f'{name}:{px:.5f}:{py:.5f}')
                    labels[ident]={'Id':ident,'Name':name,'English':p.get('name:en',name),'Russian':p.get('name:ru',p.get('name:en',name)),'X':px,'Y':py,'MinZoom':max(6,min(14,p.get('min_zoom',12))),'Country':False}
        checkpoint.write_bytes(gzip.compress(json.dumps({'Features':features[feature_start:],'Labels':labels,'Tiles':used,'Counts':dict(counts)},separators=(',',':')).encode(),mtime=0))
        print(slug,'finished LOD',z,'groups',len(features),flush=True)
    (WORK/(slug+'-source-tiles.json')).write_text(json.dumps(used,indent=2)+'\n')
    return save(slug,features,list(labels.values()),[lon-.6,lat-.5,lon+.6,lat+.5],{'Name':'Protomaps OSM','Version':'4.14.3 / 2026-03-31','License':'OSM ODbL Produced Work; attribution required; derivative geometry licensing review before public redistribution','URL':cityprep.SOURCE,'SourceIdentity':json.loads((ROOT/'offline-map-workspace/source-identity.json').read_text()),'OriginalFeatures':dict(counts),'SourceTiles':len(used)})

if __name__=='__main__':
    packages=[global_package(),regional('zurich',47.3782,8.5402),regional('warsaw',52.2297,21.0122)]
    (OUT/'MANIFEST.json').write_text(json.dumps({'Schema':1,'Packages':packages,'NewSourceDownloadedBytes':downloaded,'NaturalEarthSources':sources,'NaturalEarthSourceBytes':sum(s['Bytes'] for s in sources),'RegionalDownloads':0,'DevelopmentDependencies':{name:importlib.metadata.version(name) for name in ['shapely','mapbox-vector-tile','pmtiles']},'RuntimeDependenciesAdded':[],'WorldLabelSources':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'assets/geography/countries.geojson',ROOT/'assets/geography/cities.geojson']}},indent=2)+'\n')
