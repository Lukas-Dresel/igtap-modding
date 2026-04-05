#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_URL="https://github.com/pseudo-psychic/IGTAS.git"
SRC_DIR="$SCRIPT_DIR/src"

echo "=== Building IGTAS (imported) ==="

# Use .NET 10 SDK if installed in user dir
if [ -d "$HOME/.dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
fi

# Clone or update the repo
if [ -d "$SRC_DIR/.git" ]; then
    echo "Updating IGTAS repo..."
    git -C "$SRC_DIR" pull --ff-only
else
    echo "Cloning IGTAS repo..."
    git clone "$REPO_URL" "$SRC_DIR"
fi

# Patch hardcoded Windows HintPath to use MSBuild variable
sed -i 's|<HintPath>C:\\Program Files (x86)\\Steam\\steamapps\\common\\IGTAP_TAS\\IGTAP_Data\\Managed\\Unity.InputSystem.dll</HintPath>|<HintPath>$(ManagedDir)/Unity.InputSystem.dll</HintPath>|' "$SRC_DIR/IGTAS.csproj"

# Build
dotnet build -c Release "$SRC_DIR/IGTAS.csproj"

# Stage output
OUT_DIR="$SCRIPT_DIR/bin/Release/netstandard2.1"
mkdir -p "$OUT_DIR"
cp "$SRC_DIR"/bin/Release/net472/IGTAS.dll "$OUT_DIR/"
echo "Staged DLLs:"
ls "$OUT_DIR/"*.dll
