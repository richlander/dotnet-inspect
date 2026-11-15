#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <package.nupkg> <linux-x64|linux-arm64>" >&2
    exit 2
fi

package=$1
rid=$2
case "$rid" in
    linux-x64) expected_machine="Advanced Micro Devices X86-64" ;;
    linux-arm64) expected_machine="AArch64" ;;
    *) echo "Unsupported RID: $rid" >&2; exit 2 ;;
esac

binary=$(mktemp)
trap 'rm -f -- "$binary"' EXIT
unzip -p "$package" "tools/any/$rid/dotnet-inspect" > "$binary"

machine=$(readelf --file-header "$binary" | sed -n 's/^[[:space:]]*Machine:[[:space:]]*//p')
if [ "$machine" != "$expected_machine" ]; then
    echo "$package: expected $expected_machine, found ${machine:-no ELF machine}" >&2
    exit 1
fi

versions=$(readelf --version-info --wide "$binary" |
    sed -n '/Version needs section/,$s/.*Name: \(GLIBC_[^[:space:]]*\).*/\1/p')
if [ -z "$versions" ]; then
    echo "$package: no GLIBC version requirements found" >&2
    exit 1
fi

while IFS= read -r version; do
    if [[ ! "$version" =~ ^GLIBC_[0-9]+(\.[0-9]+)+$ ]]; then
        echo "$package: unsupported GLIBC version requirement $version" >&2
        exit 1
    fi
done <<< "$versions"

maximum=$(printf '%s\n' "$versions" | sort -V | tail -n 1)
if [ "$(printf '%s\n' "$maximum" GLIBC_2.27 | sort -V | tail -n 1)" != GLIBC_2.27 ]; then
    echo "$package: requires $maximum, newer than GLIBC_2.27" >&2
    exit 1
fi

echo "$package: $rid requires at most $maximum"
