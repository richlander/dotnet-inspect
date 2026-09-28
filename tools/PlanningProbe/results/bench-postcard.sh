#!/usr/bin/env bash
# usage: bench-postcard.sh <outfile> <budget-ms> <dll>...
# Postcard: six closings x Before/NLinq/After, rotated across 6 rounds.
# Columns: variant round load1 assembly answer samples median p10 p90 alloc
set -euo pipefail
OUT=$1; BUDGET=$2; shift 2; DLLS=("$@")
P=${PROBE:-/tmp/pp-bench/postcard-probe/PlanningProbe}
V=()
for c in exists count head tail rows window; do for i in before nlinq after; do V+=("pc:$c:$i"); done; done
load1() { uptime | sed -E 's/.*load averages?: *([0-9.]+).*/\1/'; }
: > "$OUT"
for r in 1 2 3 4 5 6; do
  n=${#V[@]}; order=()
  for ((i=0;i<n;i++)); do if ((r%2)); then order+=("${V[$(( (i+r) % n ))]}"); else order+=("${V[$(( (n-1-i+r) % n ))]}"); fi; done
  for v in "${order[@]}"; do l=$(load1); "$P" "$BUDGET" "$v" "${DLLS[@]}" | sed "s/^/$v\t$r\t$l\t/" >> "$OUT"; done
done
echo "rows=$(wc -l < "$OUT")"
