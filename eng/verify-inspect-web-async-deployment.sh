#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 5 ]]; then
  echo "Usage: verify-inspect-web-async-deployment.sh <compiler|runtime> <publish-engine.dll> <published-wwwroot> <receipt.json> <compile-receipts>" >&2
  exit 1
fi

lowering="$1"
assembly="$2"
core_assembly="$(dirname "$assembly")/InspectWeb.Engine.Core.dll"
site="$3"
receipt="$4"
compile_receipts="$5"
if [[ "$lowering" != "compiler" && "$lowering" != "runtime" ]]; then
  echo "Expected lowering must be 'compiler' or 'runtime'." >&2
  exit 1
fi
if [[ ! -f "$core_assembly" ]]; then
  echo "Published Engine.Core assembly was not found: $core_assembly" >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
census="$scratch/async-census.json"
graph="$scratch/browser-engine-restore-graph.json"
graph_result="$scratch/async-project-graph.json"
version_prefix=$(
  "$dotnet" msbuild \
    "$repo_root/src/dotnet-inspect/dotnet-inspect.csproj" \
    -getProperty:VersionPrefix \
    -nologo
)
if [[ -z "$version_prefix" ]]; then
  echo "The authoritative product VersionPrefix is empty." >&2
  exit 1
fi

"$dotnet" run \
  "$repo_root/prototypes/inspect-web/scripts/verify-async-lowering.cs" \
  -- \
  "$assembly" \
  "$core_assembly" \
  "$lowering" \
  "$census"

"$repo_root/eng/generate-inspect-web-engine-facade.sh" \
  --contract \
  "$assembly" \
  "$scratch/inspect-web-engine.d.ts" \
  "$version_prefix"
cmp \
  "$repo_root/prototypes/inspect-web/src/inspect-web-engine.d.ts" \
  "$scratch/inspect-web-engine.d.ts"

"$node" \
  "$repo_root/prototypes/inspect-web/scripts/verify-published-engine-facade.ts" \
  "$site"

graph_properties=()
if [[ "$lowering" == "runtime" ]]; then
  graph_properties+=("-p:Features=runtime-async=on")
fi
"$dotnet" msbuild \
  "$repo_root/prototypes/inspect-web/engine/InspectWeb.Engine.csproj" \
  -t:GenerateRestoreGraphFile \
  -p:RestoreGraphOutputPath="$graph" \
  -p:Configuration=Release \
  -p:MSBuildEnableWorkloadResolver=false \
  "${graph_properties[@]}" \
  -nologo \
  -v:q
"$node" \
  "$repo_root/prototypes/inspect-web/scripts/verify-async-project-graph.ts" \
  "$lowering" \
  "$repo_root" \
  "$graph" \
  "$compile_receipts" \
  "$graph_result"

mkdir -p "$(dirname "$receipt")"
"$node" --input-type=module - "$assembly" "$site" "$lowering" \
  "$repo_root/prototypes/inspect-web/src/inspect-web-engine.d.ts" \
  "$census" "$graph_result" "$receipt" <<'JS'
import { createHash } from "node:crypto";
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

const [assembly, site, lowering, contract, censusPath, graphResultPath, receipt] =
  process.argv.slice(2);
if (!assembly
    || !site
    || !lowering
    || !contract
    || !censusPath
    || !graphResultPath
    || !receipt) {
  throw new Error("missing async deployment receipt argument");
}
const census = JSON.parse(readFileSync(censusPath, "utf8"));
const graphResult = JSON.parse(readFileSync(graphResultPath, "utf8"));
const coreAssembly = resolve(dirname(assembly), "InspectWeb.Engine.Core.dll");
if (!Number.isInteger(graphResult.repository_project_count)
    || graphResult.repository_project_count <= 0) {
  throw new Error("invalid async project graph result");
}
const counts = [
  census.async_method_count,
  census.compiler_async_method_count,
  census.runtime_async_method_count,
];
const expectedAssemblies = new Set([
  "InspectWeb.Engine.dll",
  "InspectWeb.Engine.Core.dll",
]);
if (census.assembly_count !== expectedAssemblies.size
    || !Array.isArray(census.assemblies)
    || census.assemblies.length !== expectedAssemblies.size
    || census.assemblies.some(
      assembly => !expectedAssemblies.delete(assembly.file)
        || !Number.isInteger(assembly.async_method_count)
        || assembly.async_method_count <= 0)
    || expectedAssemblies.size !== 0
    || !counts.every(Number.isInteger)
    || census.async_method_count <= 0
    || census.async_method_count
      !== census.compiler_async_method_count + census.runtime_async_method_count
    || (lowering === "compiler"
      && (census.compiler_async_method_count !== census.async_method_count
        || census.runtime_async_method_count !== 0))
    || (lowering === "runtime"
      && (census.runtime_async_method_count !== census.async_method_count
        || census.compiler_async_method_count !== 0))) {
  throw new Error("invalid async method census");
}
const frameworkFiles = readdirSync(resolve(site, "_framework"));
const webcil = frameworkFiles.filter(
  name => /^InspectWeb\.Engine\.[A-Za-z0-9]+\.wasm$/.test(name));
if (webcil.length !== 1) {
  throw new Error(
    `Expected one published InspectWeb.Engine WebCIL image; found ${webcil.length}.`);
}
const coreWebcil = frameworkFiles.filter(
  name => /^InspectWeb\.Engine\.Core\.[A-Za-z0-9]+\.wasm$/.test(name));
if (coreWebcil.length !== 1) {
  throw new Error(
    `Expected one published InspectWeb.Engine.Core WebCIL image; found ${coreWebcil.length}.`);
}
function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}
writeFileSync(
  receipt,
  `${JSON.stringify({
    schema: 4,
    method: "InspectionEngine.AsyncLoweringCanary",
    lowering,
    result: "inspect-web-async-lowering-ok",
    assembly_count: census.assembly_count,
    async_method_count: census.async_method_count,
    compiler_async_method_count: census.compiler_async_method_count,
    runtime_async_method_count: census.runtime_async_method_count,
    repository_project_count: graphResult.repository_project_count,
    publish_assembly_sha256: sha256(assembly),
    publish_core_assembly_sha256: sha256(coreAssembly),
    published_webcil_file: webcil[0],
    published_webcil_sha256: sha256(resolve(site, "_framework", webcil[0])),
    published_core_webcil_file: coreWebcil[0],
    published_core_webcil_sha256: sha256(
      resolve(site, "_framework", coreWebcil[0])),
    contract_sha256: sha256(contract),
  })}\n`);
JS

echo "InspectWeb $lowering-async deployment evidence passed."
