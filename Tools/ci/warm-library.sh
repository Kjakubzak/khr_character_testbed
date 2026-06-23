#!/usr/bin/env bash
# warm-library.sh — cold-prime the Unity Library/ so later runs are fast.
# Usage:  ./Tools/ci/warm-library.sh [--project <path>] [--timeout 60]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""; TIMEOUT=60
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    --timeout) TIMEOUT="$2"; shift 2;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done

PROJECT="$(resolve_project_path "$PROJECT")"
UNITY="$(find_unity_exe "$PROJECT")"
assert_no_lock "$PROJECT"

echo "[ci] Warm-Library: priming Library/ for '$PROJECT'"
if compile_only "$UNITY" "$PROJECT" "$PROJECT/Logs/ci" warm "$TIMEOUT"; then
  echo "[ci] Warm-Library OK."
else
  echo "[ci] Warm-Library FAILED (compile errors or timeout)." >&2
  exit 1
fi
