# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Small structural fixtures; geographic correctness is tested against the optional real package."""
import gzip
import json
import sqlite3
from pathlib import Path

root = Path(__file__).resolve().parent.parent / '.local' / 'global-atlas-fixtures'
root.mkdir(parents=True, exist_ok=True)
for name in ['valid', 'missing-coordinate', 'missing-content', 'unsupported']:
    path = root / (name + '.gsvector')
    if path.exists():
        path.unlink()
    with sqlite3.connect(path) as db:
        db.executescript('CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT); CREATE TABLE contents(id INTEGER PRIMARY KEY,data BLOB); CREATE TABLE tiles(z INTEGER,x INTEGER,y INTEGER,content INTEGER,PRIMARY KEY(z,x,y)) WITHOUT ROWID;')
        manifest = dict(Schema=1, Format='GeoStackGlobalVector', MinZoom=0, MaxZoom=1,
                        Bounds=[-180, -85.05112878, 180, 85.05112878], Source='Synthetic structural fixture',
                        SourceETag='fixture', DataDate='2026-11-15', Attribution='GeoStack test',
                        Compression='gzip', TileType='MVT', TileCount=5)
        if name == 'unsupported':
            manifest['TileType'] = 'PNG'
        db.execute('INSERT INTO metadata VALUES(?,?)', ('manifest', json.dumps(manifest)))
        db.execute('INSERT INTO contents VALUES(1,?)', (gzip.compress(b''),))
        for z in range(2):
            for x in range(1 << z):
                for y in range(1 << z):
                    if name == 'missing-coordinate' and (z, x, y) == (1, 1, 1):
                        continue
                    db.execute('INSERT INTO tiles VALUES(?,?,?,?)', (z, x, y, 2 if name == 'missing-content' else 1))
(root / 'corrupt.gsvector').write_bytes(b'Invalid SQLite database')
