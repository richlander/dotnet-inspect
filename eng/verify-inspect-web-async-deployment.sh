#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 5 ]]; then
  echo "Usage: verify-inspect-web-async-deployment.sh <compiler|runtime> <publish-engine.dll> <published-wwwroot> <receipt.json> <compile-receipts>" >&2
  exit 1
fi

lowering="$1"
assembly="$2"
site="$3"
receipt="$4"
compile_receipts="$5"
if [[ "$lowering" != "compiler" && "$lowering" != "runtime" ]]; then
  echo "Expected lowering must be 'compiler' or 'runtime'." >&2
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

"$dotnet" run \
  "$repo_root/prototypes/inspect-web/scripts/verify-async-lowering.cs" \
  -- \
  "$assembly" \
  "$lowering" \
  "$census"

"$repo_root/eng/generate-inspect-web-engine-facade.sh" \
  --contract \
  "$assembly" \
  "$scratch/inspect-web-engine.d.ts"
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
import { resolve } from "node:path";

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
if (!Number.isInteger(graphResult.repository_project_count)
    || graphResult.repository_project_count <= 0) {
  throw new Error("invalid async project graph result");
}
const counts = [
  census.async_method_count,
  census.compiler_async_method_count,
  census.runtime_async_method_count,
];
if (!counts.every(Number.isInteger)
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
function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}
writeFileSync(
  receipt,
  `${JSON.stringify({
    schema: 3,
    method: "InspectionEngine.AsyncLoweringCanary",
    lowering,
    result: "inspect-web-async-lowering-ok",
    async_method_count: census.async_method_count,
    compiler_async_method_count: census.compiler_async_method_count,
    runtime_async_method_count: census.runtime_async_method_count,
    repository_project_count: graphResult.repository_project_count,
    publish_assembly_sha256: sha256(assembly),
    published_webcil_file: webcil[0],
    published_webcil_sha256: sha256(resolve(site, "_framework", webcil[0])),
    contract_sha256: sha256(contract),
  })}\n`);
JS

echo "InspectWeb $lowering-async deployment evidence passed."
