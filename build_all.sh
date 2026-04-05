#!/usr/bin/env bash
# Build all IGTAP mods
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

failed=0
for mod_dir in "$SCRIPT_DIR"/mod*/; do
    case "$(basename "$mod_dir")" in *IMPORTED*) continue ;; esac
    if [ -f "$mod_dir/build.sh" ]; then
        bash "$mod_dir/build.sh" || failed=1
        echo ""
    fi
done

if [ "$failed" -eq 0 ]; then
    echo "=== All builds succeeded ==="
else
    echo "=== Some builds failed ==="
    exit 1
fi
