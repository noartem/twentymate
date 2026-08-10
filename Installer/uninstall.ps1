<#
.SYNOPSIS
    Removes TwentyMate installed via install.ps1.

.PARAMETER KeepSettings
    Keep settings and statistics in %APPDATA%\TwentyMate.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Installer\uninstall.ps1
#>

[CmdletBinding()]
param([switch]$KeepSettings)

$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "Programs\TwentyMate"
$shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TwentyMate.lnk"
$settingsDir = Join-Path $env:APPDATA "TwentyMate"

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

$running = @(Get-Process TwentyMate -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Step "Closing the app"
    foreach ($p in $running) {
        try { $p | Stop-Process -Force -ErrorAction Stop }
        catch { Write-Warning "Couldn't close process $($p.Id) — close it manually." }
    }
    Start-Sleep -Milliseconds 800
}

Write-Step "Removing autostart"
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name TwentyMate -ErrorAction SilentlyContinue

Write-Step "Removing shortcut"
Remove-Item $shortcut -Force -ErrorAction SilentlyContinue

Write-Step "Removing $installDir"
Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue

if (-not $KeepSettings) {
    Write-Step "Removing settings"
    Remove-Item $settingsDir -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "Settings kept: $settingsDir"
}

Write-Host ""
Write-Host "TwentyMate removed." -ForegroundColor Green
