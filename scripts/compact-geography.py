# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Remove unused properties from the bundled Natural Earth data without moving vertices."""
from pathlib import Path
import json

root=Path(__file__).resolve().parent.parent/'assets/geography'
fields={'countries':['NE_ID','NAME','NAME_EN','NAME_RU','LABEL_X','LABEL_Y','LABELRANK'], 'cities':['ne_id','name','name_en','name_ru','adm1name','latitude','longitude','scalerank']}
for name,keep in fields.items():
    path=root/(name+'.geojson'); original=path.stat().st_size; data=json.loads(path.read_text(encoding='utf-8'))
    for feature in data['features']: feature['properties']={key:feature['properties'].get(key) for key in keep}
    path.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(name,original,'->',path.stat().st_size,'bytes;',len(data['features']),'features')
