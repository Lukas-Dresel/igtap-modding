#!/usr/bin/env bash
# Decompile all C# assemblies from the game
set -euo pipefail
source "$(dirname "$0")/config.sh"

DECOMPILED_DIR="$OUTPUT_DIR/decompiled"
mkdir -p "$DECOMPILED_DIR"

ILSPY="${HOME}/.dotnet/tools/ilspycmd"
if ! [ -f "$ILSPY" ]; then
    echo "ERROR: ilspycmd not found. Run setup.sh first."
    exit 1
fi

# Decompile main game assembly (one file per type, nested by namespace)
echo "=== Decompiling Assembly-CSharp.dll ==="
"$ILSPY" -p --nested-directories "$DATA_DIR/Managed/Assembly-CSharp.dll" -o "$DECOMPILED_DIR/Assembly-CSharp"
echo "  -> $DECOMPILED_DIR/Assembly-CSharp/"

# Decompile firstpass assembly (one file per type, nested by namespace)
echo "=== Decompiling Assembly-CSharp-firstpass.dll ==="
"$ILSPY" -p --nested-directories "$DATA_DIR/Managed/Assembly-CSharp-firstpass.dll" -o "$DECOMPILED_DIR/Assembly-CSharp-firstpass"
echo "  -> $DECOMPILED_DIR/Assembly-CSharp-firstpass/"

echo ""
echo "=== Done. Decompiled source in $DECOMPILED_DIR ==="
ls -lah "$DECOMPILED_DIR"/Assembly-CSharp*/*.cs 2>/dev/null || ls -lah "$DECOMPILED_DIR"/Assembly-CSharp*/
