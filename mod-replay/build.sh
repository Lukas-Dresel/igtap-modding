#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../_common.sh"
GAME_DIR="${GAME_DIR:-}"
GAME_DIR="${GAME_DIR/#\~/$HOME}"
if [ -z "$GAME_DIR" ]; then GAME_DIR="$(find_game_dir "${1:-}")"; fi
check_game_dir "$GAME_DIR"
ensure_bepinex "$GAME_DIR"
export GAME_DIR
# Build compile-time dependency first (sorts after us alphabetically)
bash "$SCRIPT_DIR/../mod-speedrun/build.sh"
echo "=== Building IGTAPReplay ==="
dotnet build -c Release "$SCRIPT_DIR/Replay.csproj"
