# Validate-Glb.ps1 - GATE 3. Runs the Khronos glTF-Validator over every exported GLB and gates on numErrors==0.
# Standalone (no Unity). If the validator binary is absent, SKIPS non-fatally with install instructions UNLESS
# -Required is set, in which case a missing validator (or an unparseable report) is a HARD ERROR (CI uses -Required
# so a silent coverage loss can't sneak a bad wire through).
# Usage:  ./Tools/ci/Validate-Glb.ps1 [-ProjectPath <p>] [-InputDir <dir>] [-ReportDir <dir>] [-Required] [-FailOnWarning]
[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$InputDir,
    [string]$ReportDir,
    [switch]$Required,
    [switch]$FailOnWarning
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
if (-not $InputDir) { $InputDir = Join-Path $ProjectPath 'Assets/SampleAssets/Synthetic' }
if (-not $ReportDir) { $ReportDir = Join-Path $ProjectPath 'Artifacts/reports' }

# Locate the validator: $env:GLTF_VALIDATOR, else 'gltf_validator' on PATH.
$validator = $null
if ($env:GLTF_VALIDATOR -and (Test-Path -LiteralPath $env:GLTF_VALIDATOR)) { $validator = $env:GLTF_VALIDATOR }
if (-not $validator) {
    $cmd = Get-Command 'gltf_validator' -ErrorAction SilentlyContinue
    if ($cmd) { $validator = $cmd.Source }
}
if (-not $validator) {
    if ($Required) {
        Write-Error "[ci] GATE 3 (glTF-Validator) FAILED -- validator not found and -Required was set. Install:  npm i -g gltf-validator   (or set `$env:GLTF_VALIDATOR)."
        exit 1
    }
    Write-Warning "[ci] glTF-Validator not found -- SKIPPING GATE 3 (non-fatal)."
    Write-Warning "[ci] Install:  npm i -g gltf-validator   (or download 'gltf_validator' from KhronosGroup/glTF-Validator releases and set `$env:GLTF_VALIDATOR)."
    exit 0
}

if (-not (Test-Path -LiteralPath $InputDir)) { Write-Warning "[ci] Input dir '$InputDir' not found -- nothing to validate."; exit 0 }
$files = Get-ChildItem -LiteralPath $InputDir -Recurse -Include *.glb, *.gltf -File
if ($files.Count -eq 0) { Write-Warning "[ci] No GLB/glTF under '$InputDir' -- nothing to validate."; exit 0 }

New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
$totalErrors = 0; $totalWarnings = 0; $parsed = 0

foreach ($f in $files) {
    # The Khronos CLI writes a JSON report into the -o directory. Naming varies by version, so glob for it.
    & $validator -a -o $ReportDir $f.FullName 2>$null | Out-Null
    $report = Get-ChildItem -LiteralPath $ReportDir -Filter "*$($f.BaseName)*.json" -File -ErrorAction SilentlyContinue | Select-Object -First 1

    $json = $null
    if ($report) {
        try { $json = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json } catch { $json = $null }
    }
    if (-not $json) {
        # Fallback: some builds print the JSON report to stdout instead of a file.
        try { $json = (& $validator -a -p $f.FullName 2>$null | Out-String) | ConvertFrom-Json } catch { $json = $null }
    }
    if (-not $json -or -not $json.issues) {
        if ($Required) { Write-Error "[ci]   $($f.Name): could not parse a validator report (-Required); failing GATE 3."; exit 1 }
        Write-Warning "[ci]   $($f.Name): could not parse a validator report (skipping this file). Check your gltf_validator version/flags."
        continue
    }

    $errs = [int]$json.issues.numErrors; $warns = [int]$json.issues.numWarnings
    $totalErrors += $errs; $totalWarnings += $warns; $parsed++
    Write-Host "[ci]   $($f.Name): errors=$errs warnings=$warns"
}

Write-Host "[ci] glTF-Validator: parsed $parsed/$($files.Count) file(s); errors=$totalErrors warnings=$totalWarnings (reports in $ReportDir)"
if ($totalErrors -gt 0) { Write-Error "[ci] GATE 3 (glTF-Validator) FAILED -- $totalErrors error(s)."; exit 1 }
if ($FailOnWarning -and $totalWarnings -gt 0) { Write-Error "[ci] GATE 3 FAILED -- $totalWarnings warning(s) with -FailOnWarning."; exit 1 }
Write-Host "[ci] GATE 3 (glTF-Validator) PASS."
exit 0
