#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../_common.sh"

GAME_DIR="$(find_game_dir "${1:-}")"
check_game_dir "$GAME_DIR"
ensure_bepinex "$GAME_DIR"

# Install dependencies first
bash "$SCRIPT_DIR/../mod/install.sh" "$GAME_DIR"
bash "$SCRIPT_DIR/../mod-fixedtimestep/install.sh" "$GAME_DIR"

bash "$SCRIPT_DIR/build.sh"

PLUGINS_DIR="$GAME_DIR/BepInEx/plugins"
mkdir -p "$PLUGINS_DIR"
cp "$SCRIPT_DIR/bin/Release/netstandard2.1/IGTAPReplay.dll" "$PLUGINS_DIR/"
echo "Installed IGTAPReplay.dll -> $PLUGINS_DIR/"
