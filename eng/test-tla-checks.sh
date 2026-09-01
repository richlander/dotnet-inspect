#!/usr/bin/env bash
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
temporary=$(mktemp -d)
trap 'rm -rf "$temporary"' EXIT

fixture="$temporary/fixture"
mkdir -p \
  "$fixture/eng" \
  "$fixture/docs/design/models/override" \
  "$fixture/docs/design/models/example" \
  "$fixture/docs/design/models/foundation" \
  "$fixture/docs/design/models/middle" \
  "$fixture/docs/design/models/top" \
  "$fixture/docs/models/other" \
  "$fixture/docs/models/nested/sub"
touch \
  "$fixture/docs/design/models/override/Override.tla" \
  "$fixture/docs/design/models/override/Override.cfg" \
  "$fixture/docs/design/models/example/Example.tla" \
  "$fixture/docs/design/models/example/Example.cfg" \
  "$fixture/docs/design/models/foundation/Foundation.tla" \
  "$fixture/docs/design/models/foundation/Foundation.cfg" \
  "$fixture/docs/design/models/middle/Middle.tla" \
  "$fixture/docs/design/models/middle/Middle.cfg" \
  "$fixture/docs/design/models/top/Top.tla" \
  "$fixture/docs/design/models/top/Top.cfg" \
  "$fixture/docs/models/other/Other.tla" \
  "$fixture/docs/models/other/Other.cfg" \
  "$fixture/docs/models/nested/Nested.tla" \
  "$fixture/docs/models/nested/Nested.cfg"
printf '%s\n' \
  'docs/design/models/override/Override.cfg=Override' \
  > "$fixture/eng/tla-module-overrides.txt"
printf 'Parsing file %s\n' \
  "$fixture/docs/design/models/foundation/Foundation.tla" \
  > "$fixture/docs/design/models/middle/Middle.deps"
printf 'Parsing file %s\nParsing file %s\n' \
  "$fixture/docs/design/models/middle/Middle.tla" \
  "$fixture/docs/design/models/foundation/Foundation.tla" \
  > "$fixture/docs/design/models/top/Top.deps"

mkdir -p "$temporary/bin"
touch "$temporary/tla2tools.jar"
cat > "$temporary/bin/java" <<'EOF'
#!/bin/sh
printf '%s\n' "$*" >> "$TLA_TEST_JAVA_LOG"
case " $* " in
  *" tla2sany.SANY "*)
    last=
    for argument in "$@"; do
      last="$argument"
    done
    printf 'Parsing file %s\n' "$last"
    dependencies="${last%.tla}.deps"
    if [ -f "$dependencies" ]; then
      while IFS= read -r dependency; do
        printf '%s\n' "$dependency"
        dependency_path=${dependency#Parsing file }
        if [ ! -f "$dependency_path" ]; then
          exit 1
        fi
      done < "$dependencies"
    fi
    ;;
esac
exit 0
EOF
chmod +x "$temporary/bin/java"

run_scoped_check() {
  PATH="$temporary/bin:$PATH" \
  TLA_TEST_JAVA_LOG="$temporary/java.log" \
  TLA_TOOLS_JAR="$temporary/tla2tools.jar" \
    "$repo/eng/run-tla-checks.sh" --changed-files0
}

run_all_check() {
  PATH="$temporary/bin:$PATH" \
  TLA_TEST_JAVA_LOG="$temporary/java.log" \
  TLA_TOOLS_JAR="$temporary/tla2tools.jar" \
    "$repo/eng/run-tla-checks.sh" --all
}

cd "$fixture"

output=$(
  printf '%s\0' \
    docs/design/models/example/Example.tla |
    run_scoped_check
)

case "$output" in
  *"::group::docs/design/models/example"*) ;;
  *)
    echo "Scoped TLA+ check did not select the changed model directory." >&2
    exit 1
    ;;
esac
case "$output" in
  *"docs/models/other"*)
    echo "Scoped TLA+ check evaluated an unrelated model directory." >&2
    exit 1
    ;;
esac
case "$output" in
  *"Checked 1 module(s) and 1 configuration(s)"*) ;;
  *)
    echo "Scoped TLA+ check did not run the selected model exactly once." >&2
    exit 1
    ;;
esac

tlc_calls=$(grep -c 'tlc2.TLC' "$temporary/java.log")
if [ "$tlc_calls" != "1" ]; then
  echo "Expected one TLC invocation, found $tlc_calls." >&2
  exit 1
fi
if ! grep -Fq -- "-DTLA-Library=$fixture/docs/design/models/example" \
  "$temporary/java.log"; then
  echo "TLA+ tools did not receive the absolute repository model library." >&2
  exit 1
fi

: > "$temporary/java.log"
output=$(
  printf '%s\0%s\0' \
    docs/design/models/example/Example.tla \
    docs/design/models/example/Example.cfg |
    run_scoped_check
)
case "$output" in
  *"Checked 1 module(s) and 1 configuration(s)"*) ;;
  *)
    echo "Multiple changed files selected the same model more than once." >&2
    exit 1
    ;;
esac

: > "$temporary/java.log"
output=$(printf '%s\0' README.md | run_scoped_check)
case "$output" in
  *"Checked 0 module(s) and 0 configuration(s)"*) ;;
  *)
    echo "A non-model change selected TLA+ model work." >&2
    exit 1
    ;;
esac
if [ -s "$temporary/java.log" ]; then
  echo "A non-model change invoked the TLA+ tools." >&2
  exit 1
fi

if printf '%s' docs/design/models/example/Example.tla |
  run_scoped_check >/dev/null 2>&1; then
  echo "A non-NUL-terminated changed-file stream was accepted." >&2
  exit 1
fi

touch docs/models/RootLevel.tla
if printf '%s\0' docs/models/RootLevel.tla | run_scoped_check >/dev/null 2>&1; then
  echo "A root-level TLA+ file was not rejected." >&2
  exit 1
fi
rm -f docs/models/RootLevel.tla

touch docs/models/nested/sub/TooDeep.tla
if printf '%s\0' docs/models/nested/sub/TooDeep.tla |
  run_scoped_check >/dev/null 2>&1; then
  echo "A nested TLA+ file was not rejected." >&2
  exit 1
fi
rm -f docs/models/nested/sub/TooDeep.tla

output=$(printf '%s\0' docs/models/deleted/Deleted.tla | run_scoped_check)
case "$output" in
  *"Checked 0 module(s) and 0 configuration(s)"*) ;;
  *)
    echo "A deleted unreferenced module selected unrelated model checks." >&2
    exit 1
    ;;
esac

rm -f docs/design/models/foundation/Foundation.tla
if printf '%s\0' docs/design/models/foundation/Foundation.tla |
  run_scoped_check >/dev/null 2>&1; then
  echo "Deleting an imported module did not fail its surviving consumer." >&2
  exit 1
fi
touch docs/design/models/foundation/Foundation.tla

: > "$temporary/java.log"
output=$(
  printf '%s\0' docs/design/models/example/Deleted.cfg |
    run_scoped_check
)
case "$output" in
  *"Checked 1 module(s) and 1 configuration(s)"*) ;;
  *)
    echo "A deleted file did not select its surviving model directory." >&2
    exit 1
    ;;
esac

: > "$temporary/java.log"
output=$(printf '%s\0' eng/run-tla-checks.sh | run_scoped_check)
case "$output" in
  *"Checked 0 module(s) and 0 configuration(s)"*) ;;
  *)
    echo "A TLA+ infrastructure change selected unchanged model content." >&2
    exit 1
    ;;
esac
if [ -s "$temporary/java.log" ]; then
  echo "A TLA+ infrastructure change invoked the model-checking tools." >&2
  exit 1
fi

: > "$temporary/java.log"
output=$(
  printf '%s\0' docs/design/models/foundation/Foundation.tla |
    run_scoped_check
)
case "$output" in
  *"::group::docs/design/models/foundation"*) ;;
  *)
    echo "A changed foundation did not select its own model." >&2
    exit 1
    ;;
esac
case "$output" in
  *"::group::docs/design/models/middle"*) ;;
  *)
    echo "A changed foundation did not select its direct consumer." >&2
    exit 1
    ;;
esac
case "$output" in
  *"::group::docs/design/models/top"*) ;;
  *)
    echo "A changed foundation did not select its transitive consumer." >&2
    exit 1
    ;;
esac
case "$output" in
  *"Checked 3 module(s) and 3 configuration(s)"*) ;;
  *)
    echo "A foundation change did not check exactly its dependency closure." >&2
    exit 1
    ;;
esac

: > "$temporary/java.log"
output=$(
  printf '%s\0' docs/design/models/top/Top.tla |
    run_scoped_check
)
case "$output" in
  *"Checked 1 module(s) and 1 configuration(s)"*) ;;
  *)
    echo "A leaf consumer change selected unrelated lower-layer models." >&2
    exit 1
    ;;
esac

mkdir -p docs/models/duplicate
touch docs/models/duplicate/Example.tla
: > "$temporary/java.log"
if printf '%s\0' README.md | run_scoped_check >/dev/null 2>&1; then
  echo "Duplicate owner module names were accepted into the global library." >&2
  exit 1
fi
if [ -s "$temporary/java.log" ]; then
  echo "Duplicate module names did not fail before model checking." >&2
  exit 1
fi
rm -f docs/models/duplicate/Example.tla
rmdir docs/models/duplicate

mkdir -p docs/models/standard
touch docs/models/standard/Naturals.tla
: > "$temporary/java.log"
if printf '%s\0' README.md | run_scoped_check >/dev/null 2>&1; then
  echo "A repository module shadowed a standard TLA+ module." >&2
  exit 1
fi
if [ -s "$temporary/java.log" ]; then
  echo "A standard-module shadow did not fail before model checking." >&2
  exit 1
fi
rm -f docs/models/standard/Naturals.tla
rmdir docs/models/standard

printf '%s\n' \
  'docs/design/models/override/Override.cfg=Missing' \
  > "$fixture/eng/tla-module-overrides.txt"
if printf '%s\0' eng/tla-module-overrides.txt |
  run_scoped_check >/dev/null 2>&1; then
  echo "An override naming a missing module was not rejected." >&2
  exit 1
fi

printf '%s\n' \
  'docs/design/models/override/Override.cfg=Override' \
  > "$fixture/eng/tla-module-overrides.txt"
: > "$temporary/java.log"
output=$(run_all_check)
case "$output" in
  *"Checked 7 module(s) and 7 configuration(s)"*) ;;
  *)
    echo "The explicit repository-wide TLA+ scope did not check every model." >&2
    exit 1
    ;;
esac

if PATH="$temporary/bin:$PATH" \
  TLA_TEST_JAVA_LOG="$temporary/java.log" \
  TLA_TOOLS_JAR="$temporary/tla2tools.jar" \
  "$repo/eng/run-tla-checks.sh" >/dev/null 2>&1; then
  echo "The TLA+ runner accepted an implicit repository-wide scope." >&2
  exit 1
fi

echo "TLA+ runner scope checks passed."
