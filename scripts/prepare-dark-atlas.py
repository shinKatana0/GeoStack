# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Render genuine dark city detail from existing cached OSM vectors. No network access.

Estimated output: at most twice the two existing light city archives (~112 MiB).
Temporary space: one output archive plus PNG/source buffers. Require 1 GiB free.
Original raster files are read-only and are never replaced.
"""
from pathlib import Path
import concurrent.futures,gzip,hashlib,importlib.util,json,shutil,sqlite3,sys

ROOT=Path(__file__).resolve().parent.parent
sys.path[:0]=[str(ROOT/'offline-map-workspace/development'),str(ROOT/'.local/geographic-dev-pinned')]
spec=importlib.util.spec_from_file_location('city',ROOT/'scripts/prepare-city-maps.py');city=importlib.util.module_from_spec(spec);spec.loader.exec_module(city)
output=ROOT/'offline-maps/atlas-pilot-dark';output.mkdir(exist_ok=True)
if shutil.disk_usage(ROOT).free<1024**3:raise RuntimeError('Require 1 GiB free for local dark rendering')
cache=ROOT/'offline-map-workspace/ranges'
def read(offset,length):
    path=cache/f'{offset}-{length}.bin'
    if not path.exists():raise RuntimeError('Cached source missing; downloading is forbidden for this conversion')
    return path.read_bytes()
reader=city.Reader(read);reports=[]
for slug in ['zurich','warsaw']:
    original=ROOT/'offline-maps'/f'{slug}-city.mbtiles'
    with sqlite3.connect(f'file:{original.as_posix()}?mode=ro',uri=True) as source:
        metadata=dict(source.execute('SELECT name,value FROM metadata'))
        tiles=list(source.execute('SELECT zoom_level,tile_column,tile_row FROM tiles ORDER BY zoom_level,tile_column,tile_row'))
    for language in ['en-US','ru-RU']:
        path=output/f'{slug}-dark-{language}.mbtiles'
        if path.exists():raise FileExistsError('Never overwrite an existing generated package: '+str(path))
        temporary=path.with_suffix('.partial')
        db=sqlite3.connect(temporary);db.executescript('CREATE TABLE metadata(name TEXT PRIMARY KEY,value TEXT);CREATE TABLE tiles(zoom_level INTEGER,tile_column INTEGER,tile_row INTEGER,tile_data BLOB,PRIMARY KEY(zoom_level,tile_column,tile_row));')
        values=dict(metadata);values.update(name=f'GeoStack {slug.title()} dark city {language}',description='Genuine cached OSM geometry re-rendered with classified dark styles and source-language labels; not image inversion',gs_theme='Dark',gs_language=language)
        db.executemany('INSERT INTO metadata VALUES (?,?)',values.items());source_hashes=[];features={}
        def prepare(t):
            z,x,row=t;raw=reader.get(z,x,2**z-1-row)
            if raw is None:raise RuntimeError('Missing cached source tile')
            png,counts=city.render(raw,dark=True,language=language)
            return t,png,counts,hashlib.sha256(raw).hexdigest()
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            for i,(t,png,counts,checksum) in enumerate(pool.map(prepare,tiles)):
                db.execute('INSERT INTO tiles VALUES (?,?,?,?)',(*t,png));source_hashes.append({'Tile':t,'SourceSHA256':checksum})
                for kind,count in counts.items():features[kind]=features.get(kind,0)+count
                if i%50==0:print(slug,language,i+1,'/',len(tiles),flush=True)
        db.commit();assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok';db.close();temporary.rename(path)
        report={'File':path.name,'Theme':'Dark','Language':language,'Bytes':path.stat().st_size,'SHA256':hashlib.sha256(path.read_bytes()).hexdigest(),'TileCount':len(tiles),'Bounds':metadata['bounds'],'MinZoom':metadata['minzoom'],'MaxZoom':metadata['maxzoom'],'Source':'Existing Protomaps 4.14.3 / OSM 2026-03-31 cache; zero downloads','License':metadata['attribution'],'OriginalRasterSHA256':hashlib.sha256(original.read_bytes()).hexdigest(),'FeaturesRendered':features}
        reports.append(report);(output/'MANIFEST.json').write_text(json.dumps({'Packages':reports,'NetworkBytes':0,'Technique':'Feature-level dark cartography, not raster inversion'},indent=2)+'\n');(ROOT/'offline-map-workspace'/f'{slug}-dark-{language}-source.json').write_text(json.dumps(source_hashes,indent=2)+'\n')
        print(json.dumps(report),flush=True)
