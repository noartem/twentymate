# TwentyMate

20-20-20 eye break reminders that live in the system tray.

A native Avalonia application on .NET 10, compiled with NativeAOT — no
bundled or preinstalled .NET runtime needed, no third-party dependencies
beyond Avalonia and FluentAvaloniaUI. Fluent styling — Mica backdrop,
rounded corners, system accent color, and automatic light/dark theme
switching following Windows.

## Features

| Feature | Description |
|---|---|
| Tray icon | Logo tile: a white eye on a square that fades from blue to gray as the break approaches and turns blue again after it; its own Windows 11-style menu instead of the system context menu |
| Three reminder modes | Icon only · system notification · fullscreen overlay with a countdown ring |
| Schedule | 5–120 minute interval, 5–300 second break duration |
| Working hours | Time range (including spans past midnight) and days of the week |
| Controls | Break now, skip, snooze, pause for 30 min / 1 h / 2 h / until tomorrow |
| Sound | Soft chime at the start and end of a break, synthesized on the fly |
| Localization | English, Russian, Spanish, German, French, Portuguese (Brazil) — follows the system language by default |
| Other | Auto-start on Windows sign-in, dim all monitors, break counter |

## Installation

Download `TwentyMate-Setup-<version>.exe` from the
[Releases](https://github.com/noartem/twentymate/releases) page and run it —
installation doesn't require administrator rights. The installer is built and
published automatically by GitHub Actions when a tag like `v1.1.0` is pushed.

You can also build the installer yourself, as described below in the
"Building the installer for distribution" section.

## Building the installer for distribution

```bash
pwsh -ExecutionPolicy Bypass -File Installer\build-installer.ps1
```

Publishes the app to `dist\app` and compiles
`dist\TwentyMate-Setup-<version>.exe` — a single file you can put up online.
The version is taken from `<Version>` in `TwentyMate.csproj` (can be
overridden with the `-Version` flag).

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```bash
winget install --id JRSoftware.InnoSetup -e
```

The installer isn't signed, so on first download Windows SmartScreen will
show an "Unknown publisher" warning. To remove it, the file needs to be
signed with a Code Signing certificate (OV — reputation builds up with
download count, EV — instant trust).

## Building for development

```bash
dotnet build -c Release
```

The built `TwentyMate.exe` will appear in `bin/Release/net10.0-windows/`.
Requires the .NET 10 SDK. A regular `dotnet build`/`dotnet run` produces a
framework-dependent binary for fast iteration; only `dotnet publish` (used
by the installer scripts) triggers the NativeAOT compile, which additionally
requires the MSVC C++ build tools (Visual Studio Build Tools, "Desktop
development with C++" workload) to link the native binary.

## How it's organized

| File | Purpose |
|---|---|
| [Core/BreakScheduler.cs](Core/BreakScheduler.cs) | Working, break, pause, and non-working-hours states |
| [Core/TrayController.cs](Core/TrayController.cs) | Wires up the scheduler, icon, and windows — decides what to show |
| [Core/TrayIconFactory.cs](Core/TrayIconFactory.cs) | Draws the tray logo tile via Avalonia's own renderer, tracks its color by progress, and caches the result |
| [Platform/TrayIcon.cs](Platform/TrayIcon.cs) | Shell_NotifyIcon wrapper — the tray icon, tooltip, and balloon notifications |
| [Assets/generate-icon.py](Assets/generate-icon.py) | Rebuilds `app.ico` from the same geometry and palette as the tray icon |
| [Core/ThemeManager.cs](Core/ThemeManager.cs) | Palette and accent color, synced with the system |
| [Core/WindowEffects.cs](Core/WindowEffects.cs) | Mica and rounded corners via DWM, layered on Avalonia's own transparency support |
| [Views/BreakWindow.axaml](Views/BreakWindow.axaml) | Fullscreen break overlay |
| [Views/TrayMenuWindow.axaml](Views/TrayMenuWindow.axaml) | The tray icon's popup menu |
| [Views/SettingsWindow.axaml](Views/SettingsWindow.axaml) | Settings window |
| [Installer/TwentyMate.iss](Installer/TwentyMate.iss) | Inno Setup installer wizard script |
| [Themes/Fluent.axaml](Themes/Fluent.axaml) | Styling not already covered by FluentAvaloniaTheme |

Settings live in `%APPDATA%\TwentyMate\settings.json`, usage history (break
counters, first-run flag) in `%APPDATA%\TwentyMate\stats.json`, and the error
log in `%APPDATA%\TwentyMate\error.log`.
