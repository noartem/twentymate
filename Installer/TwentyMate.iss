; Установщик TwentyMate для Inno Setup 6.
; Собирается скриптом build-installer.ps1, который публикует приложение
; в dist\app и передаёт сюда версию через /DAppVersion.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "TwentyMate"
#define AppExeName "TwentyMate.exe"
#define AppPublisher "TwentyMate"
#define AppDescription "Напоминания о перерывах для глаз по правилу 20-20-20"
#define SourceDir "..\dist\app"

[Setup]
; GUID установщика: менять нельзя — по нему Windows находит прошлую версию для обновления.
AppId={{7D2C4E1A-9F63-4B58-A0D7-3E6C1B84F5A2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppDescription}

; Установка для текущего пользователя — без UAC и прав администратора.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir=..\dist
OutputBaseFilename=TwentyMate-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4

WizardStyle=modern
WizardSizePercent=110
ShowLanguageDialog=auto
; Закрытием запущенной копии занимается PrepareToInstall — встроенный диалог
; Restart Manager для приложения без окон только сбивает с толку.
CloseApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:TaskGroupExtra}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:TaskGroupExtra}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "{#AppDescription}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "{#AppDescription}"; Tasks: desktopicon

[Registry]
; Тот же ключ и формат, что пишет само приложение (Core\StartupManager.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "TwentyMate"; ValueData: """{app}\{#AppExeName}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
russian.TaskGroupExtra=Дополнительно:
russian.TaskAutostart=Запускать при входе в Windows
russian.CreateDesktopIcon=Создать ярлык на рабочем столе
russian.LaunchApp=Запустить TwentyMate
russian.RemoveSettings=Удалить настройки и статистику TwentyMate?%n%nЕсли планируете установить приложение заново, выберите «Нет».
english.TaskGroupExtra=Additional options:
english.TaskAutostart=Start TwentyMate when I sign in to Windows
english.CreateDesktopIcon=Create a desktop shortcut
english.LaunchApp=Launch TwentyMate
english.RemoveSettings=Remove TwentyMate settings and statistics?%n%nChoose No if you plan to reinstall the app.

[Code]

{ Закрывает запущенную копию: иначе файлы приложения заняты и обновление не пройдёт. }
procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { Трею нужно мгновение, чтобы отпустить файлы. }
  Sleep(600);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usUninstall then
    StopRunningApp;

  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\TwentyMate');
    if DirExists(SettingsDir) then
      if SuppressibleMsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO, IDNO) = IDYES then
        DelTree(SettingsDir, True, True, True);
  end;
end;
