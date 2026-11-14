; QuickTranslate installer definition for Inno Setup 6.5+
; Build via build-installer.ps1 (passes /DQtExe, /DQtVersion, /DQtStamp, /O).
;
; Machine-wide (system-level) install: requires elevation, installs to
; Program Files, creates all-users shortcuts and an HKLM uninstall entry.
;
; IMPORTANT: this installer deliberately does NOT create any autostart entry
; (no HKCU\...\Run value, no Startup-folder shortcut). An unsigned freshly
; built program writing a persistence entry triggers Windows Defender's
; Behavior:Win32/Persistence.A!ml heuristic, which quarantines the exe.
; Autostart is offered by the application's own Settings page instead.
;
; [Run] uses runasoriginaluser so the app does NOT inherit the installer's
; elevated token: it must stay a normal asInvoker tray process.
;
; Upgrades are unattended: the running tray instance is closed automatically
; right before files are copied (see [Code]). No AppMutex prompt and no Restart
; Manager "close applications" wizard page are shown.

#ifndef QtExe
  #error QtExe must point at the published QuickTranslate.exe (use build-installer.ps1)
#endif
#ifndef QtVersion
  ; 直接跑 ISCC 时的兜底版本；正常构建由 build-installer.ps1 从 csproj 传入
  #define QtVersion "1.1.0"
#endif
#ifndef QtStamp
  #define QtStamp "dev"
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
; Deliberately NO AppMutex: it shows "please close all instances" at startup,
; and even /VERYSILENT does not suppress that dialog. The running instance is
; closed by [Code] instead, only once the user actually starts the install.
; CloseApplications=no because yes/force both pop a "close applications" page
; on the Preparing to Install wizard; there is nothing left to close by then.
CloseApplications=no
RestartApplications=no
; No [Registry] / Startup-folder entries on purpose, see the header comment.

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

// The app is a ShutdownMode=OnExplicitShutdown tray process: closing its
// window only hides it. Its config is written atomically and nothing is left
// unsaved, so terminating the process is safe. WM_CLOSE / Restart Manager
// cannot reliably exit it, which is why we do this explicitly.
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
