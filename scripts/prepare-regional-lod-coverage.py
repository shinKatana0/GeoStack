# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Bind per-LOD coverage to existing compact regional geometry and verified source-tile manifests. No downloads."""
from pathlib import Path
import gzip,hashlib,json
ROOT=Path(__file__).resolve().parents[1]
result={'Schema':1,'Regions':{}}
for name in ['warsaw','zurich']:
    package=ROOT/'offline-maps/atlas-pilot-compact'/(name+'.gsatlas')
    source=ROOT/'offline-map-workspace/atlas-pilot'/(name+'-source-tiles.json')
    raw=package.read_bytes();data=json.loads(gzip.decompress(raw));tiles=json.loads(source.read_text(encoding='utf-8-sig'))
    levels=sorted({f['Lod'] for f in data['Features']});bounds={};counts={}
    for lod in levels:
        cells={(int(key.split('/')[1]),int(key.split('/')[2])) for key in tiles if int(key.split('/')[0])==lod}
        if not cells:raise ValueError('Source coverage proof missing')
        # These retained source manifests describe complete rectangles. Refuse to fill unknown holes.
        left=min(x for x,y in cells);right=max(x for x,y in cells);top=min(y for x,y in cells);bottom=max(y for x,y in cells)
        if len(cells)!=(right-left+1)*(bottom-top+1):raise ValueError('Nonrectangular source coverage needs an explicit mask')
        n=2**lod;bounds[str(lod)]=[left/n,top/n,(right+1)/n,(bottom+1)/n];counts[str(lod)]=len(cells)
    result['Regions'][name]={'PackageSHA256':hashlib.sha256(raw).hexdigest().upper(),'SourceManifestSHA256':hashlib.sha256(source.read_bytes()).hexdigest().upper(),'TileCounts':counts,'Bounds':bounds}
path=ROOT/'assets/geography/atlas-lod-coverage.json';path.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print('Verified regional LOD coverage:',path.stat().st_size,'bytes; no source or archive changed')
