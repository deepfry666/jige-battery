#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define TrayMutex "Local\Jige.Battery.Tray"
#define SetupGate "Local\Jige.Battery.Setup"
#define InstallDir "{localappdata}\Programs\Jige"

[Setup]
AppId={{DD33BE43-6D23-45E0-936F-9B6B067067E8}
AppName=几格
AppVersion={#AppVersion}
AppPublisher=FourJ
AppPublisherURL=https://github.com/deepfry666/jige-battery
AppUpdatesURL=https://github.com/deepfry666/jige-battery/releases/latest
VersionInfoVersion={#AppVersion}.0
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
SetupIconFile=..\assets\jige.ico
OutputDir=..\artifacts\release
OutputBaseFilename=Jige-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes

[Languages]
#if FileExists(AddBackslash(CompilerPath) + "Languages\ChineseSimplified.isl")
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Files]
Source: "..\artifacts\tray-win-x64\Jige.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\tray-win-x64\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\几格"; Filename: "{app}\Jige.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Jige Battery"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Jige.exe"; Parameters: "--after-setup"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent
Filename: "{app}\Jige.exe"; Parameters: "--after-setup"; WorkingDir: "{app}"; Description: "启动几格"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Jige"
Type: dirifempty; Name: "{app}"

[Code]
function CreateNamedMutex(Attributes: THandle; InitialOwner: Integer; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

var
  OperationGate: THandle;

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
    CloseTray;
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
begin
  if CurUninstallStep = usAppMutexCheck then
    CloseTray;
end;

procedure DeinitializeUninstall;
begin
  EndOperation;
end;
