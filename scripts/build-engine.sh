#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/prepare-engine.sh
dotnet build engine/RobustToolbox/Robust.Client/Robust.Client.csproj -c Release \
  -p:FullRelease=True -p:TargetOS=Android -m:1 -p:BuildInParallel=false --nologo "$@"
