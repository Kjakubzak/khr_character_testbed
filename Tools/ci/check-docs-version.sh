#!/usr/bin/env bash
# check-docs-version.sh — doc-drift lint. Fails when any Unity-editor-version string in the tracked
# docs/workflows differs from ProjectSettings/ProjectVersion.txt (the single source of truth). No Unity needed.
# Usage:  ./Tools/ci/check-docs-version.sh [--project <p>] [file ...]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""
FILES=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    *) FILES+=("$1"); shift;;
  esac
done

PROJECT="$(resolve_project_path "$PROJECT")"
EXPECTED="$(get_unity_version "$PROJECT")"
if [[ -z "$EXPECTED" ]]; then echo "[ci] could not read m_EditorVersion from ProjectVersion.txt" >&2; exit 1; fi

# Default file set: the docs/workflows that quote the editor version.
if [[ ${#FILES[@]} -eq 0 ]]; then
  FILES=(
    "$PROJECT/README.md"
    "$PROJECT/docs/ci.md"
    "$PROJECT/.github/workflows/ci.yml"
    "$PROJECT/.github/workflows/ci-nightly.yml"
  )
fi

# Unity editor versions look like 2022.3.76f1 / 6000.0.76f1: a 4-digit stream, dotted, with an f/a/b tag.
# (URP "14.0.11", package "1.0.0" and commit SHAs do not match, so they are not false-positives.)
VER_RE='[0-9]{4}\.[0-9]+\.[0-9]+[fab][0-9]+'
bad=0
for f in "${FILES[@]}"; do
  [[ -f "$f" ]] || continue
  # grep -nEo prints "<lineno>:<match>"; the match has no ':' so the split is unambiguous.
  while IFS= read -r hit; do
    n="${hit%%:*}"
    tok="${hit#*:}"
    if [[ "$tok" != "$EXPECTED" ]]; then
      echo "[ci]   $f:$n  found '$tok', expected '$EXPECTED'" >&2
      bad=1
    fi
  done < <(grep -nEo "$VER_RE" "$f" || true)
done

if [[ $bad -ne 0 ]]; then
  echo "[ci] DOC VERSION LINT FAILED — Unity version string(s) disagree with ProjectSettings/ProjectVersion.txt ('$EXPECTED'). Update the docs/workflows above (or ProjectVersion.txt if the target really changed)." >&2
  exit 1
fi
echo "[ci] Doc version lint PASS — all Unity version strings match '$EXPECTED'."
