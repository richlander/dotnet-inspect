#!/bin/bash
# Packs and installs dotnet-inspect as a global tool from local source.
# Usage: ./install.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$SCRIPT_DIR/src/dotnet-inspect/dotnet-inspect.csproj"
PACK_OUTPUT="$SCRIPT_DIR/artifacts/packages"

echo "=== Installing dotnet-inspect from source ==="

# Uninstall if already installed
if dotnet tool list -g | grep -q dotnet-inspect; then
    echo "Uninstalling existing dotnet-inspect..."
    dotnet tool uninstall -g dotnet-inspect
fi

# Clean previous packages
rm -rf "$PACK_OUTPUT"

# Pack
echo "Packing..."
dotnet pack "$PROJECT" -o "$PACK_OUTPUT" -bl:"$PACK_OUTPUT/pack.binlog"
dotnet pack "$PROJECT" -o "$PACK_OUTPUT" --ucr -bl:"$PACK_OUTPUT/pack-ucr.binlog"

# Install from local packages
echo "Installing..."
dotnet tool install -g dotnet-inspect --add-source "$PACK_OUTPUT"

echo ""
echo "Done. Run 'dotnet-inspect --version' to verify."
