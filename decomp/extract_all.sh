#!/usr/bin/env bash
# Run all extraction scripts in sequence.
# Output goes to ./output/
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/config.sh"

echo "============================================"
echo "  IGTAP Data Extraction Pipeline"
echo "  Game: $GAME_DIR"
echo "  Output: $OUTPUT_DIR"
echo "============================================"
echo ""

# Step 1: Decompile C# assemblies
echo ">>> Step 1/5: Decompiling C# assemblies"
bash "$SCRIPT_DIR/decompile.sh"
echo ""

# Step 2: Dump asset summary
echo ">>> Step 2/5: Dumping asset summary"
python3.11 "$SCRIPT_DIR/dump_assets.py"
echo ""

# Step 3: Extract tilemaps
echo ">>> Step 3/5: Extracting tilemap data"
python3.11 "$SCRIPT_DIR/extract_tilemaps.py"
echo ""

# Step 4: Extract game object hierarchy
echo ">>> Step 4/5: Extracting game object hierarchy"
python3.11 "$SCRIPT_DIR/extract_gameobjects.py"
echo ""

# Step 5: Parse course data
echo ">>> Step 5/5: Parsing course data"
python3.11 "$SCRIPT_DIR/parse_course_data.py"
echo ""

echo "============================================"
echo "  Extraction complete!"
echo "  Output: $OUTPUT_DIR"
echo ""
echo "  Optional (slow, large output):"
echo "    python3.11 $SCRIPT_DIR/extract_sprites.py"
echo "============================================"
