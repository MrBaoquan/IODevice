param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Target
)

# Enumerates the public API surface of a managed assembly so two builds can be diffed.
function Get-ApiSurface([string]$path) {
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($path))
    $flags = [System.Reflection.BindingFlags]::Public -bor `
        [System.Reflection.BindingFlags]::Instance -bor `
        [System.Reflection.BindingFlags]::Static -bor `
        [System.Reflection.BindingFlags]::DeclaredOnly
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($t in $asm.GetTypes()) {
        if (-not ($t.IsPublic -or $t.IsNestedPublic)) { continue }
        foreach ($m in $t.GetMembers($flags)) {
            $items.Add(($t.FullName + " :: " + $m.ToString()))
        }
    }
    return $items
}

function Get-DllImportModules([string]$path) {
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($path))
    $mods = New-Object System.Collections.Generic.List[string]
    foreach ($t in $asm.GetTypes()) {
        $flags = [System.Reflection.BindingFlags]::Static -bor `
            [System.Reflection.BindingFlags]::Public -bor `
            [System.Reflection.BindingFlags]::NonPublic
        foreach ($m in $t.GetMethods($flags)) {
            $a = $m.GetCustomAttributes([System.Runtime.InteropServices.DllImportAttribute], $false)
            if ($a.Count -gt 0) { $mods.Add($a[0].Value) }
        }
    }
    return ($mods | Sort-Object -Unique)
}

$modsB = Get-DllImportModules $Baseline
$modsT = Get-DllImportModules $Target
Write-Host "DllImport module  baseline : $($modsB -join ', ')"
Write-Host "DllImport module  target   : $($modsT -join ', ')"

$apiB = Get-ApiSurface $Baseline
$apiT = Get-ApiSurface $Target
Write-Host "public members    baseline : $($apiB.Count)"
Write-Host "public members    target   : $($apiT.Count)"

$diff = Compare-Object $apiB $apiT
if ($diff) {
    Write-Host "RESULT: PUBLIC API DIFFERS"
    $diff | Select-Object -First 40 | ForEach-Object {
        $tag = if ($_.SideIndicator -eq '<=') { 'ONLY-BASELINE' } else { 'ONLY-TARGET  ' }
        Write-Host "  $tag $($_.InputObject)"
    }
    exit 1
}

Write-Host "RESULT: PUBLIC API IDENTICAL"
