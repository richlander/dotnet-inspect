#!/bin/bash
# Smoke test script for dotnet-inspect
# Tests that the tool is properly installed and core commands work
# Usage: ./smoke-test.sh [tool-name]
#   tool-name: The command name to test (default: dotnet-inspect)

set -e

TOOL_NAME="${1:-dotnet-inspect}"

echo "=== Smoke Tests for $TOOL_NAME ==="
echo ""

# Helper function to run a test
run_test() {
    local name="$1"
    shift
    echo -n "Testing: $name ... "
    if "$@" > /dev/null 2>&1; then
        echo "✓ PASS"
        return 0
    else
        echo "✗ FAIL"
        echo "  Command: $*"
        return 1
    fi
}

# Helper for tests that check output contains expected text
run_test_output() {
    local name="$1"
    local expected="$2"
    shift 2
    echo -n "Testing: $name ... "
    local output
    if output=$("$@" 2>&1) && echo "$output" | grep -q "$expected"; then
        echo "✓ PASS"
        return 0
    else
        echo "✗ FAIL"
        echo "  Command: $*"
        echo "  Expected output to contain: $expected"
        return 1
    fi
}

FAILED=0

# Basic functionality
run_test "version flag" $TOOL_NAME --version || FAILED=$((FAILED + 1))
run_test "help flag" $TOOL_NAME --help || FAILED=$((FAILED + 1))

# Package command
run_test "package info (quiet)" $TOOL_NAME System.Text.Json 10.0.0 -v:q || FAILED=$((FAILED + 1))
run_test "package info (normal)" $TOOL_NAME System.Text.Json 10.0.0 || FAILED=$((FAILED + 1))
run_test_output "package versions" "10.0" $TOOL_NAME System.Text.Json --versions 5 || FAILED=$((FAILED + 1))

# API command
run_test "api types list" $TOOL_NAME type --package System.Text.Json@10.0.0 -t 5 || FAILED=$((FAILED + 1))
run_test_output "api type details" "Serialize" $TOOL_NAME member JsonSerializer --package System.Text.Json@10.0.0 -m 5 || FAILED=$((FAILED + 1))

# Assembly command
run_test "assembly info" $TOOL_NAME assembly --package System.Text.Json@10.0.0 --tfm net10.0 || FAILED=$((FAILED + 1))

# Platform command
run_test "platform info" $TOOL_NAME platform || FAILED=$((FAILED + 1))
run_test "platform list versions" $TOOL_NAME platform --list-versions || FAILED=$((FAILED + 1))

# Type command
run_test_output "type details" "JsonSerializer" $TOOL_NAME type JsonSerializer --package System.Text.Json@10.0.0 || FAILED=$((FAILED + 1))

echo ""
echo "=== Results ==="
if [ $FAILED -eq 0 ]; then
    echo "All tests passed! ✓"
    exit 0
else
    echo "$FAILED test(s) failed ✗"
    exit 1
fi
