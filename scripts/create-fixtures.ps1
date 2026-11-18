# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
. "$PSScriptRoot/environment.ps1"
$folder = Join-Path $repo '.local/fixtures'
New-Item -ItemType Directory -Force $folder | Out-Null
python "$PSScriptRoot/create-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
$exif = Join-Path $repo 'tools/ExifTool/ExifTool.exe'
$env:LC_ALL = 'C'
$env:LC_CTYPE = 'C'
$env:LANG = 'C'
foreach ($place in @(@('Paris',48.8566,2.3522),@('Moscow',55.7558,37.6173),@('Tokyo',35.6762,139.6503),@('New-York',40.7128,-74.0060))) {
    Get-ChildItem $folder -Filter "$($place[0])-*.jpg" | ForEach-Object {
        & $exif -overwrite_original "-GPSLatitude=$($place[1])" "-GPSLongitude=$([Math]::Abs($place[2]))" '-GPSLatitudeRef=N' "-GPSLongitudeRef=$(if ($place[2] -lt 0) {'W'} else {'E'})" '-DateTimeOriginal=2026:01:02 12:00:00' $_.FullName
        if ($LASTEXITCODE -ne 0) { throw 'GPS fixture tagging failed' }
    }
}
& "$repo/tools/FFmpeg/ffmpeg.exe" -nostdin -v error -f lavfi -i 'testsrc2=size=640x360:rate=12' -t 1 -c:v mpeg4 -metadata 'location=+55.7558+037.6173/' -y "$folder/Moscow.mov"
if ($LASTEXITCODE -ne 0) { throw 'MOV generation failed' }
Set-Content -LiteralPath "$folder/Corrupt.heic" -Value 'synthetic invalid HEIC file for error handling'
