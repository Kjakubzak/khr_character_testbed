#!/usr/bin/env bash
# export-goldens.sh — GATE 4. Re-export fixtures + normalize their wire (SandboxCI.ExportGoldens), then diff against
# the committed goldens (--check, default) or rewrite them (--update).
# Usage:  ./Tools/ci/export-goldens.sh [--project <p>] [--check | --update] [--timeout 60]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""; MODE="check"; TIMEOUT=60; GOLDEN_DIR=""; SNAP_DIR=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    --update) MODE="update"; shift;;
    --check) MODE="check"; shift;;
    --timeout) TIMEOUT="$2"; shift 2;;
    --golden-dir) GOLDEN_DIR="$2"; shift 2;;
    --snapshot-dir) SNAP_DIR="$2"; shift 2;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done

PROJECT="$(resolve_project_path "$PROJECT")"
UNITY="$(find_unity_exe "$PROJECT")"
if [[ -z "$GOLDEN_DIR" ]]; then GOLDEN_DIR="$PROJECT/Tests/Golden"; fi
if [[ -z "$SNAP_DIR" ]]; then SNAP_DIR="$PROJECT/Artifacts/snapshots"; fi
LOGDIR="$PROJECT/Logs/ci"
assert_no_lock "$PROJECT"

log="$LOGDIR/goldens.log"
echo "[ci] Export-Goldens: running SandboxCI.ExportGoldens"
code=0
invoke_unity_bounded "$UNITY" "$log" "$LOGDIR" goldens "$TIMEOUT" "" -- \
  -batchmode -quit -nographics -projectPath "$PROJECT" -executeMethod Samples.Editor.SandboxCI.ExportGoldens -logFile "$log" || code=$?
if [[ $code -ne 0 ]] || ! compile_log_clean "$log"; then
  echo "[ci] Export-Goldens: Unity export failed (exit $code; see $log)." >&2
  exit 1
fi

mapfile -t snaps < <(find "$SNAP_DIR" -maxdepth 1 -type f -name '*.json' 2>/dev/null | sort)
if [[ ${#snaps[@]} -eq 0 ]]; then echo "[ci] Export-Goldens produced no snapshots in '$SNAP_DIR'." >&2; exit 1; fi

if [[ "$MODE" == "update" ]]; then
  mkdir -p "$GOLDEN_DIR"
  for s in "${snaps[@]}"; do cp -f "$s" "$GOLDEN_DIR/$(basename "$s")"; done
  echo "[ci] Export-Goldens --update: wrote ${#snaps[@]} golden(s) to '$GOLDEN_DIR'. Review the diff in code review."
  exit 0
fi

mapfile -t goldens < <(find "$GOLDEN_DIR" -maxdepth 1 -type f -name '*.json' 2>/dev/null | sort)
if [[ ${#goldens[@]} -eq 0 ]]; then
  echo "[ci] No committed goldens in '$GOLDEN_DIR' -- SKIPPING the golden diff. Run '--update' once and commit them." >&2
  exit 0
fi

drift=0
for g in "${goldens[@]}"; do
  name="$(basename "$g")"; snap="$SNAP_DIR/$name"
  if [[ ! -f "$snap" ]]; then echo "[ci]   MISSING snapshot for golden $name (fixture removed?)." >&2; drift=1; continue; fi
  if diff -q "$g" "$snap" >/dev/null 2>&1; then echo "[ci]   OK: $name"; else echo "[ci]   DRIFT: $name differs from the committed golden." >&2; drift=1; fi
done
for s in "${snaps[@]}"; do
  name="$(basename "$s")"
  if [[ ! -f "$GOLDEN_DIR/$name" ]]; then echo "[ci]   NEW snapshot $name has no committed golden. Run '--update'." >&2; drift=1; fi
done

if [[ $drift -ne 0 ]]; then echo "[ci] GATE 4 (goldens) FAILED -- wire drift. If intentional, run '--update' and commit the diff." >&2; exit 1; fi
echo "[ci] GATE 4 (goldens) PASS."
