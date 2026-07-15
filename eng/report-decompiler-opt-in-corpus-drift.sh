#!/usr/bin/env bash
# Reports compiler-lowering drift between the pinned opt-in corpus SDK and the
# repository-selected current SDK.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

bash "$repo/eng/prepare-decompiler-opt-in-corpus.sh" pinned "$tmp/pinned.txt"
bash "$repo/eng/prepare-decompiler-opt-in-corpus.sh" current "$tmp/current.txt"

paste "$tmp/pinned.txt" "$tmp/current.txt" > "$tmp/pairs.tsv"

dotnet run --project "$repo/tools/IlDiffHarness" -c Release -- \
  --pairs "$tmp/pairs.tsv" \
  --max-examples "${1:-5}"
