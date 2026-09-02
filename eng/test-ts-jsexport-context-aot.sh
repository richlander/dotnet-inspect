#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
rid=${1:-linux-x64}
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

dotnet build \
  "$repo_root/tests/TsJsExport.ContextFixtures.Host/TsJsExport.ContextFixtures.Host.csproj" \
  -c Release \
  --nologo >/dev/null
dotnet publish \
  "$repo_root/src/ts-jsexport/ts-jsexport.csproj" \
  -c Release \
  -r "$rid" \
  --self-contained \
  --output "$scratch/tool" \
  --nologo >/dev/null

tool_name=ts-jsexport
if [[ "$rid" == win-* ]]; then
  tool_name=ts-jsexport.exe
fi

context_assembly="$repo_root/tests/TsJsExport.ContextFixtures.Host/bin/Release/net11.0/TsJsExport.ContextFixtures.Host.dll"
"$scratch/tool/$tool_name" \
  "$context_assembly" \
  --context TsJsExport.ContextFixtures.Host.MultiAssemblyContext \
  --assembly-search-path "$(dirname "$context_assembly")" \
  --runtime-module ./dotnet.js \
  --output "$scratch/facades"

expected=(
  TsJsExport.ContextFixtures.Alpha.ts
  TsJsExport.ContextFixtures.Beta.ts
  TsJsExport.ContextFixtures.Host.ts
)
for facade in "${expected[@]}"; do
  test -s "$scratch/facades/$facade"
done
test "$(find "$scratch/facades" -maxdepth 1 -type f | wc -l)" -eq 3

normalization_error="$scratch/normalization-error.txt"
if "$scratch/tool/$tool_name" \
  "$context_assembly" \
  --context TsJsExport.ContextFixtures.Host.NormalizationCollisionContext \
  --assembly-search-path \
    "$repo_root/tests/TsJsExport.ContextFixtures.NormalizationComposed/bin/Release/net11.0" \
  --assembly-search-path \
    "$repo_root/tests/TsJsExport.ContextFixtures.NormalizationDecomposed/bin/Release/net11.0" \
  --runtime-module ./dotnet.js \
  --output "$scratch/normalization-facades" \
  2>"$normalization_error"; then
  echo "ts-jsexport accepted canonically equivalent artifact names." >&2
  exit 1
fi
grep -Fq \
  "is not unique after Unicode normalization" \
  "$normalization_error"
test ! -e "$scratch/normalization-facades"

echo "ts-jsexport context NativeAOT gate passed."
