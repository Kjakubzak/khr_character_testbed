# Export-Goldens.ps1 — GATE 4. Re-export the fixtures, normalize their wire to snapshots (via SandboxCI.ExportGoldens),
# then diff against the committed goldens (-Check, default) or rewrite them (-Update).
# Usage:  ./Tools/ci/Export-Goldens.ps1 [-ProjectPath <p>] [-Check | -Update] [-TimeoutMinutes 60]
[CmdletBinding()]
param(
    [string]$ProjectPath,
    [switch]$Update,
    [switch]$Check,
    [int]$TimeoutMinutes = 60,
    [string]$GoldenDir,
    [string]$SnapshotDir
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
$unity = Find-UnityExe -ProjectPath $ProjectPath
if (-not $GoldenDir) { $GoldenDir = Join-Path $ProjectPath 'Tests/Golden' }
if (-not $SnapshotDir) { $SnapshotDir = Join-Path $ProjectPath 'Artifacts/snapshots' }
$logDir = Join-Path $ProjectPath 'Logs/ci'
if (-not $Update -and -not $Check) { $Check = $true }  # default to a non-destructive check
Assert-NoLock -ProjectPath $ProjectPath

# Produce fresh normalized snapshots through the editor seam (enables plugins IN CODE, exports, normalizes).
$log = Join-Path $logDir 'goldens.log'
$unityArgs = @('-batchmode', '-quit', '-nographics', '-projectPath', $ProjectPath,
    '-executeMethod', 'Samples.Editor.SandboxCI.ExportGoldens', '-logFile', $log)
Write-Host "[ci] Export-Goldens: running SandboxCI.ExportGoldens"
$code = Invoke-UnityBounded -UnityExe $unity -UnityArgs $unityArgs -LogFile $log -LogDir $logDir -RunName 'goldens' -TimeoutMinutes $TimeoutMinutes
if ($code -ne 0 -or -not (Test-CompileLogClean -LogFile $log)) {
    Write-Error "[ci] Export-Goldens: Unity export failed (exit $code; see $log)."
    exit 1
}

$snapshots = @()
if (Test-Path -LiteralPath $SnapshotDir) { $snapshots = Get-ChildItem -LiteralPath $SnapshotDir -Filter *.json -File }
if ($snapshots.Count -eq 0) { Write-Error "[ci] Export-Goldens produced no snapshots in '$SnapshotDir'."; exit 1 }

if ($Update) {
    New-Item -ItemType Directory -Force -Path $GoldenDir | Out-Null
    foreach ($s in $snapshots) { Copy-Item -LiteralPath $s.FullName -Destination (Join-Path $GoldenDir $s.Name) -Force }
    Write-Host "[ci] Export-Goldens -Update: wrote $($snapshots.Count) golden(s) to '$GoldenDir'. Review the diff in code review (the diff IS the change record)."
    exit 0
}

# -Check (default).
$committed = @()
if (Test-Path -LiteralPath $GoldenDir) { $committed = Get-ChildItem -LiteralPath $GoldenDir -Filter *.json -File }
if ($committed.Count -eq 0) {
    Write-Warning "[ci] No committed goldens in '$GoldenDir' -- SKIPPING the golden diff. Run 'Export-Goldens -Update' once and commit them."
    exit 0
}

$drift = $false
foreach ($g in $committed) {
    $snap = Join-Path $SnapshotDir $g.Name
    if (-not (Test-Path -LiteralPath $snap)) { Write-Error "[ci]   MISSING snapshot for golden '$($g.Name)' (fixture removed?)."; $drift = $true; continue }
    if ((Get-Content -LiteralPath $g.FullName -Raw) -ne (Get-Content -LiteralPath $snap -Raw)) {
        Write-Error "[ci]   DRIFT: '$($g.Name)' differs from the committed golden."
        $drift = $true
    } else { Write-Host "[ci]   OK: $($g.Name)" }
}
foreach ($s in $snapshots) {
    if (-not (Test-Path -LiteralPath (Join-Path $GoldenDir $s.Name))) { Write-Error "[ci]   NEW snapshot '$($s.Name)' has no committed golden. Run -Update."; $drift = $true }
}

if ($drift) { Write-Error "[ci] GATE 4 (goldens) FAILED -- wire drift. If intentional, run 'Export-Goldens -Update' and commit the diff."; exit 1 }
Write-Host "[ci] GATE 4 (goldens) PASS."
exit 0
