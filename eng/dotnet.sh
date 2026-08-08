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

refresh_failed=false
if install_output="$("$dotnetup" sdk install 11 --interactive false --no-progress 2>&1)"; then
    install_exit=0
else
    install_exit=$?
    refresh_failed=true
    sdk_output="$("$dotnetup" dotnet -- --list-sdks 2>&1)" || sdk_output=""
    if ! printf '%s\n' "$sdk_output" | grep -Eq '^11\.'; then
        printf '%s\n' "$install_output" >&2
        exit "$install_exit"
    fi
fi

if selected_output="$("$dotnetup" dotnet -- --version 2>&1)"; then
    selected_exit=0
else
    selected_exit=$?
fi
if [ "$selected_exit" -ne 0 ] ||
    ! printf '%s\n' "$selected_output" | grep -Eq '^11\.'; then
    if [ "$refresh_failed" = true ]; then
        printf '%s\n' "$install_output" >&2
        echo "dotnetup command isolation did not select the required .NET 11 SDK." >&2
        exit "$install_exit"
    fi
    printf '%s\n' "$selected_output" >&2
    echo "dotnetup command isolation did not select the required .NET 11 SDK." >&2
    if [ "$selected_exit" -ne 0 ]; then
        exit "$selected_exit"
    fi
    exit 1
fi

if [ "$refresh_failed" = true ]; then
    echo "warning: dotnetup could not update .NET 11; using the installed .NET 11 SDK." >&2
fi
exec "$dotnetup" dotnet -- "$@"
