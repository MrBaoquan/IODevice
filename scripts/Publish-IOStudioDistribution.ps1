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
    [switch]$KeepStaging
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
if (-not (Test-Path $Csproj)) { throw "IOStudio.csproj not found: $Csproj" }

if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'publish' }
$ArtifactName = "IOStudio-$Version-win-x64"
$Staging = Join-Path $OutputRoot $ArtifactName
$ZipPath = Join-Path $OutputRoot "$ArtifactName.zip"

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
Write-Step "Native payload source (C++):  $NativeDir"
Write-Step "Native payload source (IOUI): $IouiDir"
foreach ($required in @('IODevice.dll', 'IODevice_C_Wrapper.dll')) {
    if (-not (Test-Path (Join-Path $NativeDir $required))) {
        throw "Native payload missing: $(Join-Path $NativeDir $required). Build the native solution first (or pass -p:NativeOutputDir)."
    }
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
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

# Write entries explicitly with '/' separators. ZipFile.CreateFromDirectory on
# Windows PowerShell emits '\' separators, which is non-standard and can break
# the distribution service / extractors.
$stageFull = (Resolve-Path $Staging).Path.TrimEnd('\')
$prefixLen = $stageFull.Length + 1
$archive = [System.IO.Compression.ZipFile]::Open(
    $ZipPath,
    [System.IO.Compression.ZipArchiveMode]::Create
)
try {
    foreach ($file in Get-ChildItem -Path $Staging -Recurse -File) {
        $entryName = $file.FullName.Substring($prefixLen).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file.FullName,
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        )
    }
} finally {
    $archive.Dispose()
}

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

if (-not $KeepStaging) { Remove-Item $Staging -Recurse -Force }

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
