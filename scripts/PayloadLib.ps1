<#
    载荷比对与同步的**共用实现** —— 被以下两处 dot-source：
      · Assemble-Payload.ps1（装配：把正源镜像进载荷）
      · Verify-BuildAlignment.ps1（门禁：核对载荷是否等于正源）

    为什么必须是同一处：这两件事看起来相反（一个写、一个读），但**判据是同一个**
    ——"载荷是否等于正源"。各写一份就会分叉：装配认为一致的、门禁可能判不一致，
    于是门禁的告警失去意义，或者装配悄悄放过了它本该修的东西。

    本文件只放纯函数，没有 param 块、没有顶层执行语句 —— 这样 dot-source 它不会产生副作用。
#>

# 比对两个目录的**内容**（SHA-256），不只查"文件在不在"。
# 名字相同内容不同的旧副本正是要抓的东西 —— 只查存在性会放过它
# （实测 Simulator web 就是这样漂了 4 个半月：文件名一样、内容差 4 个月）。
function Compare-DirectoryContent {
    param([string]$Source, [string]$Target)

    $diffs = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path $Target)) {
        $diffs.Add('目标不存在')
        return $diffs
    }

    foreach ($file in (Get-ChildItem -Path $Source -Recurse -File -ErrorAction SilentlyContinue)) {
        $relative = $file.FullName.Substring($Source.Length).TrimStart('\', '/')
        $mirror = Join-Path $Target $relative
        if (-not (Test-Path $mirror)) {
            $diffs.Add("缺失 $relative")
            continue
        }
        if ((Get-FileHash $file.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash $mirror -Algorithm SHA256).Hash) {
            $diffs.Add("内容不一致 $relative")
        }
    }

    # 反向：目标里多出来的（镜像语义下不该有）
    foreach ($file in (Get-ChildItem -Path $Target -Recurse -File -ErrorAction SilentlyContinue)) {
        $relative = $file.FullName.Substring($Target.Length).TrimStart('\', '/')
        if (-not (Test-Path (Join-Path $Source $relative))) {
            $diffs.Add("多余 $relative")
        }
    }
    return $diffs
}

# 已废弃的插件：**不得进入任何产出**（DLL、配置、Android .so 都不例外）。
#
# 这份清单是**唯一权威**，被三处共用：
#   · PayloadLib 的 Sync-PluginConfigs / Sync-PluginBinaries（装配时跳过）
#   · Verify-BuildAlignment.ps1（门禁：产出里出现即报）
#   · Build-AndroidPayload / Build-IOToolkitPackage（不构建、不打包）
# 各写一份必然分叉 —— 那正是"某处忘了排除、结果又发出去了"的成因。
#
# 判据用**插件 id**（= DLL 名去掉 IOUI-Win64- 前缀、= 源码目录名、= Config 子目录名），
# 三者同一个标识，所以一份清单能同时管住三条路径。
$script:DeprecatedPlugins = @(
    'CCBOX', 'CCBOX-AH', 'CCBOX_SD',
    'ENCODER-BH38', 'ENCODER-BH38M', 'ENCODER-BRT38', 'ENCODER-BRT38M',
    'NET-BRIDGE', 'NET-IOFE', 'SERIAL-BRIDGE'
)

function Test-DeprecatedPlugin {
    param([Parameter(Mandatory = $true)][string]$Id)
    return ($script:DeprecatedPlugins -contains $Id)
}

# 从 id 集合里剔除废弃项，并回报剔掉了什么（供调用方打印，避免"静默少几个"）。
function Select-ActivePlugins {
    param([string[]]$Ids)
    $kept = @($Ids | Where-Object { -not (Test-DeprecatedPlugin -Id $_) })
    $dropped = @($Ids | Where-Object { Test-DeprecatedPlugin -Id $_ })
    return [pscustomobject]@{ Kept = $kept; Dropped = $dropped }
}

# 从**载荷**里彻底剔除废弃插件：DLL + 配置目录。
#
# 为什么光"装配时跳过"不够：载荷不是凭空生成的 ——
#   中间态 IOUI\<版本>\IOToolkit\ 与 IODevice\<版本>\IOToolkit\ 由各插件 release.cmd
#   累加而成（robocopy /E **只增不删**），打包时整棵拷进 staging。
#   于是"历史上曾构建过的废弃插件"会一直躺在载荷里，而装配层只负责往里加、不负责删。
# 实测踩过：废弃清单生效后，包里仍留着 NET-BRIDGE / NET-IOFE / SERIAL-BRIDGE
# （它们曾在 IOUI.sln 里，所以 release.cmd 跑过、把 DLL 留在了中间态）。
#
# 所以剔除要作用在**载荷**上，而不是只作用在"来源"上 —— 来源有多个，且都是积累目录。
function Remove-DeprecatedFromPayload {
    param(
        [string[]]$Roots,      # 每个 root 是一个 ExternalLibraries 目录（其下应有 *dll 与 Config\）
        [switch]$DryRun
    )
    $removedDll = @()
    $removedCfg = @()
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }

        foreach ($f in @(Get-ChildItem -LiteralPath $root -File -Filter 'IOUI-*.dll' -ErrorAction SilentlyContinue)) {
            # 名字形如 IOUI-Win64-<id>.dll / IOUI-Win32-<id>.dll
            $parts = $f.BaseName -split '-', 3
            if ($parts.Count -lt 3) { continue }
            if (-not (Test-DeprecatedPlugin -Id $parts[2])) { continue }
            $removedDll += $f.Name
            if (-not $DryRun) { Remove-Item -LiteralPath $f.FullName -Force }
        }

        $cfgRoot = Join-Path $root 'Config'
        if (Test-Path -LiteralPath $cfgRoot) {
            foreach ($d in @(Get-ChildItem -LiteralPath $cfgRoot -Directory -ErrorAction SilentlyContinue)) {
                if (-not (Test-DeprecatedPlugin -Id $d.Name)) { continue }
                $removedCfg += $d.Name
                if (-not $DryRun) { Remove-Item -LiteralPath $d.FullName -Recurse -Force }
            }
        }
    }
    return [pscustomobject]@{
        Dlls    = @($removedDll | Sort-Object -Unique)
        Configs = @($removedCfg | Sort-Object -Unique)
    }
}

# 插件配置：正源 IOUI\Config\<插件>\ → 每个目标，镜像式（先清后拷）。
# -DryRun 只比对不写入；**整个目录缺失也要报** —— 那是最严重的一类
# （实测 SDK 包里 8 个插件配置压根没有，包括一个 2022 年就存在的）。
function Sync-PluginConfigs {
    param([string]$IouiRoot, [string[]]$Targets, [switch]$DryRun)

    $result = [ordered]@{ Synced = 0; DiffCount = 0; Diffs = @(); SkippedDeprecated = @() }
    $sourceRoot = Join-Path $IouiRoot 'Config'
    if (-not (Test-Path $sourceRoot)) {
        Write-Warning "插件配置正源不存在: $sourceRoot"
        return [pscustomobject]$result
    }

    $sourceDirs = @(Get-ChildItem -Path $sourceRoot -Directory -ErrorAction SilentlyContinue)
    # 废弃插件的配置不镜像 —— 否则包里会出现"有配置、没有对应 DLL"的空壳，
    # 反而像是"这个设备受支持"。
    $deprecated = @($sourceDirs | Where-Object { Test-DeprecatedPlugin -Id $_.Name })
    if ($deprecated.Count -gt 0) {
        $result.SkippedDeprecated = @($deprecated | ForEach-Object { $_.Name })
    }
    $sourceDirs = @($sourceDirs | Where-Object { -not (Test-DeprecatedPlugin -Id $_.Name) })
    foreach ($target in $Targets) {
        if ([string]::IsNullOrWhiteSpace($target)) { continue }

        foreach ($pluginDir in $sourceDirs) {
            $destination = Join-Path $target $pluginDir.Name

            if ($DryRun) {
                if (-not (Test-Path $destination)) {
                    $result.Diffs += "$($pluginDir.Name): 目标没有这个插件配置"
                    continue
                }
                foreach ($diff in @(Compare-DirectoryContent -Source $pluginDir.FullName -Target $destination)) {
                    $result.Diffs += "$($pluginDir.Name): $diff"
                }
                continue
            }

            if (Test-Path $destination) {
                Remove-Item -Path $destination -Recurse -Force
            }
            Copy-Item -Path $pluginDir.FullName -Destination $target -Recurse -Force
            $result.Synced++
        }
    }
    # 按严重度排序，最严重的排前面。消费方（门禁）只列前若干条 ——
    # 不排序的话"整个插件配置缺失"会被一堆内容差异淹掉（实测第一次跑出来
    # 前 12 条里只露出 1 个缺失目录，其余 7 个全被截断）。
    $result.Diffs = @($result.Diffs | Sort-Object -Property @{
            Expression = {
                if ($_ -match '目标没有这个插件配置') { 0 }   # 整个目录缺失：最严重
                elseif ($_ -match '缺失 ') { 1 }              # 单文件缺失
                elseif ($_ -match '内容不一致 ') { 2 }        # 内容陈旧
                elseif ($_ -match '多余 ') { 3 }              # 多余文件
                else { 4 }
            }
        }, @{ Expression = { $_ } })
    $result.DiffCount = $result.Diffs.Count
    return [pscustomobject]$result
}

# 插件二进制：从**构建输出根**把插件 DLL 与 Core 库镜像进载荷。
#
# 这里不做"跳过源码已不存在的遗留插件"—— 那是**判定**，归门禁
# （Verify-BuildAlignment.ps1 的「插件 DLL 孤儿」一项）。判定散进搬运里就会有两套口径。
function Sync-PluginBinaries {
    param([string]$OutputDir, [string]$Library, [switch]$DryRun)

    $result = [ordered]@{ Plugins = 0; CoreLibs = 0; SkippedDeprecated = @() }
    if (-not $OutputDir -or -not (Test-Path $OutputDir)) {
        Write-Warning "IOUI 构建输出不存在: $OutputDir"
        return [pscustomobject]$result
    }
    if (-not $Library -or -not (Test-Path $Library)) {
        Write-Warning "载荷 ExternalLibraries 目录不存在: $Library"
        return [pscustomobject]$result
    }

    foreach ($dll in @(Get-ChildItem -Path $OutputDir -File -Filter 'IOUI-Win64-*.dll' -ErrorAction SilentlyContinue)) {
        # 废弃插件即使还留在构建输出里也不进载荷 —— 构建输出是**积累目录**，
        # 源码已废弃的 DLL 会一直躺在那儿（实测 10 个），只靠 glob 就会一路发出去。
        $id = $dll.BaseName.Substring('IOUI-Win64-'.Length)
        if (Test-DeprecatedPlugin -Id $id) {
            $result.SkippedDeprecated += $id
            continue
        }
        if (-not $DryRun) { Copy-Item -Path $dll.FullName -Destination $Library -Force }
        $result.Plugins++
    }

    $coreSource = Join-Path $OutputDir 'Libraries'
    $coreTarget = Join-Path $Library 'Core'
    if (Test-Path $coreSource) {
        if (-not $DryRun) {
            if (-not (Test-Path $coreTarget)) { New-Item -ItemType Directory -Path $coreTarget -Force | Out-Null }
            Copy-Item -Path (Join-Path $coreSource '*') -Destination $coreTarget -Recurse -Force
        }
        $result.CoreLibs = @(Get-ChildItem -Path $coreSource -File -ErrorAction SilentlyContinue).Count
    }
    return [pscustomobject]$result
}
