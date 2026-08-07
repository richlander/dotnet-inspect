#!/usr/bin/env bash
# Runs repository commands with the required preview SDK without replacing or
# shadowing a centrally managed dotnet installation.
set -euo pipefail

if [ "$#" -eq 0 ]; then
    echo "Usage: eng/dotnet.sh <dotnet arguments>" >&2
    exit 2
fi

install_dir="${DOTNETUP_INSTALL_DIR:-$HOME/.local/bin}"
dotnetup="$install_dir/dotnetup"

if [ ! -x "$dotnetup" ]; then
    command -v curl > /dev/null ||
        { echo "error: curl is required to install dotnetup." >&2; exit 1; }
    mkdir -p "$install_dir"
    installer="$(mktemp "${TMPDIR:-/tmp}/get-dotnetup.XXXXXX.sh")"
    trap 'rm -f "$installer"' EXIT
    curl -fsSL --retry 3 https://aka.ms/dotnetup/get-dotnetup.sh -o "$installer"
    bash "$installer" --install-dir "$install_dir"
    rm -f "$installer"
    trap - EXIT
fi

"$dotnetup" sdk install 11 --interactive false --no-progress
exec "$dotnetup" dotnet "$@"
