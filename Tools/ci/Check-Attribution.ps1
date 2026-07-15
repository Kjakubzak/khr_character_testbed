# Check-Attribution.ps1 — license-compliance lint. Fails when a committed Assets/SampleAssets/**/*.glb has no
# matching row in Assets/SampleAssets/ATTRIBUTION.md. No Unity needed. Guards the file's own rule (record
# provenance for EVERY committed asset) in a license-sensitive public repo (the hero + variants are non-commercial).
# Usage:  ./Tools/ci/Check-Attribution.ps1 [-ProjectPath <p>]
[CmdletBinding()]
param(
    [string]$ProjectPath
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
$assetsRel = 'Assets/SampleAssets'
$attribution = Join-Path $ProjectPath "$assetsRel/ATTRIBUTION.md"
if (-not (Test-Path -LiteralPath $attribution)) { Write-Error "[ci] ATTRIBUTION.md not found at '$attribution'."; exit 1 }
$attrText = Get-Content -LiteralPath $attribution -Raw

# Enumerate the *committed* (git-tracked) GLBs, so untracked local experiments don't trip the gate. LFS pointers
# are still tracked, so `git ls-files` lists them even on a no-LFS checkout (we only need the paths, not bytes).
Push-Location $ProjectPath
try { $tracked = & git ls-files -- "$assetsRel/*.glb" 2>$null }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0 -or -not $tracked) {
    # Fallback for a non-git context: enumerate on disk.
    $tracked = Get-ChildItem -Recurse -LiteralPath (Join-Path $ProjectPath $assetsRel) -Filter *.glb -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName.Substring($ProjectPath.Length + 1).Replace('\', '/') }
}

$missing = @()
$count = 0
foreach ($rel in $tracked) {
    if ([string]::IsNullOrWhiteSpace($rel)) { continue }
    $count++
    $base = [System.IO.Path]::GetFileName($rel)
    # A row cites the asset as `<name>.glb` or `<subdir>/<name>.glb`, always ending the filename with a backtick.
    # Matching "<basename>`" is robust to the subdir-prefix inconsistency and to dot-vs-dash near-duplicate names
    # (khr-character-example.glb` won't match khr-character-example-always.glb`).
    if (-not ($attrText.Contains('`' + $base + '`') -or $attrText.Contains('/' + $base + '`'))) { $missing += $rel }
}

if ($missing.Count -gt 0) {
    foreach ($m in $missing) { Write-Warning "[ci]   no ATTRIBUTION.md row for committed asset '$m'" }
    Write-Error "[ci] ATTRIBUTION CHECK FAILED — $($missing.Count) committed .glb file(s) have no provenance row in $assetsRel/ATTRIBUTION.md. Add a row (source + SPDX license) for each before committing (see the file's Rules section)."
    exit 1
}
Write-Host "[ci] Attribution check PASS — all $count committed .glb file(s) under $assetsRel have an ATTRIBUTION.md row."
exit 0
