; YT-Rec.iss — Inno Setup script that wraps the self-contained publish folder into a single
; YT-Rec-Setup.exe.  Replaces the old "unzip a folder of ~hundreds of files" portable build with a
; proper installer: one file to run, a Start-menu + desktop shortcut, and a clean uninstall entry in
; Settings ▸ Apps.  Per-user install (PrivilegesRequired=lowest) → no admin, no UAC prompt.  Still
; UNSIGNED (ADR-0005) → first launch shows the SmartScreen "More info ▸ Run anyway" step, same as before.
;
; Compiled by windows/installer/build-installer.ps1, which passes AppVersion / PublishDir / OutputDir via /D.

#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\YtRec.App\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "YT Rec"
#define AppExe "YtRec.exe"
#define AppPublisher "Resona Frame"
#define AppIco "..\YtRec.App\Assets\AppIcon.ico"

[Setup]
; A stable AppId ties upgrades + uninstall together across versions — never change it.
AppId={{8A1E0C2F-3B4D-4E6A-9C7F-2D5B1A4E9F30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://ytrec.resonaframe.com
AppSupportURL=https://ytrec.resonaframe.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#AppIco}
OutputDir={#OutputDir}
OutputBaseFilename=YT-Rec-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
; Traditional Chinese wizard for the zh-TW audience when that translation is present on the build box;
; English otherwise.  #ifexist keeps the script compiling on a vanilla Inno Setup install either way.
#ifexist "compiler:Languages\ChineseTraditional.isl"
Name: "cht"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
