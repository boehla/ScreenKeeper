; ScreenKeeper – Installer (Inno Setup 6)
;
; Pro-Benutzer-Installation ohne Adminrechte nach %LOCALAPPDATA%\Programs\ScreenKeeper. Der Ordner bleibt für den
; Benutzer schreibbar, damit das In-App-Update (ZIP laden, EXE ersetzen) auch bei einer installierten Kopie funktioniert.
;
; Aufruf (siehe .github/workflows/release.yml):
;   ISCC.exe /DAppVersion=0.7.0 /DAppVersionNumeric=0.7.0 /DSourceDir=<publish\self-contained> /DOutputDir=<Ziel> installer\ScreenKeeper.iss
;
; SourceDir enthält die self-contained Single-File-EXE und dieselben Begleitdateien wie das ZIP
; (LICENSE, README.md, BEDIENUNG.md, CHANGELOG.md).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppVersionNumeric
  #define AppVersionNumeric "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\self-contained"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "ScreenKeeper"
#define AppExeName "ScreenKeeper.exe"

[Setup]
; Feste AppId – nie ändern, sonst erkennt Setup frühere Installationen nicht mehr
AppId={{623023FB-B240-4374-9C0E-12CAE6BB70C9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Daniel Böhler (boehla)
AppPublisherURL=https://github.com/boehla/ScreenKeeper
AppSupportURL=https://github.com/boehla/ScreenKeeper/issues
AppUpdatesURL=https://github.com/boehla/ScreenKeeper/releases
AppCopyright=Copyright (c) 2026 Daniel Böhler (boehla), MIT-Lizenz
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
; Pro Benutzer, ohne UAC-Abfrage
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=ScreenKeeper-{#AppVersion}-win-x64-setup
SetupIconFile=..\ScreenKeeper\ScreenKeeper.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Läuft ScreenKeeper trotz Mutex-Prüfung noch (z. B. in einer anderen Sitzung), schließt der Restart Manager es;
; nach der Installation startet es über den [Run]-Eintrag neu, nicht über den Restart Manager.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
german.AutostartGroup=Autostart:
english.AutostartGroup=Startup:
german.AutostartTask=ScreenKeeper mit Windows starten
english.AutostartTask=Start ScreenKeeper with Windows
german.AppRunning=ScreenKeeper läuft noch. Bitte über das Tray-Symbol beenden und dann auf OK klicken.
english.AppRunning=ScreenKeeper is still running. Please exit it from the tray icon, then click OK.
german.TaskNotDeleted=Die Aufgabe „%1“ (Autostart mit Adminrechten) konnte ohne Adminrechte nicht entfernt werden. Bitte in der Aufgabenplanung löschen.
english.TaskNotDeleted=The scheduled task "%1" (startup with administrator rights) could not be removed without administrator rights. Please delete it in Task Scheduler.

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AutostartGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Gleicher Inhalt wie ScreenKeeper-<Version>-win-x64.zip
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Exakt wie AutostartService: HKCU\...\Run, Wert "ScreenKeeper" = "<Pfad zur EXE>" in Anführungszeichen, ohne Argumente.
; Nicht bei eingeschaltetem Autostart mit Adminrechten – dann startet die Aufgabe ScreenKeeper (kein Doppelstart).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenKeeper"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart; Check: not IsElevatedAutostartActive

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Benutzerdaten (%APPDATA%\ScreenKeeper, Aufnahmen im Bilder-Ordner) bleiben bei der Deinstallation absichtlich erhalten.

[Code]
const
  ProfileListKey = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList';

{ SID des aktuellen Benutzers über die ProfileList (Profilpfad = %USERPROFILE%); leer, wenn nicht gefunden. }
function GetUserSid(): String;
var
  Names: TArrayOfString;
  I: Integer;
  ProfilePath, UserProfile: String;
begin
  Result := '';
  UserProfile := GetEnv('USERPROFILE');
  if not RegGetSubkeyNames(HKLM, ProfileListKey, Names) then
    Exit;
  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if (Pos('.', Names[I]) = 0) and RegQueryStringValue(HKLM, ProfileListKey + '\' + Names[I], 'ProfileImagePath', ProfilePath) then
    begin
      StringChangeEx(ProfilePath, '%SystemDrive%', GetEnv('SystemDrive'), True);
      if CompareText(ProfilePath, UserProfile) = 0 then
      begin
        Result := Names[I];
        Exit;
      end;
    end;
  end;
end;

{ Wie SingleInstance.MutexName: Local\ScreenKeeper.<SID> (Fallback: Benutzername) }
function GetAppMutexName(): String;
var
  User: String;
begin
  User := GetUserSid();
  if User = '' then
    User := GetUserNameString();
  Result := 'Local\ScreenKeeper.' + User;
end;

{ Wartet, bis ScreenKeeper beendet ist; False = Abbruch durch den Benutzer bzw. im stillen Modus. }
function EnsureAppClosed(Silent: Boolean): Boolean;
var
  MutexName: String;
begin
  Result := True;
  MutexName := GetAppMutexName();
  while CheckForMutexes(MutexName) do
  begin
    if Silent or (MsgBox(CustomMessage('AppRunning'), mbError, MB_OKCANCEL) <> IDOK) then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := EnsureAppClosed(WizardSilent());
end;

function InitializeUninstall(): Boolean;
begin
  Result := EnsureAppClosed(UninstallSilent());
end;

{ Wie ElevatedAutostartService.GetTaskName }
function GetElevatedTaskName(Sid: String): String;
begin
  Result := 'ScreenKeeper\Autostart-' + Sid;
end;

{ Gibt es die Aufgabe? (schtasks /Query ohne Erhöhung) }
function ElevatedTaskExists(Sid: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := (Sid <> '') and Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "' + GetElevatedTaskName(Sid) + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

{ Autostart mit Adminrechten eingeschaltet? Merker in settings.json, wie ihn ScreenKeeper schreibt (eingerückt, camelCase),
  und die Aufgabe ist vorhanden. Bleibt doch ein Run-Wert übrig, entfernt ScreenKeeper ihn beim Start selbst. }
function IsElevatedAutostartActive(): Boolean;
var
  Json: AnsiString;
begin
  Result := False;
  if LoadStringFromFile(ExpandConstant('{userappdata}\ScreenKeeper\settings.json'), Json) then
    Result := (Pos('"elevatedAutostart": true', String(Json)) > 0) and ElevatedTaskExists(GetUserSid());
end;

{ Deinstallation: Aufgabe des Autostarts mit Adminrechten ohne Erhöhung entfernen. ScreenKeeper legt sie so an, dass der
  Benutzer sie löschen darf; klappt es trotzdem nicht, nur ein Hinweis (die Aufgabe zeigt dann auf eine fehlende EXE). }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Sid, TaskName, Schtasks: String;
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  Sid := GetUserSid();
  if Sid = '' then
    Exit;
  if not ElevatedTaskExists(Sid) then
    Exit;
  TaskName := GetElevatedTaskName(Sid);
  Schtasks := ExpandConstant('{sys}\schtasks.exe');
  if Exec(Schtasks, '/Delete /TN "' + TaskName + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    Log('Aufgabe ' + TaskName + ' entfernt')
  else
  begin
    Log('Aufgabe ' + TaskName + ' nicht entfernt (Exit-Code ' + IntToStr(ResultCode) + ')');
    if not UninstallSilent() then
      MsgBox(FmtMessage(CustomMessage('TaskNotDeleted'), [TaskName]), mbInformation, MB_OK);
  end;
end;
