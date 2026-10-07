#!/usr/bin/env bash
# Checks every Lean model against the build bar in docs/lean-methodology.md:
# each model pins the repository Lean toolchain, has no package dependencies,
# ignores its .lake build output, and builds from a clean .lake with no errors
# or warnings (Lean reports every `sorry` as a warning). After the build,
# eng/lean/CheckAxioms.lean inspects the elaborated modules: no declaration may
# be an axiom, and every constant may depend only on the standard axioms.
#
# Usage: eng/run-lean-checks.sh
# LEAN_MODEL_ROOTS (space-separated) overrides the model roots; the self-test
# in eng/test-lean-checks.sh uses it. `lake` and `lean` must be on PATH.
set -euo pipefail

LEAN_TOOLCHAIN="leanprover/lean4:v4.34.1"
CHECK_AXIOMS="$(cd "$(dirname "$0")" && pwd)/lean/CheckAxioms.lean"
read -r -a MODEL_ROOTS <<< "${LEAN_MODEL_ROOTS:-docs/design/models docs/models}"

failures=0
checked=0

fail() {
  echo "::error::$1" >&2
  failures=$((failures + 1))
}

check_model() {
  local dir=$1
  local toolchain
  toolchain=$(tr -d '[:space:]' < "$dir/lean-toolchain" 2>/dev/null || true)
  if [ "$toolchain" != "$LEAN_TOOLCHAIN" ]; then
    fail "$dir/lean-toolchain must be $LEAN_TOOLCHAIN, got '${toolchain:-<missing>}'."
    return
  fi

  if [ ! -f "$dir/lake-manifest.json" ] \
    || ! tr -d '[:space:]' < "$dir/lake-manifest.json" | grep -q '"packages":\[\]'; then
    fail "$dir/lake-manifest.json must declare no packages."
  fi

  if ! grep -qxE '/?\.lake/?' "$dir/.gitignore" 2>/dev/null; then
    fail "$dir must ignore its .lake build output."
  fi

  local sources
  sources=$(find "$dir" -name '*.lean' -not -path '*/.lake/*' | sort)
  if [ -z "$sources" ]; then
    fail "$dir has a lakefile.toml but no .lean sources."
    return
  fi

  local log status=0
  log=$(cd "$dir" && rm -rf .lake && lake build 2>&1) || status=$?
  if [ "$status" -ne 0 ]; then
    printf '%s\n' "$log" >&2
    fail "$dir failed to build (exit $status)."
  elif printf '%s\n' "$log" | grep -qiE '(^|[^a-z])(warning|error):'; then
    printf '%s\n' "$log" >&2
    fail "$dir built with warnings."
  else
    local modules=() source module
    while IFS= read -r source; do
      module=${source#"$dir"/}
      module=${module%.lean}
      modules+=("${module//\//.}")
    done <<< "$sources"
    status=0
    log=$(cd "$dir" && lake env lean --run "$CHECK_AXIOMS" "${modules[@]}" 2>&1) || status=$?
    if [ "$status" -ne 0 ]; then
      printf '%s\n' "$log" >&2
      fail "$dir failed the axiom check."
    fi
  fi
  rm -rf "$dir/.lake"
}

for root in "${MODEL_ROOTS[@]}"; do
  [ -d "$root" ] || continue
  while IFS= read -r -d '' lakefile; do
    dir=$(dirname "$lakefile")
    echo "::group::$dir"
    before=$failures
    check_model "$dir"
    checked=$((checked + 1))
    [ "$failures" -eq "$before" ] && echo "$dir: ok"
    echo "::endgroup::"
  done < <(find "$root" -mindepth 2 -maxdepth 2 -name lakefile.toml -print0 | sort -z)
  while IFS= read -r -d '' misplaced; do
    fail "$misplaced is not in a Lean model directory directly under $root."
  done < <(find "$root" \( -path '*/.lake' -prune \) -o \( -name '*.lean' -print0 \) \
    | while IFS= read -r -d '' file; do
        rel=${file#"$root"/}
        model=${rel%%/*}
        [ -f "$root/$model/lakefile.toml" ] || printf '%s\0' "$file"
      done)
done

if [ "$checked" -eq 0 ]; then
  fail "No Lean model directories were found under ${MODEL_ROOTS[*]}."
fi

if [ "$failures" -ne 0 ]; then
  echo "Lean checks failed: $failures problem(s) across $checked model(s)." >&2
  exit 1
fi
echo "Lean checks passed for $checked model(s)."
