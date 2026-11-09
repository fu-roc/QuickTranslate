; QuickTranslate installer definition for Inno Setup 6.5+
; Build via build-installer.ps1 (passes /DQtExe, /DQtVersion, /DQtStamp, /O).
;
; IMPORTANT: this installer deliberately does NOT create any autostart entry
; (no HKCU\...\Run value, no Startup-folder shortcut). An unsigned freshly
; built program writing a persistence entry triggers Windows Defender's
; Behavior:Win32/Persistence.A!ml heuristic, which quarantines the exe.
; Autostart is offered by the application's own Settings page instead.

#ifndef QtExe
  #error QtExe must point at the published QuickTranslate.exe (use build-installer.ps1)
#endif
#ifndef QtVersion
  #define QtVersion "1.0.0"
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
DefaultDirName={localappdata}\QuickTranslate
DefaultGroupName=QuickTranslate
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputBaseFilename=QuickTranslate-{#QtVersion}-win-x64-setup-{#QtStamp}
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
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Select text, press the hotkey, read the translation"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,QuickTranslate}"; Flags: nowait postinstall skipifsilent
