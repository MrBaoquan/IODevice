<#
    输出布局与制品清单的**共用实现** —— 被以下三处 dot-source：
      · Build-IOToolkitPackage.ps1（产出 SDK 包 + 清单）
      · Publish-IOStudioDistribution.ps1（产出应用包 + 清单）
      · Verify-BuildAlignment.ps1（门禁：核对清单与实际文件的摘要是否仍然一致）

    为什么必须是同一处：三个脚本都要回答同一个问题 —— **"这个制品放在哪、叫什么、摘要是什么"**。
    各写一份就会分叉：打包按一套命名放、门禁按另一套找，于是门禁要么假警、要么静默漏检。

    目录形状（每个产品独立版本号，互不错位）：

        <输出根>/
          <产品>/<版本>/manifest.json
          <产品>/<版本>/<平台>/<制品文件...>
          <输出根>/_staging/...            （构建中间物，不是交付物）

    平台目录用 `windows` / `android` 这类小写名；架构进文件名（win-x64 / android-arm64-v8a），
    因为同一个平台可能有多个 ABI。

    本文件只放纯函数，没有 param 块、没有顶层执行语句 —— dot-source 它不应产生副作用。
#>

# 版本号归一：目录名不带前导 v。仓库根 version.txt 写的是 "v2.4.1"，
# 而以 v 开头的目录名在打包/URL 里都不合适，所以统一在这里剥掉。
function Get-NormalizedVersion {
    param([Parameter(Mandatory = $true)][string]$Version)
    return $Version.Trim().TrimStart('v', 'V')
}

# <输出根>/<产品>/<版本>
function Get-ProductVersionDir {
    param(
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [Parameter(Mandatory = $true)][string]$Product,
        [Parameter(Mandatory = $true)][string]$Version
    )
    return (Join-Path (Join-Path $OutputRoot $Product) (Get-NormalizedVersion -Version $Version))
}

# <输出根>/<产品>/<版本>/<平台>
function Get-PlatformDir {
    param(
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [Parameter(Mandatory = $true)][string]$Product,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Platform
    )
    return (Join-Path (Get-ProductVersionDir -OutputRoot $OutputRoot -Product $Product -Version $Version) $Platform)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# 一个清单条目。路径按**相对输出根**记（如 iostudio/1.0.17/windows/x.zip），
# 这样清单自描述、搬到别处也不失效。
function New-ArtifactEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [Parameter(Mandatory = $true)][string]$ProductId,
        [Parameter(Mandatory = $true)][string]$Platform,
        [Parameter(Mandatory = $true)][string]$Architecture,
        [Parameter(Mandatory = $true)][string]$PackageType,

        # 这个制品面向哪个消费方：canonical（C++/UE 同构的规范载荷）/ unity / ue。
        # 记下来是因为同一个版本下会有多份"同名不同形状"的包，
        # 光看 platform 分不出该拿哪一份。
        [string]$Consumer = 'canonical'
    )
    if (-not (Test-Path -LiteralPath $Path)) { throw "制品不存在，无法登记: $Path" }
    $file = Get-Item -LiteralPath $Path
    $rootFull = (Resolve-Path -LiteralPath $OutputRoot).Path
    $full = $file.FullName
    $rel = if ($full.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        $full.Substring($rootFull.Length).TrimStart('\', '/')
    }
    else {
        $full
    }
    return [pscustomobject]@{
        fileName     = $file.Name
        relativePath = $rel.Replace('\', '/')
        productId    = $ProductId
        platform     = $Platform
        architecture = $Architecture
        packageType  = $PackageType
        consumer     = $Consumer
        sizeBytes    = [long]$file.Length
        sha256       = Get-Sha256 -Path $file.FullName
    }
}

function Write-ArtifactManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Dir,
        [Parameter(Mandatory = $true)][string]$Product,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Channel,
        [Parameter(Mandatory = $true)][object[]]$Artifacts,

        # 分发根。relativePath 是相对**它**记的（<产品>/<版本>/<平台>/<文件>），
        # 所以要判断"清单里某条的文件还在不在"，必须知道根。
        # 别试图从 $Dir 反推：那是 <根>\<产品>\<版本>，反推层级容易错位。
        [string]$OutputRoot
    )
    if (-not (Test-Path -LiteralPath $Dir)) { New-Item -ItemType Directory -Path $Dir -Force | Out-Null }
    $path = Join-Path $Dir 'manifest.json'

    # **合并**而不是整体覆盖。清单描述的是"这个版本目录里现在有什么"，
    # 而一次运行可能只产出部分平台（如 -Platform windows）。整体覆盖会把这次没重建的
    # 平台条目**静默删掉**，而它们的文件还在磁盘上 —— 实测踩过：跑一次 windows-only，
    # android 的两个 zip 还在，清单里却只剩下 3 条。
    # 保留策略：本次重建的按 (platform, consumer, fileName) 替换；其余有条目且文件仍在的原样留着；
    # 文件已不存在的条目剔除（不让清单指向空气）。
    #
    # 用**普通数组**累积，不用 List[object]：PowerShell 5.1 下
    # `New-Object System.Collections.Generic.List[object]` 造出来的实例一旦被 `@(...)` 包住
    # 就抛 ArgumentException「参数类型不匹配」（实测；换成 `[List[object]]::new()` 则正常 ——
    # 同一类型名，两种构造法行为不同）。清单只有几条，用数组累加既够快又躲开这个坑。
    $merged = @()
    $fresh = @{}
    foreach ($a in @($Artifacts)) { $fresh["$($a.platform)|$($a.consumer)|$($a.fileName)"] = $a }

    if ((Test-Path -LiteralPath $path) -and $OutputRoot) {
        try {
            $existing = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($old in @($existing.artifacts)) {
                $key = "$($old.platform)|$($old.consumer)|$($old.fileName)"
                if ($fresh.ContainsKey($key)) { continue }          # 本次重建，用新的
                $rel = [string]$old.relativePath
                if ([string]::IsNullOrWhiteSpace($rel)) { continue }
                $oldFile = Join-Path $OutputRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
                if (Test-Path -LiteralPath $oldFile) { $merged += $old }
            }
        }
        catch {
            Write-Warning "已有 manifest 无法解析或条目不可用，将整体重写: $path（$($_.Exception.Message)）"
        }
    }
    foreach ($a in @($Artifacts)) { $merged += $a }

    $manifest = [ordered]@{
        product     = $Product
        version     = (Get-NormalizedVersion -Version $Version)
        channel     = $Channel
        generatedAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
        artifacts   = @($merged)
    }
    # -Depth 4 足够（artifacts 是对象数组，其成员是叶子）。
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

# 门禁用：清单里登记的每个制品是否**仍然**存在且摘要一致。
# 纯读，不改任何文件。返回差异字符串数组（空 = 一致）。
function Compare-ArtifactManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$OutputRoot
    )
    $diffs = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        $diffs.Add("清单不存在: $ManifestPath")
        return $diffs
    }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($a in @($manifest.artifacts)) {
        $p = Join-Path $OutputRoot ($a.relativePath -replace '/', '\')
        if (-not (Test-Path -LiteralPath $p)) {
            $diffs.Add("$($a.fileName): 清单已登记但文件不存在")
            continue
        }
        $actualSize = (Get-Item -LiteralPath $p).Length
        if ($actualSize -ne $a.sizeBytes) {
            $diffs.Add("$($a.fileName): 大小不符（清单 $($a.sizeBytes)，实际 $actualSize）")
            continue
        }
        $actualHash = Get-Sha256 -Path $p
        if ($actualHash -ne $a.sha256) {
            $diffs.Add("$($a.fileName): sha256 不符（清单 $($a.sha256.Substring(0,12))…，实际 $($actualHash.Substring(0,12))…）")
        }
    }
    return $diffs
}

# 打 zip。条目名一律用 '/' 分隔 —— .NET 的 ZipFile.CreateFromDirectory 在 Windows
# PowerShell 下会写成 '\'，那不符合 zip 规范，分发服务与解压端都可能认不出。
# 这个实现集中在这里，是为了让两个打包脚本用同一套口径（各写一份必然分叉）。
#
# Items: @{ Path = <绝对路径，文件或目录>; Entry = <zip 内条目名> }
function New-ArtifactZip {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][object[]]$Items
    )
    Add-Type -AssemblyName System.IO.Compression | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $parent = Split-Path -Parent $ZipPath
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }

    $archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    $count = 0
    try {
        foreach ($item in $Items) {
            $src = $item.Path
            $entry = ($item.Entry -replace '\\', '/').TrimEnd('/')
            if (-not (Test-Path -LiteralPath $src)) { continue }
            if (Test-Path -LiteralPath $src -PathType Leaf) {
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $src, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
                $count++
                continue
            }
            $srcFull = (Resolve-Path -LiteralPath $src).Path.TrimEnd('\', '/')
            $prefixLen = $srcFull.Length + 1
            foreach ($file in Get-ChildItem -LiteralPath $srcFull -Recurse -File) {
                $rel = $file.FullName.Substring($prefixLen).Replace('\', '/')
                # 空 Entry = 条目直接落在 zip 根（用于"整目录即包内容"的情形，
                # 如 IOStudio 的 staging）。此时不能拼成 "/$rel"。
                $name = if ([string]::IsNullOrEmpty($entry)) { $rel } else { "$entry/$rel" }
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $file.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal)
                $count++
            }
        }
    }
    finally { $archive.Dispose() }
    return $count
}

# 平台隔离断言：一个平台的包里**不得**出现另一个平台的载荷。
# 这条是实测抓出来的 —— 把 staging 整个打进 zip 时，Windows 包里混进了 27 个
# Android 条目（两个平台的载荷装进同一个包，装的人会装错，而包本身"看起来成功"）。
# 判据用目录名而不是扩展名：binaries\Android 与 binaries\Win64 才是平台落点。
function Assert-ZipPlatformIsolation {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][ValidateSet('windows', 'android')][string]$ForbiddenPlatform
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally { $archive.Dispose() }

    $forbidden = if ($ForbiddenPlatform -eq 'android') { 'binaries/Android' } else { 'binaries/Win32', 'binaries/Win64' }
    $hits = @($names | Where-Object {
            $n = $_
            @($forbidden | Where-Object { $n -like "*/$_/*" }).Count -gt 0
        })
    if ($hits.Count -gt 0) {
        throw "平台隔离被破坏：$(Split-Path -Leaf $ZipPath) 里混进了 $ForbiddenPlatform 载荷 $($hits.Count) 条（例：$($hits[0])）。"
    }
    $badSep = @($names | Where-Object { $_.Contains('\') }).Count
    if ($badSep -gt 0) {
        throw "zip 条目含反斜杠分隔符 $badSep 条（须为 '/'）：$(Split-Path -Leaf $ZipPath)"
    }
    return $true
}

# 读一个 zip 的条目名。门禁与打包都用它，避免各写一份解压/读条目的代码。
function Get-ZipEntryNames {
    param([Parameter(Mandatory = $true)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally { $archive.Dispose() }
}

# 制品是否为预期的 ELF 目标。**仅读文件头**，不需要 NDK 的 readelf ——
# 门禁不该依赖 NDK 是否装在这台机器上。
#   ELF 魔数 0x7F 'E' 'L' 'F'，EI_CLASS 在偏移 4（2 = 64 位），
#   e_machine 在偏移 18（小端 0xB7 = 183 = AArch64）。
function Test-ElfAarch64 {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $head = New-Object byte[] 20
        if ($fs.Read($head, 0, 20) -lt 20) { return $false }
        if ($head[0] -ne 0x7F -or $head[1] -ne 0x45 -or $head[2] -ne 0x4C -or $head[3] -ne 0x46) { return $false }
        if ($head[4] -ne 2) { return $false }                       # 64 位
        if ($head[5] -ne 1) { return $false }                       # 小端
        return ($head[18] -eq 0xB7 -and $head[19] -eq 0x00)          # AArch64
    }
    finally { $fs.Dispose() }
}
