; 拾光 Lumina 安装器（Inno Setup 7）
; 由 scripts/build-release.ps1 调用；AppVersion / PublishDir / OutputDir 通过 /D 传入。
; 安装范围：启动时询问“仅为我安装（当前用户，免管理员）”或“为所有用户安装（需 UAC）”，
; 默认当前用户；两种模式分别写入 {autopf} / {autoprograms} 映射的位置。
; 用户数据始终位于 %APPDATA%\Lumina，安装器不写入、升级不覆盖、卸载不删除。

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef AppFileVersion
  #define AppFileVersion "1.0.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\.artifacts\publish\Lumina"
#endif

#ifndef OutputDir
  #define OutputDir "..\.artifacts\release"
#endif

#define AppName "拾光"
#define AppNameEn "Lumina"
#define AppExeName "Lumina.exe"
#define AppPublisher "Lumina"

[Setup]
AppId={{0B6B0F3C-6C1D-4A38-9A4B-2E8F5D7C1A63}
AppName={#AppName} {#AppNameEn}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppNameEn} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/wanyjp/Lumina
AppSupportURL=https://github.com/wanyjp/Lumina
AppUpdatesURL=https://github.com/wanyjp/Lumina
VersionInfoVersion={#AppFileVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} {#AppNameEn} 安装程序
DefaultDirName={autopf}\Lumina
DefaultGroupName={#AppName} {#AppNameEn}
DisableProgramGroupPage=yes
AllowNoIcons=yes
; 最终用户许可协议：安装器显示可滚动的协议文本框；
; 同意勾选框默认不勾选，未勾选时“下一步”不可用。
LicenseFile=EULA.txt
; 默认“仅为我安装”；启动对话框可切换为“为所有用户安装”（触发 UAC）。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename={#AppNameEn}-{#AppVersion}-win-x64-setup
SetupIconFile=..\Lumina.App\Assets\lumina.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
; 升级时由 Restart Manager 关闭旧实例，安装完成后不自动重启。
CloseApplications=force
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked
Name: "autostart"; Description: "开机时自动启动 {#AppName}"; GroupDescription: "启动选项："; Flags: unchecked

[Files]
Source: "EULA.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName} {#AppNameEn}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName} {#AppNameEn}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lumina"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;
  { 卸载只移除程序目录；个人数据目录原样保留，避免误删用户数据。 }
  if not UninstallSilent then
    MsgBox('卸载只会移除程序文件，个人数据仍保留在 %APPDATA%\Lumina。' + #13#10 +
           '如需彻底清除，请在卸载后手动删除该目录（删除后不可恢复）。',
           mbInformation, MB_OK);
end;
