# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Bounded development-only Natural Earth preprocessing. No runtime Python dependency.

Reads cached 50m boundaries/water and two public-domain 10m ZIPs, without extracting
archives. Produces fixed-point, spatially split, multiscale geometry; no roads.
"""
from pathlib import Path
import collections,gzip,hashlib,io,json,math,shutil,struct,sys,urllib.request,zipfile
ROOT=Path(__file__).resolve().parent.parent
sys.path[:0]=[str(ROOT/'offline-map-workspace/development'),str(ROOT/'.local/geographic-dev-pinned')]
from shapely.geometry import Polygon,LineString,shape,box
from shapely.ops import transform
from shapely import make_valid
WORK=ROOT/'.local/mapdata/global/source';WORK.mkdir(parents=True,exist_ok=True)
OUT=ROOT/'assets/geography/world.gsbase'; downloaded=0;sources=[]
previous_manifest=OUT.with_suffix('.manifest.json')
expected_sources={s['Name']:s['SHA256'] for s in json.loads(previous_manifest.read_text())['Sources']} if previous_manifest.exists() else {}
if shutil.disk_usage(ROOT).free<1024**3:raise RuntimeError('At least 1 GiB free space required')
def acquire(name,category):
 global downloaded
 url=f'https://naturalearth.s3.amazonaws.com/10m_{category}/{name}.zip';p=WORK/(name+'.zip')
 if not p.exists():
  with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'GeoStack-compact-basemap-preparation'}),timeout=90) as response:
   data=response.read(32*1024**2+1)
  if len(data)>32*1024**2 or downloaded+len(data)>40*1024**2:raise RuntimeError('Source download budget exceeded')
  downloaded+=len(data);p.write_bytes(data)
 data=p.read_bytes();digest=hashlib.sha256(data).hexdigest()
 if name in expected_sources and digest!=expected_sources[name]:raise RuntimeError('Pinned source changed; review a dataset update explicitly: '+name)
 sources.append({'Name':name,'URL':url,'Bytes':len(data),'SHA256':digest,'License':'Natural Earth public domain'})
 archive=zipfile.ZipFile(io.BytesIO(data))
 if sum(i.file_size for i in archive.infolist())>160*1024**2:raise RuntimeError('Unexpected expanded source size')
 return archive.read(name+'.shp')
def shp_geometries(data):
 offset=100
 while offset<len(data):
  length=struct.unpack_from('>i',data,offset+4)[0]*2;record=memoryview(data)[offset+8:offset+8+length];offset+=8+length
  kind=struct.unpack_from('<i',record)[0]
  if kind==0:continue
  if kind not in (3,5):raise ValueError('Only 2D lines/polygons are supported')
  parts,points=struct.unpack_from('<ii',record,36);starts=list(struct.unpack_from('<'+'i'*parts,record,44))+[points]
  xy=list(struct.iter_unpack('<dd',record[44+4*parts:44+4*parts+16*points]))
  rings=[xy[a:b] for a,b in zip(starts,starts[1:]) if b-a>=3]
  if kind==3:
   for ring in rings:yield LineString(ring)
  else:
   # Shapefile shells are clockwise, holes counterclockwise; preserve nesting.
   shells=[];holes=[]
   for ring in rings:
    signed=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(ring,ring[1:]))
    (shells if signed<0 else holes).append(ring)
   if not shells:shells=rings;holes=[]
   for shell in shells:
    p=Polygon(shell);inside=[h for h in holes if p.contains(Polygon(h).representative_point())]
    g=Polygon(shell,inside)
    yield g if g.is_valid else g.buffer(0)
def project(x,y,z=None):
 def one(lon,lat):return ((lon+180)/360,(1-math.asinh(math.tan(math.radians(max(-85.05112878,min(85.05112878,lat)))))/math.pi)/2)
 if hasattr(x,'__iter__'):
  result=[one(a,b) for a,b in zip(x,y)];return tuple(zip(*result))
 return one(x,y)
def components(g):
 if g.geom_type in ('Polygon','LineString'):yield g
 else:
  for sub in getattr(g,'geoms',[]):yield from components(sub)
def paths(g):
 return [list(g.exterior.coords)]+[list(r.coords) for r in g.interiors] if g.geom_type=='Polygon' else [list(g.coords)]
def split(g,depth=0):
 if sum(len(p) for p in paths(g))<=768 or depth>=10:yield g;return
 x0,y0,x1,y1=g.bounds
 cuts=[box(x0,y0,(x0+x1)/2,y1),box((x0+x1)/2,y0,x1,y1)] if x1-x0>=y1-y0 else [box(x0,y0,x1,(y0+y1)/2),box(x0,(y0+y1)/2,x1,y1)]
 for cut in cuts:
  for part in components(g.intersection(cut)):
   if not part.is_empty:yield from split(part,depth+1)
features=[];counts=collections.Counter();vertices=collections.Counter();expanded_source=0
def add(kind,geometries):
 for original in geometries:
  if original.is_empty:continue
  original=original.intersection(box(-180,-85.05112878,180,85.05112878))
  for lod,tolerance in [(0,.04),(8,.008),(12,.0015)]:
   if kind=='urban' and lod==0 and original.area<.02:continue
   g=transform(project,original.simplify(tolerance,preserve_topology=True))
   if not g.is_valid:g=make_valid(g)
   for part in components(g):
    if part.is_empty:continue
    x0,y0,x1,y1=part.bounds
    for x in range(max(0,int(x0*64)),min(63,int(x1*64))+1):
     for y in range(max(0,int(y0*64)),min(63,int(y1*64))+1):
      for cell in components(part.intersection(box(x/64,y/64,(x+1)/64,(y+1)/64))):
       if cell.is_empty:continue
       for chunk in split(cell):
        pp=paths(chunk)
        if not pp or any(len(p)<2 for p in pp):continue
        features.append((lod,kind,chunk.geom_type=='Polygon',chunk.bounds,pp));counts[(lod,kind)]+=1;vertices[(lod,kind)]+=sum(map(len,pp))
 print(kind,'features',len(features),flush=True)
add('land',shp_geometries(acquire('ne_10m_land','physical')))
add('urban',shp_geometries(acquire('ne_10m_urban_areas','cultural')))
for kind,name in [('water','lakes'),('river','rivers_lake_centerlines'),('boundary','admin_0_boundary_lines_land'),('region','admin_1_states_provinces_lines')]:
 p=ROOT/'offline-map-workspace/atlas-pilot'/('ne_50m_'+name+'.geojson');data=p.read_bytes();digest=hashlib.sha256(data).hexdigest()
 if p.name in expected_sources and digest!=expected_sources[p.name]:raise RuntimeError('Pinned cached source changed: '+p.name)
 sources.append({'Name':p.name,'Bytes':len(data),'SHA256':digest,'License':'Natural Earth public domain','URL':'https://www.naturalearthdata.com/'})
 add(kind,(shape(f['geometry']) for f in json.loads(data)['features'] if f['geometry']))
raw=io.BytesIO();raw.write(b'GSBASE02');raw.write(struct.pack('<ii',2,len(features)))
kinds=['land','urban','water','river','boundary','region']
for lod,kind,polygon,bounds,pp in features:
 raw.write(struct.pack('<BBB4fI',lod,kinds.index(kind),polygon,*bounds,len(pp)))
 for path in pp:
  raw.write(struct.pack('<I',len(path)))
  for x,y in path:raw.write(struct.pack('<II',round(max(0,min(1,x))*1e9),round(max(0,min(1,y))*1e9)))
data=raw.getvalue();packed=gzip.compress(data,compresslevel=9,mtime=0)
if len(packed)>30*1024**2 or len(data)>100*1024**2:raise RuntimeError('Runtime budget exceeded; do not install')
OUT.write_bytes(packed)
report={'Schema':2,'File':'world.gsbase','CompressedBytes':len(packed),'ExpandedBytes':len(data),'SHA256':hashlib.sha256(packed).hexdigest(),'NewSourceBytes':downloaded,'Features':len(features),'Vertices':sum(vertices.values()),'LODs':{str(lod):{'Features':{k:v for (l,k),v in counts.items() if l==lod},'Vertices':{k:v for (l,k),v in vertices.items() if l==lod}} for lod in [0,8,12]},'Sources':sources,'UrbanMeaning':'Historical approximate settlement extents; not administrative or current legal city boundaries','WorldCoverage':'Web Mercator extent, land/coastlines, water, boundaries; no road or building claims','Generator':'scripts/prepare-compact-basemap.py','CoordinateScale':1000000000}
(OUT.with_suffix('.manifest.json')).write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k not in ['Sources','LODs']}),flush=True)
