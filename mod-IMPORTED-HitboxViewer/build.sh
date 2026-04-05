#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_URL="https://github.com/Rostmoment/HitboxViewer.git"
SRC_DIR="$SCRIPT_DIR/src"

echo "=== Building HitboxViewer (imported) ==="

# Use .NET 10 SDK if installed in user dir
if [ -d "$HOME/.dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
fi

# Clone or update the repo
if [ -d "$SRC_DIR/.git" ]; then
    echo "Updating HitboxViewer repo..."
    git -C "$SRC_DIR" pull --ff-only
else
    echo "Cloning HitboxViewer repo..."
    git clone "$REPO_URL" "$SRC_DIR"
fi

# Build
dotnet build -c Release "$SRC_DIR/HitboxViewer.sln"

# Stage output to standard location for install_all.sh compatibility
OUT_DIR="$SCRIPT_DIR/bin/Release/netstandard2.1"
mkdir -p "$OUT_DIR"
cp "$SRC_DIR"/bin/Release/net35/HitboxViewer.dll "$OUT_DIR/"
cp "$SRC_DIR"/bin/Release/net35/UniverseLib*.dll "$OUT_DIR/"
echo "Staged DLLs:"
ls "$OUT_DIR/"*.dll
