; Inno Setup script for Purple Star Notes.
; Build with:  ISCC.exe installer\PurpleStarNotes.iss
; Override the version:  ISCC.exe /DMyAppVersion=1.2.0 installer\PurpleStarNotes.iss
;
; This packages the self-contained publish output (..\publish) into a single
; PurpleStarNotesSetup.exe that installs per-user (no admin needed), creates
; Start menu / optional desktop shortcuts, and registers an uninstaller.
; Because the AppId is stable, re-running a newer installer upgrades in place.

#ifndef MyAppVersion
  #define MyAppVersion "1.1.3"
#endif

#define MyAppName "Purple Star Notes"
#define MyAppPublisher "mdoulos"
#define MyAppExeName "PurpleStarNotes.exe"

[Setup]
; A fixed GUID identifies the app across versions so upgrades replace it.
AppId={{B2E1D4C3-6A5F-4B8E-9D21-7C3E2F1A4B5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\PurpleStarNotes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=PurpleStarNotesSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
