English | [Russian](BUILD.ru.md)

# Build and run GeoStack

<!-- section:requirements -->
## Requirements

Use Windows 11 x64, the .NET 10 SDK selected by `global.json`, PowerShell and Python 3.10 or newer. WPF requires Windows. NuGet restore needs network access unless the packages are already cached. The environment script keeps packages and CLI state in the repository's `.local` directory.

<!-- section:commands -->
## Compile and start

Run from the repository root:

```powershell
. ./scripts/environment.ps1
dotnet restore GeoStack.sln --ignore-failed-sources -p:NuGetAudit=false
dotnet build GeoStack.sln -c Release --no-restore
python scripts/check-public-tree.py --root . --report .local/public-audit.json
```

The compact world atlas is included; no map generation is needed to compile or view it. To start with a separate local profile:

```powershell
$demoProfile = Join-Path $PWD '.local/demo-profile'
dotnet run --project src/GeoStack.App -c Release --no-build -- --data $demoProfile
```

This command keeps the demo index and preferences separate from other profiles. Adding media and generating previews requires the tools below. Do not use personal files for transfer tests.

<!-- section:tests -->
## Media tools and tests

Supply ExifTool and FFmpeg separately. For a development run, the application looks for `tools/ExifTool/ExifTool.exe` and `tools/FFmpeg/ffmpeg.exe` under the repository root. Keep ExifTool's companion files with its executable. These tools are not included here; [the dependency inventory](release/dependencies.lock.json) records the versions and hashes used for the existing Windows package. Review each tool's license before obtaining or redistributing it.

After supplying the tools, run the full test script:

```powershell
./scripts/test.ps1
```

The script uses synthetic/licensed fixtures and may download missing samples. The GitHub workflow runs only the tool-independent source checks and transfer tests. Optional map generators need additional source inputs or Python packages; ordinary builds do not run them.

<!-- section:portable -->
## Portable packaging

Compiling and running from source is separate from producing a portable package. `scripts/build-portable.ps1` remains a release-engineering script and needs additional packaging inputs and local tools; it is not a complete public packaging procedure for this source-only snapshot.

Windows binary distribution still has unresolved tool/source and notice requirements. Do not upload a locally generated portable package. See [distribution status](docs/REDISTRIBUTION_STATUS.md).
