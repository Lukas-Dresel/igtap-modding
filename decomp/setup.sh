#!/usr/bin/env bash
# Install all dependencies needed for IGTAP data extraction
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# --- System-level dependencies ---
echo "=== Checking system dependencies ==="
NEED_APT=()

# python3.11
if ! command -v python3.11 &>/dev/null; then
    NEED_APT+=(python3.11 python3.11-venv python3.11-dev)
fi

# pip for python3.11
if ! python3.11 -m pip --version &>/dev/null 2>&1; then
    NEED_APT+=(python3-pip)
fi

# .NET SDK (for ilspycmd)
if ! command -v dotnet &>/dev/null; then
    NEED_APT+=(dotnet-sdk-8.0)
fi

if [ ${#NEED_APT[@]} -gt 0 ]; then
    echo "Installing system packages: ${NEED_APT[*]}"
    sudo apt-get update
    sudo apt-get install -y "${NEED_APT[@]}"
else
    echo "System dependencies OK"
fi

# --- Python dependencies ---
echo ""
echo "=== Installing Python dependencies ==="
python3.11 -m pip install --force-reinstall UnityPy Pillow brotli lz4

# --- .NET ILSpy decompiler ---
echo ""
echo "=== Installing .NET ILSpy decompiler ==="
if ! command -v ilspycmd &>/dev/null && ! [ -f "$HOME/.dotnet/tools/ilspycmd" ]; then
    dotnet tool install -g ilspycmd
else
    echo "ilspycmd already installed"
fi

echo ""
echo "=== All dependencies installed ==="
echo "You can now run:"
echo "  bash $SCRIPT_DIR/extract_all.sh"
