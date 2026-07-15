# Check-DocsVersion.ps1 — doc-drift lint. Fails when any Unity-editor-version string in the tracked
# docs/workflows differs from ProjectSettings/ProjectVersion.txt (the single source of truth). No Unity needed.
# Usage:  ./Tools/ci/Check-DocsVersion.ps1 [-ProjectPath <p>] [-Files <f1,f2,...>]
[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string[]]$Files
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
$expected = Get-ProjectUnityVersion -ProjectPath $ProjectPath

if (-not $Files -or $Files.Count -eq 0) {
    $Files = @(
        (Join-Path $ProjectPath 'README.md'),
        (Join-Path $ProjectPath 'docs/ci.md'),
        (Join-Path $ProjectPath '.github/workflows/ci.yml'),
        (Join-Path $ProjectPath '.github/workflows/ci-nightly.yml')
    )
}

# Unity editor versions look like 2022.3.76f1 / 6000.0.76f1: a 4-digit stream, dotted, with an f/a/b tag.
# (URP "14.0.11", package "1.0.0" and commit SHAs do not match, so they are not false-positives.)
$verRe = '[0-9]{4}\.[0-9]+\.[0-9]+[fab][0-9]+'
$bad = $false
foreach ($f in $Files) {
    if (-not (Test-Path -LiteralPath $f)) { continue }
    $n = 0
    foreach ($line in Get-Content -LiteralPath $f) {
        $n++
        foreach ($m in [regex]::Matches($line, $verRe)) {
            if ($m.Value -ne $expected) {
                Write-Warning "[ci]   ${f}:${n}  found '$($m.Value)', expected '$expected'"
                $bad = $true
            }
        }
    }
}

if ($bad) {
    Write-Error "[ci] DOC VERSION LINT FAILED — Unity version string(s) disagree with ProjectSettings/ProjectVersion.txt ('$expected'). Update the docs/workflows above (or ProjectVersion.txt if the target really changed)."
    exit 1
}
Write-Host "[ci] Doc version lint PASS — all Unity version strings match '$expected'."
exit 0
