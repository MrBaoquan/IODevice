<#
.SYNOPSIS
    从**规范载荷**派生出各消费方视图（C++ / Unity / UE），各自成包。

.DESCRIPTION
    IOToolkit 的规范载荷只有一种形状：

        IOToolkit/include/**            （头文件）
        IOToolkit/binaries/<平台>/**     （库 + 插件 + Core + Config）

    C++ 与 UE 直接消费这个形状（UE 只是把它放在 `ThirdParty/IOToolkit/` 下）。
    但 **Unity 形状完全不同**，它要的是 Unity 包里的插件布局：

        Plugins/IODevice_CSharp_Wrapper.dll          托管包装（DllImport 入口）
        Plugins/x86_64/{IODevice.dll, IODevice.lib, IODevice_C_Wrapper.dll, ExternalLibraries/**}
        Plugins/Android/arm64-v8a/{iodevice.so, libiodevice.so}
        Plugins/Android/ExternalLibraries/IOUI-ANDROID-*.so

    本脚本把"规范载荷 → 各消费方形状"这一步变成可重复的，而不是让每个消费方仓库
    手工镜像一份。实测手工镜像的代价：Unity 包里
      · `Plugins/x86_64/` **缺 IODevice_C_Wrapper.dll**（而 x86 那份有）——
        而 Windows 的 DllImport 名字正是 `IODevice_C_Wrapper`（见 IONativeWrapper.cs），
        所以 64 位 Windows 上根本挂不上；
      · `IODevice.dll` 停在 683520 字节（当前构建是 691200）；
      · `ExternalLibraries` 只有 39 个插件（正源 50 个）；
      · 托管包装 132608 字节（当前 58368）。
    四项都是静默的。

    **本脚本不产 .meta**：Unity 的 .meta 是 Unity 的资产标识（GUID），由 Unity 包仓库
    自己拥有与维护。这里只给"文件名与落点都对"的原生载荷，Unity 仓库在其上叠加 .meta。

.PARAMETER StagingDir
    规范载荷所在的 staging 根（其下应有 `IOToolkit\`）。即 Build-IOToolkitPackage 的 staging。

.PARAMETER OutputRoot
    视图落点根。每个视图写到 `<OutputRoot>\<视图名>\`。

.PARAMETER View
    要产出哪些视图。默认全部（按 staging 里实际存在的平台自动裁剪）。

.OUTPUTS
    每个视图一个对象：Name / Dir / Platform / FileCount。

.NOTES
    Android 的 `libiodevice.so` 与 `iodevice.so` 是**两个名字都放**：
    `DllImport("iodevice")` 在 Android 上按 Linux 惯例去找 `libiodevice.so`，
    而加载器/模块目录语义用的是裸名 `iodevice.so`。证据是 Unity 现有包里两个名字都在
    （`Plugins/Android/arm64-v8a/`）。如果后续确认只需要其中一个，删掉另一个即可。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StagingDir,
    [Parameter(Mandatory = $true)][string]$OutputRoot,

    [ValidateSet('unity-win', 'unity-android', 'ue')]
    [string[]]$View = @('unity-win', 'unity-android', 'ue'),

    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host ">> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   $Text" -ForegroundColor Green }

$canonical = Join-Path $StagingDir 'IOToolkit'
if (-not (Test-Path $canonical)) { throw "规范载荷不存在: $canonical（先跑 Windows/Android 载荷构建）" }

# Unity 的平台目录名（与 Unity 包里的既成约定一致，不是 Windows 那套 Win64/Win32）
$unityArchDir = @{ 'Win64' = 'x86_64'; 'Win32' = 'x86'; 'Android' = 'arm64-v8a' }

function Copy-Tree {
    param([string]$From, [string]$To)
    if (-not (Test-Path $From)) { return 0 }
    if (-not (Test-Path $To)) { New-Item -ItemType Directory -Path $To -Force | Out-Null }
    $n = 0
    foreach ($f in Get-ChildItem -LiteralPath $From -Recurse -File) {
        $rel = $f.FullName.Substring((Resolve-Path -LiteralPath $From).Path.Length).TrimStart('\', '/')
        $dst = Join-Path $To $rel
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
        $n++
    }
    return $n
}

function Copy-File {
    param([string]$From, [string]$To)
    if (-not (Test-Path $From)) { return $false }
    $dir = Split-Path -Parent $To
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item -LiteralPath $From -Destination $To -Force
    return $true
}

$results = New-Object System.Collections.Generic.List[object]
$binRoot = Join-Path $canonical 'binaries'
$includeDir = Join-Path $canonical 'include'

# ── UE 视图 ──────────────────────────────────────────────
# UE 消费的形状与规范载荷**同构**，只是它要求放在 ThirdParty/IOToolkit/ 下。
# 所以这里只是换个根名，外加把 Android 也带上（UE 侧此前只有 Win32/Win64）。
if ($View -contains 'ue') {
    $dir = Join-Path $OutputRoot 'ue'
    if ($Clean -and (Test-Path $dir)) { Remove-Item $dir -Recurse -Force }
    $tp = Join-Path $dir 'ThirdParty\IOToolkit'
    $n = 0
    $n += Copy-Tree -From $includeDir -To (Join-Path $tp 'include')
    foreach ($plat in @(Get-ChildItem $binRoot -Directory -ErrorAction SilentlyContinue)) {
        $n += Copy-Tree -From $plat.FullName -To (Join-Path $tp "binaries\$($plat.Name)")
    }
    if ($n -eq 0) { throw "UE 视图为空 —— 规范载荷里既没有 include 也没有 binaries/*" }
    Write-Step "UE 视图：ThirdParty\IOToolkit\{include, binaries\$(($(Get-ChildItem $binRoot -Directory).Name) -join '|')}"
    Write-Ok "$n 个文件"
    $results.Add([pscustomobject]@{ Name = 'ue'; Dir = $dir; FileCount = $n })
}

# ── Unity 视图（Windows）──────────────────────────────────
if ($View -contains 'unity-win') {
    $win64 = Join-Path $binRoot 'Win64'
    if (-not (Test-Path $win64)) {
        Write-Host '   (skip) unity-win：规范载荷里没有 binaries\Win64' -ForegroundColor Yellow
    }
    else {
        $dir = Join-Path $OutputRoot 'unity-win'
        if ($Clean -and (Test-Path $dir)) { Remove-Item $dir -Recurse -Force }
        $plugins = Join-Path $dir 'Plugins'

        # 托管包装落在 Plugins\ 根（AnyCPU，与架构无关）—— Unity 侧就在这个位置。
        if (-not (Copy-File -From (Join-Path $win64 'IODevice_CSharp_Wrapper.dll') -To (Join-Path $plugins 'IODevice_CSharp_Wrapper.dll'))) {
            throw "unity-win 视图缺 IODevice_CSharp_Wrapper.dll（源: $win64）"
        }

        $n = 1
        foreach ($plat in @('Win64', 'Win32')) {
            $src = Join-Path $binRoot $plat
            if (-not (Test-Path $src)) { continue }
            $archDir = Join-Path $plugins $unityArchDir[$plat]
            # 逐个**显式**拷，而不是整目录铺过去 —— 规范载荷里还有 CSharp 包装等
            # 不该出现在架构子目录里的东西。
            foreach ($f in @('IODevice.dll', 'IODevice.lib', 'IODevice_C_Wrapper.dll')) {
                # IODevice_C_Wrapper.dll 是 **Windows 的 DllImport 目标**，必须有：
                # 实测 Unity 现有包的 x86_64 里恰恰缺它（x86 有），64 位 Windows 上挂不上。
                if (Copy-File -From (Join-Path $src $f) -To (Join-Path $archDir $f)) { $n++ }
                elseif ($f -eq 'IODevice_C_Wrapper.dll') { throw "unity-win 视图缺 $plat\IODevice_C_Wrapper.dll（Windows 的 P/Invoke 目标，缺了挂不上）" }
            }
            $n += Copy-Tree -From (Join-Path $src 'ExternalLibraries') -To (Join-Path $archDir 'ExternalLibraries')
        }
        Write-Step 'Unity 视图 (windows)：Plugins\{x86_64, x86} + Plugins\IODevice_CSharp_Wrapper.dll'
        Write-Ok "$n 个文件"
        $results.Add([pscustomobject]@{ Name = 'unity-win'; Dir = $dir; FileCount = $n })
    }
}

# ── Unity 视图（Android）──────────────────────────────────
if ($View -contains 'unity-android') {
    $and = Join-Path $binRoot 'Android'
    if (-not (Test-Path $and)) {
        Write-Host '   (skip) unity-android：规范载荷里没有 binaries\Android' -ForegroundColor Yellow
    }
    else {
        $dir = Join-Path $OutputRoot 'unity-android'
        if ($Clean -and (Test-Path $dir)) { Remove-Item $dir -Recurse -Force }
        $plugins = Join-Path $dir 'Plugins'
        $andDir = Join-Path $plugins 'Android'
        $archDir = Join-Path $andDir 'arm64-v8a'

        # Android 的托管包装：Unity 侧放 Plugins\Android\ 下。
        # 规范载荷里的 CSharp 包装是 AnyCPU 的，同一份文件即可。
        $managed = Join-Path (Join-Path $binRoot 'Win64') 'IODevice_CSharp_Wrapper.dll'
        if (-not (Copy-File -From $managed -To (Join-Path $andDir 'IODevice_CSharp_Wrapper.dll'))) {
            throw "unity-android 视图缺托管包装（源: $managed）—— 托管包装是 AnyCPU，需要 Win64 载荷提供它"
        }
        $n = 1

        # 内核：两个名字都放（理由见 .NOTES）
        $core = Join-Path $and 'iodevice.so'
        if (-not (Test-Path $core)) { throw "unity-android 视图缺内核 $core" }
        Copy-File -From $core -To (Join-Path $archDir 'iodevice.so') | Out-Null
        Copy-File -From $core -To (Join-Path $archDir 'libiodevice.so') | Out-Null   # DllImport("iodevice") 在 Android 上找的名字
        $n += 2

        # 插件 .so + 种子配置
        $n += Copy-Tree -From (Join-Path $and 'ExternalLibraries') -To (Join-Path $andDir 'ExternalLibraries')
        Write-Step 'Unity 视图 (android)：Plugins\Android\{arm64-v8a, ExternalLibraries}'
        Write-Ok "$n 个文件"
        $results.Add([pscustomobject]@{ Name = 'unity-android'; Dir = $dir; FileCount = $n })
    }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host " 消费方视图就绪（$($results.Count) 个）" -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
foreach ($r in $results) { Write-Host "  $($r.Name)  ($($r.FileCount) 个文件)" }
Write-Host '  注意：不含 .meta —— Unity 的资产标识由 Unity 包仓库自己维护。' -ForegroundColor DarkGray

return $results
