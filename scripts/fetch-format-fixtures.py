# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Fetch the licensed libheif example; keep all large fixtures outside Git."""
import hashlib
from pathlib import Path
import urllib.request

root=Path(__file__).resolve().parent.parent
folder=root/'.local/format-fixtures'; folder.mkdir(parents=True,exist_ok=True)
sources={
 'libheif-example.heic': ('https://raw.githubusercontent.com/strukturag/libheif/v1.20.2/examples/example.heic','7f8b363e4936c0666a25f64f3a92fda10bd8e5453be4592530b65a55dd98f3f2'),
 'libheif-COPYING.txt': ('https://raw.githubusercontent.com/strukturag/libheif/v1.20.2/examples/COPYING',None),
 'exiftool-QuickTime.mov': ('https://raw.githubusercontent.com/exiftool/exiftool/13.59/t/images/QuickTime.mov',None),
 'exiftool-LICENSE.txt': ('https://raw.githubusercontent.com/exiftool/exiftool/13.59/LICENSE',None)
}
rawbase='https://github.com/tellodaniel/revelraw-sample-raw-files/releases/download/v1/'
sources.update({
 'Ricoh-GR-III.DNG': (rawbase+'Ricoh-GR-III.DNG','05513ee72f7cac4165534c9f64995412084f414a033c80c473c79fd94154292e'),
 'Nikon-Z6.NEF': (rawbase+'Nikon-Z6.NEF','8ca94584578b35a422744c1a610174ce37df0691588edfdecaec3111b3fbbb02'),
 'RAW-README.md': ('https://raw.githubusercontent.com/tellodaniel/revelraw-sample-raw-files/main/README.md',None)
})
for name,(url,expected) in sources.items():
    path=folder/name
    if not path.exists(): urllib.request.urlretrieve(url,path)
    if expected and hashlib.sha256(path.read_bytes()).hexdigest()!=expected: raise ValueError('Fixture checksum mismatch: '+name)
    print(name)
