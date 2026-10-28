#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
engine=engine/RobustToolbox
patch_file="$(pwd)/patches/robust-android-platform.patch"
git submodule update --init --recursive "$engine"
if git -C "$engine" apply --reverse --check "$patch_file" 2>/dev/null; then
  echo 'Android platform patch already applied.'
else
  git -C "$engine" apply --check "$patch_file"
  git -C "$engine" apply "$patch_file"
fi
