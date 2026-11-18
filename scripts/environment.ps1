# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $repo '.local/dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$env:NUGET_PACKAGES = Join-Path $repo '.local/packages'
New-Item -ItemType Directory -Force "$repo/.local/nuget-feed" | Out-Null
Set-Location $repo
