# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""One bounded cache-only global motorway/trunk/primary pass. No source downloads.
The generated pilot is installed into source assets only if every budget passes.
"""
from pathlib import Path
import collections,gzip,hashlib,json,sqlite3,struct,sys,time
ROOT=Path(__file__).resolve().parent.parent
sys.path[:0]=[str(ROOT/'offline-map-workspace/development'),str(ROOT/'.local/geographic-dev-pinned')]
from mapbox_vector_tile import decode
from shapely.geometry import shape,box
WORK=ROOT/'.local/mapdata/roads';WORK.mkdir(parents=True,exist_ok=True)
db=sqlite3.connect((ROOT/'offline-maps/global-atlas/global-context.gsvector').resolve().as_uri()+'?mode=ro',uri=True)
raw=WORK/'roads.raw';output=WORK/'urban-roads.gslite';started=time.perf_counter();feature_count=vertices=segments=source_features=0;counts=collections.Counter();tiles=0
def lines(g):
 if g.geom_type=='LineString':yield list(g.coords)
 else:
  for sub in getattr(g,'geoms',[]):yield from lines(sub)
with raw.open('wb') as writer:
 writer.write(b'GSROAD01'+struct.pack('<ii',1,0))
 for x,y,data in db.execute('SELECT t.x,t.y,c.data FROM tiles t JOIN contents c ON c.id=t.content WHERE t.z=7 ORDER BY t.x,t.y'):
  layers=decode(gzip.decompress(data),default_options={'y_coord_down':True});roads=layers.get('roads',{});extent=roads.get('extent',4096);groups=collections.defaultdict(list)
  for f in roads.get('features',[]):
   source_features+=1;p=f['properties'];detail=p.get('kind_detail')
   if detail not in ['motorway','trunk','primary'] or float(p.get('min_zoom',0))>8:continue
   g=shape(f['geometry']).intersection(box(0,0,extent,extent)).simplify(6,preserve_topology=True)
   for line in lines(g):
    if len(line)<2:continue
    groups[detail].append([((x+a/extent)/128,(y+b/extent)/128) for a,b in line]);segments+=1;counts[detail]+=1
  for kind,paths in groups.items():
   batches=[];batch=[];length=0
   for path in paths:
    for pos in range(0,len(path)-1,767):
     part=path[pos:pos+768]
     if length+len(part)>768:batches.append(batch);batch=[];length=0
     batch.append(part);length+=len(part)
   if batch:batches.append(batch)
   for paths in batches:
    points=[p for line in paths for p in line];bounds=(min(p[0] for p in points),min(p[1] for p in points),max(p[0] for p in points),max(p[1] for p in points))
    writer.write(struct.pack('<B4fI',['motorway','trunk','primary'].index(kind),*bounds,len(paths)))
    for line in paths:
     writer.write(struct.pack('<I',len(line)))
     for a,b in line:writer.write(struct.pack('<II',round(max(0,min(1,a))*1e9),round(max(0,min(1,b))*1e9)))
    feature_count+=1;vertices+=len(points)
  tiles+=1
  if writer.tell()>64*1024**2:raise RuntimeError('Expanded scratch budget exceeded; no dataset installed')
  if tiles%2048==0:print(tiles,feature_count,vertices,flush=True)
with raw.open('r+b') as file:file.seek(12);file.write(struct.pack('<i',feature_count))
with raw.open('rb') as source,output.open('wb') as target:
 with gzip.GzipFile(fileobj=target,mode='wb',mtime=0,compresslevel=9,filename='') as packed:
  for block in iter(lambda:source.read(1048576),b''):packed.write(block)
accepted=vertices<=2000000 and feature_count<=30000 and output.stat().st_size<=30*1024**2
report={'Schema':1,'NativeZoom':7,'Tiles':tiles,'SourceRoadFeatures':source_features,'FilteredSegments':segments,'GroupedGeometry':feature_count,'Vertices':vertices,'Classes':dict(counts),'CompressedBytes':output.stat().st_size,'ExpandedBytes':raw.stat().st_size,'ProcessingSeconds':time.perf_counter()-started,'SHA256':hashlib.sha256(output.read_bytes()).hexdigest(),'Source':'Existing Protomaps v4.14.3 / OSM 2026-03-31 global-context.gsvector, native XYZ zoom7','Coverage':[-180,-85.0511287,180,85.0511287],'License':'OpenStreetMap ODbL; attribution and derivative-data licensing review required','Simplification':'6/4096 tile units at zoom7; approximately 460m equatorial source tolerance, varying with latitude','Limits':{'CompressedBytes':30*1024**2,'Vertices':2000000,'GroupedGeometry':30000},'AcceptedForPilot':accepted,'NetworkBytes':0,'Semantics':'Generalized major-road skeleton only; OSM completeness varies, not street-level coverage'}
(WORK/'global-road-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2),flush=True)
if accepted:
 import shutil
 shutil.copy2(output,ROOT/'assets/geography/urban-roads.gslite')
 (ROOT/'assets/geography/urban-roads.manifest.json').write_text(json.dumps(report,indent=2)+'\n')
