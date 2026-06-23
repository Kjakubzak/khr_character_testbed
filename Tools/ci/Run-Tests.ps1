# Run-Tests.ps1 — GATE 1 (compile) + GATE 2 (tests green + min-test-count floor).
# Compile-only FIRST (a -runTests against uncompilable code hangs forever), then -runTests (no -quit; it self-quits).
# Usage:  ./Tools/ci/Run-Tests.ps1 [-ProjectPath <p>] [-Platform PlayMode|EditMode|Both] [-Filter <f>] [-MinTests 6] [-TimeoutMinutes 60]
[CmdletBinding()]
param(
    [string]$ProjectPath,
    [ValidateSet('PlayMode', 'EditMode', 'Both')][string]$Platform = 'PlayMode',
    [string]$Filter,
    [int]$TimeoutMinutes = 60,
    [int]$MinTests = 6,
    [string]$ResultsDir
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
$unity = Find-UnityExe -ProjectPath $ProjectPath
if (-not $ResultsDir) { $ResultsDir = Join-Path $ProjectPath 'Logs/ci' }
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
Assert-NoLock -ProjectPath $ProjectPath

# GATE 1 — compile-only (fails fast instead of hanging).
Write-Host "[ci] Run-Tests: GATE 1 (compile-only)"
if (-not (Invoke-CompileOnly -UnityExe $unity -ProjectPath $ProjectPath -LogDir $ResultsDir -RunName 'compile' -TimeoutMinutes $TimeoutMinutes)) {
    Write-Error "[ci] GATE 1 (compile) FAILED."
    exit 1
}

# GATE 2 — tests. Run each requested platform (no -quit; -runTests self-quits).
$platforms = if ($Platform -eq 'Both') { @('EditMode', 'PlayMode') } else { @($Platform) }
$totalTests = 0
$anyBad = $false

foreach ($p in $platforms) {
    $tag = $p.ToLower()
    $results = Join-Path $ResultsDir "results-$tag.xml"
    $log = Join-Path $ResultsDir "run-$tag.log"
    $unityArgs = @('-batchmode', '-nographics', '-projectPath', $ProjectPath, '-runTests',
        '-testPlatform', $p, '-testResults', $results, '-logFile', $log)
    if ($Filter) { $unityArgs += @('-testFilter', $Filter) }

    Write-Host "[ci] Run-Tests: GATE 2 ($p)"
    $code = Invoke-UnityBounded -UnityExe $unity -UnityArgs $unityArgs -LogFile $log -LogDir $ResultsDir `
        -RunName "run-$tag" -ResultsFile $results -TimeoutMinutes $TimeoutMinutes
    if ($code -eq 124) { Write-Error "[ci] GATE 2 ($p) TIMEOUT — a hang, not a skip."; exit 1 }
    if (-not (Test-Path -LiteralPath $results)) {
        Write-Error "[ci] GATE 2 ($p) FAILED — no results XML (run crashed/hung), which is a failure, not a skip."
        exit 1
    }

    [xml]$xml = Get-Content -LiteralPath $results
    $run = $xml.'test-run'
    $t = [int]$run.total; $f = [int]$run.failed; $inc = [int]$run.inconclusive; $sk = [int]$run.skipped
    Write-Host "[ci]   ${p}: total=$t passed=$($run.passed) failed=$f inconclusive=$inc skipped=$sk"
    if ($f -ne 0 -or $inc -ne 0 -or $sk -ne 0) { $anyBad = $true }
    $totalTests += $t
}

if ($anyBad) { Write-Error "[ci] GATE 2 (tests) FAILED — failed/inconclusive/skipped must all be 0."; exit 1 }
if ($totalTests -lt $MinTests) {
    Write-Error "[ci] GATE 2 (floor) FAILED — ran $totalTests test(s), need >= $MinTests. A near-zero count means the dependency resolved 'hollow' (no KHR plugin/tests)."
    exit 1
}
Write-Host "[ci] GATES 1 + 2 PASS — $totalTests test(s); 0 failed/inconclusive/skipped; floor >= $MinTests."
exit 0
