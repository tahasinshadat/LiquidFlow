; Inno Setup script for FluidVoice for Windows
; Build via windows/installer/build.ps1 which passes /DArch and /DSourceDir.

#ifndef Arch
  #define Arch "arm64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\arm64"
#endif
#ifndef AppVersion
  #define AppVersion "1.6.2"
#endif

#define AppName "FluidVoice"
#define AppPublisher "FluidVoice contributors"
#define AppURL "https://github.com/altic-dev/FluidVoice"
#define AppExe "FluidVoice.exe"

[Setup]
AppId={{F1A9D4C2-7B3E-4E6A-9C21-FLUIDVOICEWIN}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=FluidVoice-Setup-{#AppVersion}-{#Arch}
OutputDir=..\dist
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
LicenseFile=..\..\LICENSE
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Launch FluidVoice at Windows startup"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch FluidVoice"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\FluidVoice\Logs"
