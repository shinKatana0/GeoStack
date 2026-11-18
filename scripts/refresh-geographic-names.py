# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Compact previously downloaded Natural Earth v5.1.2 GeoJSON, retaining source names and IDs.

Inputs: .local/geography-source/{cities,countries}.geojson from
https://github.com/nvkelso/natural-earth-vector/tree/v5.1.2/geojson
No downloads occur at build time or in the application.
"""
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
for kind, fields in {
    'cities': ['name', 'name_en', 'name_ru', 'ne_id', 'adm1name', 'latitude', 'longitude', 'scalerank'],
    'countries': ['NAME', 'NAME_EN', 'NAME_RU', 'NE_ID', 'LABEL_X', 'LABEL_Y', 'LABELRANK'],
}.items():
    data = json.loads((root / '.local/geography-source' / (kind + '.geojson')).read_text(encoding='utf-8'))
    for f in data['features']:
        props = f['properties']
        f['properties'] = {k: props.get(k, props.get(k.upper())) for k in fields}
    (root / 'assets/geography' / (kind + '.geojson')).write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
