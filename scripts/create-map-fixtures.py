# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Create tiny original raster tiles and intentionally invalid local archives for offline tests."""
import binascii
import json
from pathlib import Path
import sqlite3
import struct
import zlib
import shutil

root = Path(__file__).resolve().parent.parent / '.local/map-fixtures'
root.mkdir(parents=True, exist_ok=True)

def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', binascii.crc32(kind + data) & 0xffffffff)

# An original checkerboard, not scraped map imagery.
raw = b''.join(b'\0' + b''.join(bytes((22, 150, 117) if (x//32+y//32)%2 else (248, 193, 76)) for x in range(256)) for y in range(256))
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 256,256,8,2,0,0,0)) + chunk(b'IDAT',zlib.compress(raw)) + chunk(b'IEND',b'')
(root/'tile.png').write_bytes(png)
for name, encoding, payload in [('raster','png',png), ('vector','pbf',b'\x1f\x8bnot-raster'), ('wrong-encoding','png',b'invalid')]:
    path = root/(name+'.mbtiles')
    if path.exists():
        path.unlink()  # Only fixed synthetic files in the dedicated fixture directory.
    with sqlite3.connect(path) as db:
        db.execute('CREATE TABLE metadata (name TEXT, value TEXT)')
        db.execute('CREATE TABLE tiles (zoom_level INTEGER,tile_column INTEGER,tile_row INTEGER,tile_data BLOB,PRIMARY KEY(zoom_level,tile_column,tile_row))')
        db.executemany('INSERT INTO metadata VALUES (?,?)', [('name','GeoStack synthetic checkerboard'),('format',encoding),('bounds','-180,-85,180,85'),('minzoom','0'),('maxzoom','3'),('attribution','GeoStack synthetic fixture; original public-domain test artwork')])
        for z,x,y in [(0,0,0),(1,0,1),(3,7,4)]:
            db.execute('INSERT INTO tiles VALUES (?,?,?,?)',(z,x,y,payload))
(root/'corrupt.mbtiles').write_bytes(b'not SQLite')
clip = root.parent/'format-fixtures/Generated.MOV'
if clip.exists():
    shutil.copyfile(clip, root/'clip.mov')
print('Created synthetic MBTiles fixtures:', root)

# Deliberately expensive virtual tile table; the reader must interrupt it, not scan a billion rows.
slow=root/'slow-view.mbtiles'
if not slow.exists():
    with sqlite3.connect(slow) as db:
        db.execute("CREATE VIEW tiles AS WITH RECURSIVE sequence(value) AS (VALUES(0) UNION ALL SELECT value+1 FROM sequence WHERE value<1000000000) SELECT 22 AS zoom_level,value AS tile_column,value%64 AS tile_row,X'00' AS tile_data FROM sequence")
