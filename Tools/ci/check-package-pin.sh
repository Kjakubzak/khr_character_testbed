#!/usr/bin/env bash
# check-package-pin.sh - package-resolution preflight for the pinned UnityGLTF fork. No Unity needed.
# The project pins UnityGLTF to a personal fork by commit SHA (a single point of failure for first-open AND
# every CI job). This fails fast with a clear message if that pin is (a) internally inconsistent across
# manifest.json / packages-lock.json / README.md, or (b) unreachable / the SHA is not fetchable from the remote.
# Usage:  ./Tools/ci/check-package-pin.sh [--project <p>] [--package <name>] [--skip-remote]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""
PACKAGE="org.khronos.unitygltf"
SKIP_REMOTE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    --package) PACKAGE="$2"; shift 2;;
    --skip-remote) SKIP_REMOTE=1; shift;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done
PROJECT="$(resolve_project_path "$PROJECT")"
MANIFEST="$PROJECT/Packages/manifest.json"
LOCK="$PROJECT/Packages/packages-lock.json"
README="$PROJECT/README.md"
[[ -f "$MANIFEST" ]] || { echo "[ci] manifest.json not found at $MANIFEST" >&2; exit 1; }

# Pull the pinned git URL for the package out of manifest.json (the source of truth), no JSON parser needed.
PACKAGE_RE="${PACKAGE//./\\.}"  # escape dots so the grep -E pattern below matches the package name literally
URL="$(grep -oE "\"${PACKAGE_RE}\"[[:space:]]*:[[:space:]]*\"[^\"]+\"" "$MANIFEST" | head -n1 | sed -E 's/.*:[[:space:]]*"([^"]+)"/\1/' | tr -d '\r')"
if [[ -z "$URL" ]]; then echo "[ci] '$PACKAGE' dependency not found in manifest.json" >&2; exit 1; fi
if [[ ! "$URL" =~ ^https?://.*#[0-9a-fA-F]{7,40}$ ]]; then
  echo "[ci] '$PACKAGE' is not pinned to a git URL with a commit SHA ('#<sha>'): '$URL'" >&2; exit 1
fi
REPO_URL="${URL%%#*}"
PIN_SHA="${URL#*#}"
echo "[ci] package pin: $PACKAGE -> $REPO_URL @ $PIN_SHA"

bad=0

# (1) Internal consistency: packages-lock.json must record the same URL + resolved SHA (drift guard).
if [[ -f "$LOCK" ]]; then
  grep -qF "$URL" "$LOCK"     || { echo "[ci]   packages-lock.json does not record the manifest pin URL '$URL'." >&2; bad=1; }
  grep -qF "$PIN_SHA" "$LOCK" || { echo "[ci]   packages-lock.json does not record the pinned SHA '$PIN_SHA'." >&2; bad=1; }
else
  echo "[ci]   packages-lock.json not found - cannot confirm the resolved SHA." >&2; bad=1
fi

# (2) Doc consistency: README.md must document the same owner/repo + SHA, so the two can't silently re-drift.
if [[ -f "$README" ]]; then
  grep -qF "$REPO_URL" "$README" || { echo "[ci]   README.md does not mention the pinned repo URL '$REPO_URL'." >&2; bad=1; }
  grep -qF "$PIN_SHA" "$README"  || { echo "[ci]   README.md does not document the pinned SHA '$PIN_SHA'." >&2; bad=1; }
fi

if [[ $bad -ne 0 ]]; then
  echo "[ci] PACKAGE PIN LINT FAILED - the pin in manifest.json disagrees with packages-lock.json and/or README.md. Re-pin all three to the same URL#SHA." >&2
  exit 1
fi

# (3) Reachability: the exact resolution UPM performs on first open / in CI. Catches a deleted / renamed /
# now-private fork (URL unreachable) and a force-push that removed the pinned commit (SHA not fetchable).
if [[ $SKIP_REMOTE -ne 0 ]]; then
  echo "[ci] Package pin lint PASS (offline) - manifest/lock/README agree on $REPO_URL @ $PIN_SHA (remote reachability skipped)."
  exit 0
fi
command -v git >/dev/null 2>&1 || { echo "[ci] git not found on PATH - needed to preflight the package pin (use --skip-remote to check consistency only)." >&2; exit 1; }

echo "[ci] resolving pinned remote (git ls-remote) ..."
if ! git ls-remote --heads --tags "$REPO_URL" >/dev/null 2>&1; then
  echo "[ci] PACKAGE PIN PREFLIGHT FAILED - cannot reach '$REPO_URL'. The pinned UnityGLTF fork may be deleted, renamed, or now private. First-open package resolution and CI will fail. Fix the pin in Packages/manifest.json (mirror the fork under an org or vendor a controlled copy)." >&2
  exit 1
fi

echo "[ci] verifying the pinned SHA is fetchable ..."
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
git -C "$TMP" init -q
if ! git -C "$TMP" -c protocol.version=2 fetch -q --depth=1 "$REPO_URL" "$PIN_SHA" >/dev/null 2>&1; then
  echo "[ci] PACKAGE PIN PREFLIGHT FAILED - '$REPO_URL' is reachable but the pinned commit '$PIN_SHA' is not fetchable (likely force-pushed away). Re-pin Packages/manifest.json to a commit that exists on the remote." >&2
  exit 1
fi

echo "[ci] Package pin preflight PASS - $REPO_URL @ $PIN_SHA is reachable, fetchable, and consistent across manifest/lock/README."
