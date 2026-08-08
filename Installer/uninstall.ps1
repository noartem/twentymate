<#
.SYNOPSIS
    Удаляет TwentyMate, установленный через install.ps1.

.PARAMETER KeepSettings
    Оставить настройки и статистику в %APPDATA%\TwentyMate.

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
    Write-Step "Закрываю приложение"
    foreach ($p in $running) {
        try { $p | Stop-Process -Force -ErrorAction Stop }
        catch { Write-Warning "Не удалось закрыть процесс $($p.Id) — закройте его вручную." }
    }
    Start-Sleep -Milliseconds 800
}

Write-Step "Убираю автозапуск"
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name TwentyMate -ErrorAction SilentlyContinue

Write-Step "Удаляю ярлык"
Remove-Item $shortcut -Force -ErrorAction SilentlyContinue

Write-Step "Удаляю $installDir"
Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue

if (-not $KeepSettings) {
    Write-Step "Удаляю настройки"
    Remove-Item $settingsDir -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "Настройки оставлены: $settingsDir"
}

Write-Host ""
Write-Host "TwentyMate удалён." -ForegroundColor Green
