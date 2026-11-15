; 文件：installer.iss
; 描述：当前用户中文安装、快捷方式、升级与卸载入口。
; 版本：v1.0
; 日期：2026/11/15
; 历史：v1.0 2026/11/15 新增钰宇鼠标连点器安装版。

; 2026/11/15 新增：版本、源文件和输出目录由 scripts/release.ps1 显式提供。
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#define AppName "钰宇鼠标连点器"
#define AppExe "钰宇鼠标连点器.exe"
#if VER != EncodeVer(6, 7, 4)
  #error This release requires Inno Setup 6.7.4
#endif

[Setup]
; 2026/11/15 新增：稳定 AppId 用于升级识别，仅在当前用户目录安装。
AppId={{C29A076E-E9C4-4E03-A6D9-7043A6EB5B1C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=HQY-DEBUG
AppPublisherURL=https://github.com/HQY-DEBUG/mouse_click
AppSupportURL=https://github.com/HQY-DEBUG/mouse_click/issues
AppUpdatesURL=https://github.com/HQY-DEBUG/mouse_click/releases
DefaultDirName={localappdata}\Programs\YuyuMouseClick
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=YuyuMouseClick-v{#AppVersion}-win-x64-setup
SetupIconFile=..\..\assets\icons\mouse.ico
UninstallDisplayIcon={app}\{#AppExe}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#AppExe}
RestartApplications=no
SetupLogging=yes
UninstallDisplayName={#AppName}

[Languages]
; 2026/11/15 新增：使用固定提交的官方简体中文翻译，来源及许可随源码保存。
Name: "chinesesimplified"; MessagesFile: "vendor\ChineseSimplified.isl"

[Tasks]
; 2026/11/15 新增：桌面快捷方式由用户选择，开始菜单入口始终创建。
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
; 2026/11/15 新增：明确允许进入安装包的文件，不携带便携标记或构建机配置。
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\图标许可.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\图标来源.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\使用说明.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\更新日志.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 2026/11/15 新增：快捷方式与卸载入口均使用应用正式名称和图标。
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\卸载{#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; 2026/11/15 新增：正常安装完成可选择运行；静默验收不自动启动程序。
Filename: "{app}\{#AppExe}"; Description: "运行{#AppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
// 2026/11/15 新增：安装版不得覆盖便携目录，避免错误复用便携配置。
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if FileExists(ExpandConstant('{app}\portable.flag')) then
    Result := '此目录包含便携版，请选择其他安装目录。'
  else
    Result := '';
end;
