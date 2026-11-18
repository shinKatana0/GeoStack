# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Development-only bounded Protomaps archive cutouts -> locally styled PNG MBTiles.

Never requests an online tile server. Python packages and fonts are not runtime dependencies.
Install the pinned development wheels into offline-map-workspace/development first.
"""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import gzip
import hashlib
import io
import json
import math
from pathlib import Path
import shutil
import sqlite3
import sys
import threading
import urllib.request
import uuid

ROOT=Path(__file__).resolve().parent.parent
WORK=ROOT/'offline-map-workspace'
sys.path[:0]=[str(WORK/'development'),str(ROOT/'.local/geographic-dev-pinned')]
from pmtiles.reader import Reader
from mapbox_vector_tile import decode
from PIL import Image,ImageDraw,ImageFont

SOURCE='https://data.source.coop/protomaps/openstreetmap/v4.pmtiles'
CITIES=[('tokyo','Tokyo',35.6812,139.7671),('kyoto','Kyoto',34.9858,135.7588),('kanazawa','Kanazawa',36.5781,136.6477),('shanghai','Shanghai',31.2304,121.4737),('zurich','Zurich',47.3782,8.5402),('warsaw','Warsaw',52.2297,21.0122)]
ATTRIBUTION='OpenStreetMap contributors (ODbL); Natural Earth; Protomaps. openstreetmap.org/copyright'

class Archive:
    def __init__(self):
        self.cache=WORK/'ranges';self.cache.mkdir(parents=True,exist_ok=True)
        self.gate=threading.Lock();self.locks={};self.transferred=0
        identity=WORK/'source-identity.json'
        saved=json.loads(identity.read_text()) if identity.exists() else None
        if saved and saved['Source']!=SOURCE:raise RuntimeError('Use a separate workspace for a different source')
        self.identity=saved['ETag'] if saved else None
    def read(self,offset,length):
        if length>32*1024**2:raise RuntimeError('Refusing excessive contiguous source range')
        key=f'{offset}-{length}'
        with self.gate:lock=self.locks.setdefault(key,threading.Lock())
        with lock:
            path=self.cache/(key+'.bin')
            if path.exists():return path.read_bytes()
            with self.gate:
                if self.transferred+length>512*1024**2:raise RuntimeError('Explicit 512 MiB transfer budget exceeded')
                self.transferred+=length
            headers={'Range':f'bytes={offset}-{offset+length-1}'}
            if self.identity:headers['If-Match']=self.identity
            with urllib.request.urlopen(urllib.request.Request(SOURCE,headers=headers),timeout=90) as response:
                if response.status!=206:raise RuntimeError('Provider did not honor range; never download the planet')
                if not response.headers['Content-Range'].startswith(f'bytes {offset}-{offset+length-1}/'):raise RuntimeError('Wrong source range')
                etag=response.headers.get('ETag');data=response.read(length+1)
                with self.gate:
                    if self.identity and etag!=self.identity:raise RuntimeError('Source changed during extraction')
                    self.identity=etag
            if len(data)!=length:raise RuntimeError('Incomplete source range')
            with path.open('xb') as file:file.write(data)
            return data

def tile_xy(lat,lon,z):
    return (lon+180)/360*2**z,(1-math.asinh(math.tan(math.radians(lat)))/math.pi)/2*2**z

def render(data,dark=False,language="en-US"):
    layers=decode(gzip.decompress(data),default_options={'y_coord_down':True})
    image=Image.new('RGB',(512,512),('#24343a' if dark else '#f6f2e8'));draw=ImageDraw.Draw(image)
    font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',22)
    stats=Counter();labels=[]
    def geometry(feature,extent,fill=None,line=None,width=1):
        g=feature['geometry'];kind=g['type'];c=g['coordinates'];factor=512/extent
        def points(a):return [(round(p[0]*factor),round(p[1]*factor)) for p in a]
        polygons=[c] if kind=='Polygon' else c if kind=='MultiPolygon' else []
        for rings in polygons:
            if not rings:continue
            if len(rings)==1:draw.polygon(points(rings[0]),fill=fill)
            else:
                mask=Image.new('L',image.size);md=ImageDraw.Draw(mask);md.polygon(points(rings[0]),fill=255)
                for hole in rings[1:]:md.polygon(points(hole),fill=0)
                image.paste(fill,(0,0,512,512),mask)
        for a in [c] if kind=='LineString' else c if kind=='MultiLineString' else []:
            if len(a)>1:draw.line(points(a),fill=line,width=width,joint='curve')
    for name in ['earth','landuse','water','buildings','boundaries','roads']:
        layer=layers.get(name,{})
        for feature in layer.get('features',[]):
            stats[name]+=1;p=feature['properties'];kind=p.get('kind','');extent=layer['extent']
            if name=='earth':geometry(feature,extent,fill=('#24343a' if dark else '#f6f2e8'))
            elif name=='landuse':geometry(feature,extent,fill=('#2c4c3d' if dark else '#dce8c9') if kind in ['park','forest','wood','grass','nature_reserve','recreation_ground','golf_course','cemetery'] else ('#303f42' if dark else '#e9e4da'))
            elif name=='water':geometry(feature,extent,fill=('#152b38' if dark else '#a9d6e5'),line=('#152b38' if dark else '#a9d6e5'),width=3)
            elif name=='buildings':geometry(feature,extent,fill=('#435159' if dark else '#d4cdc1'))
            elif name=='boundaries':geometry(feature,extent,line=('#8d7d9b' if dark else '#bdabc7'),width=2)
            elif name=='roads':
                major=kind in ['highway','major_road'];rail=kind=='rail'
                width=7 if major else 3 if kind=='minor_road' else 2
                geometry(feature,extent,line=('#263336' if dark else '#bdb7ab') if not rail else ('#61757e' if dark else '#b1a7ab'),width=width+2)
                geometry(feature,extent,line=('#bba76f' if dark else '#efd18b') if major else ('#7a8e95' if dark else '#ffffff') if not rail else ('#61757e' if dark else '#ded6d9'),width=width)
    for name in ['places','water','roads']:
        layer=layers.get(name,{})
        for feature in layer.get('features',[]):
            p=feature['properties'];text=(p.get('name:ru') if language=='ru-RU' else None) or p.get('name:en') or p.get('name');g=feature['geometry']
            if not isinstance(text,str) or len(text)>35 or any(ord(c)>0x2e80 for c in text):continue
            if g['type']=='Point':point=g['coordinates']
            elif g['type']=='LineString' and len(g['coordinates'])>2:point=g['coordinates'][len(g['coordinates'])//2]
            else:continue
            x,y=[v*512/layer['extent'] for v in point]
            if 35<x<477 and 25<y<487:labels.append((0 if name=='places' else 1,x,y,text))
    occupied=[]
    for _,x,y,text in sorted(labels):
        box=draw.textbbox((x,y),text,font=font,anchor='mm');box=(box[0]-8,box[1]-6,box[2]+8,box[3]+6)
        if any(box[0]<b[2] and box[2]>b[0] and box[1]<b[3] and box[3]>b[1] for b in occupied):continue
        draw.text((x,y),text,font=font,fill=('#d0dee0' if dark else '#354851'),stroke_width=2,stroke_fill=('#24343a' if dark else '#fffdf7'),anchor='mm');occupied.append(box);stats['labels']+=1
    stream=io.BytesIO();image.save(stream,format='PNG',optimize=True)
    return stream.getvalue(),dict(stats)

def build(selected, context=False):
    if shutil.disk_usage(ROOT).free<2*1024**3:raise RuntimeError('At least 2 GiB free space required')
    output=ROOT/'offline-maps';output.mkdir(exist_ok=True);archive=Archive();reader=Reader(archive.read)
    metadata=reader.metadata();(WORK/'source-metadata.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')
    packages=[]
    for slug,name,lat,lon in CITIES:
        if selected and slug not in selected:continue
        suffix='-context' if context else ''
        path=output/(slug+suffix+'-city.mbtiles');report_path=WORK/(slug+suffix+'-report.json')
        if path.exists():
            if not report_path.exists():raise FileExistsError('Existing map has no matching preparation record: '+str(path))
            report=json.loads(report_path.read_text());assert report['Sha256']==hashlib.sha256(path.read_bytes()).hexdigest();packages.append(report);continue
        bounds=[lon-.6,lat-.5,lon+.6,lat+.5] if context else [lon-.075,lat-.055,lon+.075,lat+.055];tiles=[]
        minimum,maximum=(8,12) if context else (10,15)
        for z in range(minimum,maximum+1):
            x0,y1=tile_xy(bounds[1],bounds[0],z);x1,y0=tile_xy(bounds[3],bounds[2],z)
            tiles.extend((z,x,y) for x in range(int(x0),int(x1)+1) for y in range(int(y0),int(y1)+1))
        print(name,'planned tiles',len(tiles),flush=True)
        temporary=WORK/(slug+'-'+uuid.uuid4().hex+'.sqlite');db=sqlite3.connect(temporary)
        db.executescript('CREATE TABLE metadata(name TEXT PRIMARY KEY,value TEXT); CREATE TABLE tiles(zoom_level INTEGER,tile_column INTEGER,tile_row INTEGER,tile_data BLOB,PRIMARY KEY(zoom_level,tile_column,tile_row));')
        values={'name':'GeoStack '+name+(' regional context' if context else ' city streets'),'format':'png','scheme':'tms','minzoom':str(minimum),'maxzoom':str(maximum),'bounds':','.join(str(v) for v in bounds),'center':f'{lon},{lat},{min(13,maximum)}','attribution':ATTRIBUTION,'description':('Protomaps v4.14.3 / OSM 2026-03-31; genuine lower-detail regional context, native zooms 8-12; fixed light raster style' if context else 'Protomaps v4.14.3 / OSM 2026-03-31; locally rendered city-centre streets, water, buildings and available English labels; fixed light raster style'),'version':'1'}
        db.executemany('INSERT INTO metadata VALUES (?,?)',values.items());counts=Counter();tile_hashes=[]
        def prepare(tile):
            z,x,y=tile;source=reader.get(z,x,y)
            if source is None:raise RuntimeError('Required geographic source tile missing')
            png,stats=render(source)
            return tile,png,stats,hashlib.sha256(source).hexdigest()
        with ThreadPoolExecutor(max_workers=6) as pool:
            for i,(tile,png,stats,source_hash) in enumerate(pool.map(prepare,tiles)):
                z,x,y=tile;db.execute('INSERT INTO tiles VALUES (?,?,?,?)',(z,x,2**z-1-y,png));counts.update(stats);tile_hashes.append({'z':z,'x':x,'xyz_y':y,'sourceSha256':source_hash,'pngSha256':hashlib.sha256(png).hexdigest()})
                if i%50==0:print(name,i+1,'/',len(tiles),'network MiB',round(archive.transferred/1024**2,2),flush=True)
        db.commit();assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok';db.close()
        import os
        os.link(temporary,path)  # Atomic finalization without overwriting another dataset.
        report={'Id':slug+suffix,'Name':values['name'],'Area':name+(' regional context' if context else ' city centre'),'Center':[lon,lat],'Bounds':bounds,'MinZoom':minimum,'MaxZoom':maximum,'TileFormat':'png','TileCount':len(tiles),'Bytes':path.stat().st_size,'Sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'SourceUrl':SOURCE,'SourceEtag':archive.identity,'SourceVersion':metadata.get('version'),'SourceDataTime':metadata.get('planetiler:osm:osmosisreplicationtime'),'License':'OpenStreetMap ODbL Produced Work; Natural Earth public domain','Attribution':ATTRIBUTION,'RenderedFeatures':dict(counts),'SourceTileManifest':slug+suffix+'-source-tiles.json','RuntimeFormat':'Raster MBTiles; PMTiles is development input only'}
        report_path.write_text(json.dumps(report,indent=2),encoding='utf-8');(WORK/(slug+suffix+'-source-tiles.json')).write_text(json.dumps(tile_hashes,indent=2));packages.append(report)
        print(name,'complete',report['Bytes'],dict(counts),flush=True)
    for record in packages:
        record.update(File=record['Id']+'-city.mbtiles',SourceTileManifest='../offline-map-workspace/'+record['Id']+'-source-tiles.json',TileRowConvention='TMS')
    (output/('CONTEXT_MANIFEST.json' if context else 'MANIFEST.json')).write_text(json.dumps({'Packages':packages,'Source':SOURCE,'TransferredBytesThisRun':archive.transferred,'SourceDataBytesCached':sum(p.stat().st_size for p in archive.cache.glob('*.bin')),'Rasterization':'GeoStack original Pillow style; 512-pixel PNGs; local font not redistributed','CoverageLimit':'Regional context boxes (1.2 longitude x 1 latitude degrees), real lower-detail source tiles; fixed raster labels' if context else 'City-centre bounding boxes only; labels fixed in raster; no whole-country or live vector support'},indent=2),encoding='utf-8')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--cities',nargs='*');parser.add_argument('--context',action='store_true');args=parser.parse_args();build(args.cities,args.context)
