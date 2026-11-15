#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
temp_dir=$(mktemp -d)
cleanup() {
    rm -f -- "$temp_dir/bionic" "$temp_dir/newer" \
        "$temp_dir/bionic.nupkg" "$temp_dir/newer.nupkg" "$temp_dir/wrong-arch.nupkg"
    rmdir "$temp_dir"
}
trap cleanup EXIT

# Host startup objects would introduce a newer glibc requirement of their own.
printf '%s\n' \
    'extern int memfd_create(const char *, unsigned int); void _start(void) { (void)memfd_create("test", 0); }' |
    cc -x c -nostartfiles -no-pie -o "$temp_dir/bionic" -
printf '%s\n' \
    'extern int statx(int, const char *, int, unsigned int, void *); void _start(void) { char data[256]; (void)statx(-100, ".", 0, 0, data); }' |
    cc -x c -nostartfiles -no-pie -o "$temp_dir/newer" -

python3 - "$temp_dir" <<'PY'
from pathlib import Path
from sys import argv
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(argv[1])
for name, binary, rid in (
    ("bionic", "bionic", "linux-x64"),
    ("newer", "newer", "linux-x64"),
    ("wrong-arch", "bionic", "linux-arm64"),
):
    with ZipFile(root / f"{name}.nupkg", "w", ZIP_DEFLATED) as package:
        package.write(root / binary, f"tools/any/{rid}/dotnet-inspect")
PY

verify="$repo_root/eng/verify-linux-aot-abi.sh"
"$verify" "$temp_dir/bionic.nupkg" linux-x64

expect_failure() {
    local expected=$1
    shift
    local output
    if output=$("$verify" "$@" 2>&1); then
        echo "Unexpectedly accepted $1" >&2
        exit 1
    fi
    if [[ "$output" != *"$expected"* ]]; then
        echo "Unexpected failure: $output" >&2
        exit 1
    fi
    echo "$output"
}

expect_failure "requires GLIBC_2.28, newer than GLIBC_2.27" "$temp_dir/newer.nupkg" linux-x64
expect_failure "expected AArch64, found Advanced Micro Devices X86-64" "$temp_dir/wrong-arch.nupkg" linux-arm64
