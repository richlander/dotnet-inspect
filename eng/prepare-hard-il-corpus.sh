#!/usr/bin/env bash
# Prepares the broad-source assembly pool for the hard-IL decompiler corpus.
# Without an argument it emits one managed assembly path per line on stdout; with
# an output directory it writes <outdir>/assemblies.txt (+ sweep-manifest.json).
#
# The hard-IL corpus is an adversarial stress set: the most diabolical real
# methods, ranked by IL difficulty, drawn from a much broader pool than the fixed
# real-world corpus. The pool is the union of:
#
#   1. The top-ranked NuGet packages (docs/data/nuget-top-packages.json), acquired
#      by eng/prepare-decompiler-package-sweep.cs.
#   2. The 14 pinned real-world corpus assemblies (eng/prepare-decompiler-corpus.sh),
#      so the two corpora share affinity and can grow in lock step.
#
# Feed the emitted list to the harvester's difficulty-ranked mode:
#
#   bash eng/prepare-hard-il-corpus.sh /tmp/hard-il-pool
#   dotnet run --project tools/DecompilerHarness -c Release -- \
#     --harvest-hard-il-corpus /tmp/hard-il-corpus.jsonl --harvest-target 12000 \
#     $(cat /tmp/hard-il-pool/assemblies.txt)
set -euo pipefail

# Number of top NuGet ranks to sweep (the package list currently holds 100).
PACKAGE_COUNT="${HARD_IL_PACKAGE_COUNT:-100}"

root="$(git rev-parse --show-toplevel)"
outdir="${1:-}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# 1. Broad-source NuGet sweep -> $work/sweep/assemblies.txt.
dotnet run "$root/eng/prepare-decompiler-package-sweep.cs" -- "$work/sweep" 1 "$PACKAGE_COUNT" >&2

# 2. Fixed real-world corpus assemblies -> $work/real-world.txt.
bash "$root/eng/prepare-decompiler-corpus.sh" "$work/real-world.txt"

# 3. Union, de-duplicated, deterministic order.
#    Real-world assemblies lead so shared affinity is stable regardless of which
#    packages the sweep resolves.
combined="$work/assemblies.txt"
cat "$work/real-world.txt" "$work/sweep/assemblies.txt" \
    | awk 'NF && !seen[$0]++' > "$combined"

count="$(wc -l < "$combined" | tr -d ' ')"
echo "Hard-IL pool: $count assemblies ($PACKAGE_COUNT-rank sweep + real-world)." >&2

if [ -n "$outdir" ]; then
  mkdir -p "$outdir"
  cp "$combined" "$outdir/assemblies.txt"
  # Preserve the sweep manifest next to the list, so the resolved package
  # versions/TFMs are recoverable.
  if [ -f "$work/sweep/manifest.json" ]; then
    cp "$work/sweep/manifest.json" "$outdir/sweep-manifest.json"
  fi
  echo "Wrote $outdir/assemblies.txt (+ sweep-manifest.json)." >&2
else
  cat "$combined"
fi
