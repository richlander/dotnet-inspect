#!/usr/bin/env bash
# Measures end-to-end `find` cost by scope and cache state, to classify which
# sources are cheap enough for a blocking answer and which need streaming.
#
# Usage: measure-find-scope-cost.sh <dotnet-inspect-binary> <work-dir> [warm-samples] [cold-samples]
#
# Set ONLY="scenario ..." to run a subset. Set
# TERMINALS="markdown json jsonl tsv table count rows" to measure every
# supported Find output terminal; the default remains "tsv".
# Scenarios (each with a direct hit, a miss that forces the census and
# similarity path, a zero-result miss, and a member search):
#   platform-installed  default scope, installed shared frameworks; cold is a
#                       new user with an empty cache
#   platform-remote     default scope, empty DOTNET_ROOT (Browser/Wasm-like):
#                       ref packs are acquired from nuget.org
#   core-packages       the platform Workspace plan's former core packages
#                       (before #8818), by name, with no platform
#   package-sets        --platform --extensions --aspnetcore: the platform
#                       frameworks plus both shipped package sets
#   package-named       --package Avalonia@12.1.3 --tfm net10.0
#   package-compat      --package System.Text.Json@10.0.0 without --tfm,
#                       exercising the compatibility inventory
#   package-prefix      --package-prefix Avalonia --tfm net10.0
#
# Every scenario runs cold, then warm. Cold samples use a fresh HOME and
# NUGET_PACKAGES per sample. Each command is timed inside one Python process
# around the child alone, so interpreter startup is excluded. Output is TSV on
# stdout: scenario, state, query, terminal, sample, seconds, rows, exit,
# cache_mb, content_sha256.
set -euo pipefail

bin=${1:?binary}
work=${2:?work dir}
warm=${3:-5}
cold=${4:-3}
avalonia=Avalonia@12.1.3
zero_query=Definitely.No.Such.Type.Qzxv
terminals=${TERMINALS:-tsv}

mkdir -p "$work"
printf 'scenario\tstate\tquery\tterminal\tsample\tseconds\trows\texit\tcache_mb\tcontent_sha256\n'

terminal_args() {
  case "$1" in
    markdown) ;;
    json) printf '%s\0' --json --compact ;;
    jsonl) printf '%s\0' --jsonl ;;
    tsv) printf '%s\0' --tsv ;;
    table) printf '%s\0' --table ;;
    count) printf '%s\0' --count ;;
    rows) printf '%s\0' --tsv -n 3 ;;
    *)
      printf 'Unknown terminal: %s\n' "$1" >&2
      return 1
      ;;
  esac
}

run() {
  local scenario=$1 state=$2 query=$3 terminal=$4 sample=$5 home=$6 dotnet_root=$7
  shift 7
  local out="$work/out.txt" err="$work/err.txt" timing
  local -a format_args=()
  while IFS= read -r -d '' arg; do
    format_args+=("$arg")
  done < <(terminal_args "$terminal")
  timing=$(HOME=$home NUGET_PACKAGES=$home/nuget DOTNET_ROOT=$dotnet_root \
    python3 -c '
import subprocess, sys, time
with open(sys.argv[1], "wb") as o, open(sys.argv[2], "wb") as e:
    t = time.perf_counter()
    r = subprocess.run(sys.argv[3:], stdout=o, stderr=e)
    print(f"{time.perf_counter() - t:.2f} {r.returncode}")
' "$out" "$err" "$bin" find "$query" "$@" \
    ${format_args[@]+"${format_args[@]}"})
  local rows mb content_sha256
  case "$terminal" in
    json)
      rows=$(jq 'length' "$out")
      ;;
    jsonl)
      rows=$(($(wc -l <"$out")))
      ;;
    tsv|rows)
      rows=$(($(wc -l <"$out") > 0 ? $(wc -l <"$out") - 1 : 0))
      ;;
    count)
      rows=$(tr -d '[:space:]' <"$out")
      ;;
    markdown|table)
      if HOME=$home NUGET_PACKAGES=$home/nuget DOTNET_ROOT=$dotnet_root \
        "$bin" find "$query" "$@" --count >"$work/count.txt" 2>/dev/null; then
        rows=$(tr -d '[:space:]' <"$work/count.txt")
      else
        rows=-1
      fi
      ;;
  esac
  mb=$(du -sm "$home" 2>/dev/null | cut -f1)
  if command -v sha256sum >/dev/null 2>&1; then
    content_sha256=$(sha256sum "$out" | cut -d ' ' -f1)
  else
    content_sha256=$(shasum -a 256 "$out" | cut -d ' ' -f1)
  fi
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$scenario" "$state" "$query" "$terminal" "$sample" \
    "${timing% *}" "$rows" "${timing#* }" "$mb" "$content_sha256"
}

scenario() {
  local name=$1 dotnet_root=$2 hit=$3 miss=$4 member=$5
  shift 5
  case " ${ONLY:-$name} " in *" $name "*) ;; *) return 0 ;; esac
  local q terminal
  for ((i = 1; i <= cold; i++)); do
    for q in "$hit" "$miss" "$zero_query" "$member"; do
      for terminal in $terminals; do
        local home="$work/$name-cold-$i-${q//[^A-Za-z]/}-$terminal"
        rm -rf "$home"; mkdir -p "$home"
        run "$name" cold "$q" "$terminal" "$i" "$home" "$dotnet_root" "$@"
        rm -rf "$home"
      done
    done
  done
  local warm_home="$work/$name-warm"
  rm -rf "$warm_home"; mkdir -p "$warm_home"
  run "$name" prime "$hit" tsv 0 "$warm_home" "$dotnet_root" "$@" >/dev/null
  for ((i = 1; i <= warm; i++)); do
    for q in "$hit" "$miss" "$zero_query" "$member"; do
      for terminal in $terminals; do
        run "$name" warm "$q" "$terminal" "$i" "$warm_home" "$dotnet_root" "$@"
      done
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
scenario package-compat "$installed_root" JsonSerializer JsonSerialiser .Parse \
  --package System.Text.Json@10.0.0
scenario package-prefix "$installed_root" Button Buton .Measure \
  --package-prefix Avalonia --tfm net10.0
