<#
.SYNOPSIS
    构建 Android(arm64-v8a) 原生载荷：IODevice 内核 .so + 插件 .so，并落到指定目录。

.DESCRIPTION
    这是 Android 侧**此前缺失的那一步**。实测家底：6 个插件工程已经写好
    `IOUI-ANDROID-<ID>` 的 CMakeLists、IODevice 也有 `IODEVICE_ANDROID_LITE` 目标，
    但**没有任何脚本驱动它们** —— 只有人手敲过几次 cmake，产物散落在
    `IOUI/IOUI/build_android_ioui/` 这类 in-source 目录里，其中 2 个还是 5~6 月的旧物，
    `iodevice.so` 更是只剩一份备份副本，且**无任何东西比对它们与源码**。
    本脚本把那一步变成可重复的：同样的输入 → 同样的产物集。

    产物命名受**加载器契约**约束（`IODevice/Source/Private/RawIOFactory.cpp`）：

        IOUI-ANDROID-<DllName 转大写>.so        插件
        iodevice.so                              内核

    所以本脚本按 id **推导**应得文件名再核对实际产物 —— 而不是把名字抄一遍。
    抄一遍的话，CMakeLists 里的 OUTPUT_NAME 被改错时这里不会报，而运行时会在
    `ResolveExternalPluginPath` 那里静默找不到文件。

    ABI / API / Release 与既有构建树一致：arm64-v8a、android-24、Release。

.PARAMETER OutputDir
    落点目录。布局与 Windows 侧同构（Windows 是 binaries\Win64\）：

        <OutputDir>\iodevice.so
        <OutputDir>\ExternalLibraries\IOUI-ANDROID-*.so
        <OutputDir>\ExternalLibraries\Config\<插件>\      （种子配置，见 NOTES）

.PARAMETER BuildRoot
    out-of-source 构建根，默认 <仓库根>\build\android。
    刻意**不用**各工程下那个 in-source `build_android_*` —— 那些会污染子模块工作树，
    且不在 .gitignore 覆盖范围内。

.PARAMETER NdkRoot
    NDK 根。不传则依次尝试 $env:ANDROID_NDK_ROOT，再到 Android SDK 的 ndk\ 下取最新一个。

.NOTES
    Config 是**种子**不是真相源：插件通过宿主下发的 SetPluginRuntimeRoot 拿到配置根，
    所以真正生效的配置来自宿主工程（如 Unity 的 StreamingAssets）。包里放一份是为了
    开箱可跑与文档可查，不要把"改了包里的 Config"当成改了现场行为。
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$IouiRootDir,
    [string]$NdkRoot,
    [string]$Abi = 'arm64-v8a',
    [string]$ApiLevel = '24',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$BuildRoot,
    [string]$OutputDir,

    [string]$CmakeExe,
    [string]$NinjaExe,

    [switch]$SkipBuild,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host ">> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   $Text" -ForegroundColor Green }

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $ScriptDir }
if (-not $IouiRootDir) { $IouiRootDir = Join-Path (Split-Path -Parent $RepoRoot) 'IOUI' }
if (-not $BuildRoot) { $BuildRoot = Join-Path $RepoRoot 'build\android' }

. (Join-Path $ScriptDir 'OutputLayout.ps1')

# ── 目标表 ──────────────────────────────────────────────
# 注意 $RepoRoot 是 **IODevice 子模块根**（脚本在 <子模块>/scripts 下），
# 所以 C++ 内核工程是 <子模块>\IODevice\（与 release_iodevice.bat 的口径一致）。
# Expect 由 Id 推导（见文件头）：插件走 IOUI-ANDROID-<大写>，内核固定 iodevice.so。
$targets = @(
    [pscustomobject]@{ Id = 'iodevice'; Source = (Join-Path $RepoRoot 'IODevice'); Expect = 'iodevice.so' }
    [pscustomobject]@{ Id = 'IOUI'; Source = (Join-Path $IouiRootDir 'IOUI'); Expect = 'IOUI-ANDROID-IOUI.so' }
    [pscustomobject]@{ Id = 'Simulator'; Source = (Join-Path $IouiRootDir 'Simulator'); Expect = 'IOUI-ANDROID-SIMULATOR.so' }
    [pscustomobject]@{ Id = 'MP-YDL'; Source = (Join-Path $IouiRootDir 'MP-YDL'); Expect = 'IOUI-ANDROID-MP-YDL.so' }
    [pscustomobject]@{ Id = 'MP-NJLJ'; Source = (Join-Path $IouiRootDir 'MP-NJLJ'); Expect = 'IOUI-ANDROID-MP-NJLJ.so' }
    [pscustomobject]@{ Id = 'MP-NJQK'; Source = (Join-Path $IouiRootDir 'MP-NJQK'); Expect = 'IOUI-ANDROID-MP-NJQK.so' }
    [pscustomobject]@{ Id = 'NETIO'; Source = (Join-Path $IouiRootDir 'NETIO'); Expect = 'IOUI-ANDROID-NETIO.so' }
)

# ── NDK 解析 ────────────────────────────────────────────
if (-not $NdkRoot) { $NdkRoot = $env:ANDROID_NDK_ROOT }
if (-not $NdkRoot) {
    $sdk = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME }
    if ($sdk -and (Test-Path (Join-Path $sdk 'ndk'))) {
        # 取版本号最大的（目录名形如 25.1.8937393）。
        # 先按正则过滤掉非版本目录（如 sources/），否则 [version] 转换会抛。
        $ndkDirs = @(Get-ChildItem (Join-Path $sdk 'ndk') -Directory |
                Where-Object { $_.Name -match '^\d+(\.\d+)+' })
        if ($ndkDirs.Count -gt 0) {
            $NdkRoot = ($ndkDirs |
                Sort-Object { [version](($_.Name -split '-')[0]) } |
                Select-Object -Last 1).FullName
        }
    }
}
$toolchain = if ($NdkRoot) { Join-Path $NdkRoot "build\cmake\android.toolchain.cmake" } else { $null }
if (-not $toolchain -or -not (Test-Path $toolchain)) {
    throw "NDK toolchain 未找到。请用 -NdkRoot 指定，或设 `$env:ANDROID_NDK_ROOT，或让 Android SDK 的 ndk\ 下存在一个版本。(试用路径: $toolchain)"
}
Write-Step "NDK toolchain: $toolchain"
Write-Step "ABI $Abi / API $ApiLevel / $Configuration"

# ── cmake / ninja 解析 ─────────────────────────────────
# 优先用 Android SDK 自带的 cmake（它**同捆 ninja**，版本与既有构建树一致），
# 这样不依赖 PATH 上装了什么。
if (-not $CmakeExe -or -not $NinjaExe) {
    $sdkRoot = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME }
    if (-not $sdkRoot) { $sdkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
    $sdkCmakeDir = Join-Path $sdkRoot 'cmake'
    if (Test-Path $sdkCmakeDir) {
        $sdkCmake = (Get-ChildItem $sdkCmakeDir -Directory |
            Sort-Object { [version]$_.Name } | Select-Object -Last 1).FullName
        if (-not $CmakeExe) {
            $candidate = Join-Path $sdkCmake 'bin\cmake.exe'
            if (Test-Path $candidate) { $CmakeExe = $candidate }
        }
        if (-not $NinjaExe) {
            $candidate = Join-Path $sdkCmake 'bin\ninja.exe'
            if (Test-Path $candidate) { $NinjaExe = $candidate }
        }
    }
}
if (-not $CmakeExe) {
    $found = Get-Command cmake -ErrorAction SilentlyContinue
    if ($found) { $CmakeExe = $found.Source }
}
if (-not $NinjaExe) {
    $found = Get-Command ninja -ErrorAction SilentlyContinue
    if ($found) { $NinjaExe = $found.Source }
}
if (-not $CmakeExe) { throw 'cmake 未找到。装 Android SDK 的 cmake 包，或用 -CmakeExe 指定。' }
if (-not $NinjaExe) { throw 'ninja 未找到。装 Android SDK 的 cmake 包（自带 ninja），或用 -NinjaExe 指定。' }
Write-Step "cmake: $CmakeExe"
Write-Step "ninja: $NinjaExe"

# Boost 是**头文件**用法（CMakeLists 里开了 BOOST_ERROR_CODE_HEADER_ONLY /
# BOOST_SYSTEM_NO_LIB），所以 x64-windows 那份 include 对 Android 同样够用。
$boostInclude = $null
if ($env:VCPKG_ROOT) { $boostInclude = Join-Path $env:VCPKG_ROOT 'installed\x64-windows\include' }
if (-not $boostInclude -or -not (Test-Path (Join-Path $boostInclude 'boost\asio.hpp'))) {
    throw "找不到 boost/asio.hpp。设 `$env:VCPKG_ROOT 指向 vcpkg 根（需要有 installed\x64-windows\include\boost\asio.hpp）。"
}

# ── 配置 + 构建 ─────────────────────────────────────────
$results = New-Object System.Collections.Generic.List[object]
foreach ($t in $targets) {
    if (-not (Test-Path (Join-Path $t.Source 'CMakeLists.txt'))) {
        throw "目标 $($t.Id) 没有 CMakeLists.txt: $($t.Source)"
    }
    $buildDir = Join-Path $BuildRoot $t.Id
    if ($Clean -and (Test-Path $buildDir)) { Remove-Item $buildDir -Recurse -Force }
    if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir -Force | Out-Null }

    # 把 ninja 所在目录放进 PATH —— cmake 的 Ninja 生成器会去找它。
    $env:PATH = "$(Split-Path -Parent $NinjaExe);$env:PATH"

    Write-Host ''
    Write-Step "配置 $($t.Id)"
    $cfgArgs = @(
        '-G', 'Ninja',
        '-S', $t.Source,
        '-B', $buildDir,
        "-DCMAKE_TOOLCHAIN_FILE=$toolchain",
        "-DANDROID_ABI=$Abi",
        "-DANDROID_PLATFORM=android-$ApiLevel",
        "-DCMAKE_BUILD_TYPE=$Configuration",
        "-DCMAKE_MAKE_PROGRAM=$NinjaExe",
        "-DIOUI_BOOST_INCLUDE_DIR=$boostInclude"
    )
    & $CmakeExe @cfgArgs
    if ($LASTEXITCODE -ne 0) { throw "配置失败: $($t.Id)（退出码 $LASTEXITCODE）" }

    if (-not $SkipBuild) {
        Write-Step "构建 $($t.Id)"
        & $CmakeExe --build $buildDir
        if ($LASTEXITCODE -ne 0) { throw "构建失败: $($t.Id)（退出码 $LASTEXITCODE）" }
    }

    # 产物就在 build 目录根（所有 CMakeLists 都是 PREFIX "" + OUTPUT_NAME）。
    $produced = Join-Path $buildDir $t.Expect
    if (-not (Test-Path $produced)) {
        # 退一步：目录里任何 .so 都能告诉我们实际叫什么 —— 报出来才有诊断价值。
        $found = @(Get-ChildItem $buildDir -File -Filter '*.so' -ErrorAction SilentlyContinue |
                ForEach-Object { $_.Name })
        $detail = if ($found.Count -gt 0) { "实际产出: $($found -join ', ')" } else { '目录里没有任何 .so' }
        throw "目标 $($t.Id) 未产出预期文件 $($t.Expect)。$detail"
    }
    if (-not (Test-ElfAarch64 -Path $produced)) {
        throw "目标 $($t.Id) 的 $($t.Expect) 不是 ELF64/AArch64 —— ABI 参数没生效？"
    }
    $sizeMb = [math]::Round((Get-Item $produced).Length / 1MB, 1)
    Write-Ok "$($t.Expect)  ($sizeMb MB, ELF64/AArch64)"
    $results.Add([pscustomobject]@{ Id = $t.Id; Name = $t.Expect; Path = $produced; Source = $t.Source })
}

# ── 落地 ────────────────────────────────────────────────
if ($OutputDir) {
    Write-Host ''
    Write-Step "落点到 $OutputDir"
    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
    $extDir = Join-Path $OutputDir 'ExternalLibraries'
    if (-not (Test-Path $extDir)) { New-Item -ItemType Directory -Path $extDir -Force | Out-Null }

    # 落地并**剥符号**。
    #
    # 为什么必须 strip：NDK 的默认编译参数带 `-g`，Release 也不例外，于是 .so 会带上
    # 全部调试信息。实测（2026-10-15）：
    #     IOUI-ANDROID-NETIO.so   31,094,064 字节 → strip 后 2,421,304（调试信息占 27 MB）
    #     IOUI-ANDROID-SIMULATOR.so 7,540,472 → 984,416
    # 直接进 APK 就是十倍级的体积浪费。项目既有产物也是**剥过的**
    # （对照 UNIPlayer 里的副本：NETIO 2,415,776 / SIMULATOR 988,504，与 strip 后几乎逐字节相同，
    #  说明同一份代码、只差调试信息）。
    #
    # 只剥**落点的副本**，构建目录里那份保持未剥 —— 需要符号化崩溃栈时从那里取。
    $stripExe = Join-Path (Split-Path -Parent (Split-Path -Parent $toolchain)) 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-strip.exe'
    if (-not (Test-Path $stripExe)) {
        # NDK 布局在不同版本间变过，退一步按 NDK 根找。
        $stripExe = Get-ChildItem -Path $NdkRoot -Recurse -Filter 'llvm-strip.exe' -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ($stripExe) { Write-Step "剥符号: $stripExe" }
    else { Write-Warning 'NDK 里找不到 llvm-strip.exe —— 落点的 .so 会带着调试信息（体积约为剥过的 10 倍）。' }

    foreach ($r in $results) {
        $dst = if ($r.Id -eq 'iodevice') { Join-Path $OutputDir $r.Name } else { Join-Path $extDir $r.Name }
        Copy-Item -LiteralPath $r.Path -Destination $dst -Force
        $before = (Get-Item -LiteralPath $dst).Length
        if ($stripExe) {
            # -s（--strip-all）：.so 的 .dynsym 会保留（动态链接需要），只去掉 .symtab 与 .debug_*。
            & $stripExe -s $dst 2>$null | Out-Null
        }
        $after = (Get-Item -LiteralPath $dst).Length
        $savedPct = if ($before -gt 0) { [int](100 - ($after * 100 / $before)) } else { 0 }
        Write-Ok ("-> {0}  {1:N0} -> {2:N0} 字节（省 {3}%）" -f $r.Name, $before, $after, $savedPct)
        if (-not (Test-ElfAarch64 -Path $dst)) { throw "strip 之后不是 ELF64/AArch64 了: $dst" }
    }

    # 种子配置：只放**确实构建了 .so 的**那些插件。没有 .so 的插件带配置进包毫无意义。
    $configSrcRoot = Join-Path $IouiRootDir 'Config'
    $extConfigDir = Join-Path $extDir 'Config'
    foreach ($r in $results) {
        if ($r.Id -eq 'iodevice') { continue }
        $src = Join-Path $configSrcRoot $r.Id
        if (-not (Test-Path $src)) { continue }
        $dst = Join-Path $extConfigDir $r.Id
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }   # 镜像式：先清后拷
        Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
    }
    $seeded = @(Get-ChildItem $extConfigDir -Directory -ErrorAction SilentlyContinue)
    Write-Ok "种子配置 $($seeded.Count) 个插件"
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host " Android 载荷就绪（$($results.Count) 个目标）" -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
foreach ($r in $results) { Write-Host "  $($r.Name)" }
if (-not $OutputDir) { Write-Host "  （未指定 -OutputDir，仅构建未落地）" -ForegroundColor Yellow }

# 供调用方（打包脚本）按对象消费，而不是去解析控制台文本。
[pscustomobject]@{
    Targets = $results
    Abi     = $Abi
    ApiLevel = $ApiLevel
}
