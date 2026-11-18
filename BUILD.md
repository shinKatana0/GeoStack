English | [Russian](BUILD.ru.md)

# Build GeoStack

<!-- section:requirements -->
## Requirements

Use Windows 11 x64, the .NET 10 SDK selected by `global.json`, PowerShell and Python 3.10 or newer. WPF requires Windows. Restore uses NuGet; compilation and ordinary application browsing do not upload media. Use the repository-local environment. Do not change global Git configuration or Windows security settings.

<!-- section:commands -->
## Compile source

From the repository root:

```powershell
. ./scripts/environment.ps1
dotnet restore GeoStack.sln --ignore-failed-sources -p:NuGetAudit=false
dotnet build GeoStack.sln -c Release --no-restore
python scripts/check-public-tree.py --root . --report .local/public-audit.json
```

The source includes pinned compact geographic inputs and notices. Full-world high-density archives and optional city maps are not needed to compile. A deterministic compiler setting is not proof of independent byte-identical builds.

<!-- section:tests -->
## Media tools and tests

The full suite needs separately obtained ExifTool/Perl and FFmpeg in `tools/ExifTool` and `tools/FFmpeg`, plus small synthetic/licensed fixtures. These executables are deliberately not published here. Preserve their licenses and verify exact provenance; never use personal media for move tests.

```powershell
./scripts/test.ps1
```

Fixture scripts may explicitly fetch licensed samples if absent. Optional map regeneration uses the documented Python requirements and pinned source inputs; it is not part of normal builds. Do not regenerate large atlas/benchmark data.

<!-- section:portable -->
## Local portable builds

After supplying verified local tools:

```powershell
./scripts/build-portable.ps1 -OutputFolder dist/GeoStack-local-review-win-x64
```

This produces a local self-contained Windows review only. It must not overwrite production. Exact binary-source/notice obligations remain unresolved, so do not upload the output or publish it through CI. See [compliance](docs/REDISTRIBUTION_STATUS.md).
