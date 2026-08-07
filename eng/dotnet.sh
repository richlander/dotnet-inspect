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
    installer="$(mktemp "${TMPDIR:-/tmp}/get-dotnetup.XXXXXX")"
    bootstrap_dir="$(mktemp -d "$install_dir/.dotnetup-bootstrap.XXXXXX")"
    trap 'rm -f "$installer"; rm -rf "$bootstrap_dir"' EXIT
    curl -fsSL --retry 3 https://aka.ms/dotnetup/get-dotnetup.sh -o "$installer"
    if ! bootstrap_output="$(bash "$installer" --install-dir "$bootstrap_dir" 2>&1)"; then
        printf '%s\n' "$bootstrap_output" >&2
        exit 1
    fi
    [ -x "$bootstrap_dir/dotnetup" ] ||
        { echo "error: dotnetup installation did not produce an executable." >&2; exit 1; }
    mv -f "$bootstrap_dir/dotnetup" "$dotnetup"
    rm -f "$installer"
    rm -rf "$bootstrap_dir"
    trap - EXIT
fi

if ! install_output="$("$dotnetup" sdk install 11 --interactive false --no-progress 2>&1)"; then
    printf '%s\n' "$install_output" >&2
    exit 1
fi
exec "$dotnetup" dotnet -- "$@"
