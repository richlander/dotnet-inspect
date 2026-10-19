#!/usr/bin/env bash
# Counterbalanced exact-head run: base, byte-identical base copy (A/A), head; every case.
set -euo pipefail
root="/home/rich/perf-work/ctcensus3"
source /opt/dotnet-env
rt="$(dotnet --list-runtimes | awk '$1 == "Microsoft.NETCore.App" && $2 ~ /^11[.]/ { gsub(/[\[\]]/, "", $3); print $3 "/" $2; exit }')"
mkdir -p "$root/base2-out"; cp "$root/base-out/dotnet-inspect" "$root/base2-out/dotnet-inspect"
names=(base base2 head)
declare -A bin=([base]="$root/base-out/dotnet-inspect" [base2]="$root/base2-out/dotnet-inspect" [head]="$root/head-out/dotnet-inspect")
for k in "${names[@]}"; do [ -x "${bin[$k]}" ] || { echo "missing $k" >&2; exit 1; }; done
rounds=${ROUNDS:-24}; out="$root/results"; mkdir -p "$out"
echo "host=$(hostname) start=$(date -u +%FT%TZ) load=$(cut -d' ' -f1-3 /proc/loadavg) runtime=$rt rounds=$rounds"
med() { sort -n | awk '{v[NR]=$1} END {print (NR%2)?v[(NR+1)/2]:(v[NR/2]+v[NR/2+1])/2}'; }
run_case() {
  local tag="$1"; shift
  for k in "${names[@]}"; do for i in 1 2 3; do "${bin[$k]}" "$@" >/dev/null 2>&1 || true; done; : > "$out/t-$k-$tag"; done
  for r in $(seq 0 $((rounds-1))); do
    order=(); for j in 0 1 2; do order+=("${names[$(( (r + j) % 3 ))]}"); done
    if (( r % 2 )); then order=("${order[2]}" "${order[1]}" "${order[0]}"); fi
    for k in "${order[@]}"; do
      s=$(date +%s%N); "${bin[$k]}" "$@" > "$out/o-$k-$tag" 2>/dev/null || true; e=$(date +%s%N)
      echo $(( (e - s) / 1000 )) >> "$out/t-$k-$tag"
    done
  done
  cmp -s "$out/o-base-$tag" "$out/o-head-$tag" && same=identical || same=DIFFERENT
  lines=$(wc -l < "$out/o-head-$tag")
  paste "$out/t-base-$tag" "$out/t-base2-$tag" "$out/t-head-$tag" | awk -v tag="$tag" -v lines="$lines" -v same="$same" '
    function med(a, n,   i, j, t) { for (i = 1; i <= n; i++) for (j = i + 1; j <= n; j++) if (a[j] < a[i]) { t = a[i]; a[i] = a[j]; a[j] = t }
      return (n % 2) ? a[(n + 1) / 2] : (a[n / 2] + a[n / 2 + 1]) / 2 }
    function p95(a, n,   i, j, t, k) { for (i = 1; i <= n; i++) for (j = i + 1; j <= n; j++) if (a[j] < a[i]) { t = a[i]; a[i] = a[j]; a[j] = t }
      k = int(n * 0.95 + 0.999); return a[k] }
    { n++; b[n] = $1; c[n] = $2; h[n] = $3; b2[n] = $1; h2[n] = $3; rc[n] = $2 / $1; rh[n] = $3 / $1 }
    END { printf "%s\tbase=%.1f\tcopy=%.1f\thead=%.1f\tbase_p95=%.1f\thead_p95=%.1f\tcopy/base=%.4f\thead/base=%.4f\tlines=%s\t%s\n",
      tag, med(b, n) / 1000, med(c, n) / 1000, med(h, n) / 1000, p95(b2, n) / 1000, p95(h2, n) / 1000, med(rc, n), med(rh, n), lines, same }'
}
run_case "Type-UnsafeMembers-Utf8JsonReader" type System.Text.Json.Utf8JsonReader --library "$rt/System.Text.Json.dll" -S "Unsafe Members"
run_case "Type-UnsafeMembers-JsonSerializer" type System.Text.Json.JsonSerializer --library "$rt/System.Text.Json.dll" -S "Unsafe Members"
run_case "Type-UnsafeMembers-String" type System.String --library "$rt/System.Private.CoreLib.dll" -S "Unsafe Members"
run_case "Type-UnsafeMembers-Task" type System.Threading.Tasks.Task --library "$rt/System.Private.CoreLib.dll" -S "Unsafe Members"
run_case "Type-UnsafeMembers-File" type System.IO.File --library "$rt/System.Private.CoreLib.dll" -S "Unsafe Members"
run_case "Library-UnsafeMembers-CoreLib" library "$rt/System.Private.CoreLib.dll" -S "Unsafe Members"
run_case "Library-TopLeverage-STJ" library "$rt/System.Text.Json.dll" -S "Top Leverage"
echo "end=$(date -u +%FT%TZ) load=$(cut -d' ' -f1-3 /proc/loadavg)"
