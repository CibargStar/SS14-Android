#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Install a Debug build first; run-as intentionally requires a debuggable APK.
if [[ "${1:-}" != "--use-built-fixtures" ]]; then
  bash scripts/build-fixtures.sh
fi
adb shell am force-stop io.ss14.runtimeprobe
adb shell run-as io.ss14.runtimeprobe rm -f files/runtime-probe-result.json
for version in v1 v2; do
  adb shell run-as io.ss14.runtimeprobe mkdir -p "files/runtime-probe/$version"
  for filename in ExternalPayload.dll sha256.txt Robust.NetSerializer.dll Robust.NetSerializer.dll.sha256; do
    # Stream through stdin; no external-storage or executable-file permissions required.
    adb shell "run-as io.ss14.runtimeprobe sh -c 'cat > files/runtime-probe/$version/$filename'" \
      < "artifacts/fixtures/$version/$filename"
  done
done
adb shell am start -n io.ss14.runtimeprobe/io.ss14.runtimeprobe.MainActivity
for attempt in $(seq 1 30); do
  if adb shell run-as io.ss14.runtimeprobe test -f files/runtime-probe-result.json; then
    adb exec-out run-as io.ss14.runtimeprobe cat files/runtime-probe-result.json > artifacts/android-probe-result.json
    python3 - <<'PY'
import json, pathlib, sys
result = json.loads(pathlib.Path('artifacts/android-probe-result.json').read_text())
print(json.dumps(result, ensure_ascii=False, indent=2))
sys.exit(0 if result['Passed'] else 1)
PY
    exit $?
  fi
  sleep 1
done
echo "Timed out waiting for runtime-probe-result.json. Check adb logcat and the app screen." >&2
exit 1
