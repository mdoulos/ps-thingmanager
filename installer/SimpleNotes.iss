; Inno Setup script for Simple Notes.
; Build with:  ISCC.exe installer\SimpleNotes.iss
; Override the version:  ISCC.exe /DMyAppVersion=1.2.0 installer\SimpleNotes.iss
;
; This packages the self-contained publish output (..\publish) into a single
; SimpleNotesSetup.exe that installs per-user (no admin needed), creates Start
; menu / optional desktop shortcuts, and registers an uninstaller. Because the
; AppId is stable, re-running a newer installer upgrades the app in place.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Simple Notes"
#define MyAppPublisher "mdoulos"
#define MyAppExeName "SimpleNotes.exe"

[Setup]
; A fixed GUID identifies the app across versions so upgrades replace it.
AppId={{A7F3C2E1-5B4D-4E9A-9C21-8F6D2B1A3C4E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\SimpleNotes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SimpleNotesSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; Install per-user so updating never needs an admin prompt.
PrivilegesRequired=lowest
; Cleanly close a running copy (e.g. during an in-app update) and relaunch it.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Everything produced by `dotnet publish ... -o publish`.
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
