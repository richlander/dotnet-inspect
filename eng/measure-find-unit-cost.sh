#!/usr/bin/env bash
# Measures per-package unit cost of `find` so budgets can be computed:
# acquisition (cold), Type census, and member scan (warm), plus bytes added.
#
# Usage: measure-find-unit-cost.sh <dotnet-inspect-binary> <sample.tsv> <work-dir> [warm-samples]
# sample.tsv columns: group, package, version (header row required).
# Output TSV: group, package, version, phase, sample, seconds, exit, cache_kb
#   phase: startup | cold-type | warm-type | warm-member
# Misses (`ZzqNoSuchType`, `.ZzqNoSuchMember`) force the complete census and
# member scan without depending on any particular package's names.
set -uo pipefail

bin=${1:?binary}
sample=${2:?sample.tsv}
work=${3:?work dir}
warm=${4:-3}
mkdir -p "$work"

now() { python3 -c 'import time; print(time.time())'; }

timed() {
  local home=$1; shift
  local start end status=0
  start=$(now)
  HOME=$home NUGET_PACKAGES=$home/nuget "$bin" "$@" >/dev/null 2>"$work/err.txt" || status=$?
  end=$(now)
  printf '%.3f\t%s' "$(python3 -c "print($end - $start)")" "$status"
}

printf 'group\tpackage\tversion\tphase\tsample\tseconds\texit\tcache_kb\n'

base="$work/startup"
rm -rf "$base"; mkdir -p "$base"
for ((i = 1; i <= warm; i++)); do
  printf 'fixed\t-\t-\tstartup\t%s\t%s\t0\n' "$i" "$(timed "$base" --version)"
done

tail -n +2 "$sample" | while IFS=$'\t' read -r group package version; do
  home="$work/home"
  rm -rf "$home"; mkdir -p "$home"
  coord="$package@$version"
  row=$(timed "$home" find ZzqNoSuchType --package "$coord")
  kb=$(du -sk "$home" | cut -f1)
  printf '%s\t%s\t%s\tcold-type\t1\t%s\t%s\n' "$group" "$package" "$version" "$row" "$kb"
  for ((i = 1; i <= warm; i++)); do
    row=$(timed "$home" find ZzqNoSuchType --package "$coord")
    printf '%s\t%s\t%s\twarm-type\t%s\t%s\t%s\n' "$group" "$package" "$version" "$i" "$row" "$kb"
    row=$(timed "$home" find .ZzqNoSuchMember --package "$coord")
    printf '%s\t%s\t%s\twarm-member\t%s\t%s\t%s\n' "$group" "$package" "$version" "$i" "$row" "$kb"
  done
  rm -rf "$home"
done
