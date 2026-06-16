param(
    [string]$HostAddress = "127.0.0.1",
    [int]$Port = 9000
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Engine = Join-Path $Root "engine\build\Release\VRStickScopeEngine.exe"

if (-not (Test-Path -LiteralPath $Engine)) {
    throw "Engine executable was not found: $Engine. Run .\scripts\build_vr_stick_scope.ps1 first."
}

$env:VRSTICKSCOPE_OSC_ENABLE = "1"
$env:VRSTICKSCOPE_CORRECTION_ENABLE = "1"
$env:VRSTICKSCOPE_OSC_HOST = $HostAddress
$env:VRSTICKSCOPE_OSC_PORT = [string]$Port

Start-Process -FilePath $Engine -ArgumentList "--vrchat-osc", "--enable-correction" -WorkingDirectory $Root -WindowStyle Hidden

Write-Host "Started VRStickScopeEngine with experimental VRChat OSC correction output."
Write-Host "Target: $HostAddress`:$Port"
Write-Host "SteamVR driver registration was not changed."
