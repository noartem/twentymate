<#
.SYNOPSIS
    Собирает TwentyMate и упаковывает его в установщик-мастер для распространения.

.DESCRIPTION
    Публикует приложение в dist\app и компилирует Installer\TwentyMate.iss
    в единый dist\TwentyMate-Setup-<версия>.exe.

    По умолчанию сборка self-contained: .NET вшит в приложение, поэтому
    установщик работает на любой Windows 10 1809+/11 без предустановленного
    рантайма. Нужен Inno Setup 6 (winget install JRSoftware.InnoSetup).

.PARAMETER FrameworkDependent
    Лёгкая сборка без вшитого .NET (~1 МБ установщик). Требует у пользователя
    установленный .NET 8 Desktop Runtime.

.PARAMETER Version
    Переопределить версию. По умолчанию берётся из <Version> в TwentyMate.csproj.

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

if (-not (Test-Path $project)) { throw "Не найден $project" }
if (-not (Test-Path $iss)) { throw "Не найден $iss" }

# ── Инструменты ───────────────────────────────────────────────────────────────

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "Не найден dotnet. Установите .NET 8 SDK." }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw "Не найден Inno Setup 6. Установите: winget install --id JRSoftware.InnoSetup -e"
}

# ── Версия ────────────────────────────────────────────────────────────────────

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version |
        Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw "Не удалось определить версию — задайте -Version" }

# ── Публикация ────────────────────────────────────────────────────────────────

Write-Step "Публикация $Version$(if (-not $FrameworkDependent) { ' со встроенным .NET' })"

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
if ($LASTEXITCODE -ne 0) { throw "Сборка завершилась с ошибкой" }

$builtExe = Join-Path $appDir "TwentyMate.exe"
if (-not (Test-Path $builtExe)) { throw "После сборки не найден $builtExe" }

$payload = (Get-ChildItem $appDir -Recurse -File | Measure-Object Length -Sum).Sum

# ── Компиляция установщика ────────────────────────────────────────────────────

Write-Step "Сборка установщика"

& $iscc "/DAppVersion=$Version" $iss | ForEach-Object {
    if ($_ -match "^\s*(Error|Warning)") { Write-Host $_ -ForegroundColor Yellow }
}
if ($LASTEXITCODE -ne 0) { throw "Inno Setup завершился с ошибкой" }

$setup = Join-Path $distDir "TwentyMate-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Установщик не найден: $setup" }

$setupSize = (Get-Item $setup).Length

Write-Host ""
Write-Host "Установщик готов: $setup" -ForegroundColor Green
Write-Host ("Размер: {0:N1} МБ (приложение — {1:N1} МБ)" -f ($setupSize / 1MB), ($payload / 1MB))
