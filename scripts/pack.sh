#!/usr/bin/env bash
# Pack all SmartData projects with a single frozen version stamp shared
# across every project. See scripts/pack.ps1 for rationale.
set -euo pipefail

CONFIG="${1:-Release}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

export SMARTDATA_STAMP="$(date -u +'%y.%-m.%-d-%H%M%S')"
echo "Packing SmartData with stamp: $SMARTDATA_STAMP"

dotnet pack "$REPO_ROOT/SmartData.slnx" -c "$CONFIG"

echo "Done. Artifacts: $REPO_ROOT/artifacts/"
