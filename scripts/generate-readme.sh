#!/usr/bin/env bash
# Generate a release README.md from mod.info files.
# Usage: ./scripts/generate-readme.sh [version]
# Output: writes to stdout
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
VERSION="${1:-dev}"

read_info() {
    local file="$1" key="$2"
    grep "^${key}=" "$file" 2>/dev/null | head -1 | sed "s/^${key}=//"
}

cat <<EOF
# IGTAP Mods v${VERSION}

A collection of mods for **IGTAP: an Incremental Game That's Also a Platformer Demo**.

## Included Mods

| Mod | DLL | Description |
|-----|-----|-------------|
EOF

for mod_dir in "$REPO_DIR"/mod*/; do
    case "$(basename "$mod_dir")" in *IMPORTED*) continue ;; esac
    [ -f "$mod_dir/mod.info" ] || continue

    mod_name="$(read_info "$mod_dir/mod.info" "name")"
    mod_dll="$(read_info "$mod_dir/mod.info" "dll")"
    mod_desc="$(read_info "$mod_dir/mod.info" "description")"
    [ -z "$mod_name" ] && continue

    echo "| $mod_name | \`$mod_dll\` | $mod_desc |"
done

echo ""
echo "## Dependencies Between Mods"
echo ""
echo "- **IGTAP Core** (\`IGTAPMod.dll\`) is required by all other mods"
echo "- **Fixed Timestep** (\`IGTAPFixedTimestep.dll\`) is required by Replay and Speedrun Timer"
echo "- **Speedrun Timer** (\`IGTAPSpeedrun.dll\`) is optional — Replay can integrate with it if present"
echo ""
echo "## Quick Install"
echo ""
echo "1. Download \`igtap-mods-v${VERSION}.zip\` from the release assets"

cat <<'EOF'
2. Extract the zip
3. Run the installer:
   ```bash
   chmod +x install-release.sh
   ./install-release.sh
   ```
   Or with auto-accept: `./install-release.sh -y`

The installer will:
- Detect your game installation (Steam)
- Install BepInEx 5.4.23.5 if not already present
- Copy all mod DLLs into `BepInEx/plugins/`

## Manual Install

### 1. Install BepInEx

Download [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) for your platform and extract it into the game directory.

### 2. Set Steam Launch Options

Right-click the game in Steam → Properties → Launch Options:

- **Linux/macOS:** `./run_bepinex.sh %command%`
- **Windows:** Set the path to `run_bepinex.sh %command%`

### 3. Install Mods

Copy the desired `.dll` files into `<game directory>/BepInEx/plugins/`.

**Important:** If you're picking individual mods rather than installing all of them, make sure to include their dependencies:
- All mods need `IGTAPMod.dll`
- `IGTAPReplay.dll` and `IGTAPSpeedrun.dll` also need `IGTAPFixedTimestep.dll`

### 4. Launch the Game

Start the game through Steam. Press **F8** to open the debug menu.

## Requirements

- [IGTAP: an Incremental Game That's Also a Platformer Demo](https://store.steampowered.com/app/IGTAP) (Steam)
- [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) (installed automatically by the installer)
EOF
