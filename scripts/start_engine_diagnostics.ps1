param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Engine = Join-Path $Root "engine\build\Release\VRStickScopeEngine.exe"

if (-not (Test-Path -LiteralPath $Engine)) {
    throw "Engine executable was not found: $Engine. Run .\scripts\build_vr_stick_scope.ps1 first."
}

Remove-Item Env:\VRSTICKSCOPE_OSC_ENABLE -ErrorAction SilentlyContinue
Remove-Item Env:\VRSTICKSCOPE_CORRECTION_ENABLE -ErrorAction SilentlyContinue
Remove-Item Env:\VRSTICKSCOPE_OSC_HOST -ErrorAction SilentlyContinue
Remove-Item Env:\VRSTICKSCOPE_OSC_PORT -ErrorAction SilentlyContinue

Start-Process -FilePath $Engine -WorkingDirectory $Root -WindowStyle Hidden

Write-Host "Started VRStickScopeEngine in diagnostics-only mode."
Write-Host "OSC output is disabled."
Write-Host "SteamVR driver registration was not changed."
