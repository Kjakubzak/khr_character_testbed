#!/usr/bin/env bash
# check-attribution.sh - license-compliance lint. Fails when a committed Assets/SampleAssets/**/*.glb has no
# matching attribution and checksum-pinned manifest row, or when its recorded SHA-256 is stale. No Unity needed.
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
MANIFEST="$PROJECT/$ASSETS_REL/ASSET_MANIFEST.tsv"
[[ -f "$ATTRIBUTION" ]] || { echo "[ci] ATTRIBUTION.md not found at $ATTRIBUTION" >&2; exit 1; }
[[ -f "$MANIFEST" ]] || { echo "[ci] ASSET_MANIFEST.tsv not found at $MANIFEST" >&2; exit 1; }

expected_header=$'path\tsha256\tlicense\tsource_or_derivation'
IFS= read -r actual_header < "$MANIFEST"
[[ "$actual_header" == "$expected_header" ]] || { echo "[ci] unexpected ASSET_MANIFEST.tsv header" >&2; exit 1; }

if ! awk -F '\t' '
  NR == 1 { next }
  NF != 4 || $1 == "" || length($2) != 64 || $2 !~ /^[0-9a-f]+$/ || $3 == "" || $4 == "" { exit 1 }
' "$MANIFEST"; then
  echo "[ci] malformed or incomplete ASSET_MANIFEST.tsv row" >&2
  exit 1
fi

duplicates="$(tail -n +2 "$MANIFEST" | cut -f1 | sort | uniq -d)"
[[ -z "$duplicates" ]] || { echo "[ci] duplicate manifest rows: $duplicates" >&2; exit 1; }

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

# Enumerate the committed GLBs, so untracked local experiments do not trip the gate.
TRACKED_FILE="$(mktemp)"
trap 'rm -f "$TRACKED_FILE"' EXIT
(cd "$PROJECT" && git ls-files -- "$ASSETS_REL" 2>/dev/null | awk '/\.glb$/') > "$TRACKED_FILE" || true
if [[ ! -s "$TRACKED_FILE" ]]; then
  (cd "$PROJECT" && find "$ASSETS_REL" -name '*.glb' -print 2>/dev/null | sort) > "$TRACKED_FILE"
fi

missing=0
count=0
while IFS= read -r rel; do
  [[ -z "$rel" ]] && continue
  count=$(( count + 1 ))
  base="$(basename "$rel")"
  # A row cites the asset as `<name>.glb` or `<subdir>/<name>.glb`, always ending the filename with a backtick.
  # Matching "<basename>`" is robust to the subdir-prefix inconsistency and to dot-vs-dash near-duplicate names.
  # Anchor the START (a backtick or slash must precede the name) as well as the end, so one asset name can't
  # false-match as a suffix of another asset's row.
  if ! grep -qF -- "\`${base}\`" "$ATTRIBUTION" && ! grep -qF -- "/${base}\`" "$ATTRIBUTION"; then
    echo "[ci]   no ATTRIBUTION.md row for committed asset '$rel'" >&2
    missing=$(( missing + 1 ))
  fi
  expected="$(awk -F '\t' -v path="$rel" 'NR > 1 && $1 == path { print $2 }' "$MANIFEST")"
  if [[ -z "$expected" ]]; then
    echo "[ci]   no ASSET_MANIFEST.tsv row for committed asset '$rel'" >&2
    missing=$(( missing + 1 ))
    continue
  fi
  actual="$(sha256_file "$PROJECT/$rel")"
  if [[ "$actual" != "$expected" ]]; then
    echo "[ci]   SHA-256 mismatch for '$rel': expected $expected, got $actual" >&2
    missing=$(( missing + 1 ))
  fi
done < "$TRACKED_FILE"

while IFS=$'\t' read -r rel _; do
  [[ -z "$rel" ]] && continue
  if ! grep -Fxq -- "$rel" "$TRACKED_FILE"; then
    echo "[ci]   manifest row does not name a committed GLB: '$rel'" >&2
    missing=$(( missing + 1 ))
  fi
done < <(tail -n +2 "$MANIFEST")

if [[ $missing -ne 0 ]]; then
  echo "[ci] ATTRIBUTION CHECK FAILED - $missing attribution, manifest, or checksum error(s). Update $ASSETS_REL/ATTRIBUTION.md and $ASSETS_REL/ASSET_MANIFEST.tsv together." >&2
  exit 1
fi
echo "[ci] Attribution check PASS - all $count committed .glb file(s) under $ASSETS_REL have attribution and verified manifest hashes."
