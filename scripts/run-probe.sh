#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/build-fixtures.sh
dotnet run --project src/RuntimeProbe.Cli -c Release -- artifacts/fixtures
