<#
.SYNOPSIS
    Builds TwentyMate and packages it into a setup wizard for distribution.

.DESCRIPTION
    Publishes the app to dist\app and compiles Installer\TwentyMate.iss
    into a single dist\TwentyMate-Setup-<version>.exe.

    By default the build is self-contained: .NET is bundled into the app, so
    the installer works on any Windows 10 1809+/11 without a preinstalled
    runtime. Requires Inno Setup 6 (winget install JRSoftware.InnoSetup).

.PARAMETER FrameworkDependent
    Lightweight build without .NET bundled in (~1 MB installer). Requires
    the user to have the .NET 8 Desktop Runtime installed.

.PARAMETER Version
    Override the version. By default it's taken from <Version> in TwentyMate.csproj.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Installer\build-installer.ps1
#>

[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [string]$Version
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "TwentyMate.csproj"
$iss = Join-Path $PSScriptRoot "TwentyMate.iss"
$distDir = Join-Path $root "dist"
$appDir = Join-Path $distDir "app"

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

if (-not (Test-Path $project)) { throw "$project not found" }
if (-not (Test-Path $iss)) { throw "$iss not found" }

# ── Tools ─────────────────────────────────────────────────────────────────────

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet not found. Install the .NET 8 SDK." }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw "Inno Setup 6 not found. Install it: winget install --id JRSoftware.InnoSetup -e"
}

# ── Version ───────────────────────────────────────────────────────────────────

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw "Couldn't determine the version — pass -Version" }

# ── Publish ───────────────────────────────────────────────────────────────────

Write-Step "Publishing $Version$(if (-not $FrameworkDependent) { ' with .NET bundled in' })"

if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $(if ($FrameworkDependent) { "false" } else { "true" }),
    "-p:PublishSingleFile=false",
    "-p:DebugType=none",
    "-p:Version=$Version",
    "-o", $appDir
)

& $dotnet @publishArgs | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$builtExe = Join-Path $appDir "TwentyMate.exe"
if (-not (Test-Path $builtExe)) { throw "$builtExe not found after build" }

$payload = (Get-ChildItem $appDir -Recurse -File | Measure-Object Length -Sum).Sum

# ── Compile the installer ────────────────────────────────────────────────────

Write-Step "Building installer"

& $iscc "/DAppVersion=$Version" $iss | ForEach-Object {
    if ($_ -match "^\s*(Error|Warning)") { Write-Host $_ -ForegroundColor Yellow }
}
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$setup = Join-Path $distDir "TwentyMate-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Installer not found: $setup" }

$setupSize = (Get-Item $setup).Length

Write-Host ""
Write-Host "Installer ready: $setup" -ForegroundColor Green
Write-Host ("Size: {0:N1} MB (app — {1:N1} MB)" -f ($setupSize / 1MB), ($payload / 1MB))
