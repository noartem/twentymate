<#
.SYNOPSIS
    Builds TwentyMate and installs it for the current user.

.DESCRIPTION
    Installation doesn't require administrator rights: the app is placed in
    %LOCALAPPDATA%\Programs\TwentyMate, and a shortcut is added to the Start menu.
    Autostart is enabled through the app's own settings, so the Run registry
    key is created by the app itself on first launch.

.PARAMETER SelfContained
    Build with .NET bundled in (~150 MB), so it doesn't depend on an
    installed runtime. By default the build is lightweight and requires the
    .NET 8 Desktop Runtime.

.PARAMETER NoAutostart
    Don't enable launching at Windows sign-in.

.PARAMETER NoLaunch
    Don't launch the app after installation.

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

if (-not (Test-Path $project)) { throw "$project not found" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet not found. Install the .NET 8 SDK." }

# ── Build ─────────────────────────────────────────────────────────────────────

Write-Step "Building$(if ($SelfContained) { ' with .NET bundled in' })"

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
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$builtExe = Join-Path $publishDir $exeName
if (-not (Test-Path $builtExe)) { throw "$builtExe not found after build" }

# ── Stop a running copy ──────────────────────────────────────────────────────

$running = @(Get-Process TwentyMate -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Step "Closing the running copy"
    foreach ($p in $running) {
        try { $p | Stop-Process -Force -ErrorAction Stop }
        catch { Write-Warning "Couldn't close process $($p.Id) — close it manually from the tray icon menu." }
    }
    Start-Sleep -Milliseconds 800
}

# ── Copy files ────────────────────────────────────────────────────────────────

Write-Step "Installing to $installDir"

New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $installDir -Recurse -Force

$installedExe = Join-Path $installDir $exeName

# ── Start menu shortcut ───────────────────────────────────────────────────────

Write-Step "Start menu shortcut"

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcut = Join-Path $startMenu "TwentyMate.lnk"

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $installedExe
$link.WorkingDirectory = $installDir
$link.Description = "20-20-20 rule eye break reminders"
$link.IconLocation = "$installedExe,0"
$link.Save()

# ── Autostart ─────────────────────────────────────────────────────────────────

if (-not $NoAutostart) {
    Write-Step "Enabling launch at Windows sign-in"

    # The app creates the registry key for this setting itself, so we write to
    # the settings file, not the registry: otherwise the app would wipe the key
    # on its next launch.
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

# ── Launch ────────────────────────────────────────────────────────────────────

$size = "{0:N1} MB" -f ((Get-ChildItem $installDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host ""
Write-Host "TwentyMate installed: $installedExe ($size)" -ForegroundColor Green
Write-Host "Shortcut: $shortcut"
if (-not $NoAutostart) { Write-Host "Autostart: enabled" }
Write-Host "To uninstall: Installer\uninstall.ps1"

if (-not $NoLaunch) {
    Write-Step "Launching"
    Start-Process $installedExe
}
