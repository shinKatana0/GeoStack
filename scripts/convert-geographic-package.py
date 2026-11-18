# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Convert reviewed GeoJSON/GeoJSONSeq to indexed GeoStack geographic packages.

Development only. Standard library for normalized GeoJSON; optional Shapely for Overture WKB preparation.
The input must carry reviewed levels and parent relationships. Numeric ADM levels are never guessed.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import sqlite3
import uuid

APPLICATION_ID = 0x47534731  # GSG1

def unwrapped_bounds(geometry):
    polygons = [geometry['coordinates']] if geometry['type'] == 'Polygon' else geometry['coordinates']
    for polygon in polygons:
        points = polygon[0]
        unwrapped = [points[0][0]]
        for point in points[1:]:
            delta = (point[0] - unwrapped[-1] + 180) % 360 - 180
            unwrapped.append(unwrapped[-1] + delta)
        west, east = min(unwrapped), max(unwrapped)
        south, north = min(p[1] for p in points), max(p[1] for p in points)
        while west < -180:
            west += 360; east += 360
        while west > 180:
            west -= 360; east -= 360
        if east > 180:
            yield west, south, 180, north
            yield -180, south, east - 360, north
        else:
            yield west, south, east, north

def validate_geometry(geometry):
    if geometry.get('type') not in ('Polygon', 'MultiPolygon'):
        raise ValueError('Only polygonal geometry is accepted')
    polygons = [geometry['coordinates']] if geometry['type'] == 'Polygon' else geometry['coordinates']
    if not polygons:
        raise ValueError('Empty geometry')
    for polygon in polygons:
        if not polygon:
            raise ValueError('Empty polygon')
        for ring in polygon:
            if len(ring) < 4 or ring[0] != ring[-1]:
                raise ValueError('Open or degenerate ring')
            if any(len(p) != 2 or not all(math.isfinite(v) for v in p) or not -180 <= p[0] <= 180 or not -90 <= p[1] <= 90 for p in ring):
                raise ValueError('Invalid geographic coordinate')

def convert(input_path, manifest_path, output_path):
    if output_path.exists():
        raise FileExistsError('Output exists; choose a new filename for an update')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    required = ('Id','Name','Version','Source','SourceUrl','License','LicenseUrl','Attribution','Coverage','DataDate','Semantics')
    if any(not manifest.get(k) for k in required):
        raise ValueError('Complete provenance, licensing and semantic review are required')
    if input_path.suffix == '.geojsonseq':
        features = (json.loads(line.lstrip('\x1e')) for line in input_path.open(encoding='utf-8') if line.strip())
    else:
        features = iter(json.loads(input_path.read_text(encoding='utf-8'))['features'])
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = output_path.with_name(output_path.name + '.' + uuid.uuid4().hex + '.tmp')
    count = 0; part = 0; levels = set()
    db = sqlite3.connect(temporary)
    try:
        db.executescript(f'''PRAGMA application_id={APPLICATION_ID}; PRAGMA user_version=1;
        CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        CREATE TABLE features(fid INTEGER PRIMARY KEY,id TEXT UNIQUE NOT NULL,level TEXT NOT NULL,parent_id TEXT,country_id TEXT NOT NULL,source_name TEXT NOT NULL,name_en TEXT,name_ru TEXT,ancestors TEXT NOT NULL,semantics TEXT NOT NULL,geometry TEXT NOT NULL,warnings TEXT NOT NULL);
        CREATE TABLE bounds(part_id INTEGER PRIMARY KEY,feature_id INTEGER NOT NULL REFERENCES features(fid));
        CREATE VIRTUAL TABLE spatial_index USING rtree(part_id,min_x,max_x,min_y,max_y);
        CREATE INDEX feature_country ON features(country_id,level);''')
        for feature in features:
            p = feature['properties']; g = feature['geometry']; validate_geometry(g)
            if p['level'] not in ('country','region','city','district') or not p.get('id') or not p.get('name') or not p.get('country_id') or not p.get('semantics'):
                raise ValueError('Missing reviewed level, stable identifier, country or semantics')
            count += 1; levels.add(p['level'])
            payload = json.dumps(g, ensure_ascii=False, separators=(',', ':'))
            if len(payload.encode('utf-8')) > 8*1024*1024:
                raise ValueError('A single geometry exceeds the 8 MiB runtime limit; use a smaller source geometry or reviewed simplification')
            db.execute('INSERT INTO features VALUES (?,?,?,?,?,?,?,?,?,?,?,?)', (count,p['id'],p['level'],p.get('parent_id'),p['country_id'],p['name'],p.get('name_en'),p.get('name_ru'),json.dumps(p.get('ancestors',[]),separators=(',',':')),p['semantics'],payload,json.dumps(p.get('warnings',[]),separators=(',',':'))))
            for west,south,east,north in unwrapped_bounds(g):
                part += 1
                db.execute('INSERT INTO bounds VALUES (?,?)',(part,count))
                db.execute('INSERT INTO spatial_index VALUES (?,?,?,?,?)',(part,west,east,south,north))
        if count == 0:
            raise ValueError('No polygons')
        if db.execute('SELECT f.id FROM features f LEFT JOIN features c ON f.country_id=c.id AND c.level="country" WHERE c.id IS NULL LIMIT 1').fetchone():
            raise ValueError('Every feature must reference an included country polygon')
        manifest.update(SchemaVersion=1,FeatureCount=count,Levels=sorted(levels),InputSha256=hashlib.sha256(input_path.read_bytes()).hexdigest())
        db.execute('INSERT INTO metadata VALUES (?,?)',('manifest',json.dumps(manifest,ensure_ascii=False,sort_keys=True,separators=(',',':'))))
        db.commit()
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok' or db.execute("SELECT rtreecheck('spatial_index')").fetchone()[0] != 'ok':
            raise ValueError('Package integrity failure')
        db.close()
        os.link(temporary, output_path)  # Atomic, same-volume, refuses an existing output.
        return {'path':str(output_path),'features':count,'indexParts':part,'bytes':output_path.stat().st_size,'sha256':hashlib.sha256(output_path.read_bytes()).hexdigest()}
    finally:
        db.close()
        if temporary.exists():
            temporary.unlink()

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input',type=Path)
    parser.add_argument('manifest',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args()
    print(json.dumps(convert(args.input,args.manifest,args.output),indent=2))
