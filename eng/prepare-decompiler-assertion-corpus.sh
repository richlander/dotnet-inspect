#!/usr/bin/env bash
# Restores the fixed decompiler corpus and appends the current local product
# assemblies. This is the recommended assertion-scan audit corpus: pinned
# published packages provide stable breadth, while the local product assemblies
# exercise the current decompiler/analysis code under review.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

bash "$root/eng/prepare-decompiler-corpus.sh" "$tmp/pinned.txt"

declare -a local_assemblies=(
  "$root/artifacts/bin/DotnetInspector.Core/release/DotnetInspector.Core.dll"
  "$root/artifacts/bin/DotnetInspector.Packages/release/DotnetInspector.Packages.dll"
  "$root/artifacts/bin/DotnetInspector.Services/release/DotnetInspector.Services.dll"
  "$root/artifacts/bin/DotnetInspector.Services/release/ILInspector.Metadata.dll"
  "$root/artifacts/bin/DotnetInspector.Services/release/ILInspector.MetadataPrimitives.dll"
  "$root/artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll"
  "$root/artifacts/bin/dotnet-inspect/release/ILInspector.Analysis.dll"
  "$root/artifacts/bin/dotnet-inspect/release/ILInspector.Decompiler.dll"
)

for assembly in "${local_assemblies[@]}"; do
  if [ ! -f "$assembly" ]; then
    echo "Missing local product assembly: $assembly" >&2
    echo "Build first: dotnet build src/dotnet-inspect -c Release -p:PublishAot=false" >&2
    exit 1
  fi
done

{
  cat "$tmp/pinned.txt"
  printf '%s\n' "${local_assemblies[@]}"
} | awk '!seen[$0]++' > "$tmp/assertion-corpus.txt"

if [ -n "$out" ]; then
  cp "$tmp/assertion-corpus.txt" "$out"
else
  cat "$tmp/assertion-corpus.txt"
fi
