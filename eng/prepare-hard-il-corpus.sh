#!/usr/bin/env bash
# Prepares the broad-source assembly pool for the hard-IL decompiler corpus.
# Requires an output directory and writes:
#   <outdir>/assemblies.txt      the deduped managed assembly path list
#   <outdir>/sweep/              the extracted sweep packages (durable)
#   <outdir>/sweep-manifest.json the sweep manifest (resolved versions/TFMs)
#
# The sweep tool copies each selected assembly into <outdir>/sweep/packages/... and
# records those paths, so the sweep output MUST live in the durable output
# directory (not a temp dir) or the emitted paths would dangle after cleanup.
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

outdir="${1:-}"
if [ -z "$outdir" ]; then
  echo "usage: $0 <output-directory>" >&2
  echo "  writes <output-directory>/assemblies.txt and keeps the extracted sweep" >&2
  echo "  packages under <output-directory>/sweep so the listed paths stay valid." >&2
  exit 2
fi

root="$(git rev-parse --show-toplevel)"
mkdir -p "$outdir"
outdir="$(cd "$outdir" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# 1. Broad-source NuGet sweep. The sweep extracts assemblies into
#    <outdir>/sweep/packages/... and lists those durable paths, so it must write
#    into the persisted output directory, never the temp dir.
dotnet run "$root/eng/prepare-decompiler-package-sweep.cs" -- "$outdir/sweep" 1 "$PACKAGE_COUNT" >&2

# 2. Fixed real-world corpus assemblies -> $work/real-world.txt (durable repo/nuget
#    cache paths, so the intermediate list itself may be ephemeral).
bash "$root/eng/prepare-decompiler-corpus.sh" "$work/real-world.txt"

# 3. Union, de-duplicated, deterministic order.
#    Real-world assemblies lead so shared affinity is stable regardless of which
#    packages the sweep resolves. Dedup on the assembly file name (not the full
#    path): the harvester and benchmark key libraries by assembly name, so an
#    assembly reachable via two pool sources (e.g. a package that is both a
#    real-world pin and a top-N sweep hit) must contribute one library, not two.
#    Real-world leading means its pinned version wins the overlap.
sweep_list="$outdir/sweep/assemblies.txt"
[ -f "$sweep_list" ] || sweep_list=/dev/null
cat "$work/real-world.txt" "$sweep_list" \
    | awk -F/ '$0 != "" && !seen[$NF]++' > "$outdir/assemblies.txt"

# Surface the sweep manifest at the top level so resolved versions/TFMs are easy
# to find (it also remains at <outdir>/sweep/manifest.json).
if [ -f "$outdir/sweep/manifest.json" ]; then
  cp "$outdir/sweep/manifest.json" "$outdir/sweep-manifest.json"
fi

count="$(wc -l < "$outdir/assemblies.txt" | tr -d ' ')"
echo "Hard-IL pool: $count assemblies ($PACKAGE_COUNT-rank sweep + real-world)." >&2
echo "Wrote $outdir/assemblies.txt (sweep packages under $outdir/sweep)." >&2
