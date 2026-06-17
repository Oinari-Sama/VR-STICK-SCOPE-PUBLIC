param(
    [string]$HostAddress = "127.0.0.1",
    [int]$Port = 9000
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Engine = Join-Path $Root "engine\build\Release\InariKontrollerEngine.exe"

if (-not (Test-Path -LiteralPath $Engine)) {
    throw "Engine executable was not found: $Engine. Run .\scripts\build_inari_kontroller.ps1 first."
}

$env:InariKontroller_OSC_ENABLE = "1"
$env:InariKontroller_CORRECTION_ENABLE = "1"
$env:InariKontroller_OSC_HOST = $HostAddress
$env:InariKontroller_OSC_PORT = [string]$Port

Start-Process -FilePath $Engine -ArgumentList "--vrchat-osc", "--enable-correction" -WorkingDirectory $Root -WindowStyle Hidden

Write-Host "Started InariKontrollerEngine with experimental VRChat OSC correction output."
Write-Host "Target: $HostAddress`:$Port"
Write-Host "SteamVR driver registration was not changed."
