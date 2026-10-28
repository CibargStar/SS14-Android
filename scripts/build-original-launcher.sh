#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/prepare-original-launcher.sh
dotnet build src/SS14.Android -c Debug -p:AndroidLauncher=true -t:SignAndroidPackage "$@"
