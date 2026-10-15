<#
.SYNOPSIS
    插件载荷装配：把插件二进制、Core 库与插件配置从各自的**正源**镜像到目标载荷目录。

.DESCRIPTION
    这个脚本是**两个交付形态共用的装配层**，本身不是构建、也不是打包：

        IOUI/Config/<插件>/          ┐
        IOUI/Binaries/Win64/Release/ ├─→ Assemble-Payload.ps1 ─→ 载荷目录
        Binaries/Win64/Release/      ┘                          （ExternalLibraries\...）

    两个交付形态各自调用它：
      · IOToolkit SDK 包（给 Unity / UE4 嵌入）——Build-IOToolkitPackage.ps1
      · IOStudio 应用包（给终端用户）——Publish-IOStudioDistribution.ps1

    **为什么不各自复制一份装配逻辑**：实测两条链路此前**各自漏配置**，而且还漏得不一样 ——
    IOStudio 那边漏到只剩 2/30，SDK 那边 22/30。同一份载荷、两套装配，就是两处会独立腐烂。
    装配逻辑收在这里之后，「载荷 == 正源」成了两个形态共同的可断言不变量。

    **为什么是镜像（先清后拷）而不是 xcopy 累加**：
    xcopy /E /Y 不删除目标里多出来的东西，于是目标目录会变成"历次构建的堆积"而不是
    "正源的投影"。实测 SDK 包里就混着 `IOToolkit_SNAP7.zip`、`信捷PLC-新版...` 这类
    手工放进去的内容；IOStudio 那侧的 Simulator web 也因此带着 3 个上一版的 assets chunk。
    先清后拷让"多余的文件"从设计上不可能存在 —— 代价是 0（目标本就该等于正源）。

.PARAMETER IouiRootDir
    IOUI 根目录。插件配置正源是它下面的 `Config\<插件>\`。

.PARAMETER Target
    一个或多个**插件配置目录**（即某个载荷根下的 `ExternalLibraries\Config`）。
    对每个目标，把正源里的全部插件配置镜像进去。

.PARAMETER IouiOutputDir
    IOUI 构建输出（`Binaries\Win64\<配置>`），提供插件 DLL 与 `Libraries\*.dll`。
    给了它就一并同步二进制；不给则只同步配置。

.PARAMETER LibraryDir
    载荷根下的 `ExternalLibraries` 目录。插件 DLL 落到这里，Core 库落到 `Core\`。

.PARAMETER VerifyOnly
    只比对、不写入。用于门禁与人工核查。

.OUTPUTS
    一个摘要对象：SyncedConfigs / SyncedPlugins / SyncedCoreLibs / DiffCount。

.EXAMPLE
    # 装配到 SDK 包的 Win64 载荷
    .\Assemble-Payload.ps1 -IouiRootDir ..\..\IOUI `
        -Target ..\..\v2.4.1\IOToolkit\binaries\Win64\ExternalLibraries\Config `
        -IouiOutputDir ..\..\IOUI\Binaries\Win64\Release `
        -LibraryDir ..\..\v2.4.1\IOToolkit\binaries\Win64\ExternalLibraries
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$IouiRootDir,
    [string[]]$Target = @(),
    [string]$IouiOutputDir,
    [string]$LibraryDir,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 比对与同步的实现在 PayloadLib.ps1 —— 门禁（Verify-BuildAlignment.ps1）dot-source
# 的是同一个文件，所以"装配是否等于正源"与"门禁判定是否等于正源"用的是同一把尺子。
. (Join-Path $PSScriptRoot 'PayloadLib.ps1')

if ($VerifyOnly) {
    # 只比对不写入。这条路径给门禁与人工核查用 —— 与装配走的是**同一个** Compare-DirectoryContent，
    # 所以"装配后是否等于正源"与"现在是否等于正源"是同一把尺子。
    $report = [ordered]@{ Targets = @(); DiffCount = 0 }
    $checked = Sync-PluginConfigs -IouiRoot $IouiRootDir -Targets $Target -DryRun
    foreach ($diff in $checked.Diffs) {
        Write-Host "  $diff" -ForegroundColor Yellow
    }
    Write-Host "  比对目标 $($Target.Count) 个，差异 $($checked.DiffCount) 处"
    [pscustomobject]@{
        SyncedConfigs  = 0
        SyncedPlugins  = 0
        SyncedCoreLibs = 0
        DiffCount      = $checked.DiffCount
    }
}
else {
    $configs = Sync-PluginConfigs -IouiRoot $IouiRootDir -Targets $Target
    $binaries = [pscustomobject]@{ Plugins = 0; CoreLibs = 0 }
    if ($IouiOutputDir -or $LibraryDir) {
        $binaries = Sync-PluginBinaries -OutputDir $IouiOutputDir -Library $LibraryDir
    }
    [pscustomobject]@{
        SyncedConfigs  = $configs.Synced
        SyncedPlugins  = $binaries.Plugins
        SyncedCoreLibs = $binaries.CoreLibs
        DiffCount      = $configs.DiffCount
    }
}
