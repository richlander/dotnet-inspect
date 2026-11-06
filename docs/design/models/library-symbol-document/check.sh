#!/usr/bin/env bash
# Runs the query catalog against a Library symbol document and compares each
# answer with its recorded expectation. Usage: check.sh [document] [--record]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
doc="${1:-$here/system-text-json.symbols.json}"
record="${2:-}"
failed=0

# Grep contract: a matching record line is one complete JSON value.
line="$(grep -F '"name": "ParseAsync"' "$doc")"
[ "$(printf '%s\n' "$line" | wc -l | tr -d ' ')" = 1 ] || { echo "FAIL grep: expected one line"; failed=1; }
printf '%s\n' "${line%,}" | jq -e '.documentation_id' >/dev/null || { echo "FAIL grep: line is not a JSON record"; failed=1; }

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
[ "$failed" = 0 ] && echo "All queries match."
exit "$failed"
