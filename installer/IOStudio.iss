; ── IOStudio 安装脚本 (Inno Setup 6) ─────────────────────────────────
;
; 为什么是**按用户**安装 (PrivilegesRequired=lowest):
;   IOStudio 的自更新是"就地替换安装目录里的文件" (HiMind.Distribution.Updater)。
;   装进 Program Files 会让每次更新都要 UAC 提权, 现场根本没法用。
;   装到 %LOCALAPPDATA%\Programs\IOStudio 则当前用户始终可写 —— 与既有更新模型一致。
;
; 命令行集成:
;   只把 {app}\bin 加进用户 PATH, 里面放一个 iostudio.cmd 转发到 IOStudio.exe。
;   不把 {app} 直接加进 PATH 的原因见 installer\iostudio.cmd 顶部注释。
;
; 编译 (由 scripts\Publish-IOStudioDistribution.ps1 -MakeInstaller 调用):
;   iscc /DAppVersion=1.0.15 /DStagingDir=<绝对路径> /DOutputDir=<绝对路径> IOStudio.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; dotnet 发布出来的目录 (安装包与渠道 zip 用同一批比特, 不另立第二个真相源)
#ifndef StagingDir
  #define StagingDir "..\Binaries\Win64\Release\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

[Setup]
; AppId 一旦发布就不能改 —— Windows 靠它认"这是同一个产品", 改了会变成并存两个安装。
AppId={{8F3A2C41-7D5E-4B9A-9C21-6E4B0D7A3F58}
AppName=IOStudio
AppVersion={#AppVersion}
AppVerName=IOStudio {#AppVersion}
AppPublisher=HiMind
AppPublisherURL=https://himind.andcrane.com
DefaultDirName={localappdata}\Programs\IOStudio
DefaultGroupName=IOStudio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=IOStudio-Setup-{#AppVersion}
SetupIconFile=..\IOStudio\Assets\avalonia-logo.ico
UninstallDisplayIcon={app}\IOStudio.exe
; 让 Explorer 等已运行的程序重新读取环境变量 (PATH 生效的关键)
ChangesEnvironment=yes
VersionInfoVersion={#AppVersion}

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "addtopath"; Description: "把 iostudio 加入 PATH（可在任意终端直接调用）"; GroupDescription: "命令行集成："; Flags: checkedonce
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Dirs]
Name: "{app}\bin"

[Files]
; 应用本体。per-user 配置刻意**不装**: 它们属于现场, 覆盖会毁掉用户已配好的设备与量程
; (与发布脚本剔除这两份的口径一致)。因此卸载也不会动它们。
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Config\IODevice.xml,Config\IOStudioSettings.xml,Logs\*,Temp\*"
; 命令入口
Source: "iostudio.cmd"; DestDir: "{app}\bin"; Flags: ignoreversion

[Icons]
Name: "{group}\IOStudio"; Filename: "{app}\IOStudio.exe"
Name: "{autodesktop}\IOStudio"; Filename: "{app}\IOStudio.exe"; Tasks: desktopicon

[Registry]
; PATH 追加。Check 保证幂等 —— 重复安装不会把同一条路径塞进去两次。
; 配合 ChangesEnvironment=yes, Setup 结束时会广播 WM_SETTINGCHANGE。
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}\bin"; Check: NeedsAddPath('{app}\bin'); Tasks: addtopath

[UninstallDelete]
; 只清运行期日志。Config\ 刻意**不删** —— 那是现场的设备表与量程配置, 属于用户。
Type: filesandordirs; Name: "{app}\Logs"

[Run]
Filename: "{app}\IOStudio.exe"; Description: "启动 IOStudio"; Flags: nowait postinstall skipifsilent

[Code]
const
  EnvironmentKey = 'Environment';

/// PATH 里是否已经有这一段 (大小写不敏感)。用于让追加幂等。
function NeedsAddPath(Param: string): Boolean;
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(Paths) + ';') = 0;
end;

/// 卸载时**只删自己加的那一段**, 其余条目原样保留、不重排。
/// (PATH 是拼接字符串, 系统不会替我们记账, 所以必须精确匹配到分隔符。)
procedure EnvRemovePath(Path: string);
var
  Paths: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
    exit;

  P := Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';');
  if P = 0 then
    exit;

  if P = 1 then
    // 位于开头: 连同其后的分号一起切掉, 否则会留下一个前导分号
    Paths := Copy(Paths, Length(Path) + 2, Length(Paths))
  else
    Delete(Paths, P - 1, Length(Path) + 1);

  RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    EnvRemovePath(ExpandConstant('{app}\bin'));
end;
