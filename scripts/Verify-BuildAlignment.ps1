<#
.SYNOPSIS
    构建对齐检查：核对「要发出去的东西」是否与最新的插件库和资源配置一致。

.DESCRIPTION
    这个脚本**只读**，不改任何文件、不改构建行为。它回答一个问题：
    「这轮准备发布的产物，它的每一类外置资源，是不是都对得上最新的源？」

    为什么需要它：这些失配全部是**静默**的 —— 打出来的包照样"成功"，只是内容陈旧或多余。
    实测（2026-10-15）三类同时漂移：
      · 插件 DLL：56 个在发，其中 6 个源码目录已完全不存在（最早 2019），3 个产物比源旧
      · 插件配置：`IOUI/Config/` 有 30 个正源，只有 2 个插件会被 staged
      · Simulator web：发的是 5 月的快照，正源已是 9 月（三国志：三维视图都不在里面）

    检查项与判据：

      | 族                | 判据                                                       |
      |-------------------|------------------------------------------------------------|
      | 插件 DLL 孤儿     | 目录里有 DLL，但源工程 `IOUI/<ID>/*.vcxproj` 不存在         |
      | 插件 DLL 陈旧     | 源最新修改时间 > DLL 修改时间（含容差，见下）               |
      | 配置 正源覆盖     | `IOUI/Config/<ID>` 里哪些按 csproj 规则根本不会被 staged     |
      | 配置 内容一致     | 会被 staged 的，逐文件 SHA-256 必须与正源一致               |
      | 配置 落点垃圾     | staging 落点里出现正源没有的配置目录 / 散落裸文件            |
      | Simulator web     | 每个落点都必须含 `platformScene-*.js`（三维是懒加载 chunk）  |
      | native            | `IODevice/Source` + `IODevice_C_Wrapper` 源 vs `IODevice.dll`|

    两个判据上的坑（都实测过，写在这里免得后来人重踩）：

    **① 不要用「在不在解决方案里」判陈旧。** `CCBOX` / `ENCODER-*` / `SLDS-D10` 是
    2026-10-12 构建的，但不在 `IOUI.sln` 里（走别的构建路径）。按 sln 判会把它们误报。
    可靠判据是「源是否还存在 + 源是否比产物新」。

    **② mtime 有同日边界误报。** 实测 `NET-IOFE` 的源与 DLL 都是 2025-08-08。
    所以 DLL 用容差（默认 2 秒），配置这类小文件直接比**内容哈希**。

.PARAMETER Configuration
    检查哪个配置的构建输出（默认 Release，即发布用的那个）。

.PARAMETER Strict
    有 Error 级发现时以退出码 2 结束（可用于门禁）。**默认只告警、退出码 0** ——
    因为时间戳不是硬证据，不该拦住一次本来正确的发布。

.PARAMETER StagingDir
    可选：额外核对一份已解包的发布产物（发布脚本的 staging 目录），
    能发现「构建输出对齐、但打包环节漏了」这类问题。

.EXAMPLE
    .\Verify-BuildAlignment.ps1

.EXAMPLE
    .\Verify-BuildAlignment.ps1 -StagingDir ..\publish\IOStudio-1.0.19-win-x64 -Strict
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$IouiRootDir,
    [string]$IouiDir,
    [string]$NativeSourceDir,
    [string]$NativeWrapperSourceDir,
    [string]$NativeDir,
    [string]$StagingDir,

    # IOToolkit SDK 包的交付目录覆盖（默认 <仓库根>\IOToolkit-<version.txt>）。
    # 与打包脚本的 -DeliverableDir 同理：**先验候选、再动真交付**。
    [string]$SdkRoot,

    # 分发制品根（默认 <父仓库根>\publish）。用来看 publish\<产品>\<版本>\manifest.json
    # 里登记的大小与 sha256 是否仍然对得上 —— 制品被替换/截断/手改都会露出来。
    [string]$PublishRoot,

    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── 布局解析 ────────────────────────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir              # …/IODevice
$IouiParent = Split-Path -Parent $RepoRoot             # …/（IOUI 与 IODevice 同级）

if (-not $IouiRootDir) { $IouiRootDir = Join-Path $IouiParent 'IOUI' }
if (-not $IouiDir) { $IouiDir = Join-Path $IouiRootDir "Binaries\Win64\$Configuration" }
if (-not $NativeSourceDir) { $NativeSourceDir = Join-Path $RepoRoot 'IODevice\Source' }
if (-not $NativeWrapperSourceDir) { $NativeWrapperSourceDir = Join-Path $RepoRoot 'IODevice_C_Wrapper' }
if (-not $NativeDir) { $NativeDir = Join-Path $RepoRoot "Binaries\Win64\$Configuration" }

# mtime 容差：修改时间只有秒级分辨率，同批产出的源与产物可能极接近。
$MtimeToleranceSeconds = 2

# 插件配置的**正源**：IOUI 根下受 git 跟踪的 Config\<插件>\。
#
# 它也是发布时的唯一权威：Publish-IOStudioDistribution.ps1 会按一条通用规则
# 把这里的每个插件配置镜像进 staging（先清后拷）。所以本脚本不再维护"哪些插件会被 staged"
# 的清单 —— 那种清单会在规则变化时变成过期的假警报（实测：加了通用镜像规则后，
# 旧清单仍报 28 条"不会被 staged"，全是假警）。
$configSourceRoot = Join-Path $IouiRootDir 'Config'

$findings = New-Object System.Collections.Generic.List[object]
function Add-Finding {
    param(
        [ValidateSet('Error', 'Warn', 'Info')] [string]$Severity,
        [string]$Family,
        [string]$Item,
        [string]$Detail
    )
    $findings.Add([pscustomobject]@{
            Severity = $Severity
            Family   = $Family
            Item     = $Item
            Detail   = $Detail
        })
}

function Newest-Mtime {
    param([string]$Root, [string[]]$Patterns)
    if (-not $Root -or -not (Test-Path $Root)) { return $null }
    $files = @()
    foreach ($pattern in $Patterns) {
        $files += Get-ChildItem -Path $Root -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
    }
    if ($files.Count -eq 0) { return $null }
    return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
}

# 比对实现来自 PayloadLib.ps1 —— 与装配层（Assemble-Payload.ps1）**同一份**。
# 门禁与装配各写一份会分叉：装配认为一致的、门禁可能判不一致，那门禁的告警就失去意义。
. (Join-Path $PSScriptRoot 'PayloadLib.ps1')

# 制品清单与 ELF 判定的实现来自 OutputLayout.ps1 —— 与两个打包脚本**同一份**。
# 同理：门禁若自带一份"清单长什么样"的假设，打包改了格式它就会误判。
. (Join-Path $PSScriptRoot 'OutputLayout.ps1')

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' 构建对齐检查' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  配置    : $Configuration"
Write-Host "  插件输出: $IouiDir"
Write-Host "  native  : $NativeDir"
if ($StagingDir) { Write-Host "  发布产物: $StagingDir" }
Write-Host ''

# ── 1. 插件 DLL：孤儿与陈旧 ─────────────────────────────
Write-Host '>> 插件 DLL' -ForegroundColor Cyan
if (-not (Test-Path $IouiDir)) {
    Add-Finding -Severity Error -Family '插件 DLL' -Item $IouiDir -Detail '构建输出目录不存在（未构建？）'
}
else {
    $dlls = @(Get-ChildItem -Path $IouiDir -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue)
    Write-Host "   目录里有 $($dlls.Count) 个插件 DLL"

    foreach ($dll in $dlls) {
        $id = $dll.BaseName.Substring('IOUI-Win64-'.Length)

        # 已废弃插件：源码目录还在，所以下面那条"源码不存在"的判据抓不到它 ——
        # 但它**不该出现在任何产出里**。构建输出是积累目录，废弃后没人清理，
        # 只靠 glob 就会一路发出去（实测 10 个一直躺在 Release 里）。
        if (Test-DeprecatedPlugin -Id $id) {
            Add-Finding -Severity Warn -Family '插件 DLL' -Item "$id（已废弃）" `
                -Detail "已列入废弃清单（PayloadLib.ps1 的 DeprecatedPlugins），不该再出现在构建输出里。装配层已排除它，但文件仍在会污染判断。"
            continue
        }

        $projectDir = Join-Path $IouiRootDir $id
        $hasProject = (Test-Path $projectDir) -and
            (@(Get-ChildItem -Path $projectDir -Filter '*.vcxproj' -File -ErrorAction SilentlyContinue).Count -gt 0)

        if (-not $hasProject) {
            # 源码已不存在 = 纯遗留二进制。这不是"可能陈旧"，而是**不该外发**。
            Add-Finding -Severity Error -Family '插件 DLL' -Item $id `
                -Detail "源码工程不存在，产物时间 $($dll.LastWriteTime.ToString('yyyy-MM-dd'))。该 DLL 会被原样发出去。"
            continue
        }

        $newest = Newest-Mtime -Root $projectDir -Patterns @('*.cpp', '*.h', '*.hpp', '*.vcxproj')
        if ($newest -and $newest -gt $dll.LastWriteTime.AddSeconds($MtimeToleranceSeconds)) {
            Add-Finding -Severity Warn -Family '插件 DLL' -Item $id `
                -Detail "源 $($newest.ToString('yyyy-MM-dd')) 比产物 $($dll.LastWriteTime.ToString('yyyy-MM-dd')) 新，可能未重编。"
        }
    }
}

# ── 2. 插件配置 ─────────────────────────────────────────
Write-Host '>> 插件配置' -ForegroundColor Cyan
if (-not (Test-Path $configSourceRoot)) {
    Add-Finding -Severity Error -Family '插件配置' -Item $configSourceRoot -Detail '配置正源目录不存在'
}
else {
    $sourceDirs = @(Get-ChildItem -Path $configSourceRoot -Directory -ErrorAction SilentlyContinue)
    Write-Host "   正源 $($sourceDirs.Count) 个插件配置"

    # 2a. 覆盖：**发布脚本会镜像全部正源**（Publish-IOStudioDistribution.ps1 的
    #     「Sync plugin configs from their source of truth」一段，一条规则覆盖所有插件）。
    #     所以这里不再按 csproj 的 ItemGroup 判"会不会进包" —— 那只是构建期的一层，
    #     真正决定产物内容的是发布脚本的镜像步骤。
    #     本项改成核对**构建输出快照**与正源是否一致：快照陈旧不会影响发布产物
    #     （发布前会被镜像覆盖），但会让"从 Binaries 直接跑 IOStudio"的本地调试看到旧配置，
    #     而且它正是当初 web 漂了 4 个半月的原因，值得单独报出来。
    $snapshotLocations = @(
        @{ Label = 'IOUI 构建输出'; Root = (Join-Path $IouiDir 'ExternalLibraries\Config') },
        @{ Label = 'IODevice 构建输出/ExternalLibraries'; Root = (Join-Path $NativeDir 'ExternalLibraries\Config') },
        @{ Label = 'IODevice 构建输出/Config'; Root = (Join-Path $NativeDir 'Config') }
    ) | Where-Object { Test-Path $_.Root }

    foreach ($snapshot in $snapshotLocations) {
        foreach ($dir in $sourceDirs) {
            $mirror = Join-Path $snapshot.Root $dir.Name
            if (-not (Test-Path $mirror)) { continue }   # 该落点本就不放这个插件 → 不是失配
            $diffs = @(Compare-DirectoryContent -Source $dir.FullName -Target $mirror)
            if ($diffs.Count -gt 0) {
                $head = ($diffs | Select-Object -First 3) -join '；'
                $more = if ($diffs.Count -gt 3) { " …等 $($diffs.Count) 处" } else { '' }
                Add-Finding -Severity Warn -Family '插件配置' -Item "$($snapshot.Label)/$($dir.Name)" `
                    -Detail "快照与正源不一致: $head$more（发布时会被镜像覆盖；但本地从 Binaries 跑会看到旧配置）"
            }
        }
    }
    Add-Finding -Severity Info -Family '插件配置' -Item '覆盖方式' `
        -Detail "发布脚本按一条规则镜像全部 $($sourceDirs.Count) 个正源（不依赖 csproj 的逐项登记）"

    # 2c. 落点垃圾：孤儿的配置目录 + 散落裸文件
    # 这两类都是历史坏拷贝的痕迹。散落裸文件（Config 根下直接躺着 config.ini）
    # 是 xcopy 参数解析错时把文件铺平的典型症状。
    $stagingConfigRoots = @(
        (Join-Path $IouiDir 'ExternalLibraries\Config'),
        (Join-Path $NativeDir 'ExternalLibraries\Config')
    ) | Where-Object { Test-Path $_ } | Select-Object -Unique

    foreach ($root in $stagingConfigRoots) {
        foreach ($item in (Get-ChildItem -Path $root -ErrorAction SilentlyContinue)) {
            # `_legacy*` 是**有意保留的保管目录**：源码已丢失或位置错乱的产物被移到这里
            # （见目录内 README）。它们不再参与 glob，所以不会被打包；本脚本不该把它们
            # 报成"孤儿落点"——那是它自己造成的假警报。
            if ($item.Name -like '_legacy*') { continue }

            if ($item.PSIsContainer) {
                if (-not (Test-Path (Join-Path $configSourceRoot $item.Name))) {
                    Add-Finding -Severity Warn -Family '插件配置' -Item "$($item.Name) (落点孤儿)" `
                        -Detail "在 $root 里，但正源没有对应插件 —— 多半是已删插件的残留"
                }
            }
            else {
                Add-Finding -Severity Warn -Family '插件配置' -Item "$($item.Name) (落点散落)" `
                    -Detail "裸文件躺在 Config 根下（$root）—— 坏拷贝的痕迹"
            }
        }
    }
}

# ── 3. Simulator web：完整性 + 与正源的一致性 ───────────
Write-Host '>> Simulator web' -ForegroundColor Cyan
$webSourceRoot = Join-Path $configSourceRoot 'Simulator\web'
$webAssetsSource = Join-Path $IouiRootDir 'SimulatorWeb\src'

$webLocations = @(
    @{ Label = '正源 IOUI/Config/Simulator'; Path = $webSourceRoot },
    @{ Label = 'IOUI 构建输出'; Path = (Join-Path $IouiDir 'ExternalLibraries\Config\Simulator\web') },
    @{ Label = 'IODevice 构建输出/ExternalLibraries'; Path = (Join-Path $NativeDir 'ExternalLibraries\Config\Simulator\web') },
    @{ Label = 'IODevice 构建输出/Config'; Path = (Join-Path $NativeDir 'Config\Simulator\web') }
)
if ($StagingDir) {
    $webLocations += @{ Label = '发布产物/ExternalLibraries'; Path = (Join-Path $StagingDir 'ExternalLibraries\Config\Simulator\web') }
    $webLocations += @{ Label = '发布产物/Config'; Path = (Join-Path $StagingDir 'Config\Simulator\web') }
}

foreach ($location in $webLocations) {
    if (-not (Test-Path $location.Path)) {
        Add-Finding -Severity Info -Family 'Simulator web' -Item $location.Label -Detail '该落点没有 web 目录'
        continue
    }
    $assets = Join-Path $location.Path 'assets'
    # 三维视图是**懒加载**的独立 chunk：主 bundle 里只有一个动态 import 的名字。
    # 所以必须确认 chunk 实际存在，否则页面上「三维视图」是一片空白。
    $hasScene = @(Get-ChildItem -Path $assets -Filter 'platformScene-*.js' -File -ErrorAction SilentlyContinue).Count -gt 0
    $newest = Newest-Mtime -Root $assets -Patterns @('*.js')
    $when = if ($newest) { $newest.ToString('yyyy-MM-dd') } else { '?' }

    if (-not $hasScene) {
        Add-Finding -Severity Error -Family 'Simulator web' -Item $location.Label `
            -Detail "缺 platformScene-*.js（产物 $when）—— 三维视图会加载失败"
    }
    else {
        Add-Finding -Severity Info -Family 'Simulator web' -Item $location.Label -Detail "含三维 chunk（$when）"
    }
}

if (Test-Path $webAssetsSource) {
    $newestSource = Newest-Mtime -Root $webAssetsSource -Patterns @('*.ts', '*.vue')
    if ($newestSource) {
        Write-Host "   前端源最新 $($newestSource.ToString('yyyy-MM-dd'))"
        foreach ($location in $webLocations) {
            if (-not (Test-Path $location.Path)) { continue }
            $newestBuilt = Newest-Mtime -Root (Join-Path $location.Path 'assets') -Patterns @('*.js')
            if ($newestBuilt -and $newestSource -gt $newestBuilt.AddSeconds($MtimeToleranceSeconds)) {
                Add-Finding -Severity Warn -Family 'Simulator web' -Item $location.Label `
                    -Detail "产物 $($newestBuilt.ToString('yyyy-MM-dd')) 早于前端源 $($newestSource.ToString('yyyy-MM-dd')) —— 需重跑 npm run build"
            }
        }
    }
}
else {
    Add-Finding -Severity Info -Family 'Simulator web' -Item '前端源' -Detail "本机没有 $webAssetsSource，无法比对时间"
}

# ── 4. native：源 vs DLL ────────────────────────────────
Write-Host '>> native' -ForegroundColor Cyan
foreach ($pair in @(
        @{ Name = 'IODevice.dll'; Source = $NativeSourceDir },
        @{ Name = 'IODevice_C_Wrapper.dll'; Source = $NativeWrapperSourceDir }
    )) {
    $dllPath = Join-Path $NativeDir $pair.Name
    if (-not (Test-Path $dllPath)) {
        Add-Finding -Severity Error -Family 'native' -Item $pair.Name -Detail '构建输出里没有这个 DLL'
        continue
    }
    $newest = Newest-Mtime -Root $pair.Source -Patterns @('*.cpp', '*.h', '*.hpp', '*.inl')
    if ($newest) {
        $dllTime = (Get-Item $dllPath).LastWriteTime
        if ($newest -gt $dllTime.AddSeconds($MtimeToleranceSeconds)) {
            Add-Finding -Severity Warn -Family 'native' -Item $pair.Name `
                -Detail "源 $($newest.ToString('yyyy-MM-dd HH:mm')) 比 DLL $($dllTime.ToString('yyyy-MM-dd HH:mm')) 新，可能未重编。"
        }
        else {
            Add-Finding -Severity Info -Family 'native' -Item $pair.Name -Detail "产物不早于源（$($dllTime.ToString('yyyy-MM-dd HH:mm'))）"
        }
    }
}

# ── 5. 可选：核对最终发布产物 ──────────────────────────
if ($StagingDir -and (Test-Path $StagingDir)) {
    Write-Host '>> 发布产物' -ForegroundColor Cyan

    $stagedPlugins = Join-Path $StagingDir 'ExternalLibraries'
    if (Test-Path $stagedPlugins) {
        $builtHashes = @{}
        foreach ($dll in @(Get-ChildItem -Path $IouiDir -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue)) {
            $builtHashes[$dll.Name] = (Get-FileHash $dll.FullName -Algorithm SHA256).Hash
        }
        foreach ($dll in @(Get-ChildItem -Path $stagedPlugins -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue)) {
            if (-not $builtHashes.ContainsKey($dll.Name)) {
                Add-Finding -Severity Warn -Family '发布产物' -Item $dll.Name -Detail '产物里有、构建输出里没有（来源不明）'
            }
            elseif ((Get-FileHash $dll.FullName -Algorithm SHA256).Hash -ne $builtHashes[$dll.Name]) {
                Add-Finding -Severity Warn -Family '发布产物' -Item $dll.Name -Detail '与构建输出的内容不一致'
            }
        }
    }
}

# ── 5b. IOToolkit SDK 包 ────────────────────────────────
# SDK 包（给 Unity / UE4 嵌入）与应用包消费的是**同一份载荷**，但由另一条链路装配。
# 实测它此前 30 个插件配置里只有 22 个 —— 而且缺的那批里有一个（SLDS-D10）从 2022 年
# 就在正源里、四级目录一个都没有。这类缺失完全无声，只有门禁能报出来。
Write-Host '>> IOToolkit SDK 包' -ForegroundColor Cyan

$versionFile = Join-Path $IouiParent 'version.txt'
# 先声明：版本文件缺失时下面的分支不会给它赋值，而后面的 Android 小节会读它
# —— Set-StrictMode 下读未定义变量会直接抛。
#
# 名字**必须**与参数 $SdkRoot 不同：PowerShell 变量名大小写不敏感，$sdkRoot 就是
# $SdkRoot 本身 —— 先把它置 null 再 `if ($SdkRoot)`，条件恒为假，-SdkRoot 会被静默忽略
# （实测：传了 -SdkRoot 仍走默认路径）。
$sdkPackageRoot = $null
if (-not (Test-Path $versionFile)) {
    Add-Finding -Severity Info -Family 'IOToolkit SDK 包' -Item 'version.txt' -Detail '仓库根没有 version.txt，跳过 SDK 包检查'
}
else {
    $sdkVersion = (Get-Content -Path $versionFile -Raw).Trim()
    if ($sdkVersion -notmatch '^v') { $sdkVersion = "v$sdkVersion" }
    $sdkPackageRoot = if ($SdkRoot) { $SdkRoot } else { Join-Path $IouiParent "IOToolkit-$sdkVersion" }

    if (-not (Test-Path $sdkPackageRoot)) {
        Add-Finding -Severity Info -Family 'IOToolkit SDK 包' -Item "IOToolkit-$sdkVersion" `
            -Detail '交付目录不存在（未打包过）'
    }
    else {
        # 每个平台的插件配置落点 + Studio 的那份。
        # Android 落点的配置是**按确实构建出 .so 的插件**种子的（不是全 30 个），
        # 所以它不参与下面 Sync-PluginConfigs 的"应等于正源全集"比对 ——
        # 那是 Windows 载荷的语义。Android 单独在下面按 .so 集合核对。
        $sdkConfigTargets = @(
            foreach ($plat in @('Win32', 'Win64')) {
                $p = Join-Path $sdkPackageRoot "IOToolkit\binaries\$plat\ExternalLibraries\Config"
                if (Test-Path $p) { $p }
            }
            $studioConfig = Join-Path $sdkPackageRoot 'IOToolkit_Studio\ExternalLibraries\Config'
            if (Test-Path $studioConfig) { $studioConfig }
        )
        Write-Host "   交付目录 $sdkPackageRoot；配置落点 $($sdkConfigTargets.Count) 个（布局 <包>\IOToolkit\binaries\<平台>\ExternalLibraries\Config）"

        $checked = Sync-PluginConfigs -IouiRoot $IouiRootDir -Targets $sdkConfigTargets -DryRun
        # 同一个问题会在每个平台落点各出现一次（Win32 / Win64 / Studio）—— 合并计数，
        # 否则 8 个缺失插件会报成十几条，把"有几类问题"这个信息淹掉。
        # 严重度编进 Item 的**前缀**：报告末尾是按 (Severity, Item) 排序的，而 PowerShell 的
        # Sort-Object 不稳定 —— 只把严重度放在 Detail 里的话，顺序会被打乱
        # （实测"整个配置缺失"跑到了内容差异后面）。前缀同时让人一眼看出问题类别。
        # 同一个问题会在每个平台落点各出现一次，这里合并计数。
        $kindOf = {
            param($text)
            if ($text -match '目标没有这个插件配置') { '1-整个缺失' }
            elseif ($text -match '缺失 ') { '2-文件缺失' }
            elseif ($text -match '内容不一致 ') { '3-内容陈旧' }
            elseif ($text -match '多余 ') { '4-多余文件' }
            else { '9-其他' }
        }
        $grouped = @(
            $checked.Diffs | Group-Object | ForEach-Object {
                [pscustomobject]@{
                    Kind  = & $kindOf $_.Name
                    Text  = $_.Name
                    Count = $_.Count
                }
            } | Sort-Object -Property Kind, Text
        )
        foreach ($item in @($grouped | Select-Object -First 12)) {
            $suffix = if ($item.Count -gt 1) { "（$($item.Count) 个落点）" } else { '' }
            Add-Finding -Severity Warn -Family 'IOToolkit SDK 包' -Item "插件配置·$($item.Kind)" `
                -Detail "$($item.Text)$suffix"
        }
        if ($grouped.Count -gt 12) {
            Add-Finding -Severity Warn -Family 'IOToolkit SDK 包' -Item '插件配置·zz 汇总' `
                -Detail "…还有 $($grouped.Count - 12) 类问题未列出（共 $($checked.DiffCount) 处）"
        }
        if ($checked.DiffCount -eq 0) {
            Add-Finding -Severity Info -Family 'IOToolkit SDK 包' -Item '插件配置' `
                -Detail "全部 $($sdkConfigTargets.Count) 个落点与正源逐文件一致"
        }

        # 孤儿 DLL 不该出现在交付产物里（它们是"源码已不存在却一直在发"的那批）
        $orphanNames = @('COMDEV', 'ExternalTest', 'MOTION-PLAT', 'MOTION-PLAT_1f', 'SERIALPORT', 'SOCKET-IOFE')
        $leaked = @(Get-ChildItem -Path $sdkPackageRoot -Recurse -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue |
            Where-Object { $id = $_.BaseName.Substring('IOUI-Win64-'.Length); $orphanNames -contains $id })
        if ($leaked.Count -gt 0) {
            Add-Finding -Severity Error -Family 'IOToolkit SDK 包' -Item '遗留插件' `
                -Detail "$($leaked.Count) 个源码已不存在的 DLL 混进了交付包: $(($leaked | Select-Object -First 4 | ForEach-Object { $_.Name }) -join ', ')"
        }

        # 已废弃插件：源码还在，所以上面那条"源码不存在"抓不到 —— 但它们明确不该外发。
        # 装配层已按清单排除（PayloadLib 的 $DeprecatedPlugins），这里是**独立复核**：
        # 万一有人手工塞进包、或装配层的排除被改坏，这一条能当场报出来。
        $deprecatedInPkg = @(Get-ChildItem -Path $sdkPackageRoot -Recurse -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue |
            Where-Object { Test-DeprecatedPlugin -Id $_.BaseName.Substring('IOUI-Win64-'.Length) })
        if ($deprecatedInPkg.Count -gt 0) {
            Add-Finding -Severity Error -Family 'IOToolkit SDK 包' -Item '废弃插件' `
                -Detail "$($deprecatedInPkg.Count) 个已废弃插件混进了交付包: $(($deprecatedInPkg | Select-Object -First 4 | ForEach-Object { $_.Name }) -join ', ')"
        }

        # 同一内容产出两份的历史包袱：报出来让人确认哪个才该留
        $legacyIntermediate = Join-Path $IouiParent $sdkVersion
        if ((Test-Path $legacyIntermediate) -and (Test-Path $sdkPackageRoot)) {
            Add-Finding -Severity Info -Family 'IOToolkit SDK 包' -Item $sdkVersion `
                -Detail "旧的中间目录仍在（$legacyIntermediate）。新流程只产出 IOToolkit-$sdkVersion，确认无用后可删"
        }
    }
}

# ── 6. Android 载荷：缺失、命名契约、ABI ─────────────────
# Android .so 的加载契约是 **IOUI-ANDROID-<DllName 转大写>.so**（RawIOFactory.cpp）。
# 命名在这里特别值得设一道门：Linux 区分大小写，一个 `IOUI-ANDROID-Simulator.so`
# 在 Windows 上"看起来没问题"，在设备上就是静默的"设备初始化失败"。
#
# 期望清单**从源码推导**（哪些插件工程真有 CMakeLists），不写死 6 个名字 ——
# 写死的话，新增一个 Android 插件时门禁不会提醒它没被构建。
Write-Host '>> Android 载荷' -ForegroundColor Cyan

$androidPayloadDirs = @(
    @{ Label = 'SDK 包'; Path = (Join-Path $sdkPackageRoot 'IOToolkit\binaries\Android\ExternalLibraries') }
    @{ Label = 'IOUI 构建输出'; Path = (Join-Path $IouiDir 'ExternalLibraries') }
) | Where-Object { $_.Path -and (Test-Path $_.Path) }

# 期望的插件 id：有 CMakeLists.txt 的插件目录 + 内核 iodevice
$androidCapable = @(
    Get-ChildItem -Path $IouiRootDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'CMakeLists.txt') } |
        Select-Object -ExpandProperty Name
)
$androidCoreDir = Join-Path $IouiParent 'IODevice\IODevice'
if (Test-Path (Join-Path $androidCoreDir 'CMakeLists.txt')) { $androidCapable += 'iodevice' }

if ($androidCapable.Count -eq 0) {
    Add-Finding -Severity Info -Family 'Android 载荷' -Item '期望集' -Detail '源码里没有 Android CMakeLists，跳过'
}
else {
    Write-Host "   源码中具备 Android 构建的插件 $($androidCapable.Count) 个"
    if ($androidPayloadDirs.Count -eq 0) {
        Add-Finding -Severity Info -Family 'Android 载荷' -Item '落点' `
            -Detail '没有可检查的 Android 载荷落点（未构建过 Android）'
    }
    foreach ($loc in $androidPayloadDirs) {
        $sos = @(Get-ChildItem -Path $loc.Path -File -Filter '*.so' -ErrorAction SilentlyContinue)
        if ($sos.Count -eq 0) { continue }

        # 命名契约 + ABI
        foreach ($so in $sos) {
            if ($so.Name -cnotmatch '^IOUI-ANDROID-[A-Z0-9\-]+\.so$') {
                Add-Finding -Severity Error -Family 'Android 载荷' -Item "$($loc.Label)/命名" `
                    -Detail "$($so.Name) 不符合加载器契约（须为大写 IOUI-ANDROID-<DllName>.so）—— Linux 区分大小写，设备上会静默加载失败"
            }
            elseif (-not (Test-ElfAarch64 -Path $so.FullName)) {
                Add-Finding -Severity Error -Family 'Android 载荷' -Item "$($loc.Label)/ABI" `
                    -Detail "$($so.Name) 不是 ELF64/AArch64（ABI 参数或工具链不对，装到 arm64 设备上会加载失败）"
            }
        }

        # 覆盖：有哪些源码支持 Android 却没产出 .so
        $built = @($sos | ForEach-Object { $_.BaseName.Substring('IOUI-ANDROID-'.Length) })
        $missing = @($androidCapable | Where-Object { $_ -ne 'iodevice' } | Where-Object { $built -notcontains $_ })
        if ($missing.Count -gt 0) {
            Add-Finding -Severity Warn -Family 'Android 载荷' -Item "$($loc.Label)/覆盖" `
                -Detail "源码支持 Android 但落点里没有 .so: $($missing -join ', ')（跑 Build-AndroidPayload.ps1）"
        }
        $coreSo = Join-Path (Split-Path -Parent $loc.Path) 'iodevice.so'
        if (-not (Test-Path $coreSo)) {
            $coreSo = Join-Path (Split-Path -Parent (Split-Path -Parent $loc.Path)) 'iodevice.so'
        }
        if (-not (Test-Path $coreSo)) {
            Add-Finding -Severity Warn -Family 'Android 载荷' -Item "$($loc.Label)/内核" `
                -Detail '缺 iodevice.so（Android 内核），插件无法被加载'
        }
        if ($missing.Count -eq 0 -and (Test-Path $coreSo)) {
            Add-Finding -Severity Info -Family 'Android 载荷' -Item $loc.Label `
                -Detail "$($sos.Count) 个插件 .so + iodevice.so，命名与 ABI 均符合契约"
        }
    }
}

# ── 7. 分发制品清单（publish\）：登记的文件是否仍然对得上 ─
# manifest.json 记的是"产出那一刻"的大小与 sha256。制品被替换、被截断、被手改都会
# 与它不符 —— 而分发服务的 inspect 恰恰要求这几个字段逐字一致，不符就不该发。
Write-Host '>> 分发制品清单' -ForegroundColor Cyan

# 分发制品的根是**父仓库根**下的 publish\（所有产品的产出都集中在那儿）。
# 这里用 $IouiParent（= IODevice 子模块的父目录），与两个打包脚本的默认值一致 ——
# 若门禁按另一套路径找，就会"报告一切正常"而实际什么都没看。
$publishRoot = if ($PublishRoot) { $PublishRoot } else { Join-Path $IouiParent 'publish' }
$manifests = @()
if (Test-Path $publishRoot) {
    $manifests = @(Get-ChildItem -Path $publishRoot -Recurse -File -Filter 'manifest.json' -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -notmatch '\\_staging\\' })
}
if ($manifests.Count -eq 0) {
    Add-Finding -Severity Info -Family '分发制品清单' -Item 'publish' `
        -Detail "没有找到 manifest.json（$publishRoot）—— 尚未用新流程产出过制品"
}
else {
    Write-Host "   publish 下 $($manifests.Count) 份清单"
    $totalArtifacts = 0
    foreach ($m in $manifests) {
        $diffs = @(Compare-ArtifactManifest -ManifestPath $m.FullName -OutputRoot $publishRoot)
        $doc = Get-Content -LiteralPath $m.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $totalArtifacts += @($doc.artifacts).Count
        $label = "$($doc.product)/$($doc.version)"
        if ($diffs.Count -gt 0) {
            foreach ($d in @($diffs | Select-Object -First 4)) {
                Add-Finding -Severity Error -Family '分发制品清单' -Item "$label" -Detail $d
            }
        }
        else {
            Add-Finding -Severity Info -Family '分发制品清单' -Item $label `
                -Detail "$(@($doc.artifacts).Count) 个制品的大小与 sha256 与清单一致"
        }

        # ── 7b. 消费方视图：必备条目是否真的在里面 ─────────
        # 光"制品存在且摘要对得上"不够 —— 包可能内容对得上清单、却缺消费方真正要的文件。
        # 这里按 (consumer, platform) 逐条核对必备条目。
        # 三处的判据都有实测来由：
        #   · unity/windows 必须有 IODevice_C_Wrapper.dll —— Windows 的 DllImport 名字就是它
        #     （IONativeWrapper.cs 的非 Android 分支），实测 Unity 现有包的 x86_64 里恰恰缺这个；
        #   · unity/android 必须有 libiodevice.so —— DllImport("iodevice") 在 Android 上按
        #     Linux 惯例找 lib 前缀；
        #   · ue 必须同时含 Win64 与 Android —— UE 侧此前只有 Win32/Win64。
        $viewRequirements = @{
            'unity|windows' = @(
                'Plugins/IODevice_CSharp_Wrapper.dll',
                'Plugins/x86_64/IODevice.dll',
                'Plugins/x86_64/IODevice_C_Wrapper.dll',
                'Plugins/x86_64/ExternalLibraries/IOUI-Win64-IOUI.dll'
            )
            'unity|android' = @(
                'Plugins/Android/IODevice_CSharp_Wrapper.dll',
                'Plugins/Android/arm64-v8a/iodevice.so',
                'Plugins/Android/arm64-v8a/libiodevice.so',
                'Plugins/Android/ExternalLibraries/IOUI-ANDROID-IOUI.so'
            )
            'ue|windows' = @(
                'ThirdParty/IOToolkit/include/IODevice.h',
                'ThirdParty/IOToolkit/binaries/Win64/IODevice.dll',
                'ThirdParty/IOToolkit/binaries/Android/iodevice.so',
                'ThirdParty/IOToolkit/binaries/Android/ExternalLibraries/IOUI-ANDROID-IOUI.so'
            )
        }
        foreach ($a in @($doc.artifacts)) {
            $c = $a.PSObject.Properties['consumer']
            if (-not $c -or [string]::IsNullOrWhiteSpace($a.consumer) -or $a.consumer -eq 'canonical') { continue }
            $key = "$($a.consumer)|$($a.platform)"
            if (-not $viewRequirements.ContainsKey($key)) { continue }
            $zipPath = Join-Path $publishRoot ($a.relativePath -replace '/', '\')
            if (-not (Test-Path $zipPath)) { continue }   # 上面已作为摘要不符报过
            $names = Get-ZipEntryNames -ZipPath $zipPath
            $missing = @($viewRequirements[$key] | Where-Object { $names -notcontains $_ })
            if ($missing.Count -gt 0) {
                Add-Finding -Severity Error -Family '消费方视图' -Item "$label · $key" `
                    -Detail "$($a.fileName) 缺消费方必备条目 $($missing.Count) 个: $($missing -join ', ')"
            }
            else {
                Add-Finding -Severity Info -Family '消费方视图' -Item "$label · $key" `
                    -Detail "$($a.fileName)：$($names.Count) 个条目，必备条目齐备"
            }
        }
    }
}

# ── 报告 ────────────────────────────────────────────────
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' 检查结果' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$errors = @($findings | Where-Object Severity -eq 'Error')
$warns = @($findings | Where-Object Severity -eq 'Warn')
$infos = @($findings | Where-Object Severity -eq 'Info')

if ($findings.Count -eq 0) {
    Write-Host '  未发现失配。' -ForegroundColor Green
}
else {
    foreach ($group in ($findings | Group-Object Family)) {
        Write-Host ''
        Write-Host "  [$($group.Name)]" -ForegroundColor White
        # Info 太多时只统计，避免淹掉真正要看的东西
        $shown = @($group.Group | Where-Object Severity -ne 'Info')
        if ($shown.Count -eq 0) { $shown = @($group.Group) }
        foreach ($finding in ($shown | Sort-Object Severity, Item)) {
            $color = switch ($finding.Severity) {
                'Error' { 'Red' }
                'Warn' { 'Yellow' }
                default { 'DarkGray' }
            }
            Write-Host ("    {0,-5} {1,-34} {2}" -f $finding.Severity, $finding.Item, $finding.Detail) -ForegroundColor $color
        }
        $infoCount = @($group.Group | Where-Object Severity -eq 'Info').Count
        if ($infoCount -gt 0) {
            Write-Host "    (另有 $infoCount 条 Info，表示该项已对齐)" -ForegroundColor DarkGray
        }
    }
}

Write-Host ''
Write-Host "  汇总: Error $($errors.Count) / Warn $($warns.Count) / Info $($infos.Count)" `
    -ForegroundColor $(if ($errors.Count) { 'Red' } elseif ($warns.Count) { 'Yellow' } else { 'Green' })
Write-Host ''
if ($errors.Count -gt 0) {
    Write-Host '  Error 级 = 不该外发（如源码已不存在的遗留二进制、缺失的三维 chunk）。' -ForegroundColor Red
}
if ($warns.Count -gt 0) {
    Write-Host '  Warn  级 = 可能陈旧或缺失，请人工确认后再打包。' -ForegroundColor Yellow
}
Write-Host '  本脚本只读，不修改任何文件，也不阻断构建（除非传 -Strict）。' -ForegroundColor DarkGray
Write-Host ''

if ($Strict -and ($errors.Count -gt 0)) {
    exit 2
}
exit 0
