# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
. "$PSScriptRoot/environment.ps1"
python "$PSScriptRoot/fetch-format-fixtures.py"
if ($LASTEXITCODE -ne 0) { throw 'Licensed fixture acquisition failed' }
$folder = Join-Path $repo '.local/format-fixtures'
$exif = Join-Path $repo 'tools/ExifTool/ExifTool.exe'
$ffmpeg = Join-Path $repo 'tools/FFmpeg/ffmpeg.exe'
$env:LC_ALL = 'C'; $env:LC_CTYPE = 'C'; $env:LANG = 'C'
Copy-Item "$folder/libheif-example.heic" "$folder/HEIC-GPS.heic" -Force
Copy-Item "$folder/libheif-example.heic" "$folder/HEIF-noGPS.heif" -Force
& $exif -overwrite_original '-GPSLatitude=51.4778' '-GPSLatitudeRef=N' '-GPSLongitude=0.0015' '-GPSLongitudeRef=W' "$folder/HEIC-GPS.heic"
if ($LASTEXITCODE -ne 0) { throw 'HEIC derivative metadata generation failed' }
foreach ($extension in @('webp','tiff')) {
    & $ffmpeg -nostdin -v error -i "$repo/.local/fixtures/Paris-01.jpg" -frames:v 1 -y "$folder/Generated.$extension"
    if ($LASTEXITCODE -ne 0) { throw "Image generation failed: $extension" }
}
foreach ($pair in @(@('MOV','mpeg4'),@('MP4','mpeg4'),@('M4V','mpeg4'),@('MKV','mpeg4'),@('AVI','mpeg4'),@('WebM','libvpx-vp9'))) {
    $format = if ($pair[0] -in @('MP4','M4V')) { 'mp4' } elseif ($pair[0] -eq 'MKV') { 'matroska' } else { $pair[0].ToLowerInvariant() }
    & $ffmpeg -nostdin -v error -f lavfi -i 'testsrc2=size=640x360:rate=12' -t 1 -c:v $pair[1] -metadata 'location=-33.8600+151.2000/' -f $format -y "$folder/Generated.$($pair[0])"
    if ($LASTEXITCODE -ne 0) { throw "Video generation failed: $($pair[0])" }
}
# Exercise Apple's common mdta ISO 6709 location structure on a valid generated file.
& $exif -overwrite_original -n '-Keys:GPSCoordinates=37.7749 -122.4194 15' "$folder/Generated.MOV"
if ($LASTEXITCODE -ne 0) { throw 'QuickTime key generation failed' }
Set-Content "$folder/Malformed.heic" -Value 'Deliberately malformed fixture'
$aviBytes = [IO.File]::ReadAllBytes("$folder/Generated.AVI")
$signature = [Text.Encoding]::ASCII.GetBytes('FMP4')
for ($position = 0; $position -le $aviBytes.Length - 4; $position++) {
    if ($aviBytes[$position] -eq $signature[0] -and $aviBytes[$position+1] -eq $signature[1] -and $aviBytes[$position+2] -eq $signature[2] -and $aviBytes[$position+3] -eq $signature[3]) { [Array]::Copy([Text.Encoding]::ASCII.GetBytes('ZZZZ'), 0, $aviBytes, $position, 4) }
}
[IO.File]::WriteAllBytes("$folder/Unsupported-codec.AVI", $aviBytes)
Copy-Item "$repo/.local/fixtures/Paris-01.jpg" "$folder/Generated.jpg" -Force
Copy-Item "$repo/.local/fixtures/Paris-01.jpg" "$folder/Orientation-6.jpg" -Force
& $exif -overwrite_original -n '-Orientation=6' "$folder/Orientation-6.jpg"
if ($LASTEXITCODE -ne 0) { throw 'JPEG orientation generation failed' }
Copy-Item "$repo/.local/fixtures/nested/No-GPS.png" "$folder/Generated.png" -Force
$expected = @{}
foreach ($file in Get-ChildItem $folder -File) {
    if ($file.Extension.ToLowerInvariant() -in @('.heic','.heif','.mov','.mp4','.m4v','.mkv','.avi','.webm','.jpg','.png','.webp','.tiff','.dng','.nef')) { $expected[$file.Name] = $file.Name -notin @('Malformed.heic', 'exiftool-QuickTime.mov', 'Unsupported-codec.AVI') }
}
$expected | ConvertTo-Json | Set-Content "$folder/expected.json" -Encoding utf8
