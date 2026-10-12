; Schleimige Screenshots - Installer (nutzerweit, ohne Administratorrechte)
;
; Bauen:  Windows : build.ps1   (erkennt ISCC.exe von Inno Setup 6 automatisch)
;         Linux   : installer/build.sh   (Docker + Inno Setup unter Wine)
; Direkt: iscc /DAppVersion=1.0.1 installer\SchleimigeScreenshots.iss  ->  dist\SchleimigeScreenshots-Setup-1.0.1.exe
;
; Hinweis: Diese Datei ist UTF-8 mit BOM (wegen der Umlaute) - beim Bearbeiten so beibehalten.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\SchleimigeScreenshots.exe"
#endif

#define AppName      "Schleimige Screenshots"
#define AppExe       "SchleimigeScreenshots.exe"
#define AppPublisher "Treaffox"
#define AppUrl       "https://github.com/Treaffox/schleimigescreenshots"
; Name des Single-Instance-Mutex der App (Program.cs) - damit erkennt der Installer eine laufende Instanz
#define AppMutex     "SchleimigeScreenshots.SingleInstance"

[Setup]
; Feste ID: sorgt dafuer, dass neue Versionen die alte Installation aktualisieren statt eine zweite anzulegen
AppId={{D10936D0-0FD3-4D87-AEEA-5D68F1050114}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
VersionInfoCompany={#AppPublisher}

; Nutzerweit: keine Administratorrechte, Standardziel ist %LocalAppData%\Programs\Schleimige Screenshots
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393

; Laufende Instanz beenden wir selbst (siehe [Code]); kein Restart-Manager-Dialog
CloseApplications=no
SetupMutex=SchleimigeScreenshotsSetupMutex

OutputDir=..\dist
OutputBaseFilename=SchleimigeScreenshots-Setup-{#AppVersion}
SetupIconFile=..\src\App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard-large.png
WizardSmallImageFile=wizard-small.png
WizardImageBackColor=$2A170F
ShowLanguageDialog=auto
; Die EXE ist bereits komprimiert - stärkere Kompression kostet nur Zeit
Compression=lzma2/fast
SolidCompression=yes

[Languages]
Name: "german";  MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
german.TaskAutostart=Mit Windows starten (der Hotkey ist dann nach der Anmeldung sofort verfügbar)
german.TaskGroupOptions=Weitere Optionen:
german.StopFailed=%1 läuft noch und konnte nicht beendet werden.%n%nBitte im Infobereich der Taskleiste mit der rechten Maustaste auf das Symbol klicken, „Beenden“ wählen und danach erneut versuchen.
german.RemoveUserData=Sollen auch die Einstellungen und Protokolle von %1 gelöscht werden?
english.TaskAutostart=Start with Windows (the hotkey is available right after sign-in)
english.TaskGroupOptions=Additional options:
english.StopFailed=%1 is still running and could not be closed.%n%nPlease right-click its icon in the notification area, choose "Exit", and try again.
english.RemoveUserData=Do you also want to delete the settings and logs of %1?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart";   Description: "{cm:TaskAutostart}";      GroupDescription: "{cm:TaskGroupOptions}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Startmenü (Alle Apps / Suche; dort lässt sich der Eintrag per Rechtsklick an "Start" anheften) und Desktop
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Gleicher Eintrag wie die Autostart-Option in den App-Einstellungen (AutoStart.cs)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SchleimigeScreenshots"; \
  ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Stilles Update durch die App selbst (Parameter /RESTARTAPP=1): danach die neue Version starten
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: RestartRequested

[Code]
const
  RunKey   = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'SchleimigeScreenshots';

var
  InstallReached: Boolean;

{ Der Updater der App ruft das Setup mit /RESTARTAPP=1 auf }
function RestartRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;

function AppIsRunning(): Boolean;
begin
  Result := CheckForMutexes('{#AppMutex}');
end;

procedure Taskkill(const Params: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function WaitForAppExit(Milliseconds: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while AppIsRunning() and (Waited < Milliseconds) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  Result := not AppIsRunning();
end;

{ Beendet eine laufende Instanz: erst höflich (WM_CLOSE, die App beendet sich dann sauber), sonst hart.
  Die App speichert Einstellungen sofort - es gehen keine Daten verloren. }
function StopApp(): Boolean;
begin
  if AppIsRunning() then
  begin
    Taskkill('/IM {#AppExe}');
    if not WaitForAppExit(5000) then
    begin
      Taskkill('/F /IM {#AppExe}');
      WaitForAppExit(3000);
    end;
  end;
  Result := not AppIsRunning();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopApp() then
    Result := FmtMessage(CustomMessage('StopFailed'), ['{#AppName}']);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then InstallReached := True;
end;

{ Sicherheitsnetz für Updates: scheitert die Installation, nachdem die App beendet wurde (z. B. Virenscanner, Platte voll),
  die vorhandene Version wieder starten, damit der Nutzer nicht ohne laufende App dasteht. }
procedure DeinitializeSetup();
var
  Exe: String;
  ResultCode: Integer;
begin
  if RestartRequested() and (not InstallReached) then
  begin
    Exe := AddBackslash(WizardDirValue) + '{#AppExe}';
    if FileExists(Exe) then
      Exec(Exe, '--restarted', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopApp();
  if not Result then
    MsgBox(FmtMessage(CustomMessage('StopFailed'), ['{#AppName}']), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Autostart-Eintrag auch entfernen, wenn er über die App-Einstellungen angelegt wurde }
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, RunValue);

    { Einstellungen/Protokolle nur auf ausdrücklichen Wunsch löschen (Standard: behalten) }
    if not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('RemoveUserData'), ['{#AppName}']), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(ExpandConstant('{userappdata}\SchleimigeScreenshots'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\SchleimigeScreenshots'), True, True, True);
      end;
  end;
end;
