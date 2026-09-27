#!/usr/bin/env bash
# usage: bench3way.sh <dir-with-probe-*> <outfile> <dll>...
set -euo pipefail
W=$1; OUT=$2; shift 2; DLLS=("$@")
: > "$OUT"
orders=("base final2 final3" "final2 final3 base" "final3 base final2" "base final3 final2" "final2 base final3" "final3 final2 base")
for r in 1 2 3 4 5 6; do
  read -ra order <<< "${orders[$((r-1))]}"
  for side in "${order[@]}"; do "$W/probe-$side/PresenceProbe" 3000 "${DLLS[@]}" | sed "s/^/$side\t$r\t/" >> "$OUT"; done
done
echo "rows=$(wc -l < "$OUT")"
