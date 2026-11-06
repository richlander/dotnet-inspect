#!/usr/bin/env bash
# Checks a Library symbol document.
#   check.sh                    the excerpt: recorded catalog answers and invariants
#   check.sh <document>         any produced document: invariants and the grep property
#   check.sh "" --record        rewrite the excerpt's recorded answers after a reviewed change
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
doc="${1:-}"
record="${2:-}"
excerpt=0
if [ -z "$doc" ]; then doc="$here/system-text-json.symbols.json"; excerpt=1; fi
failed=0

# Grep property: every member line is one complete JSON record.
lines="$(grep -c '^{"documentation_id"' "$doc")"
parsed="$(grep '^{"documentation_id"' "$doc" | sed 's/,$//' | jq -c '.documentation_id' | wc -l | tr -d ' ')"
[ "$lines" = "$parsed" ] && [ "$lines" = "$(jq '.members | length' "$doc")" ] || { echo "FAIL grep: $parsed of $lines member lines parse"; failed=1; }

# Invariants hold for any document.
for invariant in "$here"/invariants/*.jq; do
  [ "$(jq -r -f "$invariant" "$doc")" = true ] || { echo "FAIL invariant $(basename "$invariant")"; failed=1; }
done

# Recorded answers describe the excerpt only.
if [ "$excerpt" = 1 ]; then
  for query in "$here"/queries/*.jq; do
    expected="${query%.jq}.expected"
    actual="$(jq -r -f "$query" "$doc")"
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
