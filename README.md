# TwentyMate

20-20-20 eye break reminders that live in the system tray.

A native WPF application on .NET 8 with no third-party dependencies. Fluent
styling — Mica backdrop, rounded corners, system accent color, and automatic
light/dark theme switching following Windows.

## Features

| Feature | Description |
|---|---|
| Tray icon | Logo tile: a white eye on a square that fades from blue to gray as the break approaches and turns blue again after it; its own Windows 11-style menu instead of the system context menu |
| Three reminder modes | Icon only · system notification · fullscreen overlay with a countdown ring |
| Schedule | 5–120 minute interval, 5–300 second break duration |
| Working hours | Time range (including spans past midnight) and days of the week |
| Controls | Break now, skip, snooze, pause for 30 min / 1 h / 2 h / until tomorrow |
| Sound | Soft chime at the start and end of a break, synthesized on the fly |
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
powershell -ExecutionPolicy Bypass -File Installer\build-installer.ps1
```

Publishes the app to `dist\app` and compiles
`dist\TwentyMate-Setup-<version>.exe` (~47 MB) — a single file you can put up
online. The version is taken from `<Version>` in `TwentyMate.csproj`
(can be overridden with the `-Version` flag); the `-FrameworkDependent` flag
builds a lightweight variant without .NET bundled in.

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

The built `TwentyMate.exe` will appear in `bin/Release/net8.0-windows/`.
Requires the .NET 8 SDK; running a regular build requires the .NET 8 Desktop
Runtime.

## How it's organized

| File | Purpose |
|---|---|
| [Core/BreakScheduler.cs](Core/BreakScheduler.cs) | Working, break, pause, and non-working-hours states |
| [Core/TrayController.cs](Core/TrayController.cs) | Wires up the scheduler, icon, and windows — decides what to show |
| [Core/TrayIconFactory.cs](Core/TrayIconFactory.cs) | Draws the tray logo tile with GDI+, tracks its color by progress, and caches the result |
| [Assets/generate-icon.py](Assets/generate-icon.py) | Rebuilds `app.ico` from the same geometry and palette as the tray icon |
| [Core/ThemeManager.cs](Core/ThemeManager.cs) | Palette and accent color, synced with the system |
| [Core/WindowEffects.cs](Core/WindowEffects.cs) | Mica, rounded corners, and dark border via DWM |
| [Views/BreakWindow.xaml](Views/BreakWindow.xaml) | Fullscreen break overlay |
| [Views/TrayMenuWindow.xaml](Views/TrayMenuWindow.xaml) | The tray icon's popup menu |
| [Views/SettingsWindow.xaml](Views/SettingsWindow.xaml) | Settings window |
| [Installer/TwentyMate.iss](Installer/TwentyMate.iss) | Inno Setup installer wizard script |
| [Themes/Fluent.xaml](Themes/Fluent.xaml) | Windows 11-style control styles |

Settings and the error log live in `%APPDATA%\TwentyMate\`.
