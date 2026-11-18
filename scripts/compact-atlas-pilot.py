# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Development-only cartographic area generalization; roads/water/boundaries stay unchanged.

Original optional packages remain available for comparison. No source archive is modified.
"""
from pathlib import Path
import gzip,hashlib,json,sys
ROOT=Path(__file__).resolve().parent.parent
sys.path[:0]=[str(ROOT/'offline-map-workspace/development'),str(ROOT/'.local/geographic-dev-pinned')]
from shapely.geometry import Polygon

source=ROOT/'offline-maps/atlas-pilot';output=ROOT/'offline-maps/atlas-pilot-compact';output.mkdir(exist_ok=True)
manifest=json.loads((source/'MANIFEST.json').read_text());results=[]
for entry in manifest['Packages']:
    original=(source/entry['File']).read_bytes();assert hashlib.sha256(original).hexdigest()==entry['SHA256']
    data=json.loads(gzip.decompress(original))
    for feature in data['Features']:
        if feature['Lod']==0 or feature['Kind'] not in ['park','urban']:continue
        tolerance=(16 if feature['Lod']==8 else 24)/4096/2**feature['Lod'];paths=[]
        for ring in feature['Paths']:
            if len(ring)<4:continue
            geometry=Polygon(ring)
            if geometry.area<tolerance*tolerance:continue
            simplified=geometry.simplify(tolerance,preserve_topology=True)
            if simplified.is_empty:continue
            paths.append([[round(x,9),round(y,9)] for x,y in simplified.exterior.coords])
        feature['Paths']=paths
    data['Features']=[f for f in data['Features'] if f['Paths']]
    data['Generalization']='Park/urban rings: 1 native pixel at LOD8, 1.5 at LOD10; sub-pixel islands/holes omitted. Roads, water, rivers and boundaries unchanged. Visual map only, never administrative lookup.'
    raw=json.dumps(data,separators=(',',':'),ensure_ascii=False).encode();compressed=gzip.compress(raw,compresslevel=9,mtime=0);(output/entry['File']).write_bytes(compressed)
    report=dict(entry);report.update(Bytes=len(compressed),ExpandedBytes=len(raw),SHA256=hashlib.sha256(compressed).hexdigest(),Features=len(data['Features']),Vertices=sum(len(p) for f in data['Features'] for p in f['Paths']),InputSHA256=entry['SHA256'],Generalization=data['Generalization']);results.append(report)
    print(entry['Id'],report['Bytes'],'bytes',report['Vertices'],'vertices',flush=True)
manifest['Packages']=results;manifest['Variant']='Compact cartographic areas';manifest['AdditionalDownloads']=0
(output/'MANIFEST.json').write_text(json.dumps(manifest,indent=2)+'\n')
