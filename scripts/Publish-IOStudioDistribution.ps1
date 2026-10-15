<#
.SYNOPSIS
    Publish a clean, distribution-ready IOStudio package for HiMind software distribution.

.DESCRIPTION
    Produces a reproducible `directory-zip` artifact for product com.himind.iostudio:

      1. Clean staging (a fresh folder, never a re-used/dirty build output)
      2. dotnet publish IOStudio.csproj (Release, win-x64) with the independent Updater.
         Default is a SINGLE-FILE publish: all managed assemblies (Avalonia / IOStudio.dll /
         deps.json) and native runtime libraries are bundled into IOStudio.exe, so the root
         no longer carries the managed DLL soup.
      3. Include the external, field-replaceable native payload that a clean `dotnet publish`
         does NOT carry (it comes from the C++ build / the LATEST IOUI build, not from
         project items):
           - IODevice.dll, IODevice_C_Wrapper.dll            (C++ build)
           - ExternalLibraries/IOUI-Win64-*.dll              (latest IOUI plugins, full set)
           - ExternalLibraries/Core/*.dll                    (latest IOUI Libraries/*)
           - ExternalLibraries/Config/Simulator + Config/Simulator
         These stay OUTSIDE the exe so they can be swapped without rebuilding.
         libvlc is the opposite: it is embedded INSIDE the exe (see LibVlcInitializer) and
         must NOT appear as a loose folder in the root.
      4. Prune anything that must not ship:
           - Temp/ and Logs/
           - per-user config (Config/IODevice.xml, Config/IOStudioSettings.xml) that
             would otherwise clobber field settings during a self-update
           - symbols/link artifacts (*.pdb, *.ipdb, *.iobj, *.exp, *.lib, *.ilk)
           - libvlc/ (must be embedded in the exe, never loose)
           - a nested self-contained runtime folder (win-x64/), if present
      5. Verify: single-file (IOStudio.dll must NOT be loose), libvlc embedded in the exe
         (no loose folder), the latest-built IOUI plugin set, updater/, distribution.json
      6. Zip and report size + SHA-256, ready for software.distribution.artifact.inspect

    This script never uploads and never publishes a Release. Publishing is a network
    write that must be confirmed by the user through the software-distribution skill;
    this script only produces and verifies the local artifact.

.PARAMETER Version
    Release version, e.g. 1.0.3. Drives VersionPrefix (AssemblyVersion) and the file name.

.PARAMETER UpdaterSource
    Directory containing HiMind.Distribution.Updater.exe.
    Resolution order: -UpdaterSource > $env:HIMIND_UPDATER_SOURCE > project default.

.PARAMETER OutputRoot
    Folder that receives the staging directory and the .zip. Defaults to <repo>/publish.

.PARAMETER SelfContained
    Publish self-contained win-x64. Defaults to $false to match the already-released,
    field-verified layout (the field machines provide the .NET runtime). Set $true only
    for machines without the runtime.

.PARAMETER SingleFile
    Publish a single-file IOStudio.exe (default $true). The external native payload
    (IODevice*.dll, ExternalLibraries/) is always kept outside the exe.

.PARAMETER SkipPublish
    Skip the dotnet publish step and only prune/verify/zip an existing staging folder.

.EXAMPLE
    .\Publish-IOStudioDistribution.ps1 -Version 1.0.3 -UpdaterSource 'D:\sdk\windows-updater'

.NOTES
    Contract: product_id=com.himind.iostudio, channel=stable, platform=windows,
    architecture=x64, package_type=directory-zip.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Channel = 'stable',
    [string]$ProductId = 'com.himind.iostudio',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$UpdaterSource,
    [string]$OutputRoot,

    [bool]$SelfContained = $false,
    [bool]$SingleFile = $true,

    [switch]$SkipPublish,
    [switch]$KeepStaging,

    # 额外产出一个按用户安装的 Windows 安装包 (Inno Setup)。渠道制品仍是 directory-zip,
    # 安装包只是给"想双击装一下"的现场用的额外交付物 —— 两者用的是同一批比特。
    [switch]$MakeInstaller,

    # 跳过构建对齐检查。默认会跑（只告警不阻断）；确实需要时用它显式跳过。
    [switch]$SkipAlignmentCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host ">> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   $Text" -ForegroundColor Green }
function Write-Skip([string]$Text) { Write-Host "   (pruned) $Text" -ForegroundColor Yellow }

if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z.\-]*$') {
    throw "Version '$Version' is not a valid version string (expected e.g. 1.0.2)."
}

# AssemblyVersion/FileVersion must be purely numeric; strip any prerelease/build suffix.
$numericCore = [regex]::Match($Version, '^\d+(\.\d+){0,3}').Value
if ([string]::IsNullOrWhiteSpace($numericCore)) {
    throw "Version '$Version' has no numeric core (expected e.g. 1.0.2 or 1.0.2-rc.1)."
}
$numericParts = @($numericCore.Split('.'))
while ($numericParts.Count -lt 3) { $numericParts += '0' }
$NumericVersion = ($numericParts[0..2] -join '.')

# --- Resolve layout ---------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$Csproj = Join-Path $RepoRoot 'IOStudio\IOStudio.csproj'
# 父仓库根（= IODevice 子模块的父目录）。所有产品的分发产出都集中到它的 publish\，
# 不再散在子模块的 publish\ 里 —— 那样会有两个 publish，谁也说不清哪个是当前。
$OuterRoot = Split-Path -Parent $RepoRoot
if (-not (Test-Path $Csproj)) { throw "IOStudio.csproj not found: $Csproj" }

if (-not $OutputRoot) { $OutputRoot = Join-Path $OuterRoot 'publish' }
$ArtifactName = "IOStudio-$Version-win-x64"

. (Join-Path $ScriptDir 'OutputLayout.ps1')
# 废弃插件清单与剔除实现（PayloadLib）—— 与 IOToolkit 打包共用同一份权威清单。
. (Join-Path $ScriptDir 'PayloadLib.ps1')

# 分发制品按 <根>\<产品>\<版本>\<平台>\ 落位；staging 放到 _staging\ 下 ——
# 此前 staging 与成品同层（都是 <根>\IOStudio-<ver>-win-x64），
# 于是"一个解开的目录"和"一个可交付制品"在目录里长得一样，靠名字区分。
$Product = 'iostudio'
$PlatformDir = Get-PlatformDir -OutputRoot $OutputRoot -Product $Product -Version $Version -Platform 'windows'
$Staging = Join-Path $OutputRoot "_staging\$ArtifactName"
$ZipPath = Join-Path $PlatformDir "$ArtifactName.zip"

# --- Resolve updater source -------------------------------------------------
if (-not $UpdaterSource) { $UpdaterSource = $env:HIMIND_UPDATER_SOURCE }
if (-not $UpdaterSource) {
    $UpdaterSource = "F:\VSProjects\工作流\publish\distribution-sdk\windows-updater"
}
$UpdaterExe = Join-Path $UpdaterSource 'HiMind.Distribution.Updater.exe'
if (-not (Test-Path $UpdaterExe)) {
    throw "HiMind.Distribution.Updater.exe not found at '$UpdaterExe'. Pass -UpdaterSource or set HIMIND_UPDATER_SOURCE."
}
Write-Step "Updater source: $UpdaterExe"

# --- Resolve native payload sources ----------------------------------------
# IODevice.dll / IODevice_C_Wrapper.dll come from the C++ build; the device plugins
# (ExternalLibraries\IOUI-Win64-*.dll) and their Core libs come from the LATEST IOUI
# build output. None of these are IOStudio project items, so a clean `dotnet publish`
# would drop them. They are copied next to the exe (kept OUTSIDE the single-file exe)
# by the csproj target IncludeNativeRuntimePayload. Fail loudly if a source is missing.
$NativeDir = Join-Path $RepoRoot "Binaries\Win64\$Configuration"
$IouiDir = Join-Path (Split-Path -Parent $RepoRoot) "IOUI\Binaries\Win64\$Configuration"
$IouiRootDir = Join-Path (Split-Path -Parent $RepoRoot) 'IOUI'
Write-Step "Native payload source (C++):  $NativeDir"
Write-Step "Native payload source (IOUI): $IouiDir"
foreach ($required in @('IODevice.dll', 'IODevice_C_Wrapper.dll')) {
    if (-not (Test-Path (Join-Path $NativeDir $required))) {
        throw "Native payload missing: $(Join-Path $NativeDir $required). Build the native solution first (or pass -p:NativeOutputDir)."
    }
}

# --- 构建对齐检查 -----------------------------------------------------------
# 各类外置资源（插件 DLL、插件配置、Simulator web、native）是否都对得上最新的源？
# 这些失配全是**静默**的：包照样"成功"，只是内容陈旧或多余。实测三类同时漂移过 ——
# 6 个源码已不存在的遗留 DLL 一直在发、30 个正源配置只有 2 个进包、
# Simulator web 发的是 5 月的快照（三维视图都不在里面）。
#
# 检查逻辑只有**一处**：scripts\Verify-BuildAlignment.ps1。这里只负责调用，
# 不再自带任何同类检查 —— 否则两条判据迟早分叉。
# 默认只告警不阻断（-Strict 才失败）：时间戳不是硬证据，不该拦住一次本来正确的发布。
$AlignmentScript = Join-Path $ScriptDir 'Verify-BuildAlignment.ps1'
if ((Test-Path $AlignmentScript) -and -not $SkipAlignmentCheck) {
    & $AlignmentScript -Configuration $Configuration -IouiRootDir $IouiRootDir -IouiDir $IouiDir -NativeDir $NativeDir
}
elseif ($SkipAlignmentCheck) {
    Write-Warning '已跳过构建对齐检查（-SkipAlignmentCheck）。产物可能包含陈旧或多余的资源。'
}
if (-not (Test-Path (Join-Path $IouiDir 'IOUI-Win64-IOUI.dll'))) {
    throw "IOUI plugins missing: $(Join-Path $IouiDir 'IOUI-Win64-IOUI.dll'). Build IOUI ($Configuration|x64) first (or pass -p:IouiOutputDir)."
}
if (-not (Test-Path (Join-Path $IouiDir 'Libraries'))) {
    throw "IOUI Core libs missing: $(Join-Path $IouiDir 'Libraries'). Build IOUI first."
}

# --- Clean staging ----------------------------------------------------------
Write-Step "Preparing staging: $Staging"
if (-not $SkipPublish) {
    if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
    New-Item -ItemType Directory -Path $Staging -Force | Out-Null
}

# --- Publish ----------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Step "dotnet publish $Configuration win-x64 (self-contained=$SelfContained, single-file=$SingleFile)"
    $publishArgs = @(
        'publish', $Csproj,
        '-c', $Configuration,
        '-r', 'win-x64',
        "--self-contained=$SelfContained",
        "-p:PublishSingleFile=$SingleFile",
        "-p:VersionPrefix=$Version",
        "-p:AssemblyVersion=$NumericVersion.0",
        "-p:FileVersion=$NumericVersion.0",
        "-p:InformationalVersion=$Version",
        "-p:HiMindUpdaterSource=$UpdaterSource",
        '-p:HiMindRequireUpdater=true',
        "-p:NativeOutputDir=$NativeDir",
        "-p:IouiOutputDir=$IouiDir",
        '-p:HiMindRequireNativePayload=true',
        '-p:SkipPostPublish=true',
        '-o', $Staging,
        '-v', 'quiet',
        '-nologo'
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
} else {
    Write-Step "SkipPublish set: reusing existing staging (no dotnet publish)"
    if (-not (Test-Path $Staging)) { throw "SkipPublish set but staging does not exist: $Staging" }
}

# --- 装配插件载荷（共用装配层）---------------------------------------------
# 装配逻辑**只有一处**：scripts\Assemble-Payload.ps1。IOStudio 应用包与
# IOToolkit SDK 包（Build-IOToolkitPackage.ps1）都调它 —— 两条链路消费的是同一份载荷，
# 各自复制一份装配就是两处会独立腐烂（实测它们此前就各自漏配置，而且漏得不一样：
# 这边只剩 2/30，SDK 那边 22/30）。
#
# 它做两件事：把 IOUI\Config\<插件>\ 的配置**镜像**进 staging（先清后拷），
# 并按需同步插件 DLL 与 Core 库。镜像而不是累加，是为了让"载荷 == 正源"可断言 ——
# xcopy 累加会留下正源已删的文件（实测 Simulator web 残留 3 个上一版的 assets chunk）。
$payload = & (Join-Path $ScriptDir 'Assemble-Payload.ps1') `
    -IouiRootDir $IouiRootDir `
    -Target @(
        (Join-Path $Staging 'ExternalLibraries\Config'),
        (Join-Path $Staging 'Config')
    ) `
    -IouiOutputDir $IouiDir `
    -LibraryDir (Join-Path $Staging 'ExternalLibraries')
Write-Ok "payload assembled: 配置 $($payload.SyncedConfigs) 个插件 / 插件 DLL $($payload.SyncedPlugins) / Core 库 $($payload.SyncedCoreLibs)"

# 废弃插件从载荷里彻底剔除（装配层只负责"加"，这里负责"删"）——
# 载荷目录是积累式的，废弃插件的旧 DLL 会跟着每个版本一路发出去。
$pruned = Remove-DeprecatedFromPayload -Roots @(
    (Join-Path $Staging 'ExternalLibraries')
)
if ($pruned.Dlls.Count -gt 0 -or $pruned.Configs.Count -gt 0) {
    Write-Step "剔除废弃插件：DLL $($pruned.Dlls.Count) 个 / 配置 $($pruned.Configs.Count) 个"
    if ($pruned.Dlls.Count -gt 0) { Write-Host "      $($pruned.Dlls -join ', ')" -ForegroundColor DarkGray }
    if ($pruned.Configs.Count -gt 0) { Write-Host "      配置: $($pruned.Configs -join ', ')" -ForegroundColor DarkGray }
}
# --- Prune non-shippable content -------------------------------------------
Write-Step 'Pruning non-shippable content'

$pruneDirs = @('Temp', 'Logs')
foreach ($d in $pruneDirs) {
    $p = Join-Path $Staging $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Skip $d }
}

# Per-user config: must never overwrite the field's existing settings on update.
$userConfig = @('Config\IODevice.xml', 'Config\IOStudioSettings.xml')
foreach ($f in $userConfig) {
    $p = Join-Path $Staging $f
    if (Test-Path $p) { Remove-Item $p -Force; Write-Skip $f }
}

# Test artifacts: CLI 跑出来的状态转储与预检报告不是产品内容, 不能随版本发出去。
$junkTest = Get-ChildItem -Path (Join-Path $Staging 'Config\Motion') -File -Include '*.cli-status.json', 'validate-*.md' -ErrorAction SilentlyContinue
foreach ($f in $junkTest) { Remove-Item $f.FullName -Force }
if ($junkTest) { Write-Skip "test artifacts in Config\Motion ($($junkTest.Count) files)" }

# Symbols and link artifacts.
$junkPatterns = @('*.pdb', '*.ipdb', '*.iobj', '*.exp', '*.lib', '*.ilk')
$junk = Get-ChildItem -Path $Staging -Recurse -File -Include $junkPatterns -ErrorAction SilentlyContinue
foreach ($f in $junk) { Remove-Item $f.FullName -Force }
if ($junk) { Write-Skip "symbols/link artifacts ($($junk.Count) files)" }

# Nested self-contained runtime folder left by an older ad-hoc build.
$nested = Join-Path $Staging 'win-x64'
if (Test-Path (Join-Path $nested 'coreclr.dll')) {
    Remove-Item $nested -Recurse -Force
    Write-Skip 'nested runtime folder win-x64\'
}

# libvlc is embedded inside the exe now: no libvlc folder may remain in the root.
$libvlc = Join-Path $Staging 'libvlc'
if (Test-Path $libvlc) {
    Remove-Item $libvlc -Recurse -Force
    Write-Skip 'libvlc\ (embedded in exe; should not be loose)'
}

# --- Verify required content ------------------------------------------------
Write-Step 'Verifying required content'

$exePath = Join-Path $Staging 'IOStudio.exe'
if (-not (Test-Path $exePath)) { throw "IOStudio.exe missing in staging: $exePath" }

# Single-file guard: the managed assemblies must be INSIDE the exe, not loose.
if ($SingleFile) {
    foreach ($loose in @('IOStudio.dll', 'IOStudio.deps.json', 'IOStudio.runtimeconfig.json')) {
        if (Test-Path (Join-Path $Staging $loose)) {
            throw "Single-file publish expected, but '$loose' is loose in staging. PublishSingleFile did not apply."
        }
    }
    $looseAvalonia = @(Get-ChildItem -Path $Staging -File -Filter 'Avalonia*.dll' -ErrorAction SilentlyContinue)
    if ($looseAvalonia.Count -gt 0) {
        throw "Single-file publish expected, but $($looseAvalonia.Count) Avalonia*.dll are loose in staging."
    }
    Write-Ok 'single-file verified (managed assemblies bundled into IOStudio.exe)'
}

# libvlc must be embedded in the exe, never a loose folder in the root.
if (Test-Path (Join-Path $Staging 'libvlc')) {
    throw "libvlc folder is loose in staging. It must be embedded inside IOStudio.exe."
}
$exeBytes = [System.IO.File]::ReadAllBytes($exePath)
$exeText = [System.Text.Encoding]::ASCII.GetString($exeBytes)
if ($exeText.IndexOf('libvlc.win-x64.zip', [System.StringComparison]::Ordinal) -lt 0) {
    throw "IOStudio.exe does not embed the libvlc archive (resource 'libvlc.win-x64.zip' not found). Video/waveform would be unavailable."
}
Write-Ok 'libvlc verified (embedded in IOStudio.exe, no loose folder)'

# External, field-replaceable native payload must be present and OUTSIDE the exe.
$nativeRequired = @('IODevice.dll', 'IODevice_C_Wrapper.dll')
foreach ($n in $nativeRequired) {
    if (-not (Test-Path (Join-Path $Staging $n))) {
        throw "$n missing in staging. Device functionality would be broken (this was the 1.0.2 defect)."
    }
}
$pluginCount = @(Get-ChildItem -Path (Join-Path $Staging 'ExternalLibraries') -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue).Count
if ($pluginCount -lt 10) {
    throw "ExternalLibraries has only $pluginCount IOUI-Win64-*.dll; expected the full latest-built plugin set."
}
if (-not (Test-Path (Join-Path $Staging 'ExternalLibraries\Core'))) {
    throw "ExternalLibraries\Core missing in staging. Vendor SDK libs (PCI/SNAP7/MODBUS/opencv...) would be unavailable."
}
$coreCount = @(Get-ChildItem -Path (Join-Path $Staging 'ExternalLibraries\Core') -File -Filter '*.dll' -ErrorAction SilentlyContinue).Count
Write-Ok "native payload present (IODevice.dll, IODevice_C_Wrapper.dll, $pluginCount plugins, $coreCount core libs)"

$updaterDst = Join-Path $Staging 'updater\HiMind.Distribution.Updater.exe'
if (-not (Test-Path $updaterDst)) {
    throw "updater/HiMind.Distribution.Updater.exe missing in staging. Self-update would be unusable."
}
Write-Ok 'updater/HiMind.Distribution.Updater.exe present'

$distJson = Join-Path $Staging 'distribution.json'
if (-not (Test-Path $distJson)) { throw "distribution.json missing in staging." }
$cfg = Get-Content $distJson -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($cfg.baseUrl) -or $cfg.baseUrl -match 'example\.com') {
    throw "distribution.json baseUrl is a placeholder: '$($cfg.baseUrl)'"
}
if ($cfg.productId -ne $ProductId) {
    throw "distribution.json productId '$($cfg.productId)' != expected '$ProductId'"
}
if ($cfg.enabled -ne $true) { throw "distribution.json enabled must be true." }
Write-Ok "distribution.json ok (baseUrl=$($cfg.baseUrl), productId=$($cfg.productId), channel=$($cfg.channel))"

# Guard: no per-user config slipped back in.
foreach ($f in $userConfig) {
    $p = Join-Path $Staging $f
    if (Test-Path $p) { throw "User config must not ship: $f (would clobber field settings on update)." }
}

# --- Zip --------------------------------------------------------------------
Write-Step "Creating archive: $ZipPath"
# 打包实现来自 OutputLayout.ps1（与 IOToolkit 打包共用一份）。
# 原先这里自己写了一段 zip —— 两处各写一份迟早分叉，而 zip 条目名分隔符
# （必须 '/'，写目录会得到 '\'）正是最容易分叉的那种细节。
$entryCount = New-ArtifactZip -ZipPath $ZipPath -Items @(
    @{ Path = $Staging; Entry = '' }
)
Write-Ok "archive written ($entryCount entries)"

# Verify the archive itself (separators + required entries) before reporting.
$check = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $entryNames = @($check.Entries | ForEach-Object { $_.FullName })
} finally {
    $check.Dispose()
}
if ($entryNames | Where-Object { $_ -match '\\' }) {
    throw "Archive contains non-standard backslash separators."
}
if ($entryNames -notcontains 'updater/HiMind.Distribution.Updater.exe') {
    throw "Archive is missing updater/HiMind.Distribution.Updater.exe."
}
if ($entryNames -notcontains 'IOStudio.exe' -or $entryNames -notcontains 'distribution.json') {
    throw "Archive is missing IOStudio.exe or distribution.json."
}
foreach ($n in @('IODevice.dll', 'IODevice_C_Wrapper.dll', 'ExternalLibraries/IOUI-Win64-IOUI.dll')) {
    if ($entryNames -notcontains $n) {
        throw "Archive is missing external native payload: $n."
    }
}
if ($entryNames -match '^libvlc/') {
    throw "Archive still contains a loose libvlc/ folder; it must be embedded in IOStudio.exe."
}
$archivePlugins = @($entryNames | Where-Object { $_ -match '^ExternalLibraries/IOUI-Win64-.*\.dll$' }).Count
if ($archivePlugins -lt 10) {
    throw "Archive has only $archivePlugins IOUI plugins; expected the full latest-built set."
}
if (-not ($entryNames | Where-Object { $_ -match '^ExternalLibraries/Core/.*\.dll$' })) {
    throw "Archive is missing ExternalLibraries/Core/*.dll."
}
if ($SingleFile) {
    foreach ($loose in @('IOStudio.dll', 'IOStudio.deps.json')) {
        if ($entryNames -contains $loose) {
            throw "Single-file publish expected, but archive contains loose '$loose'."
        }
    }
}
if ($entryNames -match '^Temp/|^Logs/|^Config/IODevice\.xml$|^Config/IOStudioSettings\.xml$') {
    throw "Archive still contains non-shippable content (Temp/Logs/user config)."
}
Write-Ok "archive verified ($($entryNames.Count) entries, standard separators, $archivePlugins plugins)"

$zip = Get-Item $ZipPath
$hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# --- Optional: Windows installer (Inno Setup) --------------------------------
# 必须在删掉 staging 之前编译 —— Inno 读的是目录, 不是 zip。
$setupExe = $null
$setupHash = $null
if ($MakeInstaller) {
    Write-Step "Building Windows installer (Inno Setup)"
    $iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        throw "ISCC.exe (Inno Setup 6) not found. Install it with 'winget install JRSoftware.InnoSetup', or drop -MakeInstaller."
    }
    $iss = Join-Path $RepoRoot 'installer\IOStudio.iss'
    if (-not (Test-Path $iss)) { throw "Installer script not found: $iss" }

    & $iscc "/DAppVersion=$Version" "/DStagingDir=$Staging" "/DOutputDir=$PlatformDir" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed with exit code $LASTEXITCODE" }

    $setupExe = Get-Item (Join-Path $PlatformDir "IOStudio-Setup-$Version.exe")
    $setupHash = (Get-FileHash $setupExe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $setupSizeMb = [math]::Round($setupExe.Length / 1MB, 1)
    Write-Ok "installer built: $($setupExe.Name) ($setupSizeMb MB)"
}

if (-not $KeepStaging) {
    Remove-Item $Staging -Recurse -Force
    # _staging 空掉就一并收走。否则 publish\ 下会留一个空壳目录（实测留着），
    # 而它的名字看起来像"有中间产物没清"。
    $stagingRoot = Split-Path -Parent $Staging
    if ((Test-Path $stagingRoot) -and -not @(Get-ChildItem -Path $stagingRoot -Force)) {
        Remove-Item -Path $stagingRoot -Force
    }
}

# --- Manifest ---------------------------------------------------------------
# 记录每个制品的文件名/大小/sha256 与渠道参数。此前这些只出现在下面的控制台输出里，
# 跑完就没了 —— 而分发服务的 inspect/release 恰恰需要这几个字段逐字一致。
# 安装包与 zip 是**同一批比特的两种形态**（同一 staging 产出），都登记，但 package_type 不同：
# 渠道制品是 directory-zip，安装包是额外交付物（不进渠道、不上传）。
$artifacts = New-Object System.Collections.Generic.List[object]
$artifacts.Add((New-ArtifactEntry -Path $ZipPath -OutputRoot $OutputRoot -ProductId $ProductId `
            -Platform 'windows' -Architecture 'x64' -PackageType 'directory-zip'))
if ($setupExe) {
    $artifacts.Add((New-ArtifactEntry -Path $setupExe.FullName -OutputRoot $OutputRoot -ProductId $ProductId `
                -Platform 'windows' -Architecture 'x64' -PackageType 'installer'))
}
$manifestPath = Write-ArtifactManifest -Dir (Get-ProductVersionDir -OutputRoot $OutputRoot -Product $Product -Version $Version) `
    -Product $Product -Version $Version -Channel $Channel -Artifacts $artifacts -OutputRoot $OutputRoot

# --- Summary + inspect handoff ---------------------------------------------
Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host ' IOStudio distribution artifact ready' -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
Write-Host "  product_id   : $ProductId"
Write-Host "  version      : $Version"
Write-Host "  channel      : $Channel"
Write-Host "  platform     : windows"
Write-Host "  architecture : x64"
Write-Host "  package_type : directory-zip"
Write-Host "  file         : $($zip.Name)"
Write-Host "  path         : $($zip.FullName)"
Write-Host "  size         : $($zip.Length) bytes"
Write-Host "  sha256       : $hash"
if ($setupExe) {
    Write-Host "  installer    : $($setupExe.Name)"
    Write-Host "  installer size   : $($setupExe.Length) bytes"
    Write-Host "  installer sha256 : $setupHash"
}
Write-Host "  manifest     : $manifestPath"
Write-Host ''
Write-Host '  Next: run the software-distribution skill step' -ForegroundColor Cyan
Write-Host '        software.distribution.artifact.inspect with:' -ForegroundColor Cyan
Write-Host "          workspace_root = $OutputRoot"
Write-Host "          artifact_path  = $($zip.FullName)"
Write-Host "          product_id     = $ProductId"
Write-Host "          version        = $Version"
Write-Host "          channel        = $Channel"
Write-Host '          platform       = windows'
Write-Host '          architecture   = x64'
Write-Host '          package_type   = directory-zip'
Write-Host ''
Write-Host '  Publishing (release.publish) requires explicit user confirmation.' -ForegroundColor Yellow
