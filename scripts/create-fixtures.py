# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Generate synthetic scenery only. Never read existing media."""
from pathlib import Path
import math
import struct
import subprocess
import zlib

def write_png(path, width, height, pixels):
    def chunk(kind, data):
        return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    scanlines=b''.join(b'\0'+bytes(pixels[y*width*3:(y+1)*width*3]) for y in range(height))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(scanlines))+chunk(b'IEND',b''))

folder = Path(__file__).resolve().parent.parent / '.local' / 'fixtures'
folder.mkdir(parents=True, exist_ok=True)
palettes = {'Paris': ((160, 204, 212),(53,103,100)), 'Moscow': ((206,184,214),(100,74,125)), 'Tokyo': ((242,192,168),(163,84,92)), 'New-York': ((153,194,218),(46,82,126))}
for place,(sky,land) in palettes.items():
    for i in range(1,4):
        pixels=bytearray(800*560*3)
        for y in range(560):
            mix=y/560
            for x in range(800):
                color=tuple(int(c*(1-mix)+245*mix) for c in sky)
                if (x-(590-i*30))**2+(y-120)**2<2500: color=(255,240,195)
                ridge=230+90*math.sin(x/110+i)+40*math.cos(x/55)
                if y>ridge: color=land
                if y>410+70*math.sin(x/130+i): color=tuple(int(c*.7) for c in land)
                pixels[(y*800+x)*3:(y*800+x)*3+3]=bytes(color)
        temp=folder/f'{place}-{i:02d}.png'
        write_png(temp,800,560,pixels)
        ffmpeg=folder.parent.parent/'tools/FFmpeg/ffmpeg.exe'
        subprocess.run([str(ffmpeg),'-nostdin','-v','error','-i',str(temp),'-q:v','2','-y',str(temp.with_suffix('.jpg'))],check=True)
        temp.unlink()
nested=folder/'nested'
nested.mkdir(exist_ok=True)
write_png(nested/'No-GPS.png',320,240,bytes((25,140,130))*320*240)
old=folder/'No-GPS.png'
if old.exists(): old.unlink()
