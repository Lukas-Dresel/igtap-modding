#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "=== Building IGTAPDashPlus ==="
dotnet build -c Release "$SCRIPT_DIR/DashPlus.csproj"
