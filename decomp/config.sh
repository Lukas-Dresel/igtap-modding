#!/usr/bin/env bash
# Shared configuration for all extraction scripts
# Source this from other scripts: source "$(dirname "$0")/config.sh"

GAME_DIR="/home/honululu/.local/share/Steam/steamapps/common/IGTAP an Incremental Game That's Also a Platformer Demo"
DATA_DIR="$GAME_DIR/IGTAP_Data"
OUTPUT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/output"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

mkdir -p "$OUTPUT_DIR"
