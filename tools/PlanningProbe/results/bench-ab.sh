#!/usr/bin/env bash
# usage: PROBES="a:path b:path" VARIANTS="..." bench-ab.sh <outfile> <budget-ms> <dll>...
set -euo pipefail
OUT=$1; BUDGET=$2; shift 2; DLLS=("$@")
read -ra P <<< "$PROBES"; read -ra V <<< "$VARIANTS"
jobs=(); for p in "${P[@]}"; do for v in "${V[@]}"; do jobs+=("$p|$v"); done; done
: > "$OUT"; n=${#jobs[@]}
for r in 1 2 3 4 5 6; do
  for ((i=0;i<n;i++)); do
    if ((r%2)); then j=${jobs[$(( (i+r) % n ))]}; else j=${jobs[$(( (n-1-i+r) % n ))]}; fi
    name=${j%%|*}; name=${name%%:*}; path=${j%%|*}; path=${path#*:}; v=${j#*|}
    "$path" "$BUDGET" "$v" "${DLLS[@]}" | sed "s/^/$name:$v\t$r\t/" >> "$OUT"
  done
done
echo "rows=$(wc -l < "$OUT")"
