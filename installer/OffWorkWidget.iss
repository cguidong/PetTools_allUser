#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppVersion
  #define AppVersion "1.0.2"
#endif
#ifdef InstallerQATest
  #define ProductId "{{C2D1CD23-3649-437D-B077-0B40C4B13FA0}"
  #define OutputName "OffWorkWidget-Installer-QA"
  #define InstallLeaf "OffWorkWidget-QA"
  #define ShortcutName "下班倒计时安装测试"
#else
  #define ProductId "{{B312EA3B-93C3-481D-AF00-5F60D9B84B35}"
  #define OutputName "下班倒计时-Setup-" + AppVersion + "-x64"
  #define InstallLeaf "OffWorkWidget"
  #define ShortcutName "下班倒计时"
#endif
[Setup]
AppId={#ProductId}
AppName=下班倒计时
AppVersion={#AppVersion}
AppPublisher=下班倒计时
DefaultDirName={localappdata}\Programs\{#InstallLeaf}
DefaultGroupName={#ShortcutName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile={#PayloadDir}\assets\app-icon.ico
UninstallDisplayIcon={app}\OffWorkWidget.WinUI.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic windows11 hidebevels
CloseApplications=no
RestartApplications=no
SetupLogging=yes
UninstallLogging=yes
DisableWelcomePage=yes
DisableDirPage=yes
DisableReadyPage=yes
WizardSizePercent=110,100
[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
[Messages]
ButtonCancel=取消
FinishedHeadingLabel=安装完成
FinishedLabel=下班倒计时已准备就绪。%n愿每一天，都有值得期待的下班时刻。
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,prerequisites\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\prerequisites\VC_redist.x64.exe"; Flags: dontcopy
[Icons]
Name: "{group}\{#ShortcutName}"; Filename: "{app}\OffWorkWidget.WinUI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\OffWorkWidget.WinUI.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\OffWorkWidget.WinUI.exe"; Description: "启动下班倒计时"; Flags: nowait postinstall skipifsilent runasoriginaluser
[Code]
const
  EventModifyState = $0002;
function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
var
  RuntimeReboot: Boolean;
  ContentTop, ContentHeight: Integer;
  InstallPage: TWizardPage;
  InstallPath: TNewEdit;
  DesktopShortcut: TNewCheckBox;

procedure BrowseInstallFolder(Sender: TObject);
var
  Directory: string;
begin
  Directory := InstallPath.Text;
  if BrowseForFolder('选择安装位置', Directory, False) then
    InstallPath.Text := Directory;
end;

procedure InitializeWizard;
var
  Heading, Detail, PathLabel: TNewStaticText;
  BrowseButton: TNewButton;
begin
  WizardForm.Font.Name := 'Microsoft YaHei UI';
  WizardForm.Font.Size := 9;
  WizardForm.WizardSmallBitmapImage.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False;
  ContentTop := WizardForm.InnerNotebook.Top;
  ContentHeight := WizardForm.InnerNotebook.Height;
  InstallPage := CreateCustomPage(wpWelcome, '', '');
  WizardForm.NextButton.Font.Name := 'Microsoft YaHei UI';
  WizardForm.NextButton.Font.Size := 10;
  WizardForm.CancelButton.Font.Name := 'Microsoft YaHei UI';
  WizardForm.CancelButton.Font.Size := 10;

  Heading := TNewStaticText.Create(WizardForm);
  Heading.Parent := InstallPage.Surface;
  Heading.SetBounds(0, ScaleY(28), InstallPage.SurfaceWidth, ScaleY(42));
  Heading.Caption := '把下班，设成期待';
  Heading.Font.Name := 'Microsoft YaHei UI';
  Heading.Font.Size := 20;
  Heading.Font.Style := [fsBold];

  Detail := TNewStaticText.Create(WizardForm);
  Detail.Parent := InstallPage.Surface;
  Detail.SetBounds(0, ScaleY(88), InstallPage.SurfaceWidth, ScaleY(40));
  Detail.AutoSize := False;
  Detail.WordWrap := True;
  Detail.Caption := '倒计时、休息安排与贴心提醒，安静陪伴每个工作日。';
  Detail.Font.Name := 'Microsoft YaHei UI';
  Detail.Font.Size := 10;
  Detail.Font.Color := $00807870;

  PathLabel := TNewStaticText.Create(WizardForm);
  PathLabel.Parent := InstallPage.Surface;
  PathLabel.SetBounds(0, ScaleY(150), InstallPage.SurfaceWidth, ScaleY(20));
  PathLabel.Caption := '安装位置';
  PathLabel.Font.Name := 'Microsoft YaHei UI';
  PathLabel.Font.Size := 10;

  InstallPath := TNewEdit.Create(WizardForm);
  InstallPath.Parent := InstallPage.Surface;
  InstallPath.SetBounds(0, ScaleY(179), InstallPage.SurfaceWidth - ScaleX(82), ScaleY(30));
  InstallPath.Text := WizardDirValue;
  InstallPath.ReadOnly := True;
  InstallPath.Font.Name := 'Segoe UI';
  InstallPath.Font.Size := 10;
  BrowseButton := TNewButton.Create(WizardForm);
  BrowseButton.Parent := InstallPage.Surface;
  BrowseButton.SetBounds(InstallPage.SurfaceWidth - ScaleX(72), ScaleY(179), ScaleX(72), ScaleY(30));
  BrowseButton.Caption := '更改';
  BrowseButton.Font.Name := 'Microsoft YaHei UI';
  BrowseButton.Font.Size := 10;
  BrowseButton.OnClick := @BrowseInstallFolder;

  DesktopShortcut := TNewCheckBox.Create(WizardForm);
  DesktopShortcut.Parent := InstallPage.Surface;
  DesktopShortcut.SetBounds(0, ScaleY(232), InstallPage.SurfaceWidth, ScaleY(24));
  DesktopShortcut.Caption := '创建桌面快捷方式';
  DesktopShortcut.Font.Name := 'Microsoft YaHei UI';
  DesktopShortcut.Font.Size := 10;
  DesktopShortcut.Checked := WizardIsTaskSelected('desktopicon');

  WizardForm.FinishedHeadingLabel.Left := ScaleX(32);
  WizardForm.FinishedHeadingLabel.Width := WizardForm.ClientWidth - ScaleX(64);
  WizardForm.FinishedLabel.Left := ScaleX(32);
  WizardForm.FinishedLabel.Width := WizardForm.ClientWidth - ScaleX(64);
  WizardForm.RunList.Left := ScaleX(32);
  WizardForm.RunList.Width := WizardForm.ClientWidth - ScaleX(64);
  WizardForm.YesRadio.Left := ScaleX(32);
  WizardForm.NoRadio.Left := ScaleX(32);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := PageID = wpSelectTasks;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = InstallPage.ID then begin
    WizardForm.MainPanel.Visible := False;
    WizardForm.InnerNotebook.Top := ScaleY(24);
    WizardForm.InnerNotebook.Height := ContentHeight + ContentTop - ScaleY(24);
    WizardForm.NextButton.Caption := '开始安装';
    WizardForm.BackButton.Visible := False;
    WizardForm.ActiveControl := WizardForm.NextButton;
  end else begin
    WizardForm.MainPanel.Visible := True;
    WizardForm.InnerNotebook.Top := ContentTop;
    WizardForm.InnerNotebook.Height := ContentHeight;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = InstallPage.ID then begin
    WizardForm.DirEdit.Text := Trim(InstallPath.Text);
    if DesktopShortcut.Checked then WizardSelectTasks('desktopicon')
    else WizardSelectTasks('');
  end;
end;

function InitializeSetup: Boolean;
begin
#ifdef InstallerQATest
  Result := GetEnv('OFFWORKWIDGET_TEST_DATA') <> '';
  if not Result then
    MsgBox('QA 安装必须指定 OFFWORKWIDGET_TEST_DATA 隔离目录。', mbError, MB_OK);
#else
  Result := True;
#endif
end;

function InstanceSuffix: string;
begin
  Result := GetUserNameString;
  if GetEnv('OFFWORKWIDGET_TEST_DATA') <> '' then
    Result := Result + '-WinUI-Isolated';
end;

function CloseRunningWidget: Boolean;
var
  Signal: THandle;
  Attempt: Integer;
  MutexName: string;
begin
  MutexName := 'Local\OffWorkWidget-' + InstanceSuffix;
  Result := not CheckForMutexes(MutexName);
  if Result then exit;
  Signal := OpenEvent(EventModifyState, False,
    'Local\OffWorkWidget-InstallerExit-' + InstanceSuffix);
  if Signal = 0 then exit;
  try
    SetEvent(Signal);
  finally
    CloseHandle(Signal);
  end;
  for Attempt := 1 to 100 do begin
    Sleep(100);
    if not CheckForMutexes(MutexName) then begin
      Result := True;
      exit;
    end;
  end;
end;

function HasVCRuntime: Boolean;
var
  Installed, Major, Minor, Build, Revision: Cardinal;
  Key: string;
begin
#ifdef InstallerQATest
  { Only the separate QA build bypasses runtime installation on the development PC. }
  Log('QA BUILD: prerequisite installation bypassed');
  Result := True;
#else
  Key := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  Result := RegQueryDWordValue(HKLM64, Key, 'Installed', Installed) and
    RegQueryDWordValue(HKLM64, Key, 'Major', Major) and
    RegQueryDWordValue(HKLM64, Key, 'Minor', Minor) and
    RegQueryDWordValue(HKLM64, Key, 'Bld', Build) and
    RegQueryDWordValue(HKLM64, Key, 'Rbld', Revision);
  if Result then
    Result := (Installed = 1) and
      ((Major > 14) or ((Major = 14) and ((Minor > 51) or
      ((Minor = 51) and ((Build > 36247) or
      ((Build = 36247) and (Revision >= 0)))))));
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  ExitCode: Integer;
begin
  Result := '';
  if not CloseRunningWidget then begin
    Result := '请先从旧版下班倒计时的托盘菜单选择“退出”，然后重试安装。';
    exit;
  end;
  if not HasVCRuntime then begin
    ExtractTemporaryFile('VC_redist.x64.exe');
    if not ShellExec('runas', ExpandConstant('{tmp}\VC_redist.x64.exe'),
      '/install /passive /norestart', '', SW_SHOWNORMAL,
      ewWaitUntilTerminated, ExitCode) then begin
      Result := '未完成 Microsoft Visual C++ x64 运行库安装。请允许系统授权，然后重试。错误：' + IntToStr(ExitCode);
      exit;
    end;
    if (ExitCode <> 0) and (ExitCode <> 3010) and (ExitCode <> 1638) then begin
      Result := 'Microsoft Visual C++ x64 运行库安装失败，错误：' + IntToStr(ExitCode);
      exit;
    end;
    RuntimeReboot := ExitCode = 3010;
    if not HasVCRuntime then
      Result := '运行库尚未就绪。请重启电脑后重新安装，或先安装包内要求的 Microsoft Visual C++ v14 x64 运行库。';
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeReboot;
end;

function InitializeUninstall: Boolean;
begin
  Result := CloseRunningWidget;
  if not Result then
    MsgBox('请先从下班倒计时的托盘菜单选择“退出”，然后重新卸载。个人设置会保留。', mbInformation, MB_OK);
end;