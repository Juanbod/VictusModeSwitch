#ifndef AppVersion
  #define AppVersion "2.2.0"
#endif

#define AppName "Victus Mode Switch"
#define AppExeName "VictusModeSwitch.exe"
#define Publisher "Juanbod"
#define ProjectUrl "https://github.com/Juanbod/VictusModeSwitch"

[Setup]
AppId={{A6D4D784-E68E-4BA1-9EE5-E7B110F32D61}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
DefaultDirName={localappdata}\Programs\VictusModeSwitch
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=VictusModeSwitch-Setup
SetupIconFile=..\assets\VictusModeSwitch.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
SetupLogging=yes
InfoBeforeFile=WARNING.txt
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
AppMutex=Local\VictusModeSwitch.8A4F

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Tasks]
Name: "pauseomen"; Description: "{cm:PauseOmen}"; Flags: checkedonce

[CustomMessages]
english.PauseOmen=Pause OMEN Gaming Hub background helpers (reversible on uninstall)
russian.PauseOmen=Отключить фоновые помощники OMEN Gaming Hub (отменяется при удалении)
ukrainian.PauseOmen=Вимкнути фонові помічники OMEN Gaming Hub (скасовується під час видалення)

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Install.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\Configure-Elevated.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\BiosBroker.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\BiosBroker.vbs"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\Uninstall.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\Remove-Elevated.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\Install.ps1"; Flags: dontcopy
Source: "..\scripts\Configure-Elevated.ps1"; Flags: dontcopy
Source: "..\scripts\BiosBroker.ps1"; Flags: dontcopy
Source: "..\scripts\BiosBroker.vbs"; Flags: dontcopy

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Broker"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--settings"; WorkingDir: "{app}"
Name: "{autoprograms}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[CustomMessages]
english.ConfiguringApp=Configuring the hardware helper and startup entry...
russian.ConfiguringApp=Настройка аппаратного помощника и автозапуска...
ukrainian.ConfiguringApp=Налаштування апаратного помічника та автозапуску...
english.ConfigurationFailed=Hardware configuration failed (exit code %d). Setup cannot continue.
russian.ConfigurationFailed=Настройка оборудования завершилась ошибкой (код %d). Установка не может быть продолжена.
ukrainian.ConfigurationFailed=Налаштування обладнання завершилося помилкою (код %d). Встановлення не може бути продовжене.
english.ConfigurationLaunchFailed=Could not start hardware configuration: %s
russian.ConfigurationLaunchFailed=Не удалось запустить настройку оборудования: %s
ukrainian.ConfigurationLaunchFailed=Не вдалося запустити налаштування обладнання: %s
english.PreflightFailed=Hardware preflight failed (exit code %d). No application files were changed.
russian.PreflightFailed=Предварительная проверка оборудования завершилась ошибкой (код %d). Файлы программы не изменены.
ukrainian.PreflightFailed=Попередня перевірка обладнання завершилася помилкою (код %d). Файли програми не змінено.
english.CleanupFailed=Hardware cleanup failed (exit code %d). Uninstall was stopped to preserve the recovery files.
russian.CleanupFailed=Очистка оборудования завершилась ошибкой (код %d). Удаление остановлено, чтобы сохранить файлы восстановления.
ukrainian.CleanupFailed=Очищення обладнання завершилося помилкою (код %d). Видалення зупинено, щоб зберегти файли відновлення.

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Parameters: String;
begin
  Result := '';
  ExtractTemporaryFile('Install.ps1');
  ExtractTemporaryFile('Configure-Elevated.ps1');
  ExtractTemporaryFile('BiosBroker.ps1');
  ExtractTemporaryFile('BiosBroker.vbs');

  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Install.ps1') + '" -PreflightOnly';
  if WizardIsTaskSelected('pauseomen') then
    Parameters := Parameters + ' -PauseOmen';

  if not Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters,
    ExpandConstant('{tmp}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result := Format(
      ExpandConstant('{cm:ConfigurationLaunchFailed}'), [SysErrorMessage(ResultCode)]);
    Exit;
  end;

  if ResultCode <> 0 then
    Result := Format(ExpandConstant('{cm:PreflightFailed}'), [ResultCode]);
end;

procedure RunConfiguration;
var
  ResultCode: Integer;
  Parameters: String;
begin
  WizardForm.StatusLabel.Caption := ExpandConstant('{cm:ConfiguringApp}');
  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\scripts\Install.ps1') + '" -ConfigureOnly';
  if WizardIsTaskSelected('pauseomen') then
    Parameters := Parameters + ' -PauseOmen';

  if not Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    RaiseException(Format(
      ExpandConstant('{cm:ConfigurationLaunchFailed}'), [SysErrorMessage(ResultCode)]));
  end;

  if ResultCode <> 0 then
    RaiseException(Format(ExpandConstant('{cm:ConfigurationFailed}'), [ResultCode]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RunConfiguration;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Parameters: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\scripts\Uninstall.ps1') + '"';
  if not Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    RaiseException(Format(
      ExpandConstant('{cm:ConfigurationLaunchFailed}'), [SysErrorMessage(ResultCode)]));
  end;

  if ResultCode <> 0 then
    RaiseException(Format(ExpandConstant('{cm:CleanupFailed}'), [ResultCode]));
end;
