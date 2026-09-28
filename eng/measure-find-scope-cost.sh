#!/usr/bin/env bash
# Measures end-to-end `find` cost by scope and cache state, to classify which
# sources are cheap enough for a blocking answer and which need streaming.
#
# Usage: measure-find-scope-cost.sh <dotnet-inspect-binary> <work-dir> [warm-samples] [cold-samples]
#
# Set ONLY="scenario ..." to run a subset.
# Scenarios (each with a direct hit, a miss that forces the census and
# similarity path, and a member search):
#   platform-installed  default scope, installed shared frameworks; cold is a
#                       new user (fresh HOME), which acquires .NET Standard
#   platform-remote     default scope, empty DOTNET_ROOT (Browser/Wasm-like):
#                       ref packs are acquired from nuget.org
#   core-packages       the platform Workspace plan's current core packages,
#                       by name, with no platform
#   package-sets        --platform --extensions --aspnetcore: the platform
#                       frameworks plus both shipped package sets
#   package-named       --package Avalonia@12.1.3 --tfm net10.0
#   package-prefix      --package-prefix Avalonia --tfm net10.0
#
# Every scenario runs cold, then warm. Cold samples use a fresh HOME and
# NUGET_PACKAGES per sample. Each command is timed inside one Python process
# around the child alone, so interpreter startup is excluded. Output is TSV on
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
  local out="$work/out.tsv" err="$work/err.txt" timing
  timing=$(HOME=$home NUGET_PACKAGES=$home/nuget DOTNET_ROOT=$dotnet_root \
    python3 -c '
import subprocess, sys, time
with open(sys.argv[1], "wb") as o, open(sys.argv[2], "wb") as e:
    t = time.perf_counter()
    r = subprocess.run(sys.argv[3:], stdout=o, stderr=e)
    print(f"{time.perf_counter() - t:.2f} {r.returncode}")
' "$out" "$err" "$bin" find "$query" "$@" --tsv)
  local rows mb
  rows=$(($(wc -l <"$out") > 0 ? $(wc -l <"$out") - 1 : 0))
  mb=$(du -sm "$home" 2>/dev/null | cut -f1)
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$scenario" "$state" "$query" "$sample" \
    "${timing% *}" "$rows" "${timing#* }" "$mb"
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

core_packages=(
  --package Microsoft.Extensions.DependencyInjection.Abstractions
  --package Microsoft.Extensions.Configuration.Abstractions
  --package Microsoft.Extensions.Logging.Abstractions
  --package Microsoft.AspNetCore.OpenApi
  --package Microsoft.AspNetCore.Authentication.JwtBearer
)

scenario platform-installed "$installed_root" JsonSerializer JsonSerialiser .Parse
scenario platform-remote "$empty_root" JsonSerializer JsonSerialiser .Parse
scenario core-packages "$installed_root" ServiceCollection ServiceColection .AddSingleton \
  "${core_packages[@]}"
scenario package-sets "$installed_root" JsonSerializer JsonSerialiser .Parse \
  --platform --extensions --aspnetcore
scenario package-named "$installed_root" Button Buton .Measure \
  --package "$avalonia" --tfm net10.0
scenario package-prefix "$installed_root" Button Buton .Measure \
  --package-prefix Avalonia --tfm net10.0
