<#
.SYNOPSIS
    Собирает TwentyMate и устанавливает его для текущего пользователя.

.DESCRIPTION
    Установка не требует прав администратора: приложение кладётся в
    %LOCALAPPDATA%\Programs\TwentyMate, ярлык — в меню «Пуск».
    Автозапуск включается через настройки самого приложения, поэтому
    ключ реестра Run создаёт уже оно само при первом старте.

.PARAMETER SelfContained
    Собрать со встроенным .NET (~150 МБ), чтобы не зависеть от установленного
    рантайма. По умолчанию сборка лёгкая и требует .NET 8 Desktop Runtime.

.PARAMETER NoAutostart
    Не включать запуск при входе в Windows.

.PARAMETER NoLaunch
    Не запускать приложение после установки.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Installer\install.ps1
#>

[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$NoAutostart,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "TwentyMate.csproj"
$publishDir = Join-Path $root "dist"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\TwentyMate"
$exeName = "TwentyMate.exe"

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

if (-not (Test-Path $project)) { throw "Не найден $project" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "Не найден dotnet. Установите .NET 8 SDK." }

# ── Сборка ────────────────────────────────────────────────────────────────────

Write-Step "Сборка$(if ($SelfContained) { ' со встроенным .NET' })"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "-p:PublishSingleFile=true",
    "-p:DebugType=none",
    "-o", $publishDir
)

& $dotnet @publishArgs | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Сборка завершилась с ошибкой" }

$builtExe = Join-Path $publishDir $exeName
if (-not (Test-Path $builtExe)) { throw "После сборки не найден $builtExe" }

# ── Остановка запущенной копии ────────────────────────────────────────────────

$running = @(Get-Process TwentyMate -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Step "Закрываю запущенную копию"
    foreach ($p in $running) {
        try { $p | Stop-Process -Force -ErrorAction Stop }
        catch { Write-Warning "Не удалось закрыть процесс $($p.Id) — закройте его вручную через меню значка." }
    }
    Start-Sleep -Milliseconds 800
}

# ── Копирование ───────────────────────────────────────────────────────────────

Write-Step "Установка в $installDir"

New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $installDir -Recurse -Force

$installedExe = Join-Path $installDir $exeName

# ── Ярлык в меню «Пуск» ───────────────────────────────────────────────────────

Write-Step "Ярлык в меню «Пуск»"

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcut = Join-Path $startMenu "TwentyMate.lnk"

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $installedExe
$link.WorkingDirectory = $installDir
$link.Description = "Напоминания о перерывах для глаз по правилу 20-20-20"
$link.IconLocation = "$installedExe,0"
$link.Save()

# ── Автозапуск ────────────────────────────────────────────────────────────────

if (-not $NoAutostart) {
    Write-Step "Включаю запуск при входе в Windows"

    # Ключ реестра приложение ставит само по этой настройке, поэтому пишем в настройки,
    # а не в реестр: иначе приложение сотрёт ключ при следующем запуске.
    $settingsDir = Join-Path $env:APPDATA "TwentyMate"
    $settingsPath = Join-Path $settingsDir "settings.json"
    New-Item -ItemType Directory -Force $settingsDir | Out-Null

    $settings = if (Test-Path $settingsPath) {
        try { Get-Content $settingsPath -Raw | ConvertFrom-Json } catch { [pscustomobject]@{} }
    } else {
        [pscustomobject]@{}
    }

    $settings | Add-Member -Name LaunchAtLogin -Value $true -MemberType NoteProperty -Force
    $settings | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $settingsPath
}

# ── Запуск ────────────────────────────────────────────────────────────────────

$size = "{0:N1} МБ" -f ((Get-ChildItem $installDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host ""
Write-Host "TwentyMate установлен: $installedExe ($size)" -ForegroundColor Green
Write-Host "Ярлык: $shortcut"
if (-not $NoAutostart) { Write-Host "Автозапуск: включён" }
Write-Host "Удаление: Installer\uninstall.ps1"

if (-not $NoLaunch) {
    Write-Step "Запускаю"
    Start-Process $installedExe
}
