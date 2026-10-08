# Replicate NativeDispatchDeadlockTests.RunOnceAsync launch conditions and dump full status JSON.
# Key fields: pollMaxMs / pollTotalMs. UpdateCount++ happens AFTER IODeviceController.Update(),
# so if Update() blocks, pollCount will not grow.
param(
  [string]$Motion = "",
  [int]$ExitAfterMs = 4000,
  [int]$TimeoutMs = 25000
)

$dev = "O:\DevModules\IODevice\IODevice\Binaries\Win64\Debug"
$exe = Join-Path $dev "IOStudio.exe"
if (-not (Test-Path $exe)) { throw "not found: $exe" }

if (-not $Motion) {
  $Motion = Join-Path $dev "Config\Motion\simulator_platform_demo.motion"
}
Write-Host "motion = $Motion"

Get-Process -Name IOStudio -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$status = Join-Path $env:TEMP ("harness_" + [guid]::NewGuid().ToString("N") + ".json")

# Same as the test: UseShellExecute=false, CreateNoWindow=true, redirect + async drain
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.WorkingDirectory = $dev
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
foreach ($a in @("--motion", $Motion, "--play", "--native-dispatch", "--live-output",
                 "--headless", "--exit-after", "$ExitAfterMs", "--status-file", $status)) {
  $psi.Arguments += ('"' + $a + '" ')
}

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
$sw = [System.Diagnostics.Stopwatch]::StartNew()
[void]$proc.Start()

$outTask = $proc.StandardOutput.ReadToEndAsync()
$errTask = $proc.StandardError.ReadToEndAsync()

$exited = $proc.WaitForExit($TimeoutMs)
$sw.Stop()
Write-Host ("exited={0}  elapsed={1:N0}ms" -f $exited, $sw.Elapsed.TotalMilliseconds)

if (-not $exited) { $proc.Kill(); Write-Host "  -> timeout, killed" }

if (Test-Path $status) {
  $j = Get-Content $status -Raw | ConvertFrom-Json
  Write-Host "--- status ---"
  Write-Host ("phase={0}  isPlaying={1}  currentTimeMs={2}" -f $j.phase, $j.isPlaying, $j.currentTimeMs)
  Write-Host ("tickCount={0}  tickMaxEngineMs={1}  tickMaxMediaMs={2}  tickStart/Stop={3}/{4}" -f `
     $j.tickCount, $j.tickMaxEngineMs, $j.tickMaxMediaMs, $j.tickStartCount, $j.tickStopCount)
  Write-Host ("pollCount={0}  pollMaxMs={1}  pollTotalMs={2}" -f $j.pollCount, $j.pollMaxMs, $j.pollTotalMs)
  Write-Host ("sampleCount={0}  maxThreads={1}  maxPendingWork={2}  cpuDeltaMs={3}" -f `
     $j.sampleCount, $j.maxThreads, $j.maxPendingWork, $j.cpuDeltaMs)
  Write-Host ("tickError='{0}'  lastEngineError='{1}'" -f $j.tickError, $j.lastEngineError)
} else {
  Write-Host "no status file"
}
