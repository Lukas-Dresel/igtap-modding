#!/usr/bin/env bash
# Collect reference DLLs from a local game install for CI builds.
# Usage: ./scripts/collect-ci-deps.sh [/path/to/game]
#
# Produces ci-deps.zip with the directory layout expected by the CI workflow.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
source "$REPO_DIR/_common.sh"

GAME_DIR="$(find_game_dir "${1:-}")"
check_game_dir "$GAME_DIR"

MANAGED_DIR="$GAME_DIR/IGTAP_Data/Managed"
BEPINEX_DIR="$GAME_DIR/BepInEx"

if [ ! -d "$MANAGED_DIR" ]; then
    echo "ERROR: Managed directory not found: $MANAGED_DIR"
    exit 1
fi
if [ ! -d "$BEPINEX_DIR/core" ]; then
    echo "ERROR: BepInEx not installed. Run install_all.sh first."
    exit 1
fi

OUT="$REPO_DIR/ci-deps"
rm -rf "$OUT"
mkdir -p "$OUT/managed" "$OUT/bepinex/core" "$OUT/bepinex/plugins"

echo "=== Collecting Unity/game DLLs ==="

MANAGED_DLLS=(
    Assembly-CSharp.dll
    Unity.InputSystem.dll
    Unity.Mathematics.dll
    Unity.TextMeshPro.dll
    Unity.RenderPipelines.Core.Runtime.dll
    Unity.RenderPipelines.Universal.Runtime.dll
    Unity.RenderPipelines.Universal.2D.Runtime.dll
    UnityEngine.dll
    UnityEngine.CoreModule.dll
    UnityEngine.UIModule.dll
    UnityEngine.UI.dll
    UnityEngine.IMGUIModule.dll
    UnityEngine.InputLegacyModule.dll
    UnityEngine.JSONSerializeModule.dll
    UnityEngine.Physics2DModule.dll
    UnityEngine.TextRenderingModule.dll
    UnityEngine.GridModule.dll
    UnityEngine.TilemapModule.dll
    UnityEngine.ParticleSystemModule.dll
)

for dll in "${MANAGED_DLLS[@]}"; do
    src="$MANAGED_DIR/$dll"
    if [ -f "$src" ]; then
        cp "$src" "$OUT/managed/"
        echo "  + $dll"
    else
        echo "  WARNING: $dll not found at $src"
    fi
done

echo ""
echo "=== Collecting BepInEx DLLs ==="

BEPINEX_DLLS=(BepInEx.dll 0Harmony.dll)

for dll in "${BEPINEX_DLLS[@]}"; do
    src="$BEPINEX_DIR/core/$dll"
    if [ -f "$src" ]; then
        cp "$src" "$OUT/bepinex/core/"
        echo "  + $dll"
    else
        echo "  WARNING: $dll not found at $src"
    fi
done

echo ""
echo "=== Creating ci-deps.zip ==="
cd "$REPO_DIR"
rm -f ci-deps.zip
(cd ci-deps && zip -r ../ci-deps.zip .)
rm -rf "$OUT"

echo ""
echo "Done! Created ci-deps.zip ($(du -h ci-deps.zip | cut -f1))"
echo ""
echo "Upload it as a GitHub release:"
echo "  gh release create ci-deps ./ci-deps.zip --title 'CI Dependencies' --notes 'Reference DLLs for CI builds. Do not delete.'"
echo ""
echo "Then delete the local zip:"
echo "  rm ci-deps.zip"
