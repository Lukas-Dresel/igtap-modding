#!/usr/bin/env bash
# Standalone installer for pre-built IGTAP mods.
# Ships with each GitHub Release — does NOT require the source repo.
#
# Usage: ./install-release.sh [-y] [/path/to/game]
#   -y    Auto-accept all prompts (non-interactive)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

GAME_NAME="IGTAP an Incremental Game That's Also a Platformer Demo"
BEPINEX_VERSION="5.4.23.5"

# --- Parse flags ---
AUTO_YES=false
GAME_ARG=""
for arg in "$@"; do
    case "$arg" in
        -y|--yes) AUTO_YES=true ;;
        *) GAME_ARG="$arg" ;;
    esac
done

# --- OS detection ---
detect_os() {
    case "$(uname -s)" in
        Linux*)  echo "linux" ;;
        Darwin*) echo "macos" ;;
        MINGW*|MSYS*|CYGWIN*) echo "windows" ;;
        *) echo "unknown" ;;
    esac
}
OS="$(detect_os)"

# --- Find game directory ---
find_game_dir() {
    if [ -n "${1:-}" ] && [ -d "$1" ]; then
        echo "$1"
        return
    fi
    local candidates=()
    case "$OS" in
        linux)
            candidates=(
                "$HOME/.steam/steam/steamapps/common/$GAME_NAME"
                "$HOME/.local/share/Steam/steamapps/common/$GAME_NAME"
                "$HOME/.steam/debian-installation/steamapps/common/$GAME_NAME"
            )
            ;;
        macos)
            candidates=(
                "$HOME/Library/Application Support/Steam/steamapps/common/$GAME_NAME"
            )
            ;;
        windows)
            candidates=(
                "C:/Program Files (x86)/Steam/steamapps/common/$GAME_NAME"
                "C:/Program Files/Steam/steamapps/common/$GAME_NAME"
                "D:/SteamLibrary/steamapps/common/$GAME_NAME"
                "E:/SteamLibrary/steamapps/common/$GAME_NAME"
            )
            ;;
    esac
    for dir in "${candidates[@]}"; do
        if [ -d "$dir" ]; then
            echo "$dir"
            return
        fi
    done
    echo ""
}

# --- Prompt helper ---
prompt() {
    local question="$1"
    if $AUTO_YES; then
        echo "$question -> auto-accepted (-y)"
        return 0
    fi
    while true; do
        printf "\n%s [y]es / [n]o: " "$question"
        read -r choice
        case "$choice" in
            y|Y|yes|Yes) return 0 ;;
            n|N|no|No) return 1 ;;
            *) echo "  Please enter y or n." ;;
        esac
    done
}

# --- Install BepInEx ---
ensure_bepinex() {
    local game_dir="$1"
    if [ -d "$game_dir/BepInEx/core" ] && [ -f "$game_dir/BepInEx/core/BepInEx.dll" ]; then
        echo "BepInEx $BEPINEX_VERSION: already installed."
        return
    fi

    local arch=""
    case "$OS" in
        linux)   arch="linux_x64" ;;
        macos)   arch="macos_x64" ;;
        windows) arch="win_x64" ;;
        *)
            echo "ERROR: Unsupported OS: $OS"
            exit 1
            ;;
    esac

    local zip_name="BepInEx_${arch}_${BEPINEX_VERSION}.zip"
    local url="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/${zip_name}"
    local tmp_zip="$game_dir/$zip_name"

    echo "Downloading BepInEx $BEPINEX_VERSION..."
    if command -v curl &>/dev/null; then
        curl -fSL -o "$tmp_zip" "$url"
    elif command -v wget &>/dev/null; then
        wget -q -O "$tmp_zip" "$url"
    else
        echo "ERROR: Neither curl nor wget found."
        exit 1
    fi

    echo "Extracting..."
    if command -v unzip &>/dev/null; then
        unzip -o "$tmp_zip" -d "$game_dir"
    elif command -v powershell &>/dev/null; then
        powershell -Command "Expand-Archive -Force '$tmp_zip' '$game_dir'"
    else
        echo "ERROR: No unzip tool found."
        exit 1
    fi

    rm -f "$tmp_zip"
    [ "$OS" != "windows" ] && [ -f "$game_dir/run_bepinex.sh" ] && chmod u+x "$game_dir/run_bepinex.sh"
    mkdir -p "$game_dir/BepInEx/plugins"
    echo "BepInEx installed."
}

# ===========================================================

echo "============================================"
echo "  IGTAP Mod Installer"
echo "============================================"
echo ""

GAME_DIR="$(find_game_dir "${GAME_ARG:-}")"
if [ -z "$GAME_DIR" ]; then
    echo "ERROR: Could not find the game directory."
    echo "Usage: $0 [-y] [/path/to/game]"
    echo "Expected: $GAME_NAME"
    exit 1
fi
echo "Found game at:"
echo "  $GAME_DIR"
echo ""

# --- BepInEx ---
if ! prompt "Install/update BepInEx $BEPINEX_VERSION?"; then
    echo "Skipping BepInEx. Mods will not work without it."
else
    ensure_bepinex "$GAME_DIR"
fi

PLUGINS_DIR="$GAME_DIR/BepInEx/plugins"
mkdir -p "$PLUGINS_DIR" 2>/dev/null || true

# --- Install mod DLLs ---
echo ""
echo "Installing mod DLLs..."

dll_count=0
for dll in "$SCRIPT_DIR"/*.dll; do
    [ -f "$dll" ] || continue
    cp "$dll" "$PLUGINS_DIR/"
    echo "  + $(basename "$dll")"
    dll_count=$((dll_count + 1))
done

if [ "$dll_count" -eq 0 ]; then
    echo "  WARNING: No DLL files found next to this script."
    echo "  Make sure the mod DLLs are in the same directory as install-release.sh"
    exit 1
fi

# --- Summary ---
echo ""
echo "============================================"
echo "  Done! Installed $dll_count mod(s)."
echo "============================================"
echo ""
echo "Plugins directory:"
ls "$PLUGINS_DIR/"*.dll 2>/dev/null | while read -r f; do echo "  $(basename "$f")"; done

echo ""
case "$OS" in
    linux|macos)
        echo "Set Steam launch options:"
        echo "  ./run_bepinex.sh %command%"
        ;;
    windows)
        echo "Set Steam launch options:"
        echo "  \"$GAME_DIR/run_bepinex.sh\" %command%"
        ;;
esac
echo ""
