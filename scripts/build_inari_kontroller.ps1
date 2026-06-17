param(
    [string]$Configuration = "Release",
    [string]$CMakePath = "",
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($CMakePath)) {
    $cmd = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmd) {
        $CMakePath = $cmd.Source
    } else {
        $vsCMake = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
        if (Test-Path -LiteralPath $vsCMake) {
            $CMakePath = $vsCMake
        } else {
            throw "cmake.exe was not found on PATH or at the Visual Studio bundled location. Install CMake or pass -CMakePath."
        }
    }
}

$openvrHeader = Join-Path $Root "engine\thirdparty\openvr\headers\openvr.h"
$openvrDriverHeader = Join-Path $Root "engine\thirdparty\openvr\headers\openvr_driver.h"
$openvrLib = Join-Path $Root "engine\thirdparty\openvr\lib\win64\openvr_api.lib"
$openvrDll = Join-Path $Root "engine\thirdparty\openvr\bin\win64\openvr_api.dll"
foreach ($required in @($openvrHeader, $openvrDriverHeader, $openvrLib, $openvrDll)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "OpenVR SDK file is missing: $required. Run .\scripts\setup_openvr.ps1 first."
    }
}

& $CMakePath -S (Join-Path $Root "engine") -B (Join-Path $Root "engine\build") -G "Visual Studio 17 2022" -A x64
& $CMakePath --build (Join-Path $Root "engine\build") --config $Configuration

$publishDir = Join-Path $Root "gui\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
dotnet publish (Join-Path $Root "gui\InariKontrollerGUI.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:Platform=x64 `
    -p:SatelliteResourceLanguages=ja-JP `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Deterministic=true `
    -p:ContinuousIntegrationBuild=true `
    "-p:PathMap=$Root=/_/src" `
    -o $publishDir `
    -v:minimal

if (Test-Path -LiteralPath $publishDir) {
    Copy-Item -LiteralPath (Join-Path $Root "engine\build\$Configuration\InariKontrollerEngine.exe") -Destination $publishDir -Force
    Copy-Item -LiteralPath (Join-Path $Root "engine\build\$Configuration\openvr_api.dll") -Destination $publishDir -Force
}

if ($Package) {
    if (-not (Test-Path -LiteralPath $publishDir)) {
        throw "Publish directory was not found: $publishDir"
    }

    $distDir = Join-Path $Root "dist"
    $packageName = "Inari-Kontroller-v1.0.8-public"
    $packageDir = Join-Path $distDir $packageName
    $zipPath = Join-Path $distDir "$packageName.zip"

    if (Test-Path -LiteralPath $packageDir) {
        Remove-Item -LiteralPath $packageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    $appDir = Join-Path $packageDir "app"
    $docsDir = Join-Path $packageDir "docs"
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    New-Item -ItemType Directory -Path $docsDir -Force | Out-Null

    Get-ChildItem -LiteralPath $publishDir -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $appDir -Recurse -Force
    }

    $driverFiles = @(
        "driver_InariKontroller.dll",
        "driver.vrdrivermanifest",
        "InariKontroller_profile.json"
    )
    foreach ($driverFile in $driverFiles) {
        Get-ChildItem -LiteralPath $appDir -Recurse -File -Filter $driverFile -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
    Get-ChildItem -LiteralPath $appDir -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $appDir -Recurse -File -Filter "createdump.exe" -ErrorAction SilentlyContinue |
        Remove-Item -Force
    $allowedResourceDirs = @("en-US", "en-us", "ja-JP", "ja-jp")
    Get-ChildItem -LiteralPath $appDir -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -notin $allowedResourceDirs -and
            (Get-ChildItem -LiteralPath $_.FullName -File -Filter "*.mui" -ErrorAction SilentlyContinue)
        } |
        Remove-Item -Recurse -Force

    $excludedExtensions = @(".log", ".tmp", ".bak", ".cmd", ".bat", ".ps1", ".user", ".suo", ".ilk", ".iobj", ".ipdb")
    Get-ChildItem -LiteralPath $appDir -Recurse -File -Force |
        Where-Object { $_.Name -like ".*" -or $excludedExtensions -contains $_.Extension.ToLowerInvariant() } |
        Remove-Item -Force

    Copy-Item -LiteralPath (Join-Path $Root "engine\build\$Configuration\InariKontroller.exe") -Destination (Join-Path $packageDir "Inari_Kontroller.exe") -Force
    Copy-Item -LiteralPath (Join-Path $Root "README_FIRST_JA.txt") -Destination $packageDir -Force
    Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination (Join-Path $docsDir "LICENSE.txt") -Force
    Copy-Item -LiteralPath (Join-Path $Root "docs\ROLLBACK_JA.md") -Destination (Join-Path $docsDir "ROLLBACK_JA.txt") -Force
    Copy-Item -LiteralPath (Join-Path $Root "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $docsDir "THIRD_PARTY_NOTICES.txt") -Force
    $licenseSourceDir = Join-Path $Root "licenses"
    if (Test-Path -LiteralPath $licenseSourceDir) {
        Copy-Item -LiteralPath $licenseSourceDir -Destination (Join-Path $docsDir "licenses") -Recurse -Force
    }

    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $orderedFiles = @(
            (Get-Item -LiteralPath (Join-Path $packageDir "Inari_Kontroller.exe")),
            (Get-Item -LiteralPath (Join-Path $packageDir "README_FIRST_JA.txt"))
        )
        $orderedFiles += Get-ChildItem -LiteralPath $appDir -Recurse -File | Sort-Object FullName
        $orderedFiles += Get-ChildItem -LiteralPath $docsDir -Recurse -File | Sort-Object FullName

        foreach ($file in $orderedFiles) {
            $relative = $file.FullName.Substring($packageDir.Length).TrimStart("\", "/") -replace "\\", "/"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $file.FullName,
                $relative,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
    Write-Host "Package created: $zipPath"
}
