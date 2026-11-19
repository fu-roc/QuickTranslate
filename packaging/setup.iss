; Machine-wide installer built by build-installer.ps1 with Inno Setup 6.5+.

#ifndef QtExe
  #error QtExe must point at the published QuickTranslate.exe (use build-installer.ps1)
#endif
#ifndef QtVersion
  #error QtVersion must be set by build-installer.ps1
#endif
#ifndef QtStamp
  #error QtStamp must be set by build-installer.ps1
#endif

#define MyAppName "QuickTranslate"
#define MyAppExeName "QuickTranslate.exe"
#define MyAppPublisher "fu-roc"
#define MyAppURL "https://github.com/fu-roc/QuickTranslate"

[Setup]
AppId={{8F3D6A21-4C7B-4E95-A1D2-6B90C5E83F17}}
AppName={#MyAppName}
AppVersion={#QtVersion}
AppVerName={#MyAppName} {#QtVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\QuickTranslate
DefaultGroupName=QuickTranslate
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputBaseFilename=QuickTranslate-{#QtVersion}-win-x64-setup-{#QtStamp}
SetupIconFile=..\src\QuickTranslate\Assets\QuickTranslate.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#QtVersion}
; The install step closes the tray process before copying files.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#QtExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Select text, press the hotkey, read the translation"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,QuickTranslate}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  QtMutexName = 'QuickTranslate.Singleton';

procedure QtSleep(Milliseconds: Cardinal);
  external 'Sleep@kernel32.dll stdcall';

// Closing the window only hides the tray application.
procedure KillRunningApp;
var
  ResultCode, Retry: Integer;
begin
  for Retry := 1 to 3 do
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM QuickTranslate.exe /F',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Wait for the singleton mutex to disappear before files are overwritten.
    if not CheckForMutexes(QtMutexName) then
    begin
      Log('QuickTranslate: instance check passed before file copy (attempt ' + IntToStr(Retry) + ')');
      Exit;
    end;
    QtSleep(300);
  end;
  Log('QuickTranslate: running instance did not exit in time; setup will continue');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // ssInstall fires just before file copying. Cancelling the wizard therefore
  // never touches the running tray app.
  if CurStep = ssInstall then KillRunningApp;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  KillRunningApp;
end;
