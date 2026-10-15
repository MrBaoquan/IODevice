<#
.SYNOPSIS
    把 UNIPlayer 侧 IOToolkit 插件的原生载荷对齐到**最新构建**（含 IOStudio.exe）。

.DESCRIPTION
    对齐源用**已产出的制品**，不用构建输出目录。理由：`publish\iotoolkit\` 下的
    Unity 消费方视图正是 UNIPlayer 要的那种形状（Plugins/<架构>/ + 托管包装），
    而构建输出目录 `Binaries\` 是积累目录 —— 废弃插件的旧 DLL 会一直躺在里面，
    按通配取件就会把废弃项一起铺进去（这正是这次要根治的问题之一）。

        publish\iotoolkit\<ver>\windows\IOToolkit-<ver>-unity-win.zip        → Plugins\{x86_64,x86}
        publish\iotoolkit\<ver>\android\IOToolkit-<ver>-unity-android-*.zip  → Plugins\Android
        publish\iostudio\<ver>\windows\IOStudio-<ver>-win-x64.zip            → Plugins\x86_64\IOStudio.exe
        IOUI\Config\<插件>\                                                   → IOToolkit\Config\<插件>\

    **直接按 zip 条目名推算落点**，不先解压再走文件系统 —— 后者的相对路径推算容易错位
    （实测踩过：前缀长度算错 2 个字符，落点全歪）。

    **只改 Plugins\ 与 Config\<插件>\**，不碰 Editor\ / Runtime\ / asmdef 等 Unity 侧资产 ——
    那个仓库有自己的未提交改动（实测 24 处），本脚本不该动它们。

    .meta 处理：**已存在的 .meta 一律不动**（GUID 是 Unity 的资产标识，改了会断引用）。
    只为**新增**的文件/目录合成 .meta —— 模板取同目录下已有的同类 .meta（插件 DLL 的
    PluginImporter 平台开关是逐文件独立的，让 Unity 重新导入**不会**得到正确的开关：
    实测 39/40 个插件的 .meta 用同一份模板），GUID 由相对插件根的路径确定性派生，重跑稳定。

.PARAMETER DryRun
    只报"将要改什么"，不写任何文件。**建议先跑一次**。

.PARAMETER Target
    windows / android / all（默认 all）。

.PARAMETER SkipStudio
    不铺 IOStudio.exe（约 99 MB）。

.PARAMETER SkipConfig
    不动 IOToolkit\Config\<插件>\。
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$UniPlayerRoot,
    [string]$IouiRootDir,
    [string]$PublishRoot,

    [ValidateSet('all', 'windows', 'android')]
    [string]$Target = 'all',

    [switch]$SkipStudio,
    [switch]$SkipConfig,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host ">> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   $Text" -ForegroundColor Green }
function Write-Plan([string]$Text) { Write-Host "   $Text" -ForegroundColor DarkGray }

# 链接副产物判定：只认**以插件命名**的那些（IOUI-Win64-Simulator.lib / .exp / .pdb）。
#
# 不能按扩展名一概而论：`ExternalLibraries\Core\*.lib` 是**厂商导入库**
# （PCI/USB/LIBUSB/snap7 等），它们是载荷的一部分、C++/SDK 侧链接时要用，
# 不是构建垃圾。实测按扩展名一刀切会误报 32 个。
# 判据用文件名前缀而不是所在目录，因为 .pdb 也可能落在插件旁边。
function Test-PluginLinkArtifact {
    param([System.IO.FileInfo]$File)
    if ($File.DirectoryName -match '[\\/]Core$') { return $false }   # 厂商库目录，整体豁免
    return ($File.Name -match '^IOUI-(Win32|Win64|ANDROID)') -and ($File.Name -match '\.(exp|lib|pdb|ilk|iobj|ipdb)$')
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $ScriptDir }
$OuterRoot = Split-Path -Parent $RepoRoot
if (-not $IouiRootDir) { $IouiRootDir = Join-Path $OuterRoot 'IOUI' }
if (-not $PublishRoot) { $PublishRoot = Join-Path $OuterRoot 'publish' }
if (-not $UniPlayerRoot) { $UniPlayerRoot = Join-Path $OuterRoot 'UNIPlayer' }

. (Join-Path $ScriptDir 'OutputLayout.ps1')
. (Join-Path $ScriptDir 'PayloadLib.ps1')

Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$pluginRoot = Join-Path $UniPlayerRoot 'Assets\Plugins\IOToolkit'
if (-not (Test-Path $pluginRoot)) { throw "UNIPlayer 的 IOToolkit 插件目录不存在: $pluginRoot" }
$pluginsDir = Join-Path $pluginRoot 'Plugins'
$configDir = Join-Path $pluginRoot 'Config'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' UNIPlayer IOToolkit 插件对齐' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  插件目录 : $pluginRoot"
Write-Host "  分发根   : $PublishRoot"
Write-Host "  范围     : $Target$(if ($SkipStudio) { '（不含 IOStudio.exe）' })$(if ($DryRun) { '  [DRY-RUN]' })"
Write-Host ''

# ── 解析制品 ────────────────────────────────────────────
$sdkVerFile = Join-Path $OuterRoot 'version.txt'
if (-not (Test-Path $sdkVerFile)) { throw "找不到版本来源: $sdkVerFile" }
$sdkVerRaw = (Get-Content $sdkVerFile -Raw).Trim()      # v2.4.1 —— **文件名**用它
$sdkVer = $sdkVerRaw.TrimStart('v', 'V')                # 2.4.1  —— **目录名**用它
# 两种写法不是笔误：既成约定是目录剥 v、文件名保留 v（Get-PlatformDir 剥 v，zip 名用 $Version）。
# 用错会"制品明明在却报缺失"。

$sdkDir = Join-Path $PublishRoot "iotoolkit\$sdkVer"
$viewZips = New-Object System.Collections.Generic.List[string]
if ($Target -in @('all', 'windows')) {
    $p = Join-Path $sdkDir "windows\IOToolkit-$sdkVerRaw-unity-win.zip"
    if (Test-Path $p) { $viewZips.Add($p) }
}
if ($Target -in @('all', 'android')) {
    $p = Join-Path $sdkDir "android\IOToolkit-$sdkVerRaw-unity-android-arm64-v8a.zip"
    if (Test-Path $p) { $viewZips.Add($p) }
}
if ($viewZips.Count -eq 0) {
    throw "缺少制品。先跑 Build-IOToolkitPackage.ps1；期望在 $sdkDir 找到 unity 消费方视图。"
}
foreach ($z in $viewZips) { Write-Host "  视图: $(Split-Path -Leaf $z)" }

$studioZip = $null
if (-not $SkipStudio -and $Target -in @('all', 'windows')) {
    $studioRoot = Join-Path $PublishRoot 'iostudio'
    $latest = Get-ChildItem -Path $studioRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '[^0-9.]', '') } | Select-Object -Last 1
    if ($latest) {
        $studioZip = Get-ChildItem -Path (Join-Path $latest.FullName 'windows') -Filter '*.zip' -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ($studioZip) { Write-Host "  IOStudio 源: $(Split-Path -Leaf (Split-Path -Parent $studioZip))\$([IO.Path]::GetFileName($studioZip))" }
    else { Write-Warning 'publish\iostudio 下找不到制品 —— 跳过 IOStudio.exe。' }
}

# ── 计划 ────────────────────────────────────────────────
# 直接读 zip 条目。约定：条目名形如 Plugins/<相对路径>，落点 = <pluginRoot>\<相对路径>。
Write-Step '计算差异'

# 在**一次**枚举里把条目摘要也算出来 —— zip 只打开一次。
# （早先按文件逐个开 zip 取摘要的写法，300 个文件要开关 300 次，且还要多写一个 helper。）
$sha256 = [System.Security.Cryptography.SHA256]::Create()
function Get-StreamSha256 {
    param([System.IO.Stream]$Stream)
    $h = $sha256.ComputeHash($Stream)
    return (-join ($h | ForEach-Object { $_.ToString('x2') }))
}

$toAdd = @(); $toUpdate = @(); $same = 0; $activeIds = @()
$plans = @()      # @{ Zip; Entry; Rel; Dst; Len; Hash }

try {
    foreach ($zipPath in $viewZips) {
        $za = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            foreach ($e in $za.Entries) {
                if ($e.Length -eq 0 -and $e.FullName.EndsWith('/')) { continue }   # 目录条目
                $name = $e.FullName
                if (-not $name.StartsWith('Plugins/', [System.StringComparison]::Ordinal)) { continue }
                $rel = $name.Substring('Plugins/'.Length)                          # 例如 x86_64/IODevice.dll
                if ([string]::IsNullOrWhiteSpace($rel)) { continue }
                $relWin = $rel.Replace('/', '\')
                $dst = Join-Path $pluginsDir $relWin

                # 活跃插件集合：取自视图里的插件文件本身（DLL / .so）
                if ($rel -match 'ExternalLibraries/IOUI-(Win32|Win64|ANDROID)-(.+)\.(dll|so)$') {
                    $activeIds += $Matches[2].ToUpperInvariant()
                }

                $hash = $null
                if (Test-Path -LiteralPath $dst) { $hash = Get-StreamSha256 -Stream $e.Open() }
                $plans += [pscustomobject]@{ Zip = $zipPath; Entry = $name; Rel = $relWin; Dst = $dst; Len = $e.Length; Hash = $hash }
            }
        }
        finally { $za.Dispose() }
    }
}
finally { $sha256.Dispose() }
$activeIds = @($activeIds | Sort-Object -Unique)

foreach ($p in $plans) {
    if (-not (Test-Path -LiteralPath $p.Dst)) { $toAdd += $p; continue }
    if ((Get-Item -LiteralPath $p.Dst).Length -ne $p.Len) { $toUpdate += $p; continue }
    # 长度相同再比内容
    if ($p.Hash -ne (Get-Sha256 -Path $p.Dst)) { $toUpdate += $p } else { $same++ }
}

# 待删：废弃插件 / 不在活跃集合里的插件 / 构建产物
$toDelete = @()
foreach ($f in Get-ChildItem -LiteralPath $pluginsDir -Recurse -File -ErrorAction SilentlyContinue) {
    if ($f.Name -match '^IOUI-(Win32|Win64)-(.+)\.dll$') {
        $id = $Matches[2]
        if (Test-DeprecatedPlugin -Id $id) { $toDelete += [pscustomobject]@{ Path = $f.FullName; Why = '已废弃' }; continue }
        if ($activeIds -notcontains $id.ToUpperInvariant()) { $toDelete += [pscustomobject]@{ Path = $f.FullName; Why = '不在当前构建' }; continue }
    }
    if ($f.Name -match '^IOUI-ANDROID-(.+)\.so$') {
        $id = $Matches[1]
        if (Test-DeprecatedPlugin -Id $id) { $toDelete += [pscustomobject]@{ Path = $f.FullName; Why = '已废弃' } }
    }
    if (Test-PluginLinkArtifact -File $f) {
        $toDelete += [pscustomobject]@{ Path = $f.FullName; Why = '构建产物' }
    }
}

Write-Plan "新增 $($toAdd.Count) / 变更 $($toUpdate.Count) / 未变 $same / 待删 $($toDelete.Count)"
Write-Plan "活跃插件 $($activeIds.Count) 个"
if ($toAdd.Count -gt 0) {
    Write-Plan '  新增示例:'
    foreach ($x in @($toAdd | Select-Object -First 6)) { Write-Plan "    + $($x.Rel)" }
    if ($toAdd.Count -gt 6) { Write-Plan "    … 共 $($toAdd.Count) 个" }
}
if ($toUpdate.Count -gt 0) {
    Write-Plan '  变更示例:'
    foreach ($x in @($toUpdate | Select-Object -First 10)) { Write-Plan "    ~ $($x.Rel)" }
    if ($toUpdate.Count -gt 10) { Write-Plan "    … 共 $($toUpdate.Count) 个" }
}
if ($toDelete.Count -gt 0) {
    Write-Plan '  待删:'
    foreach ($x in $toDelete) { Write-Plan "    - $(Split-Path -Leaf $x.Path)  （$($x.Why)）" }
}

# ── Config 计划 ─────────────────────────────────────────
# **文件级合并**，绝不递归删除：这些目录里有 Unity 的 `.meta`（资产标识），正源里没有。
# 整目录替换会把 .meta 一起删掉 —— 实测 Config\MP-NJLJ 与 MP-NJQK 还是那仓库里
# **未提交的新增目录**，删了就是把别人的活毁了。
# 所以：正源有的覆盖过去、缺的补上（并合成 .meta）、目标里多出来的只报告不删。
$cfgPlan = @(); $cfgExtra = @(); $cfgAdd = 0; $cfgUpd = 0; $cfgSame = 0
$staleWebChunks = @()   # Config 下可安全删除的前端死 chunk
if (-not $SkipConfig) {
    Write-Step 'Config 差异'
    $cfgSrcRoot = Join-Path $IouiRootDir 'Config'
    foreach ($id in $activeIds) {
        $srcDir = Get-ChildItem -Path $cfgSrcRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name.ToUpperInvariant() -eq $id } | Select-Object -First 1
        if (-not $srcDir) { continue }
        $dstDir = Join-Path $configDir $srcDir.Name
        foreach ($sf in Get-ChildItem -LiteralPath $srcDir.FullName -Recurse -File) {
            $rel = $sf.FullName.Substring($srcDir.FullName.Length).TrimStart('\', '/')
            $df = Join-Path $dstDir $rel
            if (-not (Test-Path -LiteralPath $df)) { $cfgAdd++; $cfgPlan += [pscustomobject]@{ Src = $sf.FullName; Dst = $df; IsNew = $true } }
            elseif ((Get-Sha256 -Path $sf.FullName) -ne (Get-Sha256 -Path $df)) { $cfgUpd++; $cfgPlan += [pscustomobject]@{ Src = $sf.FullName; Dst = $df; IsNew = $false } }
            else { $cfgSame++ }
        }
        if (Test-Path $dstDir) {
            foreach ($df in Get-ChildItem -LiteralPath $dstDir -Recurse -File -ErrorAction SilentlyContinue) {
                $rel = $df.FullName.Substring($dstDir.Length).TrimStart('\', '/')
                if ($rel.EndsWith('.meta')) { continue }
                if (Test-Path -LiteralPath (Join-Path $srcDir.FullName $rel)) { continue }
                $cfgExtra += "$($srcDir.Name)/$rel"

                # 前端产物里的**hash 命名 chunk**：正源没有、且不属于 .meta，
                # 那就是上一版构建留下的死文件（vite 每次构建换 hash，旧 chunk 不会被引用）。
                # 这类可以安全删掉 —— 判据收得很窄：
                #   · 路径必须在 <插件>\web\assets\ 下；
                #   · 文件名必须带 hash 后缀（`*-<8 位以上>.js|.css`）；
                # 实测 Simulator web 就积着 index-D85_ywTf.js / index-DKIi9H1j.css 两个旧索引 chunk。
                # 不按"是否被 index.html 引用"来判 —— 动态 import 的 chunk（blueprint/litegraph/
                # platformScene/three）本来就不出现在 index.html 里，那样判会误删。
                if ($rel -match '(?i)(^|[\\/])web[\\/]assets[\\/].*-[A-Za-z0-9_-]{8,}\.(js|css)$') {
                    $staleWebChunks += $df.FullName
                }
            }
        }
    }
    Write-Plan "Config: 新增 $cfgAdd / 更新 $cfgUpd / 未变 $cfgSame"
    if ($cfgExtra.Count -gt 0) {
        Write-Host "   目标里多出 $($cfgExtra.Count) 个正源没有的文件（**不删**，请你确认）:" -ForegroundColor Yellow
        foreach ($x in @($cfgExtra | Select-Object -First 10)) { Write-Plan "    ? $x" }
        if ($cfgExtra.Count -gt 10) { Write-Plan "    … 共 $($cfgExtra.Count) 个" }
    }
    if ($staleWebChunks.Count -gt 0) {
        Write-Host "   其中 $($staleWebChunks.Count) 个是前端 hash chunk 死文件，**将删除**:" -ForegroundColor Yellow
        foreach ($x in $staleWebChunks) { Write-Plan "    x $(Split-Path -Leaf $x)" }
    }
}

if ($DryRun) {
    Write-Host ''
    Write-Host '  DRY-RUN：未写入任何文件。去掉 -DryRun 才会实际应用。' -ForegroundColor Yellow
    return
}

# ── 应用 ────────────────────────────────────────────────
Write-Step '应用'
$applied = 0
foreach ($zipPath in $viewZips) {
    $za = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $want = @($toAdd + $toUpdate | Where-Object { $_.Zip -eq $zipPath })
        foreach ($x in $want) {
            $d = Split-Path -Parent $x.Dst
            if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
            $e = $za.GetEntry($x.Entry)
            if (-not $e) { continue }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $x.Dst, $true)
            $applied++
        }
    }
    finally { $za.Dispose() }
}
Write-Ok "拷贝 $applied 个文件"

foreach ($x in $toDelete) { Remove-Item -LiteralPath $x.Path -Force }
if ($toDelete.Count -gt 0) { Write-Ok "删除 $($toDelete.Count) 个（废弃/不在当前构建/构建产物）" }

if ($studioZip) {
    $x64 = Join-Path $pluginsDir 'x86_64'
    if (Test-Path $x64) {
        $dstExe = Join-Path $x64 'IOStudio.exe'
        $old = if (Test-Path $dstExe) { (Get-Item $dstExe).Length } else { 0 }
        $za = [System.IO.Compression.ZipFile]::OpenRead($studioZip)
        try {
            $e = $za.Entries | Where-Object { $_.FullName -eq 'IOStudio.exe' } | Select-Object -First 1
            if ($e) { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dstExe, $true) }
            else { Write-Warning 'IOStudio zip 里没有 IOStudio.exe' }
        }
        finally { $za.Dispose() }
        if (Test-Path $dstExe) { Write-Ok ("IOStudio.exe  {0:N0} → {1:N0} 字节" -f $old, (Get-Item $dstExe).Length) }
    }
}

if (-not $SkipConfig) {
    foreach ($x in $cfgPlan) {
        $d = Split-Path -Parent $x.Dst
        if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        Copy-Item -LiteralPath $x.Src -Destination $x.Dst -Force
    }
    $chunkN = 0
    foreach ($c in $staleWebChunks) {
        Remove-Item -LiteralPath $c -Force
        if (Test-Path "$c.meta") { Remove-Item -LiteralPath "$c.meta" -Force }
        $chunkN++
    }
    Write-Ok "Config: 写入 $($cfgPlan.Count) 个文件；删除前端死 chunk $chunkN 个（.meta 与其余多余项未动）"
}

# ── .meta 合成（只为新增项）─────────────────────────────
function New-MetaFor {
    param([string]$Target)
    $meta = "$Target.meta"
    if (Test-Path $meta) { return $false }
    $isDir = Test-Path -LiteralPath $Target -PathType Container
    $template = $null
    $parent = Split-Path -Parent $Target
    foreach ($sib in @(Get-ChildItem -LiteralPath $parent -ErrorAction SilentlyContinue)) {
        $sMeta = "$($sib.FullName).meta"
        if (-not (Test-Path $sMeta)) { continue }
        if ((Test-Path -LiteralPath $sib.FullName -PathType Container) -ne $isDir) { continue }
        $template = $sMeta; break
    }
    if (-not $template) { return $false }
    $body = Get-Content -LiteralPath $template -Raw -Encoding UTF8
    $rel = $Target.Substring($pluginRoot.Length).TrimStart('\', '/').Replace('\', '/')
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("IOToolkit/$rel"))
        $guid = -join ($hash | ForEach-Object { $_.ToString('x2') })
    }
    finally { $md5.Dispose() }
    $body = [regex]::Replace($body, '(?m)^guid: .*$', "guid: $guid")
    Set-Content -LiteralPath $meta -Value $body -Encoding UTF8 -NoNewline
    return $true
}

$metaCount = 0
$metaNoTemplate = @()
foreach ($x in $toAdd) {
    if (New-MetaFor -Target (Split-Path -Parent $x.Dst)) { $metaCount++ }
    if (New-MetaFor -Target $x.Dst) { $metaCount++ }
}
# 新增的 Config 文件同样要 .meta —— 只给 Plugins 合成会让 Unity 自己生成，
# 而 Unity 生成的导入设置与既有同类资产未必一致。这里沿用同目录模板。
foreach ($x in $cfgPlan) {
    if (-not $x.IsNew) { continue }
    if (New-MetaFor -Target (Split-Path -Parent $x.Dst)) { $metaCount++ }
    if (New-MetaFor -Target $x.Dst) { $metaCount++ }
}
if ($studioZip) {
    $exe = Join-Path $pluginsDir 'x86_64\IOStudio.exe'
    if ((Test-Path $exe) -and (New-MetaFor -Target $exe)) { $metaCount++ }
}
Write-Ok ".meta 合成 $metaCount 个（已存在的未改动）"

# ── 核对 ────────────────────────────────────────────────
Write-Host ''
Write-Step '核对'
$fail = 0
$dep = @(Get-ChildItem -LiteralPath $pluginsDir -Recurse -File -Filter 'IOUI-*.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.BaseName -match '^IOUI-(Win32|Win64)-(.+)$' -and (Test-DeprecatedPlugin -Id $Matches[2]) })
if ($dep.Count -gt 0) { Write-Host "   x 仍有废弃插件 $($dep.Count) 个" -ForegroundColor Red; $fail++ } else { Write-Plan '已废弃插件：0' }

$junk = @(Get-ChildItem -LiteralPath $pluginsDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { Test-PluginLinkArtifact -File $_ })
if ($junk.Count -gt 0) { Write-Host "   x 仍有构建产物 $($junk.Count) 个" -ForegroundColor Red; $fail++ } else { Write-Plan '构建产物：0' }

foreach ($rel in @('x86_64\IODevice.dll', 'x86_64\IODevice_C_Wrapper.dll', 'IODevice_CSharp_Wrapper.dll',
        'x86_64\ExternalLibraries\IOUI-Win64-IOUI.dll', 'Android\arm64-v8a\libiodevice.so',
        'Android\ExternalLibraries\IOUI-ANDROID-IOUI.so')) {
    $p = Join-Path $pluginsDir $rel
    if (Test-Path $p) { Write-Plan "OK  $rel" } else { Write-Host "   x 缺 $rel" -ForegroundColor Red; $fail++ }
}

$plug = @(Get-ChildItem (Join-Path $pluginsDir 'x86_64\ExternalLibraries') -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue).Count
$plug32 = @(Get-ChildItem (Join-Path $pluginsDir 'x86\ExternalLibraries') -File -Filter 'IOUI-Win32-*.dll' -ErrorAction SilentlyContinue).Count
$so = @(Get-ChildItem (Join-Path $pluginsDir 'Android') -Recurse -File -Filter 'IOUI-ANDROID-*.so' -ErrorAction SilentlyContinue).Count
Write-Plan "Win64 插件 $plug / Win32 插件 $plug32 / Android 插件 .so $so"

Write-Host ''
if ($fail -eq 0) { Write-Host ' 对齐完成，核对通过。' -ForegroundColor Green }
else { Write-Host " 对齐完成，但有 $fail 项未通过（见上）。" -ForegroundColor Yellow }
Write-Host ' 改动只落在 Plugins\ 与 Config\<插件>\；Editor\ / Runtime\ 未动。' -ForegroundColor DarkGray
