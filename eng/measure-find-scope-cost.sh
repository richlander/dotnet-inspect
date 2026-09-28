#!/usr/bin/env bash
# Measures end-to-end `find` cost by scope and cache state, to classify which
# sources are cheap enough for a blocking answer and which need streaming.
#
# Usage: measure-find-scope-cost.sh <dotnet-inspect-binary> <work-dir> [warm-samples] [cold-samples]
#
# Set ONLY="scenario ..." to run a subset.
# Scenarios (each with a direct hit, a miss that forces the census and
# similarity path, and a member search):
#   platform-installed  default scope, installed shared framework, warm cache
#   platform-remote     default scope, empty DOTNET_ROOT (Browser/Wasm-like):
#                       ref packs are acquired from nuget.org; cold then warm
#   package-named       --package Avalonia@12.1.3 --tfm net10.0; cold then warm
#   package-prefix      --package-prefix Avalonia --tfm net10.0; cold then warm
#
# Cold samples use a fresh HOME and NUGET_PACKAGES per sample. Output is TSV on
# stdout: scenario, state, query, sample, seconds, rows, exit, cache_mb.
set -euo pipefail

bin=${1:?binary}
work=${2:?work dir}
warm=${3:-5}
cold=${4:-3}
avalonia=Avalonia@12.1.3

mkdir -p "$work"
printf 'scenario\tstate\tquery\tsample\tseconds\trows\texit\tcache_mb\n'

run() {
  local scenario=$1 state=$2 query=$3 sample=$4 home=$5 dotnet_root=$6
  shift 6
  local out="$work/out.tsv" err="$work/err.txt" start end status=0
  start=$(python3 -c 'import time; print(time.time())')
  HOME=$home NUGET_PACKAGES=$home/nuget DOTNET_ROOT=$dotnet_root \
    "$bin" find "$query" "$@" --tsv >"$out" 2>"$err" || status=$?
  end=$(python3 -c 'import time; print(time.time())')
  local rows mb
  rows=$(($(wc -l <"$out") > 0 ? $(wc -l <"$out") - 1 : 0))
  mb=$(du -sm "$home" 2>/dev/null | cut -f1)
  printf '%s\t%s\t%s\t%s\t%.2f\t%s\t%s\t%s\n' \
    "$scenario" "$state" "$query" "$sample" \
    "$(python3 -c "print($end - $start)")" "$rows" "$status" "$mb"
}

scenario() {
  local name=$1 dotnet_root=$2 hit=$3 miss=$4 member=$5
  shift 5
  case " ${ONLY:-$name} " in *" $name "*) ;; *) return 0 ;; esac
  local q
  for ((i = 1; i <= cold; i++)); do
    for q in "$hit" "$miss" "$member"; do
      local home="$work/$name-cold-$i-${q//[^A-Za-z]/}"
      rm -rf "$home"; mkdir -p "$home"
      run "$name" cold "$q" "$i" "$home" "$dotnet_root" "$@"
      rm -rf "$home"
    done
  done
  local warm_home="$work/$name-warm"
  rm -rf "$warm_home"; mkdir -p "$warm_home"
  run "$name" prime "$hit" 0 "$warm_home" "$dotnet_root" "$@" >/dev/null
  for ((i = 1; i <= warm; i++)); do
    for q in "$hit" "$miss" "$member"; do
      run "$name" warm "$q" "$i" "$warm_home" "$dotnet_root" "$@"
    done
  done
}

installed_root=${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}
empty_root="$work/empty-dotnet"
mkdir -p "$empty_root"

# Installed platform: the user's real HOME cache is warm by definition, so this
# scenario only runs warm samples against a primed private HOME.
cold_saved=$cold; cold=0
scenario platform-installed "$installed_root" JsonSerializer JsonSerialiser .Parse
cold=$cold_saved
scenario platform-remote "$empty_root" JsonSerializer JsonSerialiser .Parse
scenario package-named "$installed_root" Button Buton .Measure \
  --package "$avalonia" --tfm net10.0
scenario package-prefix "$installed_root" Button Buton .Measure \
  --package-prefix Avalonia --tfm net10.0
