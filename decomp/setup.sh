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

# .NET 10 preview SDK (for imported mods using C# 14)
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    NEED_DOTNET10=true
else
    NEED_DOTNET10=false
fi

if [ ${#NEED_APT[@]} -gt 0 ]; then
    echo "Installing system packages: ${NEED_APT[*]}"
    sudo apt-get update
    sudo apt-get install -y "${NEED_APT[@]}"
else
    echo "System dependencies OK"
fi

# --- .NET 10 preview SDK ---
if $NEED_DOTNET10; then
    echo ""
    echo "=== Installing .NET 10 preview SDK (for C# 14 support) ==="
    curl -sSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --quality preview --install-dir "$HOME/.dotnet"
    rm -f /tmp/dotnet-install.sh
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
else
    echo ".NET 10 SDK: already installed"
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
