#!/bin/bash
# Baseline test script for dotnet-inspect
# Generates output for a comprehensive set of commands
# Usage: ./baseline-test.sh [--update]
#   --update: Overwrite baseline.txt with new output
#   (no args): Compare current output against baseline

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
OUTPUT_FILE="$SCRIPT_DIR/baseline.txt"
TEMP_FILE="$SCRIPT_DIR/baseline.tmp"

# Check for --update flag
UPDATE_MODE=false
if [[ "$1" == "--update" ]]; then
    UPDATE_MODE=true
fi

# Build the tool first
echo "Building dotnet-inspect..." >&2
dotnet build "$REPO_ROOT/src/dotnet-inspect" --nologo -v q >&2

# Function to run a command and capture output with header
run_cmd() {
    local cmd="$1"
    echo "================================================================================"
    echo "COMMAND: $cmd"
    echo "================================================================================"
    # Run via dotnet run, filter out volatile data
    eval "dotnet run --project $REPO_ROOT/src/dotnet-inspect --no-build -- $cmd 2>&1" | \
        sed -E 's/^(\| Total Downloads \| )[0-9.]+[KMBT]?/\1<FILTERED>/g' | \
        sed -E 's/^(\| Version Downloads \| )[0-9.]+[KMBT]?/\1<FILTERED>/g' | \
        sed -E 's/^(\| Downloads \| )[0-9.]+[KMBT]?/\1<FILTERED>/g' | \
        sed -E 's/Updated: [0-9]{4}-[0-9]{2}-[0-9]{2}/Updated: <FILTERED>/g' | \
        sed -E 's/\| Updated \| [0-9]{4}-[0-9]{2}-[0-9]{2}/| Updated | <FILTERED>/g'
    echo ""
}

# Generate all output
{
    echo "# dotnet-inspect Baseline Output"
    echo "# This file is used for regression testing"
    echo "# Volatile data (downloads, dates) is filtered to <FILTERED>"
    echo ""

    echo ""
    echo "############################################################################"
    echo "# PACKAGE COMMAND - VERBOSITY LEVELS"
    echo "############################################################################"
    echo ""

    run_cmd "System.Text.Json 10.0.2 -v:q"
    run_cmd "System.Text.Json 10.0.2 -v:m"
    run_cmd "System.Text.Json 10.0.2 -v:n"
    run_cmd "System.Text.Json 10.0.2 -v:d"

    echo ""
    echo "############################################################################"
    echo "# PACKAGE COMMAND - SECTION FILTERING"
    echo "############################################################################"
    echo ""

    run_cmd "package --discover"
    run_cmd "System.Text.Json 10.0.2 -v:d -s:1"
    run_cmd "System.Text.Json 10.0.2 -v:d -s:1,2"
    run_cmd "System.Text.Json 10.0.2 -v:d -x:3,4"

    echo ""
    echo "############################################################################"
    echo "# PACKAGE COMMAND - SPECIAL SCENARIOS"
    echo "############################################################################"
    echo ""

    # Vulnerable package
    run_cmd "System.Text.Json 8.0.4 -v:d"

    # Deprecated package
    run_cmd "System.Text.Json 5.0.0 -v:m"

    # Tool package
    run_cmd "dotnet-ef 10.0.2 -v:d"

    # RID-specific tool
    run_cmd "Azure.Mcp 1.0.1 -v:d"

    # Community package
    run_cmd "Newtonsoft.Json 13.0.3 -v:d"

    # Versions listing
    run_cmd "System.Text.Json --versions -n 5"

    # Files listing
    run_cmd "System.Text.Json 10.0.2 --files"

    echo ""
    echo "############################################################################"
    echo "# API COMMAND"
    echo "############################################################################"
    echo ""

    # List all types
    run_cmd "api --package System.Text.Json@10.0.2 -n 10"

    # Single type
    run_cmd "api JsonSerializer --package System.Text.Json@10.0.2"

    # Member filter
    run_cmd "api JsonSerializer --package System.Text.Json@10.0.2 -m Serialize"

    # Constructors
    run_cmd "api JsonSerializerOptions --package System.Text.Json@10.0.2 --ctor"

    # Generic types
    run_cmd "api 'Option<T>' --package System.CommandLine@2.0.0-beta4.22272.1"

    # Signatures only
    run_cmd "api JsonSerializer --package System.Text.Json@10.0.2 --signatures-only -n 10"

    # Verbosity
    run_cmd "api JsonSerializer --package System.Text.Json@10.0.2 -v:q"

    # Community package (SourceLink)
    run_cmd "api JsonSerializer --package Newtonsoft.Json@13.0.3 --fields-only"

    # Platform assembly
    run_cmd "api --platform System.Text.Json -n 10"

    echo ""
    echo "############################################################################"
    echo "# ASSEMBLY COMMAND"
    echo "############################################################################"
    echo ""

    run_cmd "assembly --package System.Text.Json@10.0.2 --tfm net10.0"
    run_cmd "assembly --package System.Text.Json@10.0.2 --tfm net10.0 --audit"

    echo ""
    echo "############################################################################"
    echo "# TYPE COMMAND"
    echo "############################################################################"
    echo ""

    run_cmd "type JsonSerializer --package System.Text.Json@10.0.2"

    echo ""
    echo "############################################################################"
    echo "# DIFF COMMAND"
    echo "############################################################################"
    echo ""

    run_cmd "diff JsonSerializer --package System.Text.Json@9.0.0..10.0.2"

    echo ""
    echo "############################################################################"
    echo "# PLATFORM COMMAND"
    echo "############################################################################"
    echo ""

    run_cmd "platform"
    run_cmd "platform --list-versions"
    run_cmd "platform --framework runtime -n 10"

} > "$TEMP_FILE"

if $UPDATE_MODE; then
    mv "$TEMP_FILE" "$OUTPUT_FILE"
    echo "Baseline updated: $OUTPUT_FILE" >&2
else
    if [[ -f "$OUTPUT_FILE" ]]; then
        if diff -q "$OUTPUT_FILE" "$TEMP_FILE" > /dev/null 2>&1; then
            echo "✓ Baseline matches" >&2
            rm "$TEMP_FILE"
            exit 0
        else
            echo "✗ Baseline mismatch. Differences:" >&2
            diff "$OUTPUT_FILE" "$TEMP_FILE" >&2 || true
            rm "$TEMP_FILE"
            exit 1
        fi
    else
        echo "No baseline exists. Run with --update to create one." >&2
        rm "$TEMP_FILE"
        exit 1
    fi
fi
