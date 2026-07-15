# Check-PackagePin.ps1 — package-resolution preflight for the pinned UnityGLTF fork. No Unity needed.
# The project pins UnityGLTF to a personal fork by commit SHA (a single point of failure for first-open AND
# every CI job). This fails fast with a clear message if that pin is (a) internally inconsistent across
# manifest.json / packages-lock.json / README.md, or (b) unreachable / the SHA is not fetchable from the remote.
# Usage:  ./Tools/ci/Check-PackagePin.ps1 [-ProjectPath <p>] [-Package <name>] [-SkipRemote]
[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$Package = 'org.khronos.unitygltf',
    [switch]$SkipRemote
)
. "$PSScriptRoot/_Common.ps1"

$ProjectPath = Resolve-ProjectPath -ProjectPath $ProjectPath
$manifest = Join-Path $ProjectPath 'Packages/manifest.json'
$lock = Join-Path $ProjectPath 'Packages/packages-lock.json'
$readme = Join-Path $ProjectPath 'README.md'

if (-not (Test-Path -LiteralPath $manifest)) { Write-Error "[ci] manifest.json not found at '$manifest'."; exit 1 }

# Pull the pinned git URL for the package out of manifest.json (the source of truth) without a JSON parser,
# so this stays dependency-free and matches Check-DocsVersion.ps1's plain-text approach.
$escaped = [regex]::Escape($Package)
$pinRe = "`"$escaped`"\s*:\s*`"([^`"]+)`""
$m = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), $pinRe)
if (-not $m.Success) { Write-Error "[ci] '$Package' dependency not found in manifest.json."; exit 1 }
$url = $m.Groups[1].Value
if ($url -notmatch '^https?://.*#[0-9a-fA-F]{7,40}$') {
    Write-Error "[ci] '$Package' is not pinned to a git URL with a commit SHA ('#<sha>'): '$url'."
    exit 1
}
$repoUrl = ($url -split '#', 2)[0]
$pinSha = ($url -split '#', 2)[1]
Write-Host "[ci] package pin: $Package -> $repoUrl @ $pinSha"

$bad = $false

# (1) Internal consistency: packages-lock.json must record the same URL + resolved SHA (drift guard).
if (Test-Path -LiteralPath $lock) {
    $lockText = Get-Content -LiteralPath $lock -Raw
    if ($lockText -notmatch [regex]::Escape($url)) {
        Write-Warning "[ci]   packages-lock.json does not record the manifest pin URL '$url'."; $bad = $true
    }
    if ($lockText -notmatch [regex]::Escape($pinSha)) {
        Write-Warning "[ci]   packages-lock.json does not record the pinned SHA '$pinSha'."; $bad = $true
    }
} else { Write-Warning "[ci]   packages-lock.json not found — cannot confirm the resolved SHA."; $bad = $true }

# (2) Doc consistency: README.md must document the same owner/repo + SHA, so the two can't silently re-drift
# (the exact bug this preflight guards against). README is prose, so match the repo URL and the SHA separately.
if (Test-Path -LiteralPath $readme) {
    $readmeText = Get-Content -LiteralPath $readme -Raw
    if ($readmeText -notmatch [regex]::Escape($repoUrl)) {
        Write-Warning "[ci]   README.md does not mention the pinned repo URL '$repoUrl'."; $bad = $true
    }
    if ($readmeText -notmatch [regex]::Escape($pinSha)) {
        Write-Warning "[ci]   README.md does not document the pinned SHA '$pinSha'."; $bad = $true
    }
}

if ($bad) {
    Write-Error "[ci] PACKAGE PIN LINT FAILED — the pin in manifest.json disagrees with packages-lock.json and/or README.md. Re-pin all three to the same URL#SHA."
    exit 1
}

# (3) Reachability: the exact resolution UPM performs on first open / in CI. Catches a deleted / renamed /
# now-private fork (URL unreachable) and a force-push that removed the pinned commit (SHA not fetchable).
if ($SkipRemote) {
    Write-Host "[ci] Package pin lint PASS (offline) — manifest/lock/README agree on $repoUrl @ $pinSha (remote reachability skipped)."
    exit 0
}

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { Write-Error "[ci] git not found on PATH — needed to preflight the package pin (use -SkipRemote to check consistency only)."; exit 1 }

Write-Host "[ci] resolving pinned remote (git ls-remote) ..."
& git ls-remote --heads --tags $repoUrl *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "[ci] PACKAGE PIN PREFLIGHT FAILED — cannot reach '$repoUrl'. The pinned UnityGLTF fork may be deleted, renamed, or now private. First-open package resolution and CI will fail. Fix the pin in Packages/manifest.json (mirror the fork under an org or vendor a controlled copy)."
    exit 1
}

Write-Host "[ci] verifying the pinned SHA is fetchable ..."
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("pincheck-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
    & git -C $tmp init -q
    & git -C $tmp -c protocol.version=2 fetch -q --depth=1 $repoUrl $pinSha *> $null
    $fetchOk = ($LASTEXITCODE -eq 0)
} finally { Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue }

if (-not $fetchOk) {
    Write-Error "[ci] PACKAGE PIN PREFLIGHT FAILED — '$repoUrl' is reachable but the pinned commit '$pinSha' is not fetchable (likely force-pushed away). Re-pin Packages/manifest.json to a commit that exists on the remote."
    exit 1
}

Write-Host "[ci] Package pin preflight PASS — $repoUrl @ $pinSha is reachable, fetchable, and consistent across manifest/lock/README."
exit 0
