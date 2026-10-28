#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
repository=upstream/SS14.Launcher
patch_file="$(pwd)/patches/launcher-android-platform.patch"
git submodule update --init --recursive "$repository"
if git -C "$repository" apply --reverse --check "$patch_file" 2>/dev/null; then
  echo 'Original launcher Android patch already applied.'
else
  git -C "$repository" apply --check "$patch_file"
  git -C "$repository" apply "$patch_file"
fi
