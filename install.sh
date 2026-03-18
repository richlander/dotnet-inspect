#!/bin/sh
# Installs dotnet-inspect from local source using dotnet-install.
# Usage: ./install.sh
#
# Uses an existing dotnet-install (if available) or installs it as a
# temporary global tool, then builds dotnet-inspect from the local
# source tree into ~/.dotnet/bin/.

set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== Installing dotnet-inspect from source ==="

_used_global_tool=no

# Use existing dotnet-install if available; otherwise install the global tool temporarily
if ! command -v dotnet-install > /dev/null 2>&1; then
    echo "Installing dotnet-install global tool..."
    dotnet tool install -g dotnet-install
    _used_global_tool=yes
fi

# Install dotnet-inspect from local source
dotnet-install "$SCRIPT_DIR/src/dotnet-inspect"

# Remove the temporary global tool if we installed it
if [ "$_used_global_tool" = "yes" ]; then
    echo "Removing temporary global tool..."
    dotnet tool uninstall -g dotnet-install
fi
