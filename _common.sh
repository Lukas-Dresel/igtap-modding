#!/usr/bin/env bash
# Shared helpers for IGTAP mod build/install scripts.
# Source this: source "$SCRIPT_DIR/../_common.sh"

GAME_NAME="IGTAP an Incremental Game That's Also a Platformer Demo"
BEPINEX_VERSION="5.4.23.5"

detect_os() {
    case "$(uname -s)" in
        Linux*)  echo "linux" ;;
        Darwin*) echo "macos" ;;
        MINGW*|MSYS*|CYGWIN*) echo "windows" ;;
        *) echo "unknown" ;;
    esac
}

OS="$(detect_os)"

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

check_game_dir() {
    if [ -z "${1:-}" ]; then
        echo "ERROR: Could not find the game directory."
        echo "Usage: $0 [/path/to/game]"
        echo "Expected: $GAME_NAME"
        exit 1
    fi
    echo "Game: $1"
}

ensure_bepinex() {
    local game_dir="$1"

    if [ -d "$game_dir/BepInEx/core" ] && [ -f "$game_dir/BepInEx/core/BepInEx.dll" ]; then
        return
    fi

    echo "=== Installing BepInEx $BEPINEX_VERSION ==="

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

    echo "Downloading $url ..."
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

    if [ "$OS" != "windows" ] && [ -f "$game_dir/run_bepinex.sh" ]; then
        chmod u+x "$game_dir/run_bepinex.sh"
    fi

    mkdir -p "$game_dir/BepInEx/plugins"
    echo "BepInEx installed."
}

print_launch_instructions() {
    local game_dir="$1"
    echo ""
    case "$OS" in
        linux|macos)
            echo "Set Steam launch options:"
            echo "  ./run_bepinex.sh %command%"
            ;;
        windows)
            echo "Set Steam launch options:"
            echo "  \"$game_dir/run_bepinex.sh\" %command%"
            ;;
    esac
}
