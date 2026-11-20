#ifndef AppVersion
  #define AppVersion "0.2.17"
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
#define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{DD33BE43-6D23-45E0-936F-9B6B067067E8}_is1"

[Setup]
AppId={{DD33BE43-6D23-45E0-936F-9B6B067067E8}
AppName=几格
AppVersion={#AppVersion}
AppVerName=几格 {#AppVersion}
AppPublisher=FourJ
AppPublisherURL=https://github.com/deepfry666/jige-battery
AppUpdatesURL=https://github.com/deepfry666/jige-battery/releases/latest
VersionInfoVersion={#FileVersion}
DefaultDirName={#InstallDir}
DisableDirPage=no
UsePreviousAppDir=yes
DisableWelcomePage=no
DisableReadyPage=yes
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
#if Ver >= EncodeVer(6, 6, 0)
WizardStyle=modern dynamic windows11
WizardImageFile=..\assets\installer\wizard-light-100.png,..\assets\installer\wizard-light-150.png,..\assets\installer\wizard-light-200.png
WizardImageFileDynamicDark=..\assets\installer\wizard-dark-100.png,..\assets\installer\wizard-dark-150.png,..\assets\installer\wizard-dark-200.png
WizardSmallImageFile=..\assets\installer\wizard-small-light-100.png,..\assets\installer\wizard-small-light-150.png,..\assets\installer\wizard-small-light-200.png
WizardSmallImageFileDynamicDark=..\assets\installer\wizard-small-dark-100.png,..\assets\installer\wizard-small-dark-150.png,..\assets\installer\wizard-small-dark-200.png
#else
WizardStyle=modern
#endif
WizardSizePercent=115,115
OutputDir=..\artifacts\release
OutputBaseFilename=Jige-Setup-{#OutputVersion}-x64
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "zhcn"; MessagesFile: "Languages\ChineseSimplified.isl"

[Messages]
SetupWindowTitle=安装 %1
WelcomeLabel1=欢迎安装 [name]
WelcomeLabel2=即将在你的电脑上安装 [name/ver]。%n%n轻量 Windows 无线外设电量托盘工具，支持鼠标、键盘、耳机与蓝牙设备实时电量监控。%n%n点击「下一步」选择安装位置并继续。
WizardSelectDir=选择安装位置
SelectDirDesc=选择 [name] 的安装文件夹
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹中：
SelectDirBrowseLabel=点击「下一步」继续；如需更改安装位置，请点击「浏览」。
WizardSelectTasks=安装选项
SelectTasksDesc=自定义桌面快捷方式与启动设置
SelectTasksLabel2=勾选希望在安装 [name] 时执行的选项，然后点击「安装」：
FinishedHeadingLabel=[name] 已安装完成
FinishedLabel=几格已成功安装到你的电脑。你可以从桌面或开始菜单快捷方式随时打开它。
ClickFinish=点击「完成」关闭安装程序。
BeveledLabel=几格 · 无线外设电量托盘

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Check: ShouldOfferDesktopIcon
Name: "autostart"; Description: "开机时启动几格"; GroupDescription: "启动选项："; Check: NeedsInitialConfig

[Files]
Source: "..\artifacts\tray-win-x64\Jige.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\tray-win-x64\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\几格"; Filename: "{app}\Jige.exe"; WorkingDir: "{app}"; Comment: "无线外设电量托盘工具"
Name: "{autodesktop}\几格"; Filename: "{app}\Jige.exe"; WorkingDir: "{app}"; Comment: "无线外设电量托盘工具"; Tasks: desktopicon; Check: ShouldOfferDesktopIcon
Name: "{autodesktop}\几格"; Filename: "{app}\Jige.exe"; WorkingDir: "{app}"; Comment: "无线外设电量托盘工具"; Check: ShouldUpdateExistingDesktopIcon

[Registry]
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "{#RunName}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunName}"; ValueData: """{app}\Jige.exe"""; Tasks: autostart; Check: NeedsInitialConfig

[Run]
Filename: "{app}\Jige.exe"; Parameters: "--after-setup"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent
Filename: "{app}\Jige.exe"; Parameters: "--after-setup"; WorkingDir: "{app}"; Description: "立即启动几格"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 覆盖更新不再重建桌面快捷方式，而 UninstallLogMode=overwrite 会重置卸载日志，因此显式登记清理项。
Type: files; Name: "{autodesktop}\几格.lnk"
Type: dirifempty; Name: "{app}"

[Code]
function CreateNamedMutex(Attributes: THandle; InitialOwner: Integer; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

var
  OperationGate: THandle;
  InitialConfig: Boolean;
  IsUpgrade: Boolean;
  PreviousInstallDir: String;

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

function CanWriteToTargetDir(const TargetDir: String): Boolean;
var
  Dir, Parent, Probe: String;
begin
  Result := False;
  Dir := RemoveBackslashUnlessRoot(Trim(TargetDir));
  while (Dir <> '') and not DirExists(Dir) do
  begin
    Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Dir));
    if CompareText(Parent, Dir) = 0 then
      Exit;
    Dir := Parent;
  end;
  if (Dir = '') or not DirExists(Dir) then
    Exit;
  Probe := AddBackslash(Dir) + '.jige-install-test-' + GetDateTimeString('yyyymmddhhnnss', '-', '-') + '.tmp';
  if SaveStringToFile(Probe, '', False) then
  begin
    DeleteFile(Probe);
    Result := True;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := BeginOperation;
  if Result then
  begin
    CloseTray;
    InitialConfig := not FileExists(ExpandConstant('{#DataDir}\config.json'));
    IsUpgrade := RegKeyExists(HKCU, '{#UninstallKey}');
    PreviousInstallDir := '';
    if IsUpgrade then
    begin
      if not RegQueryStringValue(HKCU, '{#UninstallKey}', 'InstallLocation', PreviousInstallDir) or
        (Trim(PreviousInstallDir) = '') then
      begin
        PreviousInstallDir := '';
        RegQueryStringValue(HKCU, '{#UninstallKey}', 'Inno Setup: App Path', PreviousInstallDir);
      end;
    end;
  end;
end;

function NeedsInitialConfig: Boolean;
begin
  Result := InitialConfig;
end;

function ShouldOfferDesktopIcon: Boolean;
begin
  Result := not IsUpgrade;
end;

function ShouldUpdateExistingDesktopIcon: Boolean;
var
  CurrentDir, OldDir: String;
begin
  Result := False;
  if not IsUpgrade then
    Exit;
  OldDir := RemoveBackslashUnlessRoot(Trim(PreviousInstallDir));
  if OldDir = '' then
    Exit;
  CurrentDir := RemoveBackslashUnlessRoot(Trim(ExpandConstant('{app}')));
  if CompareText(CurrentDir, OldDir) = 0 then
    Exit;
  Result := FileExists(ExpandConstant('{autodesktop}\几格.lnk'));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not CanWriteToTargetDir(WizardDirValue) then
  begin
    SuppressibleMsgBox('当前账户没有所选安装目录的写入权限。请选择其他目录（例如默认位置或数据盘文件夹）。', mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder, Config, AutoStart, ExistingRun: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  if not InitialConfig then
  begin
    // 若用户更换了安装目录且此前已启用开机自启，同步更新自启路径。
    if RegQueryStringValue(HKCU, '{#RunKey}', '{#RunName}', ExistingRun) and (Trim(ExistingRun) <> '') then
      RegWriteStringValue(HKCU, '{#RunKey}', '{#RunName}', '"' + ExpandConstant('{app}\Jige.exe') + '"');
    Exit;
  end;

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
  if CheckForMutexes('{#TrayMutex}') then
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
