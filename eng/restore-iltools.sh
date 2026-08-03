#!/usr/bin/env bash
# Acquires the optional external tools that several test suites use as
# independent oracles, and prints the directories to put on PATH.
#
#   ilasm/ildasm  the native CoreCLR IL assembler/disassembler, restored from
#                 the runtime.<rid>.Microsoft.NETCore.IL{,D}Asm packages.
#   mdv           Roslyn's metadata visualizer (--mdv), installed as a .NET
#                 global tool from the dnceng dotnet-tools feed.
#
# Tests that need these tools skip when they are absent, so a machine without
# them reports a green run that proved less than it appears to. Restoring them
# is the difference between "nothing failed" and "the oracle agreed".
#
# Diagnostics go to stderr and PATH entries to stdout, one per line, so the
# output can be consumed directly:
#
#   CI:     eng/restore-iltools.sh >> "$GITHUB_PATH"
#   local:  source eng/activate-iltools.sh --mdv
#
# Do not assemble PATH from this script's output by hand. A child process
# cannot change its parent's PATH, so the joining has to happen in the calling
# shell, and every way of getting it wrong is silent -- `export PATH="$(...)"`
# reports export's status rather than this script's, a lost trailing newline
# glues the last directory to the first pre-existing PATH entry, and empty
# output prepends an empty PATH entry, which means the current directory. Each
# leaves a plausible-looking PATH with no oracles on it, which is the exact
# failure this script exists to prevent. eng/activate-iltools.sh is the one
# tested copy of that logic; IlToolsActivationTests is its gate.
#
# Packages land in artifacts/iltools (gitignored), so a re-run is a no-op.
set -euo pipefail

# Pinned so every workflow and every developer measures against the same
# assembler. Bumping it here bumps it everywhere.
ILTOOLS_VERSION=11.0.0-preview.1.26104.118

DOTNET11_FEED=https://dnceng.pkgs.visualstudio.com/public/_packaging/dotnet11/nuget/v3/index.json
NUGET_FEED=https://api.nuget.org/v3/index.json
DOTNET_TOOLS_FEED=https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json

rid=""
want_mdv=0

usage() {
    cat >&2 <<'EOF'
Usage: eng/restore-iltools.sh [--rid <rid>] [--mdv]

  --rid <rid>  Runtime identifier to restore ilasm/ildasm for
               (default: the host RID reported by `dotnet --info`).
  --mdv        Also install the `mdv` global tool.

Prints the directories to add to PATH, one per line.
EOF
}

while [ $# -gt 0 ]; do
    case "$1" in
        --rid)
            [ $# -ge 2 ] || { echo "error: --rid requires a value." >&2; exit 2; }
            rid="$2"
            shift 2
            ;;
        --mdv)
            want_mdv=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "error: unknown argument '$1'." >&2
            usage
            exit 2
            ;;
    esac
done

command -v dotnet > /dev/null || { echo "error: dotnet is not on PATH." >&2; exit 1; }

if [ -z "$rid" ]; then
    # `dotnet --info` emits CRLF on Windows, and a retained \r silently corrupts
    # the package id (runtime.win-x64\r.Microsoft.NETCore.ILAsm), so strip it.
    rid="$(dotnet --info | sed -n 's/^[ ]*RID:[ ]*//p' | head -1 | tr -d '\r' | tr -d '[:space:]')"
    [ -n "$rid" ] || { echo "error: could not determine the host RID; pass --rid." >&2; exit 1; }
fi

root="$(git rev-parse --show-toplevel)"
packages_dir="$root/artifacts/iltools/packages"

# On Git Bash, `git rev-parse --show-toplevel` reports a Windows path
# (C:/src/repo). Emitting that verbatim would break the documented
# newline-to-colon PATH assembly, because the drive colon reads as a PATH
# separator and splits every directory into "C" and "/src/repo/...". Emit MSYS
# form (/c/src/repo) where cygpath exists; elsewhere this is a no-op.
emit_path() {
    local emitted
    if [ -n "$cygpath" ]; then
        emitted="$("$cygpath" -u "$1")"
    else
        emitted="$1"
    fi

    # Never emit a blank or whitespace-only line. A consumer joining these with
    # ':' would turn one into an empty PATH entry, which means the current
    # directory -- a silent correctness and safety hazard rather than a visible
    # failure. Whitespace-only counts: it is equally unusable and equally quiet.
    case "$emitted" in
        *[![:space:]]*) ;;
        *) echo "error: refusing to emit a blank path for '$1'." >&2; exit 1 ;;
    esac
    printf '%s\n' "$emitted"
}

cygpath="$(command -v cygpath 2> /dev/null || true)"

# The package payload's extension follows the RID being restored, not the host:
# restoring win-x64 from Linux still yields ilasm.exe.
case "$rid" in
    win-*) tool_ext=".exe" ;;
    *)     tool_ext="" ;;
esac

ilasm_dir="$packages_dir/runtime.$rid.microsoft.netcore.ilasm/$ILTOOLS_VERSION/runtimes/$rid/native"
ildasm_dir="$packages_dir/runtime.$rid.microsoft.netcore.ildasm/$ILTOOLS_VERSION/runtimes/$rid/native"

if [ ! -x "$ilasm_dir/ilasm$tool_ext" ] || [ ! -x "$ildasm_dir/ildasm$tool_ext" ]; then
    echo "Restoring ilasm/ildasm $ILTOOLS_VERSION for $rid..." >&2

    proj_dir="$(mktemp -d)"
    trap 'rm -rf "$proj_dir"' EXIT
    cat > "$proj_dir/iltools.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageDownload Include="runtime.$rid.Microsoft.NETCore.ILAsm" Version="[$ILTOOLS_VERSION]" />
    <PackageDownload Include="runtime.$rid.Microsoft.NETCore.ILDAsm" Version="[$ILTOOLS_VERSION]" />
  </ItemGroup>
</Project>
EOF

    dotnet restore "$proj_dir/iltools.csproj" \
        --packages "$packages_dir" \
        --source "$DOTNET11_FEED" \
        --source "$NUGET_FEED" >&2

    chmod +x "$ilasm_dir"/* "$ildasm_dir"/* 2> /dev/null || true
fi

# Verify rather than trust the restore. A feed that serves a package whose
# layout moved would otherwise put a non-existent directory on PATH, and every
# test that needs these tools would skip -- reporting the same green run as a
# machine that never tried.
for tool in "$ilasm_dir/ilasm$tool_ext" "$ildasm_dir/ildasm$tool_ext"; do
    [ -x "$tool" ] || { echo "error: expected an executable at $tool after restore." >&2; exit 1; }
done

emit_path "$ilasm_dir"
emit_path "$ildasm_dir"

if [ "$want_mdv" -eq 1 ]; then
    # `dotnet tool install --global` installs under $DOTNET_CLI_HOME/.dotnet/tools,
    # falling back to $HOME. The shim is mdv.exe on a Windows host -- and unlike
    # the RID-keyed packages above, that follows the host, not --rid.
    tools_dir="${DOTNET_CLI_HOME:-$HOME}/.dotnet/tools"

    if [ ! -x "$tools_dir/mdv" ] && [ ! -x "$tools_dir/mdv.exe" ]; then
        echo "Installing the mdv global tool..." >&2
        # mdv ships prerelease-only from the dotnet-tools feed; it is not on
        # nuget.org under any id.
        dotnet tool install mdv --global --prerelease --add-source "$DOTNET_TOOLS_FEED" >&2
    fi

    if [ ! -x "$tools_dir/mdv" ] && [ ! -x "$tools_dir/mdv.exe" ]; then
        echo "error: expected an executable at $tools_dir/mdv after install." >&2
        exit 1
    fi
    emit_path "$tools_dir"
fi
