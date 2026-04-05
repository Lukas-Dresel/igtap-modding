#!/usr/bin/env bash
# Interactive installer for IGTAP mods
# Usage: ./install_all.sh [-y] [/path/to/game]
#   -y    Auto-accept all prompts (non-interactive)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/_common.sh"

# --- Parse flags ---
AUTO_YES=false
GAME_ARG=""
for arg in "$@"; do
    case "$arg" in
        -y|--yes) AUTO_YES=true ;;
        *) GAME_ARG="$arg" ;;
    esac
done

# --- Helpers ---

read_info() {
    local file="$1" key="$2"
    grep "^${key}=" "$file" 2>/dev/null | head -1 | sed "s/^${key}=//"
}

# Prompt the user: returns 0 for yes, 1 for skip, exits on quit
# In -y mode, always returns 0 (yes)
prompt() {
    local question="$1"
    if $AUTO_YES; then
        echo ""
        echo "$question -> auto-accepted (-y)"
        return 0
    fi
    while true; do
        printf "\n%s [y]es / [s]kip / [q]uit: " "$question"
        read -r choice
        case "$choice" in
            y|Y|yes|Yes) return 0 ;;
            s|S|skip|Skip) return 1 ;;
            q|Q|quit|Quit)
                echo ""
                echo "Aborted by user."
                exit 0
                ;;
            *) echo "  Please enter y, s, or q." ;;
        esac
    done
}

# --- Find the game ---

echo "============================================"
echo "  IGTAP Mod Installer"
echo "============================================"
echo ""

GAME_DIR="$(find_game_dir "${GAME_ARG:-}")"
if [ -z "$GAME_DIR" ]; then
    echo "ERROR: Could not find the game directory."
    echo "Usage: $0 [/path/to/game]"
    echo "Expected: $GAME_NAME"
    exit 1
fi

echo "Found game at:"
echo "  $GAME_DIR"
echo ""

# --- BepInEx ---

if [ -d "$GAME_DIR/BepInEx/core" ] && [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ]; then
    echo "BepInEx $BEPINEX_VERSION: already installed."
else
    echo "--------------------------------------------"
    echo "BepInEx $BEPINEX_VERSION (mod loader framework)"
    echo ""
    echo "  BepInEx is required for all mods to work."
    echo "  This will:"
    echo "    - Download BepInEx_${OS}_x64_${BEPINEX_VERSION}.zip from GitHub"
    echo "    - Extract it into the game directory"
    echo "    - Create BepInEx/core/, BepInEx/plugins/, etc."
    echo "    - No game files are modified or overwritten"
    echo "--------------------------------------------"

    if prompt "Install BepInEx?"; then
        ensure_bepinex "$GAME_DIR"
        echo ""
        echo "BepInEx installed successfully."
    else
        echo ""
        echo "WARNING: Skipping BepInEx. Mods will not work without it."
        echo "         You can install it manually later."
    fi
fi

PLUGINS_DIR="$GAME_DIR/BepInEx/plugins"
mkdir -p "$PLUGINS_DIR" 2>/dev/null || true

# --- Mods ---

installed=()
skipped=()

for mod_dir in "$SCRIPT_DIR"/mod*/; do
    [ -f "$mod_dir/install.sh" ] || continue
    [ -f "$mod_dir/mod.info" ] || continue

    mod_name="$(read_info "$mod_dir/mod.info" "name")"
    mod_dll="$(read_info "$mod_dir/mod.info" "dll")"
    mod_desc="$(read_info "$mod_dir/mod.info" "description")"
    mod_actions="$(read_info "$mod_dir/mod.info" "actions")"

    echo ""
    echo "--------------------------------------------"
    echo "$mod_name"
    echo ""
    echo "  $mod_desc"
    echo ""
    echo "  This will:"
    echo "    - $mod_actions"

    # Check if already installed
    if [ -f "$PLUGINS_DIR/$mod_dll" ]; then
        echo ""
        echo "  (Currently installed — will be overwritten with latest build)"
    fi
    echo "--------------------------------------------"

    if prompt "Install $mod_name?"; then
        echo ""
        bash "$mod_dir/build.sh"
        cp "$mod_dir/bin/Release/netstandard2.1/$mod_dll" "$PLUGINS_DIR/"
        echo "  -> Installed $mod_dll"
        installed+=("$mod_name")
    else
        skipped+=("$mod_name")
    fi
done

# --- Summary ---

echo ""
echo "============================================"
echo "  Installation Summary"
echo "============================================"

if [ ${#installed[@]} -gt 0 ]; then
    echo ""
    echo "  Installed:"
    for m in "${installed[@]}"; do
        echo "    + $m"
    done
fi

if [ ${#skipped[@]} -gt 0 ]; then
    echo ""
    echo "  Skipped:"
    for m in "${skipped[@]}"; do
        echo "    - $m"
    done
fi

echo ""
echo "  Plugins directory:"
ls "$PLUGINS_DIR/"*.dll 2>/dev/null | while read -r f; do echo "    $(basename "$f")"; done

print_launch_instructions "$GAME_DIR"
echo ""
