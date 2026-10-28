#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 2 || ! "$1" =~ ^[0-9]+$ || ! "$2" =~ ^[0-9]+$ ]]; then
  echo 'Usage: emulator-tap.sh <physical-x> <physical-y>' >&2
  exit 1
fi
# Android 30's `adb input tap` can report TOOL_TYPE_UNKNOWN. Avalonia
# classifies that differently from a finger. Use the actual emulated sensor.
adb emu event mouse "$1" "$2" 0 1
adb emu event mouse "$1" "$2" 0 0
