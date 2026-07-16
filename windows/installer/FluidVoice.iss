; Inno Setup script for LiquidFlow for Windows (formerly FluidVoice)
; Build via windows/installer/build.ps1 which passes /DArch and /DSourceDir.

#ifndef Arch
  #define Arch "arm64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\arm64"
#endif
#ifndef AppVersion
  #define AppVersion "1.7.4"
#endif

#define AppName "LiquidFlow"
#define AppPublisher "LiquidFlow"
#define AppURL "https://github.com/altic-dev/FluidVoice"
#define AppExe "LiquidFlow.exe"

[Setup]
; New product identity (was FluidVoice). Migration from the old FluidVoice install is handled
; in [Code] (relaunch/one-click) + by the app (moves %LOCALAPPDATA%\FluidVoice -> \LiquidFlow).
AppId={{B2E5F8A1-3C7D-4E9B-A6F2-LIQUIDFLOWWIN}}
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
OutputBaseFilename=LiquidFlow-Setup-{#AppVersion}-{#Arch}
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
Name: "startupicon"; Description: "Launch LiquidFlow at Windows startup"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch LiquidFlow"; Flags: nowait postinstall skipifsilent
; silent updates (auto-updater / re-run installer with /SILENT): relaunch the app we closed
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent; Check: IsUpdateInstall

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\LiquidFlow\Logs"

[Code]
{ ---- update-in-place + FluidVoice->LiquidFlow migration -----------------------
  Detects an existing LiquidFlow install (in-place update) OR an existing FluidVoice
  install (migration). In both cases the wizard is one-click and the app is closed
  before copying and relaunched after a silent install. The old FluidVoice folder /
  Add-Remove entry is cleaned up separately after the data migration is confirmed. }

const
  // trailing '}}' is intentional (see the FluidVoice note): the registered key ends in }}_is1.
  NewUninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B2E5F8A1-3C7D-4E9B-A6F2-LIQUIDFLOWWIN}}_is1';
  OldUninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F1A9D4C2-7B3E-4E6A-9C21-FLUIDVOICEWIN}}_is1';

var
  GIsUpdate: Boolean;   // existing LiquidFlow install
  GMigrate: Boolean;    // existing FluidVoice install to supersede
  GPrevDir: string;
  GPrevVersion: string;

function ReadInstall(Key: string; var Loc: string; var Ver: string): Boolean;
begin
  Result := False;
  if RegQueryStringValue(HKCU, Key, 'InstallLocation', Loc) or
     RegQueryStringValue(HKLM, Key, 'InstallLocation', Loc) or
     RegQueryStringValue(HKLM32, Key, 'InstallLocation', Loc) then
  begin
    Loc := RemoveBackslashUnlessRoot(Loc);
    if not (RegQueryStringValue(HKCU, Key, 'DisplayVersion', Ver) or
            RegQueryStringValue(HKLM, Key, 'DisplayVersion', Ver) or
            RegQueryStringValue(HKLM32, Key, 'DisplayVersion', Ver)) then
      Ver := 'unknown';
    Result := Loc <> '';
  end;
end;

function InitializeSetup(): Boolean;
var
  Loc, Ver: string;
begin
  GIsUpdate := False;
  GMigrate := False;
  if ReadInstall(NewUninstKey, Loc, Ver) then
  begin
    GIsUpdate := True;
    GPrevDir := Loc;
    GPrevVersion := Ver;
  end
  else if ReadInstall(OldUninstKey, Loc, Ver) then
  begin
    GMigrate := True;
    GPrevVersion := Ver;  { old FluidVoice version, for the wizard text }
  end;
  Result := True;
end;

function IsUpdateInstall(): Boolean;
begin
  Result := GIsUpdate or GMigrate;  { relaunch after a silent install in both cases }
end;

function GetDefaultDir(Param: string): string;
begin
  if GIsUpdate and (GPrevDir <> '') then
    Result := GPrevDir
  else
    Result := ExpandConstant('{autopf}\{#AppName}');  { Programs\LiquidFlow }
end;

procedure InitializeWizard();
begin
  if GIsUpdate then
  begin
    WizardForm.Caption := 'LiquidFlow Update';
    WizardForm.WelcomeLabel1.Caption := 'Update LiquidFlow';
    WizardForm.WelcomeLabel2.Caption :=
      'LiquidFlow ' + GPrevVersion + ' is already installed.' + #13#10#13#10 +
      'This will update it in place to version {#AppVersion}. Your settings, ' +
      'history, models, and hotkeys are kept.' + #13#10#13#10 + 'Click Install to continue.';
  end
  else if GMigrate then
  begin
    WizardForm.Caption := 'LiquidFlow';
    WizardForm.WelcomeLabel1.Caption := 'Install LiquidFlow';
    WizardForm.WelcomeLabel2.Caption :=
      'FluidVoice ' + GPrevVersion + ' is installed. This app has been renamed to LiquidFlow.' + #13#10#13#10 +
      'This installs LiquidFlow and carries over your settings, history, models, and hotkeys.' + #13#10#13#10 +
      'Click Install to continue.';
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { updates and migration are one-click: keep only Welcome -> Install -> Finished }
  Result := (GIsUpdate or GMigrate) and
    ((PageID = wpLicense) or (PageID = wpSelectDir) or
     (PageID = wpSelectProgramGroup) or (PageID = wpSelectTasks) or
     (PageID = wpReady));
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ResultCode: Integer;
begin
  Result := '';
  { close a running LiquidFlow (and the pre-rename FluidVoice) so its files can be replaced }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe} /IM FluidVoice.exe', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (GIsUpdate or GMigrate) and (CurPageID = wpFinished) then
    WizardForm.FinishedLabel.Caption :=
      'LiquidFlow {#AppVersion} is installed.' + #13#10#13#10 +
      'Your settings, history, and models were kept.';
end;
