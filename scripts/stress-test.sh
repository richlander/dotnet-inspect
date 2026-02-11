#!/bin/bash
# Chaos-monkey stress test for download and cache paths.
#
# Each iteration runs N commands with M random cache cleans injected at
# random positions. Cache cleans land between different commands every
# time, exposing TOCTOU races, partial-cache bugs, and download failures.
#
# The command pool covers every download surface:
#   - Package download (NuGet flat container)
#   - Ref/runtime pack download (platform resolver)
#   - PDB/symbol download (MSDL, snupkg, symbol servers)
#   - Source download (SourceLink → GitHub raw)
#   - Version index download (NuGet flat container)
#   - Diff (downloads two package versions)
#
# Usage: ./stress-test.sh [iterations] [cleans-per-loop] [tool-name]
#   iterations:      number of loops (default: 100)
#   cleans-per-loop: cache cleans injected per loop (default: 5)
#   tool-name:       command to test (default: dotnet-inspect)

set -euo pipefail

ITERATIONS="${1:-100}"
CLEANS="${2:-5}"
TOOL_NAME="${3:-dotnet-inspect}"

# --- Command pool ---
# Each entry: "label|command"
# Organized by download surface exercised.
COMMANDS=(
    # Package download — basic
    "pkg-quiet|$TOOL_NAME package System.Text.Json 10.0.0 -v:q"
    "pkg-normal|$TOOL_NAME package System.Text.Json 10.0.0"
    "pkg-detailed|$TOOL_NAME package System.Text.Json 10.0.0 -v:d"
    "pkg-vuln|$TOOL_NAME package System.Text.Json 8.0.0 -s Vuln*"

    # Package metadata variants
    "pkg-files|$TOOL_NAME package System.Text.Json 10.0.0 --files"
    "pkg-tfms|$TOOL_NAME package System.Text.Json 10.0.0 --tfms"
    "pkg-layout-lib|$TOOL_NAME package System.Text.Json 10.0.0 --layout --lib"
    "pkg-deps|$TOOL_NAME package System.Text.Json 10.0.0 --tfm net9.0 --dependencies"

    # Version index download
    "versions|$TOOL_NAME package System.Text.Json --versions -n 5"

    # API — type list (package download + API extraction)
    "api-types|$TOOL_NAME api --package System.Text.Json@10.0.0 -n 5"
    "api-types-glob|$TOOL_NAME api System.Text.Json JsonS* -v:q"

    # API — member list (single type)
    "api-members|$TOOL_NAME api System.Text.Json JsonSerializer -n 5"
    "api-member-filter|$TOOL_NAME api --package System.Text.Json@10.0.0 JsonSerializer Deserialize -n 3"

    # API — docs (triggers SourceLink + source download)
    "api-docs|$TOOL_NAME api System.Text.Json JsonSerializer --docs -n 3"

    # API — member doc with source (PDB download + SourceLink fetch)
    "api-member-src|$TOOL_NAME api --package Microsoft.Extensions.Options OptionsFactory Create"

    # API — samples (SourceLink source download)
    "api-samples|$TOOL_NAME api Newtonsoft.Json JObject --samples -v:q"

    # Samples command (downloads source via SourceLink)
    "samples-list|$TOOL_NAME samples Newtonsoft.Json JObject --list"

    # Library — from package (package download + metadata read)
    "lib-pkg|$TOOL_NAME library --package System.Text.Json -v:q"
    "lib-pkg-symbols|$TOOL_NAME library --package System.Text.Json -s Sy*"

    # Library — source link audit (PDB download + batch URL checks)
    "lib-sl-audit|$TOOL_NAME library --package Newtonsoft.Json --source-link-audit -s Source*"

    # Library — platform (ref pack download)
    "lib-platform|$TOOL_NAME library System.Text.Json -v:q"

    # Diff — downloads two versions
    "diff|$TOOL_NAME diff System.Text.Json@9.0.0..10.0.0 -v:q -n 5"

    # Find — searches across platform (ref pack)
    "find|$TOOL_NAME find JsonSerializer -v:q"

    # Extensions — scans package for extension methods
    "extensions|$TOOL_NAME extensions JsonElement --package System.Text.Json@10.0.0"
)

NUM_COMMANDS=${#COMMANDS[@]}

# Counters
pass=0
fail=0
network_errors=0
cache_errors=0
total_commands=0
total_cleans=0

LOGFILE="$(mktemp /tmp/stress-test-XXXXXX.log)"
echo "Stress test log: $LOGFILE"
echo "Chaos monkey: $ITERATIONS loops x $NUM_COMMANDS commands + $CLEANS random cache cleans per loop"
echo ""

# Build shuffled schedule: N command slots + M "clean" slots, Fisher-Yates shuffled.
build_schedule() {
    local slots=()
    for idx in $(seq 0 $((NUM_COMMANDS - 1))); do
        slots+=("cmd:$idx")
    done
    for _ in $(seq 1 "$CLEANS"); do
        slots+=("clean")
    done

    local n=${#slots[@]}
    for ((j = n - 1; j > 0; j--)); do
        local k=$((RANDOM % (j + 1)))
        local tmp="${slots[$j]}"
        slots[$j]="${slots[$k]}"
        slots[$k]="$tmp"
    done
    echo "${slots[@]}"
}

run_command() {
    local label="$1"
    local cmd="$2"
    local output=""
    local exit_code=0

    output=$(eval "$cmd" 2>&1) || exit_code=$?

    if [ $exit_code -ne 0 ]; then
        local kind="other"
        if echo "$output" | grep -qiE "download|network|timeout|socket|connect|http|retry"; then
            kind="network"
            network_errors=$((network_errors + 1))
        elif echo "$output" | grep -qiE "cache|not found|directory|file|access"; then
            kind="cache"
            cache_errors=$((cache_errors + 1))
        fi

        echo "--- $label (exit=$exit_code, $kind) ---" >> "$LOGFILE"
        echo "cmd: $cmd" >> "$LOGFILE"
        echo "$output" >> "$LOGFILE"
        echo "" >> "$LOGFILE"
        return 1
    fi
    return 0
}

for i in $(seq 1 "$ITERATIONS"); do
    printf "[%3d/%d] " "$i" "$ITERATIONS"

    schedule=($(build_schedule))
    loop_ok=true
    cmds_run=0
    cleans_run=0
    failed_labels=""

    for slot in "${schedule[@]}"; do
        if [ "$slot" = "clean" ]; then
            $TOOL_NAME cache --clean > /dev/null 2>&1 || true
            cleans_run=$((cleans_run + 1))
        else
            idx="${slot#cmd:}"
            entry="${COMMANDS[$idx]}"
            label="${entry%%|*}"
            cmd="${entry#*|}"

            if ! run_command "iteration $i: $label" "$cmd"; then
                loop_ok=false
                fail=$((fail + 1))
                failed_labels="$failed_labels $label"
            fi
            cmds_run=$((cmds_run + 1))
        fi
    done

    total_commands=$((total_commands + cmds_run))
    total_cleans=$((total_cleans + cleans_run))

    if $loop_ok; then
        echo "PASS ($cmds_run cmds, $cleans_run cleans)"
        pass=$((pass + 1))
    else
        echo "FAIL:$failed_labels ($cmds_run cmds, $cleans_run cleans)"
    fi
done

echo ""
echo "=== Chaos Stress Test Results ==="
echo "Iterations:     $ITERATIONS"
echo "Total commands: $total_commands"
echo "Total cleans:   $total_cleans"
echo "Loops passed:   $pass"
echo "Loops failed:   $fail"
echo "  Network:      $network_errors"
echo "  Cache:        $cache_errors"
echo "  Other:        $((fail - network_errors - cache_errors))"

if [ $fail -gt 0 ]; then
    echo ""
    echo "Failure log: $LOGFILE"

    # Show failure frequency by command
    echo ""
    echo "=== Failures by command ==="
    grep -oP 'iteration \d+: \K\S+' "$LOGFILE" | sort | uniq -c | sort -rn
    exit 1
else
    rm -f "$LOGFILE"
    echo "All passed."
    exit 0
fi
