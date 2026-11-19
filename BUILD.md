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

The Windows RC4 reproduction inputs and exact media-tool lock are described in [Windows release materials](docs/WINDOWS_RELEASE.md). Download the pinned compiler-input archive from the official release. This archive preserves the original build revision without importing private Git history. From the public source root, the following command verifies every compiler input and all corresponding public product sources; add --build to compile into a separate experimental directory. It never replaces the release ZIP.

```powershell
python scripts/reproduce-windows-rc4.py --inputs GeoStack-0.5.5-RC4-build-sources.zip --output .local/rc4-reproduction
```


```powershell
./scripts/build-portable.ps1 -RC4InputsArchive GeoStack-0.5.5-RC4-build-sources.zip -RC4ReferenceArchive GeoStack-0.5.5-windows-x64.zip -OutputFolder dist/rc4-reproduction -Channel Production -SkipTests
```
