#!/usr/bin/env bash
# Prepare an independent Unity 6 worktree; leave the default Unity 2022 project intact.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo="$(git -C "$here" rev-parse --show-toplevel)"
destination="$repo/../khr_character_testbed-unity6"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output)
      [[ $# -ge 2 ]] || { echo "--output needs a path" >&2; exit 2; }
      destination="$2"
      shift 2
      ;;
    --help)
      echo "Usage: bash Tools/unity6/prepare-testbed.sh [--output <new directory>]"
      exit 0
      ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ ! -e "$destination" && ! -L "$destination" ]] || {
  echo "Destination already exists: $destination" >&2
  exit 1
}
parent="$(cd "$(dirname "$destination")" && pwd -P)"
destination="$parent/$(basename "$destination")"
case "$destination/" in
  "$repo/"*) echo "Destination must be outside the source checkout" >&2; exit 1 ;;
esac

# Check against the source tree before creating the destination. Unexpected source drift fails visibly.
git -C "$repo" apply --check "$here/testbed.patch"
git -C "$repo" worktree add --detach "$destination" HEAD
git -C "$destination" apply --check "$here/testbed.patch"
git -C "$destination" apply "$here/testbed.patch"
cp "$here/Unity6HubLauncher.cs" "$destination/Assets/Editor/Unity6HubLauncher.cs"
cp "$here/Unity6HubLauncher.cs.meta" "$destination/Assets/Editor/Unity6HubLauncher.cs.meta"

echo "Unity 6 project prepared: $destination"
echo "Open it with Unity 6000.5.4f1. See Tools/unity6/README.md for launch and test commands."
