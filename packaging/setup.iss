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

#ifndef QtExe
  #error QtExe must point at the published QuickTranslate.exe (use build-installer.ps1)
#endif
#ifndef QtVersion
  ; 直接跑 ISCC 时的兜底版本；正常构建由 build-installer.ps1 从 csproj 传入
  #define QtVersion "1.0.3"
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
AppMutex=QuickTranslate.Singleton
CloseApplications=yes
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
