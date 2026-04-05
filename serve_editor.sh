#!/usr/bin/env bash
# Serve the level editor on localhost:8080
# Usage: bash serve_editor.sh [port]
set -euo pipefail

PORT="${1:-8080}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== IGTAP Level Editor ==="
echo "Open http://localhost:$PORT/editor.html in your browser"
echo "Press Ctrl+C to stop"
echo ""

cd "$SCRIPT_DIR"
python3 -m http.server "$PORT" --bind 127.0.0.1
