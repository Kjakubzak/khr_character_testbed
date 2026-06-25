#!/usr/bin/env bash
# run-tests.sh — GATE 1 (compile) + GATE 2 (tests green + min-test-count floor).
# Usage:  ./Tools/ci/run-tests.sh [--project <p>] [--platform PlayMode|EditMode|Both] [--filter <f>] [--min-tests 12] [--min-sandbox-tests 12] [--timeout 60]
# NOTE: only the testbed's OWN tests run in this consumer project - Unity 'testables' does NOT surface a
# git-package's tests into a consumer, so the floor tracks the sandbox suite (plugin's ~165 gate in its own repo).
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$HERE/_common.sh"

PROJECT=""; PLATFORM="PlayMode"; FILTER=""; TIMEOUT=60; MIN_TESTS=12; MIN_SANDBOX=12; RESULTS_DIR=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) PROJECT="$2"; shift 2;;
    --platform) PLATFORM="$2"; shift 2;;
    --filter) FILTER="$2"; shift 2;;
    --timeout) TIMEOUT="$2"; shift 2;;
    --min-tests) MIN_TESTS="$2"; shift 2;;
    --min-sandbox-tests) MIN_SANDBOX="$2"; shift 2;;
    --results-dir) RESULTS_DIR="$2"; shift 2;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done

PROJECT="$(resolve_project_path "$PROJECT")"
UNITY="$(find_unity_exe "$PROJECT")"
if [[ -z "$RESULTS_DIR" ]]; then RESULTS_DIR="$PROJECT/Logs/ci"; fi
mkdir -p "$RESULTS_DIR"
assert_no_lock "$PROJECT"

echo "[ci] Run-Tests: GATE 1 (compile-only)"
if ! compile_only "$UNITY" "$PROJECT" "$RESULTS_DIR" compile "$TIMEOUT"; then
  echo "[ci] GATE 1 (compile) FAILED." >&2
  exit 1
fi

platforms=("$PLATFORM")
if [[ "$PLATFORM" == "Both" ]]; then platforms=("EditMode" "PlayMode"); fi

# Reads a numeric attribute from the NUnit <test-run> root element (whole-file collapsed to one line first).
read_attr() {
  local file="$1" attr="$2"
  tr '\n' ' ' < "$file" | sed -n 's/.*<test-run[^>]*[[:space:]]'"$attr"'="\([0-9]*\)".*/\1/p' | head -n1
}

# Counts the testbed's OWN test-cases (classname in namespace KhrCharacterTestbed.*) for the sandbox sub-floor.
# grep exits 1 when there are no matches; the '|| true' keeps that from tripping 'set -e -o pipefail' (wc still
# prints 0 for empty input, which is the count we want).
sandbox_count() {
  local file="$1"
  { tr '\n' ' ' < "$file" | grep -o '<test-case[^>]*classname="KhrCharacterTestbed[^"]*"' | wc -l | tr -d '[:space:]'; } || true
}

total=0; sandbox=0; bad=0
for p in "${platforms[@]}"; do
  tag="$(echo "$p" | tr '[:upper:]' '[:lower:]')"
  results="$RESULTS_DIR/results-$tag.xml"
  log="$RESULTS_DIR/run-$tag.log"
  args=(-batchmode -nographics -projectPath "$PROJECT" -runTests -testPlatform "$p" -testResults "$results" -logFile "$log")
  if [[ -n "$FILTER" ]]; then args+=(-testFilter "$FILTER"); fi

  echo "[ci] Run-Tests: GATE 2 ($p)"
  code=0
  invoke_unity_bounded "$UNITY" "$log" "$RESULTS_DIR" "run-$tag" "$TIMEOUT" "$results" -- "${args[@]}" || code=$?
  if [[ $code -eq 124 ]]; then echo "[ci] GATE 2 ($p) TIMEOUT -- a hang, not a skip." >&2; exit 1; fi
  if [[ ! -f "$results" ]]; then echo "[ci] GATE 2 ($p) FAILED -- no results XML (crash/hang)." >&2; exit 1; fi

  t="$(read_attr "$results" total)"; f="$(read_attr "$results" failed)"
  inc="$(read_attr "$results" inconclusive)"; sk="$(read_attr "$results" skipped)"
  t="${t:-0}"; f="${f:-0}"; inc="${inc:-0}"; sk="${sk:-0}"
  sb="$(sandbox_count "$results")"; sb="${sb:-0}"
  echo "[ci]   $p: total=$t failed=$f inconclusive=$inc skipped=$sk sandbox=$sb"
  if [[ "$f" -ne 0 || "$inc" -ne 0 || "$sk" -ne 0 ]]; then bad=1; fi
  total=$(( total + t ))
  sandbox=$(( sandbox + sb ))
done

if [[ $bad -ne 0 ]]; then echo "[ci] GATE 2 (tests) FAILED -- failed/inconclusive/skipped must all be 0." >&2; exit 1; fi
if [[ $total -lt $MIN_TESTS ]]; then
  echo "[ci] GATE 2 (floor) FAILED -- ran $total test(s), need >= $MIN_TESTS (only the testbed's own tests run; 'testables' does not surface a git-package's tests)." >&2
  exit 1
fi
if [[ $sandbox -lt $MIN_SANDBOX ]]; then
  echo "[ci] GATE 2 (sandbox sub-floor) FAILED -- ran $sandbox sandbox test(s) (KhrCharacterTestbed.*), need >= $MIN_SANDBOX." >&2
  exit 1
fi
echo "[ci] GATES 1 + 2 PASS -- $total test(s) (sandbox $sandbox); 0 failed/inconclusive/skipped; floor >= $MIN_TESTS; sandbox sub-floor >= $MIN_SANDBOX."
