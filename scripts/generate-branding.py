# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Render the original SVG's path subset into transparent PNGs and a Windows ICO.

Uses only Python's standard library. Curves are flattened before scanline filling;
box supersampling preserves the silhouette at small Explorer/taskbar sizes.
"""
from pathlib import Path
import math
import re
import struct
import xml.etree.ElementTree as ET
import zlib

ROOT = Path(__file__).resolve().parent.parent / 'assets/branding'
NS = {'s': 'http://www.w3.org/2000/svg'}

def flatten(data):
    tokens = re.findall(r'[A-Za-z]|[-+]?(?:\d*\.\d+|\d+)', data)
    points = []
    index = 0
    current = (0, 0)
    while index < len(tokens):
        command = tokens[index]; index += 1
        count = {'M':2,'L':2,'Q':4,'C':6,'A':7,'Z':0}[command]
        args = list(map(float,tokens[index:index+count])); index += count
        if command in ('M','L'): current=tuple(args); points.append(current)
        elif command in ('Q','C'):
            start=current; end=tuple(args[-2:])
            for step in range(1,65):
                t=step/64; u=1-t
                if command=='Q': p=tuple(u*u*start[a]+2*u*t*args[a]+t*t*end[a] for a in (0,1))
                else: p=tuple(u**3*start[a]+3*u*u*t*args[a]+3*u*t*t*args[a+2]+t**3*end[a] for a in (0,1))
                points.append(p)
            current=end
        elif command=='A':
            # The master uses circular diameter arcs, each comprising half a circle.
            rx,ry,rotation,large,sweep,x,y=args
            end=(x,y); cx=(current[0]+x)/2; cy=(current[1]+y)/2
            assert rx==ry and rotation==0 and abs(math.dist(current,end)-2*rx)<.001
            angle=math.atan2(current[1]-cy,current[0]-cx); delta=math.pi if sweep else -math.pi
            for step in range(1,65):
                a=angle+delta*step/64; points.append((cx+rx*math.cos(a),cy+ry*math.sin(a)))
            current=end
        elif command=='Z': points.append(points[0])
    return points

def png(width,height,pixels):
    def chunk(kind,data): return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    rows=b''.join(b'\0'+pixels[y*width*4:(y+1)*width*4] for y in range(height))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b'')

def render(svg,size):
    factor=4 if size<=256 else 2; dimension=size*factor; scale=dimension/256
    pixels=bytearray(dimension*dimension*4)
    gradients={e.attrib['id']:e for e in svg.findall('.//s:linearGradient',NS)}
    for path in svg.findall('s:path',NS):
        points=[(x*scale,y*scale) for x,y in flatten(path.attrib['d'])]
        xs=[p[0] for p in points]; ys=[p[1] for p in points]
        x0,x1,y0,y1=min(xs),max(xs),min(ys),max(ys)
        fill=path.attrib['fill']; gradient=gradients.get(fill[5:-1]) if fill.startswith('url(') else None
        colors=[tuple(bytes.fromhex(s.attrib['stop-color'][1:])) for s in gradient] if gradient is not None else [tuple(bytes.fromhex(fill[1:]))]
        for y in range(max(0,math.floor(min(ys))), min(dimension,math.ceil(max(ys)))):
            line=y+.5; intersections=[]
            for a,b in zip(points,points[1:]):
                if (a[1]<=line<b[1]) or (b[1]<=line<a[1]): intersections.append(a[0]+(line-a[1])*(b[0]-a[0])/(b[1]-a[1]))
            intersections.sort()
            for left,right in zip(intersections[::2],intersections[1::2]):
                for x in range(max(0,math.ceil(left-.5)),min(dimension,math.ceil(right-.5))):
                    if gradient is None: color=colors[0]
                    else:
                        u=(x+.5-x0)/(x1-x0); v=(line-y0)/(y1-y0)
                        t=(u+v)/2 if gradient.attrib['x2']=='1' else v; t=max(0,min(1,t))
                        color=tuple(round(a+(b-a)*t) for a,b in zip(*colors))
                    offset=(y*dimension+x)*4; pixels[offset:offset+4]=bytes((*color,255))
    result=bytearray(size*size*4)
    for y in range(size):
        for x in range(size):
            sums=[0,0,0]; count=0
            for yy in range(factor):
                for xx in range(factor):
                    offset=((y*factor+yy)*dimension+x*factor+xx)*4
                    if pixels[offset+3]:
                        count+=1
                        for c in range(3): sums[c]+=pixels[offset+c]
            if count:
                result[(y*size+x)*4:(y*size+x)*4+4]=bytes((*[round(s/count) for s in sums],round(255*count/(factor*factor))))
    return png(size,size,bytes(result))

def main():
    svg=ET.parse(ROOT/'GeoStack.svg').getroot(); sizes=[16,24,32,48,64,128,256]
    frames=[render(svg,s) for s in sizes]; entries=[]; offset=6+16*len(sizes)
    for size,data in zip(sizes,frames):
        (ROOT/f'GeoStack-{size}.png').write_bytes(data)
        entries.append(struct.pack('<BBBBHHII',size%256,size%256,0,0,1,32,len(data),offset)); offset+=len(data)
    (ROOT/'GeoStack.ico').write_bytes(struct.pack('<HHH',0,1,len(sizes))+b''.join(entries)+b''.join(frames))
    (ROOT/'GeoStack-1024.png').write_bytes(render(svg,1024))
    print('Generated seven ICO sizes and transparent PNG exports from GeoStack.svg')

if __name__=='__main__': main()
