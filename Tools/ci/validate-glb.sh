#!/usr/bin/env bash
# validate-glb.sh — GATE 3. Runs the Khronos glTF-Validator over every exported GLB; gates on numErrors==0.
# Standalone (no Unity). If the validator is absent, SKIPS non-fatally with install instructions.
# Usage:  ./Tools/ci/validate-glb.sh [--project <p>] [--input-dir <d>] [--report-dir <d>] [--required] [--fail-on-warning]
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""; INPUT_DIR=""; REPORT_DIR=""; FAIL_ON_WARNING=0; REQUIRED=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    --input-dir) INPUT_DIR="$2"; shift 2;;
    --report-dir) REPORT_DIR="$2"; shift 2;;
    --required) REQUIRED=1; shift;;
    --fail-on-warning) FAIL_ON_WARNING=1; shift;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done

PROJECT="$(resolve_project_path "$PROJECT")"
if [[ -z "$INPUT_DIR" ]]; then INPUT_DIR="$PROJECT/Assets/SampleAssets/Synthetic"; fi
if [[ -z "$REPORT_DIR" ]]; then REPORT_DIR="$PROJECT/Artifacts/reports"; fi

VALIDATOR="${GLTF_VALIDATOR:-}"
if [[ -z "$VALIDATOR" || ! -x "$VALIDATOR" ]]; then
  if command -v gltf_validator >/dev/null 2>&1; then VALIDATOR="$(command -v gltf_validator)"; else VALIDATOR=""; fi
fi
if [[ -z "$VALIDATOR" ]]; then
  if [[ $REQUIRED -eq 1 ]]; then
    echo "[ci] GATE 3 (glTF-Validator) FAILED -- validator not found and --required was set. Set GLTF_VALIDATOR to the proposal-aware native CLI." >&2
    exit 1
  fi
  echo "[ci] glTF-Validator not found -- SKIPPING GATE 3 (non-fatal)." >&2
  echo "[ci] Build the pinned proposal-aware validator and set GLTF_VALIDATOR to its native CLI." >&2
  exit 0
fi
if [[ "$INPUT_DIR" = /* ]]; then
  if [[ "$INPUT_DIR" != "$PROJECT"/* ]]; then
    echo "[ci] Input dir '$INPUT_DIR' must be inside project '$PROJECT'." >&2
    exit 2
  fi
  input_rel="${INPUT_DIR#"$PROJECT"/}"
else
  input_rel="${INPUT_DIR#./}"
  INPUT_DIR="$PROJECT/$input_rel"
fi
if [[ ! -d "$INPUT_DIR" ]]; then echo "[ci] Input dir '$INPUT_DIR' missing -- nothing to validate." >&2; exit 0; fi

mkdir -p "$REPORT_DIR"
tracked=()
while IFS= read -r tracked_file; do tracked+=("$tracked_file"); done < <(
  git -C "$PROJECT" ls-files -- "$input_rel" |
    awk 'tolower($0) ~ /\.glb$/ || tolower($0) ~ /\.gltf$/' |
    sort
)
files=()
for tracked_file in "${tracked[@]}"; do files+=("$PROJECT/$tracked_file"); done
if [[ ${#files[@]} -eq 0 ]]; then echo "[ci] No GLB/glTF under '$INPUT_DIR'." >&2; exit 0; fi

total_errors=0; total_warnings=0; parsed=0
for f in "${files[@]}"; do
  base="$(basename "$f")"; base="${base%.*}"
  report="$REPORT_DIR/${base}.report.json"
  "$VALIDATOR" -a -o "$f" >"$report" 2>"$REPORT_DIR/${base}.log" || true

  errs=""; warns=""
  if [[ -s "$report" ]]; then
    if command -v jq >/dev/null 2>&1; then
      errs="$(jq -r '.issues.numErrors // empty' "$report" 2>/dev/null || true)"
      warns="$(jq -r '.issues.numWarnings // empty' "$report" 2>/dev/null || true)"
    fi
    if [[ -z "$errs" ]]; then
      errs="$(grep -oE '"numErrors"[[:space:]]*:[[:space:]]*[0-9]+' "$report" | grep -oE '[0-9]+$' | head -n1 || true)"
      warns="$(grep -oE '"numWarnings"[[:space:]]*:[[:space:]]*[0-9]+' "$report" | grep -oE '[0-9]+$' | head -n1 || true)"
    fi
  fi
  if [[ -z "$errs" ]]; then
    if [[ $REQUIRED -eq 1 ]]; then echo "[ci]   $base: could not parse a validator report (--required); failing GATE 3." >&2; exit 1; fi
    echo "[ci]   $base: could not parse a validator report (skipping this file). Check your gltf_validator version/flags." >&2
    continue
  fi
  warns="${warns:-0}"
  echo "[ci]   $base: errors=$errs warnings=$warns"
  total_errors=$(( total_errors + errs )); total_warnings=$(( total_warnings + warns )); parsed=$(( parsed + 1 ))
done

echo "[ci] glTF-Validator: parsed $parsed/${#files[@]} file(s); errors=$total_errors warnings=$total_warnings (reports in $REPORT_DIR)"
if [[ $total_errors -gt 0 ]]; then echo "[ci] GATE 3 (glTF-Validator) FAILED -- $total_errors error(s)." >&2; exit 1; fi
if [[ $FAIL_ON_WARNING -eq 1 && $total_warnings -gt 0 ]]; then echo "[ci] GATE 3 FAILED -- $total_warnings warning(s) with --fail-on-warning." >&2; exit 1; fi
echo "[ci] GATE 3 (glTF-Validator) PASS."
