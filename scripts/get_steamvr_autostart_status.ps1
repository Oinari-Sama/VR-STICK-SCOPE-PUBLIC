param(
    [string]$EnginePath = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($EnginePath)) {
    $candidates = @(
        (Join-Path $Root "VRStickScopeEngine.exe"),
        (Join-Path $Root "engine\build\Release\VRStickScopeEngine.exe"),
        (Join-Path $Root "gui\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\VRStickScopeEngine.exe")
    )
    $EnginePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($EnginePath) -or -not (Test-Path -LiteralPath $EnginePath)) {
    throw "VRStickScopeEngine.exe was not found. Build first with .\scripts\build_vr_stick_scope.ps1."
}

& $EnginePath --autostart-status
switch ($LASTEXITCODE) {
    0 { Write-Host "enabled"; exit 0 }
    2 { Write-Host "disabled"; exit 2 }
    default { Write-Host "unknown"; exit 1 }
}
