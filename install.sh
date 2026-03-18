#!/bin/bash
# Installs dotnet-inspect from local source using dotnet-install.
# Usage: ./install.sh
#
# Installs dotnet-install as a global tool if not already available,
# then uses it to build and install dotnet-inspect to ~/.dotnet/bin/.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== Installing dotnet-inspect from source ==="

# Ensure dotnet-install is available
if ! command -v dotnet-install &> /dev/null; then
    echo "Installing dotnet-install global tool..."
    dotnet tool install -g dotnet-install
fi

# Install dotnet-inspect from local source
dotnet-install "$SCRIPT_DIR/src/dotnet-inspect"
