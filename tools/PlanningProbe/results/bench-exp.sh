#!/usr/bin/env bash
# usage: bench-exp.sh <outfile> <budget-ms> <dll>...
set -euo pipefail
OUT=$1; BUDGET=$2; shift 2; DLLS=("$@")
P=${PROBE:-/tmp/pp-bench/exp-probe/PlanningProbe}
V=(${VARIANTS:-legacy hand hand-flat nlinq planned hand-exists nlinq-exists planned-exists})
: > "$OUT"
for r in 1 2 3 4 5 6; do
  n=${#V[@]}; order=()
  for ((i=0;i<n;i++)); do if ((r%2)); then order+=("${V[$(( (i+r) % n ))]}"); else order+=("${V[$(( (n-1-i+r) % n ))]}"); fi; done
  for v in "${order[@]}"; do "$P" "$BUDGET" "$v" "${DLLS[@]}" | sed "s/^/$v\t$r\t/" >> "$OUT"; done
done
echo "rows=$(wc -l < "$OUT")"
