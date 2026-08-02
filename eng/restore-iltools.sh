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
#   local:  export PATH="$(eng/restore-iltools.sh --mdv | tr '\n' ':')$PATH"
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
    rid="$(dotnet --info | sed -n 's/^[ ]*RID:[ ]*//p' | head -1)"
    [ -n "$rid" ] || { echo "error: could not determine the host RID; pass --rid." >&2; exit 1; }
fi

root="$(git rev-parse --show-toplevel)"
packages_dir="$root/artifacts/iltools/packages"

ilasm_dir="$packages_dir/runtime.$rid.microsoft.netcore.ilasm/$ILTOOLS_VERSION/runtimes/$rid/native"
ildasm_dir="$packages_dir/runtime.$rid.microsoft.netcore.ildasm/$ILTOOLS_VERSION/runtimes/$rid/native"

if [ ! -x "$ilasm_dir/ilasm" ] || [ ! -x "$ildasm_dir/ildasm" ]; then
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
for tool in "$ilasm_dir/ilasm" "$ildasm_dir/ildasm"; do
    [ -x "$tool" ] || { echo "error: expected an executable at $tool after restore." >&2; exit 1; }
done

echo "$ilasm_dir"
echo "$ildasm_dir"

if [ "$want_mdv" -eq 1 ]; then
    tools_dir="${DOTNET_TOOLS_PATH:-$HOME/.dotnet/tools}"

    if [ ! -x "$tools_dir/mdv" ]; then
        echo "Installing the mdv global tool..." >&2
        # mdv ships prerelease-only from the dotnet-tools feed; it is not on
        # nuget.org under any id.
        dotnet tool install mdv --global --prerelease --add-source "$DOTNET_TOOLS_FEED" >&2
    fi

    [ -x "$tools_dir/mdv" ] || { echo "error: expected an executable at $tools_dir/mdv after install." >&2; exit 1; }
    echo "$tools_dir"
fi
