#ifndef AppVersion
  #define AppVersion "0.2.3"
#endif
#ifndef FileVersion
  #define FileVersion AppVersion + ".0"
#endif
#ifndef OutputVersion
  #define OutputVersion AppVersion
#endif
#define TrayMutex "Local\Jige.Battery.Tray"
#define SetupGate "Local\Jige.Battery.Setup"
#define InstallDir "{localappdata}\Programs\Jige"
#define DataDir "{localappdata}\Jige"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunName "Jige Battery"

[Setup]
AppId={{DD33BE43-6D23-45E0-936F-9B6B067067E8}
AppName=几格
AppVersion={#AppVersion}
AppPublisher=FourJ
AppPublisherURL=https://github.com/deepfry666/jige-battery
AppUpdatesURL=https://github.com/deepfry666/jige-battery/releases/latest
VersionInfoVersion={#FileVersion}
DefaultDirName={#InstallDir}
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex={#TrayMutex}
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Jige.exe
; 旧卸载日志里有强制删除数据目录的规则。安装项沿用原路径，重新生成卸载日志。
UninstallLogMode=overwrite
UsePreviousTasks=no
SetupIconFile=..\assets\jige.ico
OutputDir=..\artifacts\release
OutputBaseFilename=Jige-Setup-{#OutputVersion}-x64
Compression=lzma2
SolidCompression=yes

[Languages]
#if FileExists(AddBackslash(CompilerPath) + "Languages\ChineseSimplified.isl")
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "autostart"; Description: "开机时启动几格"

[Files]
Source: "..\artifacts\tray-win-x64\Jige.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\tray-win-x64\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\几格"; Filename: "{app}\Jige.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "{#RunName}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunName}"; ValueData: """{app}\Jige.exe"""; Tasks: autostart; Check: NeedsInitialConfig

[Run]
Filename: "{app}\Jige.exe"; Parameters: "--after-setup"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent
Filename: "{app}\Jige.exe"; Parameters: "--after-setup"; WorkingDir: "{app}"; Description: "启动几格"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
function CreateNamedMutex(Attributes: THandle; InitialOwner: Integer; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

var
  OperationGate: THandle;
  InitialConfig: Boolean;

// 同一个互斥量串行化安装与卸载，并阻止托盘在文件替换期间从快捷方式或自启启动。
function BeginOperation: Boolean;
var
  ErrorCode: Integer;
begin
  OperationGate := CreateNamedMutex(0, 0, '{#SetupGate}');
  ErrorCode := DLLGetLastError();
  Result := (OperationGate <> 0) and (ErrorCode <> 183); // ERROR_ALREADY_EXISTS
  if not Result then
    SuppressibleMsgBox('几格安装或卸载正在进行，或无法取得安装锁。请稍后再试。', mbError, MB_OK, IDOK);
end;

procedure EndOperation;
begin
  if OperationGate <> 0 then
  begin
    CloseHandle(OperationGate);
    OperationGate := 0;
  end;
end;

procedure CloseTray;
var
  Host: HWND;
  Attempt: Integer;
begin
  for Attempt := 1 to 80 do
  begin
    if not CheckForMutexes('{#TrayMutex}') then
      Exit;
    if (Attempt = 1) or (Attempt mod 10 = 0) then
    begin
      Host := FindWindowByClassName('Jige.Tray.Host');
      if Host <> 0 then
        PostMessage(Host, $0010, 0, 0); // WM_CLOSE，等待托盘保存状态并退出。
    end;
    Sleep(100);
  end;
  // AppMutex 会继续阻止安装或卸载，直到托盘关闭。
end;

function InitializeSetup: Boolean;
begin
  Result := BeginOperation;
  if Result then
  begin
    CloseTray;
    InitialConfig := not FileExists(ExpandConstant('{#DataDir}\config.json'));
  end;
end;

function NeedsInitialConfig: Boolean;
begin
  Result := InitialConfig;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectTasks) and not InitialConfig;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder, Config, AutoStart: String;
begin
  if (CurStep <> ssPostInstall) or not InitialConfig then
    Exit;
  Folder := ExpandConstant('{#DataDir}');
  Config := Folder + '\config.json';
  if FileExists(Config) then
    Exit;
  if WizardIsTaskSelected('autostart') then
    AutoStart := 'true'
  else
  begin
    AutoStart := 'false';
    RegDeleteValue(HKCU, '{#RunKey}', '{#RunName}');
  end;

  // 即使用户取消安装结束时的启动，首次运行也会使用安装器中的选择。
  if not ForceDirectories(Folder) or not SaveStringToFile(Config,
    AnsiString('{"startWithWindows":' + AutoStart + ',"welcomeShown":false}'), False) then
    SuppressibleMsgBox('几格无法保存开机启动选项。你可以在几格设置里重新选择。', mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CompareText(RemoveBackslashUnlessRoot(WizardDirValue), ExpandConstant('{#InstallDir}')) <> 0 then
    Result := '几格使用固定的当前用户安装目录，请去掉自定义安装路径后重试。'
  else if CheckForMutexes('{#TrayMutex}') then
    Result := '几格仍在运行。请先退出托盘程序，再安装。';
end;

procedure DeinitializeSetup;
begin
  EndOperation;
end;

function InitializeUninstall: Boolean;
begin
  Result := BeginOperation;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Folder: String;
  RemoveData: Boolean;
begin
  if CurUninstallStep = usAppMutexCheck then
    CloseTray;
  if CurUninstallStep <> usPostUninstall then
    Exit;
  Folder := ExpandConstant('{#DataDir}');
  if not DirExists(Folder) then
    Exit;
  RemoveData := UninstallSilent;
  if not RemoveData then
    RemoveData := MsgBox('同时删除几格的设置和电量记录吗？', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if RemoveData and not DelTree(Folder, True, True, True) then
    SuppressibleMsgBox('未能删除全部几格数据，可能有文件仍在使用。', mbError, MB_OK, IDOK);
end;

procedure DeinitializeUninstall;
begin
  EndOperation;
end;
