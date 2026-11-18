# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Stage the pinned official runtime packages when Schannel is unavailable in a sandbox."""
from pathlib import Path
import urllib.request

folder = Path(__file__).resolve().parent.parent / '.local' / 'nuget-feed'
folder.mkdir(parents=True, exist_ok=True)
for name in ('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.aspnetcore.app.runtime.win-x64', 'microsoft.netcore.app.host.win-x64'):
    target = folder / (name + '.10.0.14.nupkg')
    if not target.exists():
        urllib.request.urlretrieve('https://api.nuget.org/v3-flatcontainer/' + name + '/10.0.14/' + target.name, target)
    print(target.name)
