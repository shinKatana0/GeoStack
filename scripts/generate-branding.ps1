# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
. "$PSScriptRoot/environment.ps1"
python "$PSScriptRoot/generate-branding.py"
if ($LASTEXITCODE -ne 0) { throw 'Branding generation failed' }
