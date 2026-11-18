# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Development-only bounded extraction from Overture's official Azure GeoParquet mirror.

Uses HTTP ranges and Parquet bbox columns; never downloads an entire planet archive.
Requires the pinned development requirements, never used by the production application.
"""
import argparse
from collections import OrderedDict
import hashlib
import io
import json
from pathlib import Path
import sys
import urllib.request

root=Path(__file__).resolve().parent.parent
development=root/'.local/geographic-dev-pinned'
if development.exists():sys.path.insert(0,str(development))
import pyarrow.compute as pc
import pyarrow.parquet as pq
from shapely import from_wkb, to_geojson

class RangeFile(io.RawIOBase):
    downloaded=0
    budget=512*1024*1024
    def __init__(self,url,cache):
        super().__init__();self.url=url;self.position=0;self.cache=cache;self.prefix=hashlib.sha256(url.encode()).hexdigest()
        request=urllib.request.Request(url,headers={'Range':'bytes=0-7'})
        with urllib.request.urlopen(request,timeout=45) as response:
            if response.status!=206:raise RuntimeError('Provider did not honor an HTTP range')
            self.length=int(response.headers['Content-Range'].split('/')[-1])
    def readable(self):return True
    def seekable(self):return True
    def tell(self):return self.position
    def seek(self,offset,whence=0):
        self.position=offset if whence==0 else self.position+offset if whence==1 else self.length+offset
        return self.position
    def read(self,size=-1):
        size=min(self.length-self.position,size if size>=0 else self.length-self.position)
        if size<=0:return b''
        if size>128*1024*1024:raise RuntimeError('Refusing an excessive contiguous remote read')
        path=self.cache/f'{self.prefix}-{self.position}-{size}.bin'
        if path.exists():data=path.read_bytes()
        else:
            if RangeFile.downloaded+size>RangeFile.budget:raise RuntimeError('Extraction exceeded its explicit network budget')
            request=urllib.request.Request(self.url,headers={'Range':f'bytes={self.position}-{self.position+size-1}'})
            with urllib.request.urlopen(request,timeout=90) as response:
                if response.status!=206:raise RuntimeError('Provider did not honor an HTTP range')
                data=response.read()
            if len(data)!=size:raise RuntimeError('Incomplete range response')
            path.write_bytes(data);RangeFile.downloaded+=len(data)
        self.position+=len(data);return data

def extract(release,kind,targets,output):
    cache=root/'.local/geographic-sources/ranges';cache.mkdir(parents=True,exist_ok=True)
    index_path=cache/(release+'-collections.parquet')
    if not index_path.exists():
        index_path.write_bytes(urllib.request.urlopen(f'https://stac.overturemaps.org/{release}/collections.parquet',timeout=45).read())
    index=pq.read_table(index_path); files={}
    for target in targets:
        west,south,east,north=target['bbox']
        filtered=index.filter((pc.field('collection')==kind)&(pc.field('type')=='Feature')&(pc.field('bbox','xmin')<=east)&(pc.field('bbox','xmax')>=west)&(pc.field('bbox','ymin')<=north)&(pc.field('bbox','ymax')>=south))
        for asset in filtered['assets'].to_pylist():
            path=asset['aws']['alternate']['s3']['href'].split('/release/',1)[1]
            files.setdefault('https://overturemapswestus2.blob.core.windows.net/release/'+path,[]).append(target)
    features={};reports=[]
    for number,(url,areas) in enumerate(sorted(files.items())):
        print(f'Inspecting {kind} file {number+1}/{len(files)}',flush=True)
        remote=RangeFile(url,cache);parquet=pq.ParquetFile(remote)
        for group in range(parquet.metadata.num_row_groups):
            metadata=parquet.metadata.row_group(group)
            statistics={metadata.column(c).path_in_schema:metadata.column(c).statistics for c in range(metadata.num_columns)}
            def may_intersect(target):
                west,south,east,north=target['bbox']
                for column,limit,minimum in [('bbox.xmin',east,True),('bbox.xmax',west,False),('bbox.ymin',north,True),('bbox.ymax',south,False)]:
                    statistic=statistics.get(column)
                    if statistic is not None and statistic.has_min_max and ((statistic.min>limit) if minimum else (statistic.max<limit)):
                        return False
                return True
            if not any(may_intersect(target) for target in areas):continue
            # Reading only bbox first avoids loading geometry for unrelated row groups.
            boxes=parquet.read_row_group(group,columns=['bbox'],use_threads=False)
            mask=None
            for target in areas:
                west,south,east,north=target['bbox']
                current=(pc.field('bbox','xmin')<=east)&(pc.field('bbox','xmax')>=west)&(pc.field('bbox','ymin')<=north)&(pc.field('bbox','ymax')>=south)
                mask=current if mask is None else mask|current
            if boxes.filter(mask).num_rows==0:continue
            table=parquet.read_row_group(group,use_threads=False).filter(mask)
            for record in table.to_pylist():
                geometry=json.loads(to_geojson(from_wkb(record.pop('geometry'))))
                feature={'type':'Feature','properties':record,'geometry':geometry}
                features[record['id']]=feature
            print(f'  row group {group}: {table.num_rows} selected; {RangeFile.downloaded/1048576:.1f} MiB transferred',flush=True)
        reports.append({'url':url,'rowGroups':parquet.metadata.num_row_groups,'remoteBytes':remote.length})
        remote.close()
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('w',encoding='utf-8') as file:
        for identifier in sorted(features):file.write(json.dumps(features[identifier],ensure_ascii=False,separators=(',',':'),default=lambda value:value.isoformat())+'\n')
    report={'source':'Overture Maps official Azure mirror','release':release,'type':kind,'targets':targets,'features':len(features),'transferredBytes':RangeFile.downloaded,'files':reports,'sha256':hashlib.sha256(output.read_bytes()).hexdigest()}
    output.with_suffix('.provenance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='files'},indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release',default='2026-10-21.0')
    parser.add_argument('--type',choices=['division_area','division'],default='division_area')
    parser.add_argument('--targets',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();extract(args.release,args.type,json.loads(args.targets.read_text(encoding='utf-8')),args.output)
