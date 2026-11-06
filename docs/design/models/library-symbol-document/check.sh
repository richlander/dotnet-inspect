#!/usr/bin/env bash
# Checks Library symbol documents.
#   check.sh                    the vectors: recorded catalog answers on the excerpt, and the
#                               layout property and invariants on every vector document
#   check.sh <document>         any produced document: the layout property and invariants
#   check.sh "" --record        rewrite the excerpt's recorded answers after a reviewed change
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
doc="${1:-}"
record="${2:-}"
failed=0

check_document() {
  local d="$1"
  # Layout property: a Member or Type-collision line is one JSON value once its trailing comma is
  # removed; a Type or lookup line is one "key": value pair, valid JSON once wrapped in braces.
  local records values pairs bad
  records="$(jq '(.members | length) + (.type_collisions | length)' "$d")"
  values="$( { grep '^{"documentation_id"' "$d" || true; } | sed 's/,$//' | jq -c '.documentation_id' | wc -l | tr -d ' ')"
  [ "$values" = "$records" ] || { echo "FAIL layout ($d): $values of $records record lines parse"; failed=1; }
  pairs="$(jq '(.types | length) + ([.parameter_type_members, .return_type_members, .produced_type_members, .attribute_type_members, .suffix_family_types, .name_word_types, .base_type_derived_types, .interface_implementing_types] | map(length) | add)' "$d")"
  bad="$( { grep -E '^"[^"]+": [{[]' "$d" | grep -v -E '^"(generator|library|request|section_outcomes|counts)": |^"(types|type_collisions|members|parameter_type_members|return_type_members|produced_type_members|attribute_type_members|suffix_family_types|name_word_types|base_type_derived_types|interface_implementing_types)": [{[]$' || true; } | sed 's/,$//; s/^/{/; s/$/}/' | jq -c 'keys' | wc -l | tr -d ' ')"
  [ "$bad" = "$pairs" ] || { echo "FAIL layout ($d): $bad of $pairs Type and lookup lines parse when wrapped"; failed=1; }
  for invariant in "$here"/invariants/*.jq; do
    [ "$(jq -r -f "$invariant" "$d")" = true ] || { echo "FAIL invariant $(basename "$invariant") ($d)"; failed=1; }
  done
}

if [ -n "$doc" ]; then
  check_document "$doc"
else
  for vector in "$here"/*.symbols.json; do check_document "$vector"; done
  excerpt="$here/system-text-json.symbols.json"
  for query in "$here"/queries/*.jq; do
    expected="${query%.jq}.expected"
    actual="$(jq -r -f "$query" "$excerpt")"
    if [ "$record" = "--record" ]; then
      printf '%s\n' "$actual" > "$expected"
    elif ! diff -u "$expected" <(printf '%s\n' "$actual"); then
      echo "FAIL $(basename "$query")"
      failed=1
    fi
  done
fi
[ "$failed" = 0 ] && echo "All checks pass."
exit "$failed"
