# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
param([switch]$SkipTests,[string]$OutputFolder='dist/GeoStack-portable-win-x64',[switch]$KeepUnacceptedCandidate,[ValidateSet('Preview','Production')][string]$Channel='Preview',[string]$MediaToolsDirectory='tools',[string]$RC4InputsArchive,[string]$RC4ReferenceArchive)
. "$PSScriptRoot/environment.ps1"
if($RC4InputsArchive -or $RC4ReferenceArchive){
    if(!$RC4InputsArchive -or !$RC4ReferenceArchive -or $Channel -ne 'Production'){throw 'RC4 reproduction requires both pinned archives and Production channel'}
    python "$PSScriptRoot/assemble-windows-rc4.py" --inputs $RC4InputsArchive --reference $RC4ReferenceArchive --output (Join-Path $repo $OutputFolder)
    if($LASTEXITCODE -ne 0){throw 'RC4 source/runtime correspondence failed; original release preserved'}
    if(!$SkipTests){
        $priorExecutable=$env:GEOSTACK_EXE
        try{$env:GEOSTACK_EXE=Join-Path $repo "$OutputFolder/GeoStack.exe";& "$PSScriptRoot/test.ps1"}
        finally{$env:GEOSTACK_EXE=$priorExecutable}
    }
    return
}
$mediaTools=[IO.Path]::GetFullPath((Join-Path $repo $MediaToolsDirectory))
if(!$mediaTools.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Media tools must be explicitly staged inside this checkout'}
if($MediaToolsDirectory -ne 'tools' -and (!(Test-Path "$mediaTools/ExifTool/ExifTool.exe") -or !(Test-Path "$mediaTools/FFmpeg/ffmpeg.exe"))){throw 'Explicit staged media tools are incomplete'}
$finalDestination=[IO.Path]::GetFullPath((Join-Path $repo $OutputFolder))
$distributionPrefix=[IO.Path]::GetFullPath((Join-Path $repo 'dist')).TrimEnd('\')+'\'
if(!$finalDestination.StartsWith($distributionPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Portable destination must be a named folder inside repository dist'}
$productionDestination=Join-Path $repo 'dist/GeoStack-portable-win-x64'
if($finalDestination.Equals($productionDestination,[StringComparison]::OrdinalIgnoreCase)){throw 'Build into a separate named staging/candidate folder. Deploy only through the validated production-package.py promotion gate.'}
if($KeepUnacceptedCandidate -and $finalDestination.Equals($productionDestination,[StringComparison]::OrdinalIgnoreCase)){throw 'Unaccepted candidates must never replace production'}
python "$PSScriptRoot/update-resources.py"
if ($LASTEXITCODE -ne 0) { throw 'Resource generation failed' }
& "$PSScriptRoot/generate-branding.ps1"
python "$PSScriptRoot/fetch-runtime.py"
if ($LASTEXITCODE -ne 0) { throw 'Runtime package acquisition failed' }

# Build outputs and local tools are deliberately excluded from Git.
if (!(Test-Path "$mediaTools/ExifTool/ExifTool.exe")) {
    $installedExif = (Get-Command exiftool -ErrorAction Stop).Source
    New-Item -ItemType Directory -Force "$mediaTools/ExifTool" | Out-Null
    Copy-Item -Path (Join-Path (Split-Path $installedExif) '*') -Destination "$mediaTools/ExifTool" -Recurse -Force
}
if (!(Test-Path "$mediaTools/FFmpeg/ffmpeg.exe")) {
    New-Item -ItemType Directory -Force "$mediaTools/FFmpeg" | Out-Null
    Copy-Item -LiteralPath (Get-Command ffmpeg -ErrorAction Stop).Source -Destination "$mediaTools/FFmpeg/ffmpeg.exe"
}
dotnet restore GeoStack.sln --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
# Never republish over a used portable directory: indexes, settings and caches are private.
$destination = Join-Path $repo ('.local/publish-' + [Guid]::NewGuid().ToString('N'))
dotnet publish src/GeoStack.App/GeoStack.App.csproj -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 -p:GeoStackChannel=$Channel -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$repo=/_/" -p:NuGetAudit=false -o $destination --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
New-Item -ItemType Directory -Force "$destination/tools/ExifTool", "$destination/tools/FFmpeg", "$destination/docs" | Out-Null
Copy-Item "$mediaTools/ExifTool/*" "$destination/tools/ExifTool" -Recurse -Force
foreach ($installerFile in @('unins000.exe', 'unins000.dat')) {
    $unneeded = Join-Path "$destination/tools/ExifTool/exiftool_files" $installerFile
    if (Test-Path -LiteralPath $unneeded) { Remove-Item -LiteralPath $unneeded }
}
Copy-Item "$mediaTools/FFmpeg/ffmpeg.exe" "$destination/tools/FFmpeg/ffmpeg.exe" -Force
& "$mediaTools/FFmpeg/ffmpeg.exe" -version 2>&1 | Out-File "$destination/tools/FFmpeg/build-info.txt" -Encoding utf8
# Ship user guidance, not engineering screenshots, traces, test reports or development fixtures.
Copy-Item "$repo/docs/PORTABLE_README.md" "$destination/README.md" -Force
Copy-Item "$repo/THIRD_PARTY_NOTICES.md" "$destination/THIRD_PARTY_NOTICES.md" -Force
Copy-Item "$repo/LICENSE" "$destination/LICENSE" -Force
foreach ($guide in @('PORTABLE.md','OFFLINE_MAPS.md','GEOGRAPHIC_ORGANIZATION.md','GEOGRAPHIC_DATASETS.md','MAP_SOURCES_AND_LICENSES.md','REDISTRIBUTION_STATUS.md','SMART_MAP_DOWNLOADS.md','MAP_DOWNLOAD_PROVIDERS.md')) {
    Copy-Item (Join-Path "$repo/docs" $guide) "$destination/docs" -Force
}
Copy-Item "$repo/docs/PORTABLE.md" "$destination/START-HERE.txt" -Force
New-Item -ItemType Directory -Force "$destination/branding" | Out-Null
Copy-Item "$repo/assets/branding/GeoStack.ico", "$repo/assets/branding/GeoStack-256.png" "$destination/branding" -Force
# Debug symbols are useful in development but unnecessary for the portable runtime.
Get-ChildItem -LiteralPath $destination -File -Filter '*.pdb' | Remove-Item
Copy-Item "$repo/licenses/*" "$destination/licenses" -Force
$runtimePackage = Join-Path $env:NUGET_PACKAGES 'microsoft.netcore.app.runtime.win-x64/10.0.14'
Get-ChildItem $runtimePackage -File | Where-Object Name -Match 'LICENSE|THIRD-PARTY' | Copy-Item -Destination "$destination/licenses" -Force
$desktopPackage = Join-Path $env:NUGET_PACKAGES 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.14'
Get-ChildItem $desktopPackage -File | Where-Object Name -Match 'LICENSE|THIRD-PARTY' | ForEach-Object { Copy-Item $_.FullName (Join-Path "$destination/licenses" ('WindowsDesktop-' + $_.Name)) -Force }
Get-FileHash "$destination/tools/ExifTool/ExifTool.exe", "$destination/tools/FFmpeg/ffmpeg.exe", "$destination/geography/*.geojson" | Select-Object @{n='File';e={ $_.Path.Substring($destination.Length + 1) }},Hash | ConvertTo-Json | Set-Content "$destination/dependencies.json" -Encoding utf8
$testFailure=$null
if (!$SkipTests) {
    $previousExecutable = $env:GEOSTACK_EXE
    try { $env:GEOSTACK_EXE = Join-Path $destination 'GeoStack.exe'; & "$PSScriptRoot/test.ps1" }
    catch {
        $testFailure=$_
        [pscustomobject]@{Stage='TestExecution';Status='FAILED_OR_BLOCKED';Error=$_.ToString();CandidateStagingPath=$destination}|ConvertTo-Json|Set-Content (Join-Path $repo ('.local/portable-test-failure-'+(Split-Path $destination -Leaf)+'.json')) -Encoding utf8
        if(!$KeepUnacceptedCandidate){throw}
    }
    finally { $env:GEOSTACK_EXE = $previousExecutable }
}
$testStatus=if($SkipTests){'NOT_TESTED'}elseif($testFailure){'FAILED_OR_BLOCKED'}else{'PASS'}
$savedPreference=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $sourceCommit=git -C $repo rev-parse HEAD 2> (Join-Path $repo '.local/portable-git-provenance.log');$commitExit=$LASTEXITCODE
    $sourceStatus=git -C $repo status --porcelain --untracked-files=no 2>> (Join-Path $repo '.local/portable-git-provenance.log');$statusExit=$LASTEXITCODE
}finally{$ErrorActionPreference=$savedPreference}
if($commitExit -ne 0 -or $statusExit -ne 0){throw 'Cannot record source provenance'}
$binaryHashes=Get-FileHash "$destination/GeoStack.exe","$destination/GeoStack.dll","$destination/GeoStack.Core.dll" | Select-Object @{n='File';e={Split-Path $_.Path -Leaf}},Hash
[pscustomobject]@{SourceCommit=$sourceCommit;ProductVersion=(Get-Item (Join-Path $destination "GeoStack.exe")).VersionInfo.ProductVersion;Channel=$Channel;TrackedChangesPresent=[bool]$sourceStatus;TestExecution=$testStatus;Accepted=$false;Scope='Candidate provenance is not owner or performance acceptance';CacheCleanupSourceSHA256=(Get-FileHash "$repo/src/GeoStack.App/AtlasControl.cs").Hash;Binaries=$binaryHashes}|ConvertTo-Json -Depth 5|Set-Content "$destination/build-provenance.json" -Encoding utf8
if($testFailure){'NOT ACCEPTED: source published, but test execution failed or was blocked. See repository-side build logs. Do not treat compilation or historical tests as current runtime validation.'|Set-Content "$destination/CANDIDATE-NOT-ACCEPTED.txt" -Encoding utf8}
$size = (Get-ChildItem $destination -Recurse -File | Measure-Object Length -Sum).Sum
if (Test-Path (Join-Path $destination 'data')) { throw 'A release package must not contain user data' }
New-Item -ItemType Directory -Force (Join-Path $repo 'dist') | Out-Null
if (Test-Path -LiteralPath $finalDestination) {
    $archive = Join-Path $repo ('.local/previous-portable-' + [Guid]::NewGuid().ToString('N'))
    $workspacePrefix = [IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
    foreach ($target in @($destination, $finalDestination, $archive)) {
        if (![IO.Path]::GetFullPath($target).StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package move target is outside the repository' }
    }
    # Preserve the old package without reading its data; all move targets were checked above.
    Move-Item -LiteralPath $finalDestination -Destination $archive
    Write-Warning "Previous package retained at $archive. Review it with scripts/report-storage.py after acceptance; repeated builds retain full runtimes. Never prune active data or the last verified rollback."
}
Move-Item -LiteralPath $destination -Destination $finalDestination
$destination = $finalDestination
Write-Host "Portable executable: $destination/GeoStack.exe"
Write-Host "Package size: $size bytes ($([Math]::Round($size / 1MB, 2)) MiB)"
if($testFailure){throw 'Candidate retained separately; tests failed or were blocked. Production was not replaced.'}
