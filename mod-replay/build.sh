#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Build compile-time dependency first (sorts after us alphabetically)
bash "$SCRIPT_DIR/../mod-speedrun/build.sh"

echo "=== Building IGTAPReplay ==="
dotnet build -c Release "$SCRIPT_DIR/Replay.csproj"
