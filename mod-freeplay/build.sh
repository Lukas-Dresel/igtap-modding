#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "=== Building IGTAPFreeplay ==="
dotnet build -c Release "$SCRIPT_DIR/Freeplay.csproj"
