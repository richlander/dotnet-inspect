#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ] || [ ! -f "$1" ]; then
    echo "Usage: $0 <fresh-linux-x64-package.nupkg>" >&2
    exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package=$(realpath "$1")
temporary=$(mktemp -d)
trap 'rm -rf -- "$temporary"' EXIT

verify="$repo_root/eng/verify-linux-x64-aot-runtime.sh"
echo "Positive: freshly packed product"
"$verify" "$package"

# statx first entered glibc at 2.28. Avoid host startup objects, which would
# introduce their own unrelated version requirements.
printf '%s\n' \
    'extern int statx(int, const char *, int, unsigned int, void *); void _start(void) { char data[256]; (void)statx(-100, ".", 0, 0, data); }' |
    cc -x c -nostartfiles -no-pie -o "$temporary/newer" -

python3 - "$package" "$temporary/newer" "$temporary/newer.nupkg" "$temporary/missing.nupkg" <<'PY'
from pathlib import Path
from sys import argv
from zipfile import ZipFile

source, newer, target, missing = map(Path, argv[1:])
entry = "tools/any/linux-x64/dotnet-inspect"
with ZipFile(source) as original, ZipFile(target, "w") as replacement, ZipFile(missing, "w") as without_binary:
    if entry not in original.namelist():
        raise SystemExit(f"missing packaged executable: {entry}")
    for member in original.infolist():
        content = original.read(member.filename)
        replacement.writestr(
            member, newer.read_bytes() if member.filename == entry
            else content)
        if member.filename != entry:
            without_binary.writestr(member, content)
PY

echo "Negative: packaged executable linked against glibc 2.28"
if output=$("$verify" "$temporary/newer.nupkg" 2>&1); then
    echo "Runtime gate accepted the glibc 2.28 executable" >&2
    exit 1
else
    status=$?
fi
case "$output" in
    *"GLIBC_2.28"*"not found"*) echo "Host launcher exited $status: $output" ;;
    *) echo "Unexpected negative-gate failure: $output" >&2; exit 1 ;;
esac

echo "Negative: missing packaged executable"
if output=$("$verify" "$temporary/missing.nupkg" 2>&1); then
    echo "Runtime gate accepted a package without its executable" >&2
    exit 1
else
    status=$?
fi
case "$output" in
    *"filename not matched"*) echo "Host launcher exited $status: $output" ;;
    *) echo "Unexpected missing-payload failure: $output" >&2; exit 1 ;;
esac
