; AION2 DPS Meter — Windows installer (Inno Setup 7, https://jrsoftware.org).
; Built by build-release.ps1:
;   ISCC /DAppVer=<x.y.z> /DPayloadDir=<self-contained publish folder> /O<output folder> AION2DpsMeter.iss
; Installs for the current user only (no administrator prompt) into %LocalAppData%\Programs\AION2 DPS Meter.
; Settings, fight history and boss timers stay in %AppData%\AionMeter; uninstall asks before deleting them.

#ifndef AppVer
  #define AppVer "0.0.0"
#endif
#ifndef PayloadDir
  #error Pass /DPayloadDir=<folder with the published program>
#endif
#define AppName "AION2 DPS Meter"
#define AppExe "AION2DpsMeter.exe"

[Setup]
; Identifies the program for upgrades and uninstall: never change it.
AppId={{AE1B2590-0A93-4500-9CB1-0F543771957A}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/cyberbadger6969/aion2-dps-meter
AppSupportURL=https://github.com/cyberbadger6969/aion2-dps-meter/issues
AppUpdatesURL=https://github.com/cyberbadger6969/aion2-dps-meter/releases
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
MinVersion=10.0
OutputBaseFilename=AION2DpsMeter-Setup-v{#AppVer}
SetupIconFile=..\src\AionMeter.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; The meter's colours: navy background, gold emblem.
WizardStyle=modern dark hidebevels
WizardBackColor=#111726
WizardImageFile=wizard.png
WizardSmallImageFile=wizard-small.png
Compression=lzma2/ultra64
SolidCompression=yes
; An update closes the running meter (only the copy installed here) through Restart Manager.
CloseApplications=force
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.NpcapTitle=Npcap is needed
en.NpcapSubtitle=A free capture driver: the meter sees the game's traffic through it.
en.NpcapText=Npcap is not installed on this computer yet. Without it the meter starts but cannot see the game.%n%n1. Click "Open npcap.com" and download the Npcap installer.%n2. Run it and keep the default options.%n3. Come back here and click Next.%n%nNpcap cannot be bundled with this installer: its licence does not allow that.
en.NpcapOpen=Open npcap.com
en.NpcapRecheck=Check again
en.NpcapFound=✔  Npcap found, you are all set.
en.NpcapMissing=✖  Npcap not found yet.
en.NpcapContinue=Npcap is still not installed. Install the meter anyway?%n%nThe meter will remind you about Npcap when it starts.
en.CloseRunning=AION2 DPS Meter is running. Close it and continue?
en.RemoveUserData=Also delete your settings, fight history and boss timers?%n%n%1
ru.NpcapTitle=Нужен Npcap
ru.NpcapSubtitle=Бесплатный драйвер захвата трафика: через него метр видит игру.
ru.NpcapText=Npcap на этом компьютере пока не установлен. Без него метр запустится, но не увидит игру.%n%n1. Нажмите «Открыть npcap.com» и скачайте установщик Npcap.%n2. Запустите его и ничего не меняйте в настройках.%n3. Вернитесь сюда и нажмите «Далее».%n%nВстроить Npcap в этот установщик нельзя: это запрещает его лицензия.
ru.NpcapOpen=Открыть npcap.com
ru.NpcapRecheck=Проверить снова
ru.NpcapFound=✔  Npcap найден, всё готово.
ru.NpcapMissing=✖  Npcap пока не найден.
ru.NpcapContinue=Npcap всё ещё не установлен. Установить метр без него?%n%nМетр напомнит про Npcap при запуске.
ru.CloseRunning=AION2 DPS Meter запущен. Закрыть его и продолжить?
ru.RemoveUserData=Удалить также настройки, историю боёв и таймеры боссов?%n%n%1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  NpcapPage: TWizardPage;
  NpcapStatus: TNewStaticText;

// Same test as the meter itself (LiveCapture.IsNpcapInstalled). Setup is 64-bit, so {sys} is the real System32.
function NpcapInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll'));
end;

procedure UpdateNpcapStatus;
begin
  if NpcapInstalled then
    NpcapStatus.Caption := CustomMessage('NpcapFound')
  else
    NpcapStatus.Caption := CustomMessage('NpcapMissing');
end;

procedure OpenNpcapClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'https://npcap.com/#download', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure RecheckClick(Sender: TObject);
begin
  UpdateNpcapStatus;
end;

// A page after "Additional tasks" that explains Npcap; skipped when Npcap is already there.
procedure InitializeWizard;
var
  Info: TNewStaticText;
  OpenButton, RecheckButton: TNewButton;
begin
  NpcapPage := CreateCustomPage(wpSelectTasks, CustomMessage('NpcapTitle'), CustomMessage('NpcapSubtitle'));

  Info := TNewStaticText.Create(NpcapPage);
  Info.Parent := NpcapPage.Surface;
  Info.AutoSize := False;
  Info.WordWrap := True;
  Info.Width := NpcapPage.SurfaceWidth;
  Info.Caption := CustomMessage('NpcapText');
  Info.AdjustHeight;

  OpenButton := TNewButton.Create(NpcapPage);
  OpenButton.Parent := NpcapPage.Surface;
  OpenButton.Caption := CustomMessage('NpcapOpen');
  OpenButton.Top := Info.Top + Info.Height + ScaleY(14);
  OpenButton.Width := ScaleX(160);
  OpenButton.Height := WizardForm.NextButton.Height;
  OpenButton.OnClick := @OpenNpcapClick;

  RecheckButton := TNewButton.Create(NpcapPage);
  RecheckButton.Parent := NpcapPage.Surface;
  RecheckButton.Caption := CustomMessage('NpcapRecheck');
  RecheckButton.Top := OpenButton.Top;
  RecheckButton.Left := OpenButton.Left + OpenButton.Width + ScaleX(10);
  RecheckButton.Width := ScaleX(140);
  RecheckButton.Height := OpenButton.Height;
  RecheckButton.OnClick := @RecheckClick;

  NpcapStatus := TNewStaticText.Create(NpcapPage);
  NpcapStatus.Parent := NpcapPage.Surface;
  NpcapStatus.Top := OpenButton.Top + OpenButton.Height + ScaleY(14);
  NpcapStatus.Font.Style := [fsBold];
  UpdateNpcapStatus;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (NpcapPage <> nil) and (PageID = NpcapPage.ID) and NpcapInstalled;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (NpcapPage <> nil) and (CurPageID = NpcapPage.ID) then
    UpdateNpcapStatus;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (NpcapPage <> nil) and (CurPageID = NpcapPage.ID) and not NpcapInstalled then
    Result := MsgBox(CustomMessage('NpcapContinue'), mbConfirmation, MB_YESNO) = IDYES;
end;

// Uninstall: the meter keeps its files open, so close the copy that runs from this folder (a portable copy
// elsewhere is left alone). Returns False when the user prefers to keep it running.
function CloseRunningMeter(Ask: Boolean): Boolean;
var
  Locator, Wmi, Procs, Proc: Variant;
  I, Pid, ExitCode: Integer;
  Dir, Path: String;
  Asked: Boolean;
begin
  Result := True;
  Asked := False;
  Dir := AddBackslash(ExpandConstant('{app}'));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Wmi.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''{#AppExe}''');
    for I := 0 to Procs.Count - 1 do
    begin
      Proc := Procs.ItemIndex(I);
      if Result and not VarIsNull(Proc.ExecutablePath) then
      begin
        Path := Proc.ExecutablePath;
        if CompareText(Copy(Path, 1, Length(Dir)), Dir) = 0 then
        begin
          if Ask and not Asked then
          begin
            Asked := True;
            Result := MsgBox(CustomMessage('CloseRunning'), mbConfirmation, MB_YESNO) = IDYES;
          end;
          if Result then
          begin
            Pid := Proc.ProcessId;
            Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /PID ' + IntToStr(Pid), '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
          end;
        end;
      end;
    end;
  except
    // No WMI: uninstall goes on and reports any file it could not remove, as usual.
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningMeter(not UninstallSilent);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    Data := ExpandConstant('{userappdata}\AionMeter');
    if DirExists(Data) and
       (MsgBox(FmtMessage(CustomMessage('RemoveUserData'), [Data]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(Data, True, True, True);
  end;
end;
