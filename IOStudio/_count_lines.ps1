Set-Location "o:\DevModules\IODevice\IODevice\IOStudio"

function CountNonBlank($file) {
    @(Get-Content $file | Where-Object { $_.Trim() -ne '' }).Count
}

function CountDir($pattern) {
    $files = @(Get-ChildItem $pattern -File -ErrorAction SilentlyContinue)
    $total = 0
    foreach ($f in $files) {
        $c = CountNonBlank $f.FullName
        Write-Output "  $($f.Name): $c"
        $total += $c
    }
    Write-Output "  >> $($files.Count) files, $total non-blank lines"
    return @{ Count = $files.Count; Lines = $total }
}

Write-Output "========================================="
Write-Output "1. FILE COUNTS AND LINE COUNTS BY LAYER"
Write-Output "========================================="

Write-Output ""
Write-Output "--- Models/Motion/*.cs ---"
CountDir "Models\Motion\*.cs" | Out-Null
$mm = @(Get-ChildItem "Models\Motion\*.cs" -File)
$mmT = 0; $mm | ForEach-Object { $mmT += CountNonBlank $_.FullName }
$mm | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($mm.Count) files, $mmT non-blank lines"

Write-Output ""
Write-Output "--- Services/*.cs (top-level) ---"
$st = @(Get-ChildItem "Services\*.cs" -File)
$stT = 0; $st | ForEach-Object { $stT += CountNonBlank $_.FullName }
$st | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($st.Count) files, $stT non-blank lines"

Write-Output ""
Write-Output "--- Services/Motion/*.cs ---"
$smf = @(Get-ChildItem "Services\Motion\*.cs" -File)
$smT = 0; $smf | ForEach-Object { $smT += CountNonBlank $_.FullName }
$smf | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($smf.Count) files, $smT non-blank lines"

Write-Output ""
Write-Output "--- Services/Senders/*.cs ---"
$ss = @(Get-ChildItem "Services\Senders\*.cs" -File)
$ssT = 0; $ss | ForEach-Object { $ssT += CountNonBlank $_.FullName }
$ss | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($ss.Count) files, $ssT non-blank lines"

Write-Output ""
Write-Output "--- Services COMBINED ---"
Write-Output "  >> $($st.Count + $smf.Count + $ss.Count) files, $($stT + $smT + $ssT) non-blank lines"

Write-Output ""
Write-Output "--- ViewModels/Timeline/*.cs ---"
$vm = @(Get-ChildItem "ViewModels\Timeline\*.cs" -File)
$vmT = 0; $vm | ForEach-Object { $vmT += CountNonBlank $_.FullName }
$vm | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($vm.Count) files, $vmT non-blank lines"

Write-Output ""
Write-Output "--- Views/Timeline/*.cs + *.axaml ---"
$vcs = @(Get-ChildItem "Views\Timeline\*.cs" -File -ErrorAction SilentlyContinue)
$vax = @(Get-ChildItem "Views\Timeline\*.axaml" -File -ErrorAction SilentlyContinue)
$vall = $vcs + $vax
$vT = 0; $vall | ForEach-Object { $vT += CountNonBlank $_.FullName }
$vall | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($vall.Count) files, $vT non-blank lines"

Write-Output ""
Write-Output "--- Controls/Timeline/*.cs + Behaviors/*.cs ---"
$ctcs = @(Get-ChildItem "Controls\Timeline\*.cs" -File -ErrorAction SilentlyContinue)
$ctbh = @(Get-ChildItem "Controls\Timeline\Behaviors\*.cs" -File -ErrorAction SilentlyContinue)
$ctall = $ctcs + $ctbh
$ctT = 0; $ctall | ForEach-Object { $ctT += CountNonBlank $_.FullName }
$ctall | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($ctall.Count) files, $ctT non-blank lines"

Write-Output ""
Write-Output "--- Converters/*.cs ---"
$cv = @(Get-ChildItem "Converters\*.cs" -File)
$cvT = 0; $cv | ForEach-Object { $cvT += CountNonBlank $_.FullName }
$cv | ForEach-Object { Write-Output "  $($_.Name): $(CountNonBlank $_.FullName)" }
Write-Output "  >> $($cv.Count) files, $cvT non-blank lines"

Write-Output ""
Write-Output "========================================="
Write-Output "2. KEY FILE LINE COUNTS"
Write-Output "========================================="
$keyFiles = @(
    "ViewModels\Timeline\TimelineEditorViewModel.cs",
    "Views\Timeline\TimelineEditorWindow.axaml.cs",
    "Controls\Timeline\KeyframePropertyPanel.axaml.cs",
    "Controls\Timeline\VideoPreviewControl.axaml.cs",
    "Services\ServiceLocator.cs"
)
foreach ($kf in $keyFiles) {
    if (Test-Path $kf) {
        $total = (Get-Content $kf).Count
        $nonblank = CountNonBlank $kf
        Write-Output "  $kf : $total total lines, $nonblank non-blank"
    } else {
        Write-Output "  $kf : NOT FOUND"
    }
}

Write-Output ""
Write-Output "========================================="
Write-Output "3. INTERFACE FILES (I*.cs)"
Write-Output "========================================="
$iSvc = @(Get-ChildItem "Services\I*.cs" -File -ErrorAction SilentlyContinue)
$iMotion = @(Get-ChildItem "Services\Motion\I*.cs" -File -ErrorAction SilentlyContinue)
$iSenders = @(Get-ChildItem "Services\Senders\I*.cs" -File -ErrorAction SilentlyContinue)
$iAll = $iSvc + $iMotion + $iSenders
Write-Output "  Total interfaces: $($iAll.Count)"
$iAll | ForEach-Object { Write-Output "  $($_.FullName.Replace('o:\DevModules\IODevice\IODevice\IOStudio\',''))" }

Write-Output ""
Write-Output "========================================="
Write-Output "4. PHASE 4.13-4.15 NEW FILES"
Write-Output "========================================="
Write-Output "--- Interfaces (I*.cs in Services/) ---"
$iSvc | ForEach-Object { Write-Output "  $($_.FullName.Replace('o:\DevModules\IODevice\IODevice\IOStudio\',''))" }
$iMotion | ForEach-Object { Write-Output "  $($_.FullName.Replace('o:\DevModules\IODevice\IODevice\IOStudio\',''))" }
$iSenders | ForEach-Object { Write-Output "  $($_.FullName.Replace('o:\DevModules\IODevice\IODevice\IOStudio\',''))" }

Write-Output "--- Behaviors ---"
$ctbh | ForEach-Object { Write-Output "  $($_.FullName.Replace('o:\DevModules\IODevice\IODevice\IOStudio\',''))" }

Write-Output "--- Converters (TimeFormatConverter) ---"
if (Test-Path "Converters\TimeFormatConverter.cs") { Write-Output "  Converters\TimeFormatConverter.cs: EXISTS" } else { Write-Output "  NOT FOUND" }

Write-Output "--- New ViewModels ---"
$newVMs = @("ViewModels\Timeline\KeyframePropertyViewModel.cs", "ViewModels\Timeline\AddTrackDialogViewModel.cs")
foreach ($nv in $newVMs) {
    if (Test-Path $nv) { Write-Output "  $nv : EXISTS ($(CountNonBlank $nv) lines)" } else { Write-Output "  $nv : NOT FOUND" }
}

Write-Output "--- ServiceLocator ---"
if (Test-Path "Services\ServiceLocator.cs") { Write-Output "  Services\ServiceLocator.cs: EXISTS ($(CountNonBlank 'Services\ServiceLocator.cs') lines)" } else { Write-Output "  NOT FOUND" }

Write-Output ""
Write-Output "========================================="
Write-Output "5. IOStudio.Tests PROJECT"
Write-Output "========================================="
$testDir = "..\IOStudio.Tests"
if (Test-Path $testDir) {
    $testFiles = @(Get-ChildItem "$testDir\*.cs" -Recurse -File | Where-Object { $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" })
    Write-Output "  Test files (excl obj/bin): $($testFiles.Count)"
    $testFiles | ForEach-Object { 
        $c = CountNonBlank $_.FullName
        Write-Output "  $($_.FullName.Replace('o:\DevModules\IODevice\IODevice\','')): $c lines"
    }
} else {
    Write-Output "  IOStudio.Tests directory NOT FOUND"
}

Write-Output ""
Write-Output "========================================="
Write-Output "GRAND TOTAL SUMMARY"
Write-Output "========================================="
Write-Output "  Models/Motion:     $($mm.Count) files, $mmT lines"
Write-Output "  Services (all):    $($st.Count + $smf.Count + $ss.Count) files, $($stT + $smT + $ssT) lines"
Write-Output "  ViewModels/TL:     $($vm.Count) files, $vmT lines"
Write-Output "  Views/TL:          $($vall.Count) files, $vT lines"
Write-Output "  Controls/TL:       $($ctall.Count) files, $ctT lines"
Write-Output "  Converters:        $($cv.Count) files, $cvT lines"
$grandFiles = $mm.Count + $st.Count + $smf.Count + $ss.Count + $vm.Count + $vall.Count + $ctall.Count + $cv.Count
$grandLines = $mmT + $stT + $smT + $ssT + $vmT + $vT + $ctT + $cvT
Write-Output "  -----------------------------------------"
Write-Output "  GRAND TOTAL:       $grandFiles files, $grandLines lines"
