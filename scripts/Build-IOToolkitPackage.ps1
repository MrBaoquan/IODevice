<#
.SYNOPSIS
    构建并打包 IOToolkit SDK 发行包（把两个子模块的最新产物聚合到一个版本目录）。

.DESCRIPTION
    取代原先的 `build_IOToolkit_v<版本>.bat`。原来那个脚本的**意图没问题** ——
    把子模块产物聚合到版本目录；问题在它聚合的**来源**与**方式**：

      · 来源是「历次构建的堆积目录」，不是正源的投影 → 缺的永远缺、多的永远多
        （实测：正源 30 个插件配置，包里只有 22 个；`SLDS-D10` 从 2022 年就在正源里，
         四级目录一个都没有）
      · xcopy /E /Y **从不删除** → 目录里混进手工放的东西（`IOToolkit_SNAP7.zip`、
        `信捷PLC-新版...`），且没有任何机制能发现
      · 全链路**不检查 errorlevel**，最后无条件打印 "Build Completed..."
      · 版本号硬编码在 9 处，且大小写不一致（`V2.4.1` vs `v2.4.1`，靠 Windows
        路径大小写不敏感才没出事）
      · 同一内容产出两份（`v<ver>\` 与 `IOToolkit-v<ver>\`，实测重复约 600 MB）

    本脚本的做法：

      1. 版本号从仓库根的 `version.txt` 读（单一来源）
      2. 构建（调 IODevice\build_IOToolkit.bat）——**失败即停**
      3. 装配到**干净的临时 staging**（每次重建，不累积）
      4. 装配插件载荷（共用层 `Assemble-Payload.ps1`）——配置镜像自正源
      5. 校验 staging 与正源一致（不通过就停，不产出）
      6. 镜像式投递到 `IOToolkit-v<ver>\`

.PARAMETER Version
    版本号（含前缀 v）。缺省读仓库根的 `version.txt`。

.PARAMETER SkipBuild
    跳过构建，直接用现有产物装配。用于「只重打包」与离线验证。

.PARAMETER SkipVerify
    跳过装配后的正源一致性校验。默认校验，**不通过即停**。

.PARAMETER KeepStaging
    保留 staging 目录（默认成功后删除），便于排查。

.EXAMPLE
    .\Build-IOToolkitPackage.ps1
    全量：构建 + 装配 + 校验 + 打包。

.EXAMPLE
    .\Build-IOToolkitPackage.ps1 -SkipBuild
    只重打包（用现有构建产物）。
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$SkipVerify,
    [switch]$KeepStaging,

    # 交付目录（及其 staging 位置）的覆盖。默认 <仓库根>\IOToolkit-<版本>。
    # 存在的理由：**试跑**。默认路径会先清空再写入，而那里可能是唯一一份已发布的产物 ——
    # 想验证流程时用一个临时目录，不要拿真交付目录冒险。
    [string]$DeliverableDir,

    # 产出哪些平台。Windows 出 DLL 载荷，Android 出 arm64-v8a 的 .so 载荷。
    # 默认 all：两者都出，且各自独立成包（互不污染 —— 平台不同、ABI 不同、消费方也不同）。
    [ValidateSet('all', 'windows', 'android')]
    [string]$Platform = 'all',

    # 分发根。制品（各消费方 zip）+ manifest.json 按 <根>\<产品>\<版本>\<平台>\ 落位。
    # 默认 <仓库根>\publish（=$Root，即 IODevice 子模块的父仓库）—— 与 IOStudio 的分发产物
    # 同一个根，一个 manifest 管全部产品。
    # 注意是**父仓库根**下的那个 publish：所有产出都集中在那里。
    [string]$OutputRoot,

    # Android 参数（转发给 Build-AndroidPayload.ps1）
    [string]$AndroidAbi = 'arm64-v8a',
    [string]$NdkRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host ">> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   $Text" -ForegroundColor Green }
function Write-Skip([string]$Text) { Write-Host "   (跳过) $Text" -ForegroundColor Yellow }

# ── 布局 ────────────────────────────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path      # …/IODevice/scripts
$RepoRoot = Split-Path -Parent $ScriptDir                          # …/IODevice
$Root = Split-Path -Parent $RepoRoot                               # …（父仓库，version.txt 所在）

# 版本号单一来源。不再让每个 build_*.bat 各自 set version=。
if (-not $Version) {
    $versionFile = Join-Path $Root 'version.txt'
    if (-not (Test-Path $versionFile)) {
        throw "找不到版本号来源 $versionFile。请创建它（内容形如 v2.4.2），或用 -Version 指定。"
    }
    $Version = (Get-Content -Path $versionFile -Raw).Trim()
}
if ($Version -notmatch '^v?\d+\.\d+\.\d+') {
    throw "版本号格式不合法: '$Version'（期望形如 v2.4.2）"
}
if ($Version -notmatch '^v') { $Version = "v$Version" }

$Deliverable = if ($DeliverableDir) { $DeliverableDir } else { Join-Path $Root "IOToolkit-$Version" }
$Staging = Join-Path $Root "_staging\IOToolkit-$Version"
if (-not $OutputRoot) { $OutputRoot = Join-Path $Root 'publish' }
$Product = 'iotoolkit'

. (Join-Path $ScriptDir 'OutputLayout.ps1')
# PayloadLib 提供废弃插件的**唯一权威清单**与剔除实现。
# 本脚本此前只在 Assemble-Payload 内部间接用到它 —— 现在要直接调 Remove-DeprecatedFromPayload，
# 所以必须自己 dot-source。
. (Join-Path $ScriptDir 'PayloadLib.ps1')

$doWindows = $Platform -in @('all', 'windows')
$doAndroid = $Platform -in @('all', 'android')

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host " IOToolkit SDK 打包  $Version   [$Platform]" -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  版本来源  : $(if ($PSBoundParameters.ContainsKey('Version')) { '-Version 参数' } else { 'version.txt' })"
Write-Host "  staging   : $Staging"
Write-Host "  交付目录  : $Deliverable"
Write-Host "  分发根    : $OutputRoot"
Write-Host ''

# ── 1. 构建 ─────────────────────────────────────────────
# 只出 Android 时不必跑 Windows 构建（build_IOToolkit.bat 编的是 Win32/Win64 原生）。
if ($doWindows -and -not $SkipBuild) {
    Write-Step '构建（IODevice\build_IOToolkit.bat）'
    $buildScript = Join-Path $RepoRoot 'build_IOToolkit.bat'
    if (-not (Test-Path $buildScript)) { throw "找不到构建脚本: $buildScript" }

    # 必须在 **$RepoRoot 作为工作目录** 下调用：build_IOToolkit.bat 的第一句是 `cd IODevice`，
    # 之后用相对路径 `call ../release_iodevice.bat` —— 那些 release_*.bat 就在 $RepoRoot 下。
    # 从别处（例如父仓库根）调用时，`cd IODevice` 会进到子模块根，`../release_*.bat`
    # 就指向了父仓库根，**每个 call 都失败**。实测表现：满屏 "不是内部或外部命令"。
    # 在这里显式切换工作目录，调用方从哪跑都一致。
    Push-Location $RepoRoot
    try {
        $buildOutput = & cmd /c "`"$buildScript`"" 2>&1
        $buildCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $buildText = ($buildOutput | Out-String)
    $buildOutput | Where-Object { $_ -match 'error|错误|失败|FAILED' } | Select-Object -First 12 |
        ForEach-Object { Write-Host "   $_" -ForegroundColor Yellow }

    # 这个 bat **不检查任何 errorlevel**，最后一句 `cd ../` 成功就返回 0 ——
    # 于是"每个 call 都失败"也会以退出码 0 收场（实测确认）。所以只靠 $LASTEXITCODE
    # 挡不住，必须再看输出里有没有 cmd 的"找不到命令/路径"这类症状。
    $looksFailed = $buildText -match "不是内部或外部命令|系统找不到指定的(文件|路径)|is not recognized"
    if ($buildCode -ne 0 -or $looksFailed) {
        Write-Host '   --- 构建输出末尾 20 行 ---' -ForegroundColor DarkGray
        $buildOutput | Select-Object -Last 20 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
        throw "构建失败（退出码 $buildCode$(if ($looksFailed) { '；输出里出现「找不到命令/路径」' } else { '' })）。**不继续打包**。"
    }
    Write-Ok "构建完成（退出码 0）"
}
elseif (-not $doWindows) {
    Write-Skip 'Windows 构建（-Platform 未含 windows）'
}
else {
    Write-Skip '构建（-SkipBuild）'
}

# ── 2. 干净的 staging ───────────────────────────────────
# 每次重建，绝不复用 —— 复用是"积累目录"的成因。
Write-Step "准备 staging: $Staging"
if (Test-Path $Staging) { Remove-Item -Path $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging -Force | Out-Null

# ── 3. 聚合两个子模块的产物（Windows）───────────────────
# 与旧脚本相同的两个来源，只是投递到 staging 而不是就地累积。
if ($doWindows) {
    $iouKit = Join-Path $Root "IOUI\$Version\IOToolkit"
    $devKit = Join-Path $Root "IODevice\$Version\IOToolkit"

    foreach ($source in @($iouKit, $devKit)) {
        if (-not (Test-Path $source)) {
            throw "聚合源不存在: $source`n先跑构建（去掉 -SkipBuild），或确认子模块的 release 脚本产出了该目录。"
        }
        Write-Step "聚合 $source"
        # robocopy /E 拷贝子目录（含空目录）；退出码 < 8 表示成功（robocopy 用它表达"有文件被拷"）。
        # 投到 staging\IOToolkit\ —— 保留包内那一层目录。
        # 旧的交付包结构是 <包>\IOToolkit\binaries\...，消费方按这个路径取库与头文件；
        # 拍平成 <包>\binaries\... 会让所有按路径消费的人失效（实测门禁一开就报"配置落点 0 个"，
        # 正是因为新产出的布局里没有 IOToolkit\ 这一层）。
        $null = robocopy $source (Join-Path $Staging 'IOToolkit') /E /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（退出码 $LASTEXITCODE）: $source" }
    }
}

# ── 3b. Android 载荷（arm64-v8a .so）─────────────────────
# 这一步此前**完全不存在**：6 个插件早有 IOUI-ANDROID-<ID> 的 CMakeLists，
# 但没人驱动，产物散在 in-source build_ 目录里、且没有任何东西比对它们与源码。
# 现在交给 Build-AndroidPayload.ps1（它负责 NDK 解析、命名契约核对、ELF 校验与落地）。
#
# 落点与 Windows 同构：binaries\Android\ 对应 binaries\Win64\。
# 配置**不**走 Assemble-Payload：那个共用装配层镜像全部 30 个正源，而 Android 只该带
# 真正构建出 .so 的那几个插件（多带等于暗示存在并不存在的平台能力）。
# Build-AndroidPayload 已按构建结果逐个种子。
if ($doAndroid) {
    Write-Step "构建 Android 载荷（$AndroidAbi）"
    $androidScript = Join-Path $ScriptDir 'Build-AndroidPayload.ps1'
    if (-not (Test-Path $androidScript)) { throw "找不到 Android 构建脚本: $androidScript" }

    $androidOut = Join-Path $Staging 'IOToolkit\binaries\Android'
    $androidArgs = @{
        RepoRoot  = $RepoRoot
        OutputDir = $androidOut
        Abi       = $AndroidAbi
    }
    if ($NdkRoot) { $androidArgs['NdkRoot'] = $NdkRoot }
    if ($SkipBuild) { $androidArgs['SkipBuild'] = $true }

    $android = & $androidScript @androidArgs
    $soCount = @(Get-ChildItem (Join-Path $androidOut 'ExternalLibraries') -File -Filter '*.so' -ErrorAction SilentlyContinue).Count
    Write-Ok "Android 载荷：$soCount 个插件 .so + iodevice.so"
}

# ── 4. 测试程序与 Studio 包（Windows）────────────────────
$testerSource = Join-Path $Root "IODevice\$Version\IOToolkit_Test_Win64"
if ($doWindows -and (Test-Path $testerSource)) {
    Write-Step '加入 IOToolkit_Tester'
    $tester = Join-Path $Staging 'IOToolkit_Tester'
    $null = robocopy $testerSource $tester /E /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（退出码 $LASTEXITCODE）: $testerSource" }
}
else {
    if (-not $doWindows) { Write-Skip 'IOToolkit_Tester（-Platform 未含 windows）' }
    else { Write-Skip "测试程序源不存在: $testerSource" }
}

# IOToolkit_Studio：可直接运行的调试台（测试程序 + 载荷 + 托管包装）。
# 旧脚本会建它，但**产物里根本没有**（上次没跑完或被手工动过），而且它的配置拷贝
# 被 `@rem` 注掉了。这里补上并纳入校验 —— 要么做对，要么从脚本删掉，不留"脚本说一套、
# 产物另一套"的中间态。
$studio = Join-Path $Staging 'IOToolkit_Studio'
if ($doWindows -and (Test-Path $testerSource)) {
    Write-Step '组装 IOToolkit_Studio'
    $null = robocopy $testerSource $studio /E /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（退出码 $LASTEXITCODE）: $testerSource -> studio" }

    $win64 = Join-Path $Staging 'IOToolkit\binaries\Win64'
    if (Test-Path (Join-Path $win64 'ExternalLibraries')) {
        $null = robocopy (Join-Path $win64 'ExternalLibraries') (Join-Path $studio 'ExternalLibraries') /E /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（退出码 $LASTEXITCODE）: Studio ExternalLibraries" }
    }
    foreach ($dll in @('IODevice.dll', 'IODevice_CSharp_Wrapper.dll')) {
        $from = Join-Path $win64 $dll
        if (Test-Path $from) { Copy-Item $from $studio -Force }
    }
    # 旧脚本会删掉 C_Wrapper（Studio 用托管包装，不需要 C 层）
    $cWrapper = Join-Path $studio 'IODevice_C_Wrapper.dll'
    if (Test-Path $cWrapper) { Remove-Item $cWrapper -Force }
}

# ── 5. 装配插件载荷（共用层）─────────────────────────────
# 必须**在 Studio 组装之后**跑：Studio 的 ExternalLibraries 是从 binaries\Win64 拷过去的，
# 若先装配再建 Studio，Studio 拿到的是未装配的旧内容（实测只剩 25 个配置）。
# 反过来，先把所有落点建出来、再一次性装配，三个目标就都能覆盖到。
#
# 只对 Windows 落点跑：这个共用装配层按设计镜像**全部**正源（30 个插件配置），
# 那是 Windows 载荷的正确语义。Android 的配置由 Build-AndroidPayload 按"确实构建出 .so"
# 逐个种子 —— 把 30 个配置塞进 Android 包等于宣称存在并不存在的平台能力。
$assemble = Join-Path $ScriptDir 'Assemble-Payload.ps1'
$configTargets = @()
if ($doWindows) {
    Write-Step '装配插件载荷（配置镜像自 IOUI\Config 正源）'

    # 循环变量刻意叫 $plat 而不是 $platform —— PowerShell 变量名大小写不敏感，
    # 用 $platform 会和本脚本的 -Platform 参数**是同一个变量**，而那个参数带
    # ValidateSet('all','windows','android')；把 'Win32' 赋进去会直接抛
    # ValidateSetFailure（实测踩过）。
    $configTargets = @(
        foreach ($plat in @('Win32', 'Win64')) {
            $p = Join-Path $Staging "IOToolkit\binaries\$plat\ExternalLibraries\Config"
            if (Test-Path $p) { $p }
        }
        $studioConfig = Join-Path $Staging 'IOToolkit_Studio\ExternalLibraries\Config'
        if (Test-Path $studioConfig) { $studioConfig }
    )
    if ($configTargets.Count -eq 0) {
        Write-Warning 'staging 里没有任何 ExternalLibraries\Config 落点 —— 载荷装配跳过。请确认构建产物结构。'
    }
    $payload = & $assemble `
        -IouiRootDir (Join-Path $Root 'IOUI') `
        -Target $configTargets `
        -IouiOutputDir (Join-Path $Root 'IOUI\Binaries\Win64\Release') `
        -LibraryDir (Join-Path $Staging 'IOToolkit\binaries\Win64\ExternalLibraries')
    Write-Ok "配置 $($payload.SyncedConfigs) 处落点 · 插件 DLL $($payload.SyncedPlugins) · Core 库 $($payload.SyncedCoreLibs)"
}
else {
    Write-Skip 'Windows 载荷装配（-Platform 未含 windows）'
}

# ── 5b. 从载荷里剔除废弃插件 ─────────────────────────────
# 必须在所有 ExternalLibraries 落点都建好之后跑（Studio 的那份是从 binaries\Win64 拷的），
# 而且必须作用在**载荷**上：中间态目录是积累式的（robocopy /E 只增不删），
# 废弃插件的 DLL 会一直躺在那里被整棵拷进来 —— 实测漏出过 3 个。
$payloadDirs = @(
    foreach ($plat in @('Win32', 'Win64', 'Android')) {
        Join-Path $Staging "IOToolkit\binaries\$plat\ExternalLibraries"
    }
    Join-Path $Staging 'IOToolkit_Studio\ExternalLibraries'
) | Where-Object { Test-Path $_ }
if ($payloadDirs.Count -gt 0) {
    $pruned = Remove-DeprecatedFromPayload -Roots $payloadDirs
    if ($pruned.Dlls.Count -gt 0 -or $pruned.Configs.Count -gt 0) {
        Write-Step "剔除废弃插件：DLL $($pruned.Dlls.Count) 个 / 配置 $($pruned.Configs.Count) 个"
        if ($pruned.Dlls.Count -gt 0) { Write-Host "      $($pruned.Dlls -join ', ')" -ForegroundColor DarkGray }
        if ($pruned.Configs.Count -gt 0) { Write-Host "      配置: $($pruned.Configs -join ', ')" -ForegroundColor DarkGray }
    }
    else {
        Write-Ok '载荷里没有废弃插件（已排除）'
    }
}

# ── 6. 校验：staging 必须等于正源 ───────────────────────
if (-not $SkipVerify -and $doWindows) {
    Write-Step '校验 staging 与正源一致'
    $result = & $assemble -IouiRootDir (Join-Path $Root 'IOUI') -Target $configTargets -VerifyOnly
    if ($result.DiffCount -gt 0) {
        throw "装配后仍有 $($result.DiffCount) 处与正源不一致 —— **不产出交付目录**。见上方逐条明细。"
    }
    Write-Ok '校验通过：插件配置与正源逐文件一致'
}
elseif (-not $SkipVerify -and $doAndroid) {
    # Android 侧没有共用的 Compare-DirectoryContent 语义可套（配置是按 .so 选的子集），
    # 改为直接断言产物集：每个 .so 都必须是 ELF64/AArch64，且文件名符合加载器契约。
    Write-Step '校验 Android 载荷'
    $extDir = Join-Path $Staging 'IOToolkit\binaries\Android\ExternalLibraries'
    $sos = @(Get-ChildItem $extDir -File -Filter '*.so' -ErrorAction SilentlyContinue)
    if ($sos.Count -lt 6) {
        throw "Android 载荷只有 $($sos.Count) 个插件 .so，期望 6 个（IOUI/Simulator/MP-YDL/MP-NJLJ/MP-NJQK/NETIO）。"
    }
    foreach ($so in $sos) {
        if ($so.Name -cnotmatch '^IOUI-ANDROID-[A-Z0-9\-]+\.so$') {
            throw "Android 载荷文件名不符合加载器契约（须为大写 IOUI-ANDROID-*.so，加载器会把 DllName 转大写）: $($so.Name)"
        }
        if (-not (Test-ElfAarch64 -Path $so.FullName)) {
            throw "Android 载荷不是 ELF64/AArch64: $($so.Name)"
        }
    }
    $coreSo = Join-Path $Staging 'IOToolkit\binaries\Android\iodevice.so'
    if (-not (Test-Path $coreSo)) { throw "Android 载荷缺内核 iodevice.so" }
    Write-Ok "校验通过：$($sos.Count) 个插件 .so + iodevice.so（均 ELF64/AArch64，命名符合契约）"
}
else {
    Write-Skip '校验（-SkipVerify）'
}

# ── 7. 投递 ─────────────────────────────────────────────
# 镜像式：先清交付目录再拷。避免它自己也变成"积累目录"（旧脚本的 xcopy 就是这个问题，
# 结果是交付目录里混着手工放的内容、且没人能发现）。
Write-Step "投递到 $Deliverable"
if (Test-Path $Deliverable) { Remove-Item -Path $Deliverable -Recurse -Force }
$null = robocopy $Staging $Deliverable /E /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（退出码 $LASTEXITCODE）: $Deliverable" }

$files = @(Get-ChildItem -Path $Deliverable -Recurse -File)
$sizeMb = [math]::Round((($files | Measure-Object -Property Length -Sum).Sum) / 1MB, 1)

# ── 8. 分发制品：按平台打 zip + manifest ─────────────────
# 交付目录（上面那份）是**解开的工作树**，供就地消费（Unity 工程按路径取库与头文件）。
# 这里产出的才是**可分发制品**：每个平台一个 zip，外加一份 manifest.json 记录
# 文件名/大小/sha256 与渠道参数 —— 此前这些只存在于控制台输出里，跑完就没了。
Write-Host ''
Write-Step "产出分发制品到 $OutputRoot"

$artifacts = New-Object System.Collections.Generic.List[object]
$sdkInclude = Join-Path $Staging 'IOToolkit\include'
$sdkProductId = 'com.himind.iotoolkit'
$winDir = Get-PlatformDir -OutputRoot $OutputRoot -Product $Product -Version $Version -Platform 'windows'
$andDir = Get-PlatformDir -OutputRoot $OutputRoot -Product $Product -Version $Version -Platform 'android'

function Add-Artifact {
    param([string]$ZipPath, [string]$Platform, [string]$Arch, [string]$Consumer)
    $artifacts.Add((New-ArtifactEntry -Path $ZipPath -OutputRoot $OutputRoot -ProductId $sdkProductId `
                -Platform $Platform -Architecture $Arch -PackageType 'directory-zip' -Consumer $Consumer))
}

# ── 8a. 规范载荷（C++ / UE 同构）─────────────────────────
# 这是唯一真相源形状：include + binaries/<平台>。C++ 直接用；UE 放进 ThirdParty/IOToolkit/。
if ($doWindows) {
    # **必须显式排除 binaries\Android** —— 实测把 staging 整个打进去会让 Windows 包里
    # 混进 27 个 Android 条目（两个平台的载荷装进同一个包，装的人会装错）。
    $items = @()
    foreach ($child in Get-ChildItem -LiteralPath $Staging) {
        if ($child.Name -eq 'IOToolkit' -and $child.PSIsContainer) {
            foreach ($sub in Get-ChildItem -LiteralPath $child.FullName) {
                if ($sub.Name -ne 'binaries' -or -not $sub.PSIsContainer) {
                    $items += @{ Path = $sub.FullName; Entry = "IOToolkit/$($sub.Name)" }
                    continue
                }
                foreach ($platDir in Get-ChildItem -LiteralPath $sub.FullName -Directory) {
                    if ($platDir.Name -eq 'Android') { continue }   # 有自己的包
                    $items += @{ Path = $platDir.FullName; Entry = "IOToolkit/binaries/$($platDir.Name)" }
                }
            }
        }
        elseif ($child.Name -ne '_views') {     # 消费方视图单独成包，不进规范载荷
            $items += @{ Path = $child.FullName; Entry = $child.Name }
        }
    }
    $winZip = Join-Path $winDir "IOToolkit-$Version-win.zip"
    $n = New-ArtifactZip -ZipPath $winZip -Items $items
    Write-Ok "canonical/windows: $(Split-Path -Leaf $winZip)（$n 个条目）"
    Add-Artifact -ZipPath $winZip -Platform 'windows' -Arch 'x64' -Consumer 'canonical'
    Assert-ZipPlatformIsolation -ZipPath $winZip -ForbiddenPlatform 'android'
}

if ($doAndroid) {
    $items = @()
    if (Test-Path $sdkInclude) {
        $items += @{ Path = $sdkInclude; Entry = 'IOToolkit/include' }
    }
    else {
        Write-Host '   注意：staging 里没有 IOToolkit\include —— Android 包将不含头文件。' -ForegroundColor Yellow
        Write-Host '         想让它自足，用 -Platform all 一起构建（头文件是平台无关的）。' -ForegroundColor DarkGray
    }
    $items += @{ Path = (Join-Path $Staging 'IOToolkit\binaries\Android'); Entry = 'IOToolkit/binaries/Android' }

    $androidZip = Join-Path $andDir "IOToolkit-$Version-android-$AndroidAbi.zip"
    $n = New-ArtifactZip -ZipPath $androidZip -Items $items
    Write-Ok "canonical/android: $(Split-Path -Leaf $androidZip)（$n 个条目）"
    Add-Artifact -ZipPath $androidZip -Platform 'android' -Arch $AndroidAbi -Consumer 'canonical'
    Assert-ZipPlatformIsolation -ZipPath $androidZip -ForbiddenPlatform 'windows'
}

# ── 8b. 消费方视图 ───────────────────────────────────────
# 规范载荷只有一种形状，但消费方要的形状不止一种：Unity 要 Unity 包的插件布局
# （Plugins/<架构>/ + 托管包装），不能靠"把规范载荷解到某处"得到。
# 装配逻辑集中在 Assemble-ConsumerViews.ps1（不产 .meta —— 那是 Unity 仓库的资产标识）。
$viewScript = Join-Path $ScriptDir 'Assemble-ConsumerViews.ps1'
if (Test-Path $viewScript) {
    Write-Step '装配消费方视图（Unity / UE）'
    $views = & $viewScript -StagingDir $Staging -OutputRoot (Join-Path $Staging '_views') -Clean

    foreach ($v in @($views)) {
        switch ($v.Name) {
            'unity-win' {
                $zip = Join-Path $winDir "IOToolkit-$Version-unity-win.zip"
                $n = New-ArtifactZip -ZipPath $zip -Items @(@{ Path = $v.Dir; Entry = '' })
                Write-Ok "unity/windows: $(Split-Path -Leaf $zip)（$n 个条目）"
                Add-Artifact -ZipPath $zip -Platform 'windows' -Arch 'x64' -Consumer 'unity'
            }
            'unity-android' {
                $zip = Join-Path $andDir "IOToolkit-$Version-unity-android-$AndroidAbi.zip"
                $n = New-ArtifactZip -ZipPath $zip -Items @(@{ Path = $v.Dir; Entry = '' })
                Write-Ok "unity/android: $(Split-Path -Leaf $zip)（$n 个条目）"
                Add-Artifact -ZipPath $zip -Platform 'android' -Arch $AndroidAbi -Consumer 'unity'
            }
            'ue' {
                # 引擎无关：这里给的是 ThirdParty 载荷，逐引擎的 BuildPlugin（RunUAT）由
                # UE 插件仓库自己做（IOToolkit_UE4_Plugin/Build_IOToolkit.bat），
                # 所以名字里**不带** UE 版本号 —— 带上会误导成"这是给 UE5.4 的成品插件"。
                $zip = Join-Path $winDir "IOToolkit-$Version-ue.zip"
                $n = New-ArtifactZip -ZipPath $zip -Items @(@{ Path = $v.Dir; Entry = '' })
                Write-Ok "ue: $(Split-Path -Leaf $zip)（$n 个条目）"
                Add-Artifact -ZipPath $zip -Platform 'windows' -Arch 'x64' -Consumer 'ue'
            }
        }
    }
}
else {
    Write-Host "   (skip) 找不到 $viewScript —— 不产出消费方视图" -ForegroundColor Yellow
}

$manifestPath = Write-ArtifactManifest -Dir (Get-ProductVersionDir -OutputRoot $OutputRoot -Product $Product -Version $Version) `
    -Product $Product -Version $Version -Channel 'stable' -Artifacts $artifacts -OutputRoot $OutputRoot
Write-Ok "manifest: $manifestPath"

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host ' IOToolkit SDK 包已就绪' -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
Write-Host "  版本      : $Version"
Write-Host "  交付目录  : $Deliverable"
Write-Host "  文件数    : $($files.Count)"
Write-Host "  大小      : $sizeMb MB"
foreach ($a in $artifacts) {
    Write-Host "  制品      : $($a.relativePath)  ($([math]::Round($a.sizeBytes / 1MB, 1)) MB)"
    Write-Host "              sha256 $($a.sha256)"
}
Write-Host ''
Write-Host '  说明：交付目录是解开的工作树（供就地消费）；publish\ 下的是可分发制品（zip + manifest）。' -ForegroundColor DarkGray
Write-Host '        IOToolkit_Studio / IOToolkit_Tester 是 Windows 调试台，只进 Windows 包。' -ForegroundColor DarkGray
Write-Host ''

# 旧的中间目录：本脚本不再产出它，但也不会擅自删 —— 先报出来让人确认。
$legacyIntermediate = Join-Path $Root $Version
if (Test-Path $legacyIntermediate) {
    Write-Host "  提示: 旧的中间目录仍在: $legacyIntermediate" -ForegroundColor Yellow
    Write-Host "        本脚本不再产出它（交付只保留 IOToolkit-$Version）。确认无用后可删。" -ForegroundColor DarkGray
}
Write-Host ''

if (-not $KeepStaging) {
    Remove-Item -Path $Staging -Recurse -Force
    # _staging 空掉就一并收走
    $stagingRoot = Split-Path -Parent $Staging
    if ((Test-Path $stagingRoot) -and -not @(Get-ChildItem -Path $stagingRoot -Force)) {
        Remove-Item -Path $stagingRoot -Force
    }
}
else {
    Write-Host "  staging 保留在: $Staging" -ForegroundColor DarkGray
}
