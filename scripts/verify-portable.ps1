# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
param([string]$FixtureDirectory)
. "$PSScriptRoot/environment.ps1"
$executable = Join-Path $repo 'dist/GeoStack-portable-win-x64/GeoStack.exe'
if (!(Test-Path $executable)) { throw 'Build the portable package first' }
$verification = Join-Path $repo ('.local/portable-check-' + [Guid]::NewGuid().ToString('N'))
if (!$FixtureDirectory) {
    $FixtureDirectory = Join-Path $repo '.local/fixtures'
    if (!(Test-Path $FixtureDirectory)) { & "$PSScriptRoot/create-fixtures.ps1" }
}
if (!(Test-Path -LiteralPath $FixtureDirectory -PathType Container)) { throw 'The fixture directory does not exist' }
# A self-contained app must start without finding any globally installed runtime.
$previousEnvironment = @{}
foreach ($name in @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_MULTILEVEL_LOOKUP')) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
try {
$env:DOTNET_ROOT = Join-Path $repo '.local/no-global-runtime'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$arguments = '--verify "' + $fixtureDirectory + '" --data "' + $verification + '"'
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { $process.Kill(); throw "Portable verification did not finish within 60 seconds. Process: $($process.Id)" }
if ($process.ExitCode -ne 0) { Get-Content "$verification/verification-error.txt" -ErrorAction SilentlyContinue; throw "Portable verification failed: $($process.ExitCode)" }
$report = Get-Content "$verification/verification.json" -Raw | ConvertFrom-Json
if ($report.Status -ne 'PASS') { throw 'Portable workflow did not pass' }
Write-Host "Evidence: $verification/verification.json"
$report | ConvertTo-Json -Depth 5
}
finally {
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name],'Process') }
}
