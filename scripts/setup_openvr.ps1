param(
    [string]$OpenVRVersion = "2.5.1"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$OpenVRUrl = "https://github.com/ValveSoftware/openvr/archive/refs/tags/v$OpenVRVersion.zip"
$TmpDir = Join-Path $Root "engine\thirdparty\.tmp"
$OpenVRDir = Join-Path $Root "engine\thirdparty\openvr"

New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OpenVRDir "headers") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OpenVRDir "lib\win64") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OpenVRDir "bin\win64") | Out-Null

$zipPath = Join-Path $TmpDir "openvr.zip"
Invoke-WebRequest -Uri $OpenVRUrl -OutFile $zipPath -UseBasicParsing
Expand-Archive -Path $zipPath -DestinationPath $TmpDir -Force

$ExtractedDir = Join-Path $TmpDir "openvr-$OpenVRVersion"
Copy-Item (Join-Path $ExtractedDir "headers\openvr.h") (Join-Path $OpenVRDir "headers\openvr.h") -Force
Copy-Item (Join-Path $ExtractedDir "headers\openvr_driver.h") (Join-Path $OpenVRDir "headers\openvr_driver.h") -Force
Copy-Item (Join-Path $ExtractedDir "lib\win64\openvr_api.lib") (Join-Path $OpenVRDir "lib\win64\openvr_api.lib") -Force
Copy-Item (Join-Path $ExtractedDir "bin\win64\openvr_api.dll") (Join-Path $OpenVRDir "bin\win64\openvr_api.dll") -Force

Remove-Item -LiteralPath $TmpDir -Recurse -Force
Write-Host "[VRStickScope] OpenVR SDK $OpenVRVersion installed under engine\thirdparty\openvr" -ForegroundColor Green
