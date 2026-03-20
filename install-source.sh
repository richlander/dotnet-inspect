#!/bin/sh
# Install dotnet-inspect from local source (developer workflow).
# Usage: ./install-source.sh
#
# Installs dotnet-inspect from the local source tree using
# dotnet-install. If dotnet-install is not available, it is
# installed first via `dotnet tool install -g`.
#
# Requires the .NET SDK.
#
# Environment variables:
#   DOTNET_INSTALL_DIR   Override the install directory

set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

main() {
    ensure_dotnet_install

    say "installing dotnet-inspect from source..."
    ensure dotnet-install "$SCRIPT_DIR/src/dotnet-inspect"

    say "done"
}

ensure_dotnet_install() {
    if command -v dotnet-install > /dev/null 2>&1; then
        return
    fi

    need_cmd dotnet
    say "dotnet-install not found; installing via dotnet tool..."
    ensure dotnet tool install -g dotnet-install
}

say() {
    printf 'dotnet-inspect: %s\n' "$1" 1>&2
}

err() {
    say "error: $1"
    exit 1
}

need_cmd() {
    if ! command -v "$1" > /dev/null 2>&1; then
        err "need '$1' (command not found)"
    fi
}

ensure() {
    if ! "$@"; then err "command failed: $*"; fi
}

main "$@" || exit 1
