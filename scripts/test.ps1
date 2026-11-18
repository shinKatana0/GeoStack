# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
. "$PSScriptRoot/environment.ps1"
$env:GEOSTACK_ROOT = $repo
& "$PSScriptRoot/create-fixtures.ps1"
& "$PSScriptRoot/create-format-fixtures.ps1"
python "$PSScriptRoot/create-map-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Map fixture generation failed' }
python "$PSScriptRoot/create-global-atlas-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Global atlas fixture generation failed' }
python "$PSScriptRoot/create-geographic-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Geographic fixture generation failed' }
dotnet restore GeoStack.sln --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
$runId=[Guid]::NewGuid().ToString('N')
python "$PSScriptRoot/test-workspace.py" prepare --id $runId
if($LASTEXITCODE -ne 0){throw 'Cannot prepare an owned test workspace'}
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
