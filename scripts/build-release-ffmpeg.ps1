# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
param([switch]$AcquireInputs)
. "$PSScriptRoot/environment.ps1"
$stage=Join-Path $repo '.local/winrelease'
$inputs=Join-Path $stage 'sources'
New-Item -ItemType Directory -Force $inputs,(Join-Path $stage 'temp') | Out-Null
$lock=Get-Content "$repo/release/windows-ffmpeg.lock.json" -Raw | ConvertFrom-Json
foreach($inputRow in @($lock.ffmpeg,$lock.toolchain,$lock.zlib)){
    $file=Join-Path $inputs ([IO.Path]::GetFileName($inputRow.url))
    if(!(Test-Path -LiteralPath $file)){
        if(!$AcquireInputs){throw "Missing pinned input; use -AcquireInputs: $file"}
        Invoke-WebRequest -Uri $inputRow.url -OutFile $file
    }
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $inputRow.sha256){throw "Pinned input hash mismatch: $file"}
}
$toolchain=Join-Path $stage 'toolchain/w64devkit'
if(!(Test-Path "$toolchain/bin/gcc.exe")){
    & "$inputs/w64devkit-x64-2.11.0.7z.exe" -y "-o$stage/toolchain"
    if($LASTEXITCODE -ne 0){throw 'Compiler extraction failed'}
}
$build=Join-Path $stage 'build/ffmpeg-9.0.1'
if(!(Test-Path "$build/configure")){
    New-Item -ItemType Directory -Force (Join-Path $stage 'build') | Out-Null
    tar.exe -xf "$inputs/ffmpeg-9.0.1.tar.xz" -C "$stage/build"
    if($LASTEXITCODE -ne 0){throw 'Source extraction failed'}
}
if(!(Test-Path "$stage/build/zlib-1.3.1/zlib.h")){
    tar.exe -xf "$inputs/zlib-1.3.1.tar.gz" -C "$stage/build"
    if($LASTEXITCODE -ne 0){throw 'zlib source extraction failed'}
}
$env:GEOSTACK_FFMPEG_BUILD=$build.Replace('\','/')
$env:TEMP=Join-Path $stage 'temp';$env:TMP=$env:TEMP
$priorPath=$env:PATH
try{
    $env:PATH="$toolchain/bin;"+$priorPath
    $savedPreference=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & "$toolchain/bin/sh.exe" "$PSScriptRoot/build-release-ffmpeg.sh"
        $buildExit=$LASTEXITCODE
    }finally{$ErrorActionPreference=$savedPreference}
    if($buildExit -ne 0){throw 'FFmpeg compilation failed'}
}finally{$env:PATH=$priorPath}
Write-Host "Built $build/ffmpeg.exe; run candidate compatibility verification before selecting it."
