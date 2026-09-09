<#
.SYNOPSIS
    Build IODevice / IODevice_C_Wrapper / IODevice_CSharp_Wrapper and deploy the
    resulting DLLs into the UNIPlayer IOToolkit Unity plugin folders.

.DESCRIPTION
    Single-source replacement for the scattered release_*.bat scripts when the
    goal is "refresh the DLLs that UNIPlayer loads". Default flow:

      1. MSBuild IODevice.sln  Release|x64 (and optionally Win32)
     2. Copy DLLs to:
           UNIPlayer\Assets\Plugins\IOToolkit\Plugins\x86_64\
           UNIPlayer\Assets\Plugins\IOToolkit\Plugins\x86\        (when -IncludeWin32)
         UNIPlayer\Assets\Plugins\IOToolkit\Plugins\            (managed wrapper top-level only)
      3. Optionally run IOStudio.Tests (xunit) for a smoke check

.PARAMETER RepoRoot
    IODevice repo root (folder that contains IODevice.sln). Defaults to the
    script's parent directory.

.PARAMETER UniPlayerRoot
    UNIPlayer Unity project root. Defaults to <RepoRoot>\..\UNIPlayer.

.PARAMETER IncludeWin32
    Also build Win32 and copy to Plugins\x86. Off by default (UNIPlayer runs x64).

.PARAMETER SkipBuild
    Skip MSBuild; only deploy existing DLLs from Binaries\.

.PARAMETER RunTests
    Run IOStudio.Tests after deploy.

.PARAMETER Clean
    Pass /t:Rebuild to MSBuild.

.EXAMPLE
    .\Deploy-IODeviceToUNIPlayer.ps1
    .\Deploy-IODeviceToUNIPlayer.ps1 -RunTests
    .\Deploy-IODeviceToUNIPlayer.ps1 -SkipBuild          # just re-copy
    .\Deploy-IODeviceToUNIPlayer.ps1 -IncludeWin32 -Clean -RunTests
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')),
    [string] $UniPlayerRoot,
    [switch] $IncludeWin32,
    [switch] $SkipBuild,
    [switch] $RunTests,
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ paths
$RepoRoot = (Resolve-Path $RepoRoot).Path
if (-not $UniPlayerRoot) {
    $UniPlayerRoot = (Resolve-Path (Join-Path $RepoRoot '..\UNIPlayer')).Path
}

$Sln = Join-Path $RepoRoot 'IODevice.sln'
$BinariesRoot = Join-Path $RepoRoot 'Binaries'
$TestsProj = Join-Path $RepoRoot 'IOStudio.Tests\IOStudio.Tests.csproj'

$PluginsRoot = Join-Path $UniPlayerRoot 'Assets\Plugins\IOToolkit\Plugins'
$Plugins_x64 = Join-Path $PluginsRoot 'x86_64'
$Plugins_x86 = Join-Path $PluginsRoot 'x86'

foreach ($p in @($Sln, $PluginsRoot)) {
    if (-not (Test-Path $p)) { throw "Path not found: $p" }
}

Write-Host "Repo       : $RepoRoot"
Write-Host "UNIPlayer  : $UniPlayerRoot"
Write-Host "Plugins x64: $Plugins_x64"
if ($IncludeWin32) { Write-Host "Plugins x86: $Plugins_x86" }

# ------------------------------------------------------------------ msbuild locate
function Resolve-MSBuild {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'msbuild.exe not on PATH and vswhere.exe not found' }
    $path = & $vswhere -latest -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $path) { throw 'MSBuild not found via vswhere' }
    return $path
}

# ------------------------------------------------------------------ build
function Invoke-Build([string]$Platform) {
    $msbuild = Resolve-MSBuild
    $target = if ($Clean) { '/t:Rebuild' } else { '/t:Build' }
    # Restore first (C# SDK projects need NuGet).
    Write-Host ">> MSBuild $Platform Release restore" -ForegroundColor Cyan
    & $msbuild $Sln /t:Restore /p:Configuration=Release /p:Platform=$Platform /v:m /nologo
    if ($LASTEXITCODE) { throw "MSBuild restore failed ($Platform) exit=$LASTEXITCODE" }

    Write-Host ">> MSBuild $Platform Release build" -ForegroundColor Cyan
    & $msbuild $Sln $target /p:Configuration=Release /p:Platform=$Platform /v:m /nologo /m
    if ($LASTEXITCODE) { throw "MSBuild build failed ($Platform) exit=$LASTEXITCODE" }
}

if (-not $SkipBuild) {
    Invoke-Build 'x64'
    if ($IncludeWin32) { Invoke-Build 'Win32' }
}

# ------------------------------------------------------------------ deploy
function Copy-IfExists([string]$Src, [string]$Dst) {
    if (-not (Test-Path $Src)) {
        Write-Warning "  MISSING: $Src  (skipped)"
        return $false
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $Dst) | Out-Null
    Copy-Item -Force $Src $Dst
    $size = (Get-Item $Dst).Length
    Write-Host ("  [ok] {0,-32} -> {1}  ({2:N0} bytes)" -f (Split-Path $Src -Leaf), $Dst, $size)
    return $true
}

function Deploy-Platform([string]$BinPlat, [string]$DstDir) {
    Write-Host ">> Deploy $BinPlat -> $DstDir" -ForegroundColor Cyan
    $srcBin = Join-Path $BinariesRoot "$BinPlat\Release"
    if (-not (Test-Path $srcBin)) { throw "Build output missing: $srcBin" }

    foreach ($name in @('IODevice.dll',
            'IODevice_C_Wrapper.dll')) {
        $ok = Copy-IfExists (Join-Path $srcBin $name) (Join-Path $DstDir $name)
        if (-not $ok) { throw "Required binary missing for deploy: $name ($srcBin)" }
    }
}

Deploy-Platform 'Win64' $Plugins_x64
if ($IncludeWin32) { Deploy-Platform 'Win32' $Plugins_x86 }

# Managed wrapper is AnyCPU-compatible; deploy only to Plugins\ root.
$managed = Join-Path $BinariesRoot 'Win64\Release\IODevice_CSharp_Wrapper.dll'
if (Test-Path $managed) {
    Copy-IfExists $managed (Join-Path $PluginsRoot 'IODevice_CSharp_Wrapper.dll') | Out-Null
}

# ------------------------------------------------------------------ tests
if ($RunTests) {
    Write-Host ">> dotnet test IOStudio.Tests" -ForegroundColor Cyan
    if (-not (Test-Path $TestsProj)) { throw "Tests project missing: $TestsProj" }
    & dotnet test $TestsProj --configuration Release --nologo
    if ($LASTEXITCODE) { throw "dotnet test failed exit=$LASTEXITCODE" }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
