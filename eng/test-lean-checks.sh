#!/usr/bin/env bash
# Self-test for eng/run-lean-checks.sh: one well-formed fixture model must pass,
# and each fixture that breaks one rule of the build bar must fail with that
# rule's diagnostic. `lake` and `lean` must be on PATH.
set -euo pipefail

runner=$(cd "$(dirname "$0")" && pwd)/run-lean-checks.sh
toolchain=$(sed -n 's/^LEAN_TOOLCHAIN="\(.*\)"$/\1/p' "$runner")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# make_model ROOT NAME SOURCE: a minimal valid model whose library is SOURCE.
make_model() {
  local dir="$1/$2"
  mkdir -p "$dir"
  printf 'name = "fixture"\nversion = "0.1.0"\ndefaultTargets = ["Fixture"]\n\n[[lean_lib]]\nname = "Fixture"\n' \
    > "$dir/lakefile.toml"
  printf '%s\n' "$toolchain" > "$dir/lean-toolchain"
  printf '{"version": "1.2.0",\n "packagesDir": ".lake/packages",\n "packages": [],\n "name": "fixture",\n "lakeDir": ".lake",\n "fixedToolchain": false}\n' \
    > "$dir/lake-manifest.json"
  printf '.lake/\n' > "$dir/.gitignore"
  printf '%s\n' "$3" > "$dir/Fixture.lean"
}

valid='theorem fixture_add_zero (n : Nat) : n + 0 = n := rfl'

# expect NAME OUTCOME PATTERN: run the checker over $work/NAME and require the
# outcome (pass or fail) and, for a failure, a diagnostic matching PATTERN.
expect() {
  local name=$1 outcome=$2 pattern=${3:-} log status=0
  log=$(LEAN_MODEL_ROOTS="$work/$name" "$runner" 2>&1) || status=$?
  if [ "$outcome" = pass ] && [ "$status" -ne 0 ]; then
    printf '%s\n' "$log" >&2
    echo "::error::Lean self-test '$name' should pass." >&2
    exit 1
  fi
  if [ "$outcome" = fail ]; then
    if [ "$status" -eq 0 ] || ! grep -qE "$pattern" <<< "$log"; then
      printf '%s\n' "$log" >&2
      echo "::error::Lean self-test '$name' should fail with /$pattern/." >&2
      exit 1
    fi
  fi
  echo "Lean self-test '$name': $outcome as expected."
}

make_model "$work/valid" m "$valid"
expect valid pass

make_model "$work/comments" m "/-
axiom free: this model assumes nothing.
-/
-- proved without sorry
$valid"
expect comments pass

make_model "$work/sorry" m 'theorem fixture_open (n : Nat) : n + 0 = n := by sorry'
expect sorry fail 'built with warnings'

make_model "$work/axiom" m "axiom fixture_assumed : 1 = 2
$valid"
expect axiom fail 'declares an axiom: fixture_assumed'

make_model "$work/attributed" m "@[simp] axiom fixture_assumed : (1 : Nat) = 2
theorem fixture_false : (1 : Nat) = 2 := fixture_assumed"
expect attributed fail 'uses \[fixture_assumed\]'

make_model "$work/modified" m "noncomputable axiom fixture_assumed : Nat
$valid"
expect modified fail 'declares an axiom: fixture_assumed'

make_model "$work/documented" m "/-- An assumption. -/ axiom fixture_assumed : (1 : Nat) = 2
$valid"
expect documented fail 'declares an axiom: fixture_assumed'

make_model "$work/native" m 'theorem fixture_native : 2 + 2 = 4 := by native_decide'
expect native fail 'depends on a non-standard axiom: fixture_native'

make_model "$work/warning" m "theorem fixture_unused (n : Nat) (h : n = n) : n + 0 = n := rfl"
expect warning fail 'built with warnings'

make_model "$work/broken" m 'theorem fixture_false : 1 = 2 := rfl'
expect broken fail 'failed to build'

make_model "$work/toolchain" m "$valid"
printf 'leanprover/lean4:v0.0.0\n' > "$work/toolchain/m/lean-toolchain"
expect toolchain fail 'lean-toolchain must be'

make_model "$work/packages" m "$valid"
sed -i.bak 's/"packages": \[\]/"packages": [{"name": "dep"}]/' "$work/packages/m/lake-manifest.json"
expect packages fail 'must declare no packages'

make_model "$work/ignore" m "$valid"
printf 'build/\n' > "$work/ignore/m/.gitignore"
expect ignore fail 'must ignore its .lake'

make_model "$work/stray" m "$valid"
printf '%s\n' "$valid" > "$work/stray/Stray.lean"
expect stray fail 'not in a Lean model directory'

mkdir -p "$work/empty"
expect empty fail 'No Lean model directories'

echo "Lean runner self-test passed."
