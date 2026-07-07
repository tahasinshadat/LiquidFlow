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
DefaultDirName={code:GetDefaultDir}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
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
; silent updates (auto-updater / re-run installer with /SILENT): relaunch the app we closed
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent; Check: IsUpdateInstall

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\FluidVoice\Logs"

[Code]
{ ---- update-in-place support ----------------------------------------------
  Detects an existing install (per-user or machine scope, either registry
  view), reuses its folder, skips the wizard pages, closes the running app
  before copying files, and relaunches it after silent updates. }

const
  // NOTE: in [Setup] only the LEADING double-brace of AppId unescapes; the trailing
  // one stays literal, so the registered key really ends in two closing braces + _is1.
  UninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F1A9D4C2-7B3E-4E6A-9C21-FLUIDVOICEWIN}}_is1';

var
  GIsUpdate: Boolean;
  GPrevDir: string;
  GPrevVersion: string;

function DetectPrevious(): Boolean;
var
  Loc, Ver: string;
begin
  Result := False;
  if RegQueryStringValue(HKCU, UninstKey, 'InstallLocation', Loc) or
     RegQueryStringValue(HKLM, UninstKey, 'InstallLocation', Loc) or
     RegQueryStringValue(HKLM32, UninstKey, 'InstallLocation', Loc) then
  begin
    GPrevDir := RemoveBackslashUnlessRoot(Loc);
    if not (RegQueryStringValue(HKCU, UninstKey, 'DisplayVersion', Ver) or
            RegQueryStringValue(HKLM, UninstKey, 'DisplayVersion', Ver) or
            RegQueryStringValue(HKLM32, UninstKey, 'DisplayVersion', Ver)) then
      Ver := 'unknown';
    GPrevVersion := Ver;
    Result := GPrevDir <> '';
  end;
end;

function IsUpdateInstall(): Boolean;
begin
  Result := GIsUpdate;
end;

function GetDefaultDir(Param: string): string;
begin
  if GIsUpdate and (GPrevDir <> '') then
    Result := GPrevDir
  else
    Result := ExpandConstant('{autopf}\{#AppName}');
end;

function InitializeSetup(): Boolean;
begin
  GIsUpdate := DetectPrevious();
  Result := True;
end;

procedure InitializeWizard();
begin
  if GIsUpdate then
  begin
    WizardForm.Caption := 'FluidVoice Update';
    WizardForm.WelcomeLabel1.Caption := 'Update FluidVoice';
    WizardForm.WelcomeLabel2.Caption :=
      'FluidVoice ' + GPrevVersion + ' is already installed.' + #13#10#13#10 +
      'This will update it in place to version {#AppVersion}. Your settings, ' +
      'history, models, and hotkeys are kept.' + #13#10#13#10 +
      'Click Install to continue.';
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { updates are one-click: keep only Welcome -> Install -> Finished }
  Result := GIsUpdate and
    ((PageID = wpLicense) or (PageID = wpSelectDir) or
     (PageID = wpSelectProgramGroup) or (PageID = wpSelectTasks) or
     (PageID = wpReady));
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ResultCode: Integer;
begin
  Result := '';
  { close a running FluidVoice so its files can be replaced }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if GIsUpdate and (CurPageID = wpFinished) then
    WizardForm.FinishedLabel.Caption :=
      'FluidVoice has been updated from ' + GPrevVersion +
      ' to version {#AppVersion}.' + #13#10#13#10 +
      'Your settings, history, and models were kept.';
end;
