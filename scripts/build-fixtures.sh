#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
for version in 1 2; do
  dotnet build "probes/Payload.V$version/Payload.V$version.csproj" -c Release --nologo
  destination="artifacts/fixtures/v$version"
  mkdir -p "$destination"
  cp "probes/Payload.V$version/bin/Release/net10.0/ExternalPayload.dll" "$destination/"
  cp "probes/Payload.V$version/bin/Release/net10.0/Robust.NetSerializer.dll" "$destination/"
  python3 - "$destination" <<'PY'
import hashlib, pathlib, sys
directory = pathlib.Path(sys.argv[1])
(directory / 'sha256.txt').write_text(hashlib.sha256((directory / 'ExternalPayload.dll').read_bytes()).hexdigest() + '\n')
(directory / 'Robust.NetSerializer.dll.sha256').write_text(hashlib.sha256((directory / 'Robust.NetSerializer.dll').read_bytes()).hexdigest() + '\n')
PY
done
