#!/usr/bin/env bash
# check-attribution.sh - license-compliance lint. Fails when a committed Assets/SampleAssets/**/*.glb has no
# matching row in Assets/SampleAssets/ATTRIBUTION.md. No Unity needed. Guards the file's own rule (record
# provenance for EVERY committed asset) in a license-sensitive public repo (the hero + variants are non-commercial).
# Usage:  ./Tools/ci/check-attribution.sh [--project <p>]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done
PROJECT="$(resolve_project_path "$PROJECT")"
ASSETS_REL="Assets/SampleAssets"
ATTRIBUTION="$PROJECT/$ASSETS_REL/ATTRIBUTION.md"
[[ -f "$ATTRIBUTION" ]] || { echo "[ci] ATTRIBUTION.md not found at $ATTRIBUTION" >&2; exit 1; }

# Enumerate the *committed* (git-tracked) GLBs, so untracked local experiments don't trip the gate. LFS pointers
# are still tracked, so `git ls-files` lists them even on a no-LFS checkout (we only need the paths, not bytes).
mapfile -t TRACKED < <(cd "$PROJECT" && git ls-files -- "$ASSETS_REL/*.glb" 2>/dev/null || true)
if [[ ${#TRACKED[@]} -eq 0 ]]; then
  # Fallback for a non-git context: enumerate on disk.
  mapfile -t TRACKED < <(cd "$PROJECT" && find "$ASSETS_REL" -name '*.glb' 2>/dev/null || true)
fi

missing=0
count=0
for rel in "${TRACKED[@]}"; do
  [[ -z "$rel" ]] && continue
  count=$(( count + 1 ))
  base="$(basename "$rel")"
  # A row cites the asset as `<name>.glb` or `<subdir>/<name>.glb`, always ending the filename with a backtick.
  # Matching "<basename>`" is robust to the subdir-prefix inconsistency and to dot-vs-dash near-duplicate names.
  needle="${base}"'`'
  if ! grep -qF -- "$needle" "$ATTRIBUTION"; then
    echo "[ci]   no ATTRIBUTION.md row for committed asset '$rel'" >&2
    missing=$(( missing + 1 ))
  fi
done

if [[ $missing -ne 0 ]]; then
  echo "[ci] ATTRIBUTION CHECK FAILED - $missing committed .glb file(s) have no provenance row in $ASSETS_REL/ATTRIBUTION.md. Add a row (source + SPDX license) for each before committing (see the file's Rules section)." >&2
  exit 1
fi
echo "[ci] Attribution check PASS - all $count committed .glb file(s) under $ASSETS_REL have an ATTRIBUTION.md row."
