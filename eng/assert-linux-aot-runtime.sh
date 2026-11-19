#!/bin/sh
set -eu

binary=/opt/dotnet-inspect
packet='eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEwIiwibmV0MTAuMCIsbnVsbF0sWyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpbWzAsMV1dLCJhIjoxLCJ4IjowLCJ2IjoiYXBpIiwieSI6IlN5c3RlbS5UZXh0Lkpzb24uSnNvblNlcmlhbGl6ZXIiLCJsIjpbIlN5c3RlbS5UZXh0Lkpzb24iXX0'

mkdir -p "$HOME"
help=$("$binary" --help)
case "$help" in
    *dotnet-inspect*) ;;
    *) echo "CLI help did not identify dotnet-inspect" >&2; exit 1 ;;
esac

skills=$("$binary" skill list)
case "$skills" in
    *query*) ;;
    *) echo "Embedded skill catalog did not list query" >&2; exit 1 ;;
esac

# Decode and re-encode a real canonical workspace packet without any network,
# SDK, compiler, host runtime or package cache in the container.
decoded=$("$binary" workspace packet decode "$packet")
case "$decoded" in
    *":Platform"*System.Text.Json.JsonSerializer*) ;;
    *) echo "Decoded packet lost its platform or selected type" >&2; exit 1 ;;
esac
encoded=$(printf '%s\n' "$decoded" | "$binary" workspace packet encode -)
if [ "$encoded" != "$packet" ]; then
    echo "Workspace packet round trip did not preserve the canonical packet" >&2
    exit 1
fi

echo "NativeAOT CLI passed help, skill list, and offline workspace packet round trip on glibc 2.27"
