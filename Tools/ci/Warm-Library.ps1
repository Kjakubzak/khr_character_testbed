# Warm-Library.ps1 — cold-prime the Unity Library/ (resolve packages + import + compile) so later runs are fast.
# Usage:  ./Tools/ci/Warm-Library.ps1 [-ProjectPath <path>] [-TimeoutMinutes 60]
[CmdletBinding()]
param(
    [string]$ProjectPath,
    [int]$TimeoutMinutes = 60
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
$unity = Find-UnityExe -ProjectPath $ProjectPath
$logDir = Join-Path $ProjectPath 'Logs/ci'
Assert-NoLock -ProjectPath $ProjectPath

Write-Host "[ci] Warm-Library: priming Library/ for '$ProjectPath' using '$unity'"
$ok = Invoke-CompileOnly -UnityExe $unity -ProjectPath $ProjectPath -LogDir $logDir -RunName 'warm' -TimeoutMinutes $TimeoutMinutes
if (-not $ok) { Write-Error "[ci] Warm-Library FAILED (compile errors or timeout)."; exit 1 }
Write-Host "[ci] Warm-Library OK."
exit 0
