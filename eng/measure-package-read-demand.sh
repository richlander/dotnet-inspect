#!/usr/bin/env bash
# Measures remote package reads for docs/design/package-read-demand.md.
#
# usage: eng/measure-package-read-demand.sh BASELINE_BINARY CANDIDATE_BINARY
#
# Each binary is a published dotnet-inspect. Every run uses its own isolated
# cache (--isolated), so runs never share state with each other or with the
# user's cache. The script reports, per build and package, cold and warm wall
# time, HTTP requests, cache hits, bytes received over TCP (when strace is
# available), and the isolated cache's size on disk. It then runs a surface
# search followed by focused commands on one session per build and reports
# whether each command's output is identical across the two builds.
set -euo pipefail

baseline=${1:?baseline binary}
candidate=${2:?candidate binary}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

packages=(
  Avalonia@12.1.2
  Dapper@2.1.89
  Serilog@4.4.0
  Humanizer.Core@3.0.10
  AWSSDK.Core@4.0.102.6
  Newtonsoft.Json@13.0.4
  Azure.Storage.Blobs@12.29.2
  SkiaSharp@4.152.1
  Microsoft.CodeAnalysis.CSharp@5.9.0
)

field() { # $1 row label, $2 file: a row of the --info table
  grep -E "^\| $1" "$2" | sed -E "s/^\| $1 *\| //; s/ *\|$//" || true
}

session_dir() { echo "${TMPDIR:-/tmp}/dotnet-inspect-$1"; }

measure() { # $1 label, $2 binary
  local label=$1 binary=$2 package name session kind seconds bytes
  for package in "${packages[@]}"; do
    name=${package%@*}
    session="read-demand-$label-$name-$$"
    local args=(find .ToString --package "$package" --tfm net10.0 --info --tips q)
    for kind in cold warm; do
      /usr/bin/time -f "%e" -o "$work/time" "$binary" "${args[@]}" --isolated "$session" \
        > "$work/$label-$name-$kind.out" 2> "$work/$label-$name-$kind.err" || true
      seconds=$(cat "$work/time")
      printf '%s %s %s: %ss requests=[%s] cache=[%s] rows=%s\n' "$label" "$name" "$kind" \
        "$seconds" "$(field HTTP "$work/$label-$name-$kind.err")" \
        "$(field Cache "$work/$label-$name-$kind.err")" \
        "$(grep -c '^|' "$work/$label-$name-$kind.out" || true)"
    done
    printf '%s %s disk=%s bytes\n' "$label" "$name" \
      "$(du -sb "$(session_dir "$session")" 2>/dev/null | cut -f1)"
    rm -rf "$(session_dir "$session")"
    if command -v strace > /dev/null; then
      strace -f -yy -qq -e trace=read,recvfrom,recvmsg,readv -e status=successful \
        -o "$work/strace" "$binary" "${args[@]}" --isolated "$session-s" > /dev/null 2>&1 || true
      bytes=$(grep -E '<TCP' "$work/strace" | sed -nE 's/.*= ([0-9]+)$/\1/p' \
        | awk '{ s += $1 } END { print s + 0 }')
      printf '%s %s cold tcp=%s bytes\n' "$label" "$name" "$bytes"
      rm -rf "$(session_dir "$session-s")"
    fi
  done
}

sequence() { # $1 label, $2 binary
  local label=$1 binary=$2 session="read-demand-seq-$1-$$" step=0 command
  local commands=(
    "find .InvalidateMeasure --package Avalonia@12.1.2 --tfm net10.0"
    "type Button --package Avalonia@12.1.2 --tfm net10.0"
    "library Avalonia.Controls --package Avalonia@12.1.2 --tfm net10.0"
    "package Avalonia@12.1.2"
    "find .InvalidateMeasure --package Avalonia@12.1.2 --tfm net10.0"
  )
  for command in "${commands[@]}"; do
    step=$((step + 1))
    # shellcheck disable=SC2086
    "$binary" $command --tips q --isolated "$session" > "$work/seq-$label-$step.out" 2>&1 || true
  done
  rm -rf "$(session_dir "$session")"
}

measure baseline "$baseline"
measure candidate "$candidate"
sequence baseline "$baseline"
sequence candidate "$candidate"
for step in 1 2 3 4 5; do
  if cmp -s "$work/seq-baseline-$step.out" "$work/seq-candidate-$step.out"; then
    echo "sequence step $step: identical"
  else
    echo "sequence step $step: DIFFERS"
  fi
done
