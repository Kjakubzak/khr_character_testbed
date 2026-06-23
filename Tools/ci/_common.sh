#!/usr/bin/env bash
# _common.sh — shared helpers for the Tools/ci bash harness.
# Source from an entrypoint:  . "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/_common.sh"
# Neutral, reusable, KHR-agnostic. Parameterized by project path + Unity path.
set -euo pipefail

resolve_project_path() {
  local p="${1:-}"
  if [[ -z "$p" ]]; then p="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"; fi
  (cd "$p" && pwd)
}

get_unity_version() {
  local proj="$1"; local pv="$proj/ProjectSettings/ProjectVersion.txt"
  if [[ ! -f "$pv" ]]; then echo "ProjectVersion.txt not found at $pv" >&2; return 1; fi
  grep '^m_EditorVersion:' "$pv" | head -n1 | sed 's/^m_EditorVersion:[[:space:]]*//' | tr -d '\r' | xargs
}

find_unity_exe() {
  local proj="$1"; local version; version="$(get_unity_version "$proj")"
  if [[ -n "${UNITY_PATH:-}" && -x "${UNITY_PATH}" ]]; then echo "$UNITY_PATH"; return 0; fi
  local candidates=(
    "$HOME/Unity/Hub/Editor/$version/Editor/Unity"
    "/opt/unity/editors/$version/Editor/Unity"
    "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
  )
  local c
  for c in "${candidates[@]}"; do
    if [[ -x "$c" ]]; then echo "$c"; return 0; fi
  done
  echo "Unity $version not found. Set UNITY_PATH to the editor binary (the project pins $version)." >&2
  return 1
}

assert_no_lock() {
  local proj="$1"
  if [[ -f "$proj/Temp/UnityLockfile" ]]; then
    echo "Unity lockfile present ($proj/Temp/UnityLockfile). One Unity per project; close it and retry." >&2
    return 1
  fi
  return 0
}

# Returns 0 (clean) when the compile log has no compiler errors.
compile_log_clean() {
  local log="$1"
  if [[ ! -f "$log" ]]; then return 1; fi
  if grep -Eq 'error CS|Compilation failed|Scripts have compiler errors' "$log"; then return 1; fi
  return 0
}

# invoke_unity_bounded <unity> <log> <logdir> <runname> <timeout_min> <results_or_empty> -- <unity args...>
# Detached launch + bounded poll + hard-timeout kill. Returns Unity's exit code, or 124 on timeout.
invoke_unity_bounded() {
  local unity="$1" log="$2" logdir="$3" runname="$4" timeout_min="$5" results="$6"; shift 6
  if [[ "${1:-}" == "--" ]]; then shift; fi
  mkdir -p "$logdir"
  rm -f "$log" "$logdir/$runname.TIMEOUT"
  if [[ -n "$results" ]]; then rm -f "$results"; fi

  "$unity" "$@" &
  local pid=$!
  echo "$pid" > "$logdir/$runname.pid"
  echo "[ci] launched Unity '$runname' pid=$pid timeout=${timeout_min}m"

  local waited=0 poll=10 limit=$(( timeout_min * 60 ))
  while true; do
    sleep "$poll"; waited=$(( waited + poll ))
    if ! kill -0 "$pid" 2>/dev/null; then break; fi
    if [[ -n "$results" && -f "$results" ]]; then
      local g=0
      while [[ $g -lt 60 ]] && kill -0 "$pid" 2>/dev/null; do sleep 5; g=$(( g + 5 )); done
      break
    fi
    if [[ $waited -ge $limit ]]; then
      echo "[ci] TIMEOUT after ${timeout_min}m; killing '$runname' pid=$pid" >&2
      kill -9 "$pid" 2>/dev/null || true
      date -u +%FT%TZ > "$logdir/$runname.TIMEOUT"
      return 124
    fi
  done
  local code=0
  wait "$pid" 2>/dev/null || code=$?
  echo "[ci] Unity '$runname' exited code=$code"
  return "$code"
}

# Compile-only batchmode pass (-quit). Returns 0 on a clean compile.
compile_only() {
  local unity="$1" proj="$2" logdir="$3" runname="${4:-compile}" timeout_min="${5:-60}"
  local log="$logdir/$runname.log"
  local code=0
  invoke_unity_bounded "$unity" "$log" "$logdir" "$runname" "$timeout_min" "" -- \
    -batchmode -quit -nographics -projectPath "$proj" -logFile "$log" || code=$?
  if [[ $code -ne 0 ]]; then echo "[ci] compile '$runname' exit $code (see $log)" >&2; fi
  if compile_log_clean "$log" && [[ $code -eq 0 ]]; then return 0; fi
  return 1
}
