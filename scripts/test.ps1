# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
. "$PSScriptRoot/environment.ps1"
$env:GEOSTACK_ROOT = $repo
# Materialize only the attributed, repository-owned geographic test packages.
$geographicTests=Join-Path $repo 'dist/GeoStack-geographic-data-0.4'
New-Item -ItemType Directory -Force $geographicTests | Out-Null
foreach($name in @('ch-selected.gsgeo','cn-selected.gsgeo','jp-selected.gsgeo','pl-selected.gsgeo')){
    $source=Join-Path "$repo/tests/GeoStack.Tests/Fixtures/Geography" $name
    $target=Join-Path $geographicTests $name
    if(!(Test-Path -LiteralPath $target)){Copy-Item -LiteralPath $source -Destination $target}
    if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw "Existing geographic test input differs; preserved without overwrite: $name"}
}
& "$PSScriptRoot/create-fixtures.ps1"
& "$PSScriptRoot/create-format-fixtures.ps1"
python "$PSScriptRoot/create-map-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Map fixture generation failed' }
python "$PSScriptRoot/create-global-atlas-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Global atlas fixture generation failed' }
python "$PSScriptRoot/create-geographic-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Geographic fixture generation failed' }
$restorePreference=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    dotnet restore GeoStack.sln --ignore-failed-sources -p:NuGetAudit=false
    $restoreExit=$LASTEXITCODE
}finally{$ErrorActionPreference=$restorePreference}
if ($restoreExit -ne 0) { throw 'Test restore failed; inspect complete restore output' }
$runId=[Guid]::NewGuid().ToString('N')
python "$PSScriptRoot/test-workspace.py" prepare --id $runId
if($LASTEXITCODE -ne 0){throw 'Cannot prepare an owned test workspace'}
# Route preview helpers to the same selected tools as metadata tests.
if($env:GEOSTACK_TEST_TOOLS){
    $selectedTools=[IO.Path]::GetFullPath($env:GEOSTACK_TEST_TOOLS)
    if(!$selectedTools.StartsWith($repo.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test tools must be staged inside this checkout'}
    $testTools=Join-Path $repo 'tests/GeoStack.Tests/bin/Release/net10.0-windows/tools'
    $marker=Join-Path $testTools '.geostack-test-tools.json'
    $entries=@(Get-ChildItem -LiteralPath $selectedTools -Recurse -File | Sort-Object FullName | ForEach-Object {[pscustomobject]@{Path=$_.FullName.Substring($selectedTools.Length+1);Hash=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    if(Test-Path -LiteralPath $testTools){
        if(!(Test-Path -LiteralPath $marker)){throw 'Existing test tool directory has no ownership marker; preserved'}
        $prior=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if(($prior | ConvertTo-Json -Compress) -ne ($entries | ConvertTo-Json -Compress)){throw 'Existing test tools differ; use a fresh test output directory'}
    }else{
        New-Item -ItemType Directory -Force $testTools | Out-Null
        Copy-Item "$selectedTools/*" $testTools -Recurse
        $entries | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8
    }
}
$previousTestRun=$env:GEOSTACK_TEST_RUN
$env:GEOSTACK_TEST_RUN=$runId
$savedPreference=$ErrorActionPreference
try{
    # Native test failures/warnings on stderr must not truncate the runner before its TRX is written.
    $ErrorActionPreference='Continue'
    dotnet test GeoStack.sln -c Release --no-restore --logger 'trx;LogFileName=GeoStack-Release.trx' --results-directory "$repo/TestResults"
    $testExit=$LASTEXITCODE
}finally{$ErrorActionPreference=$savedPreference;$env:GEOSTACK_TEST_RUN=$previousTestRun}
if ($testExit -ne 0) { throw 'Automated tests failed; inspect the complete output and TRX' }

# Keep failed runs for investigation; only a successful, marked run is disposable.
python "$PSScriptRoot/test-workspace.py" clean --id $runId --apply --confirm $runId
if($LASTEXITCODE -ne 0){throw "Tests passed but temporary run cleanup needs review: $runId"}
