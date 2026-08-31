#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 4 || "$#" -gt 5 ]]; then
  echo "Usage: verify-inspect-web-async-deployment.sh <compiler|runtime> <publish-engine.dll> <published-wwwroot> <receipt.json> [compile-receipts]" >&2
  exit 1
fi

lowering="$1"
assembly="$2"
site="$3"
receipt="$4"
compile_receipts="${5:-}"
if [[ "$lowering" == "runtime" ]]; then
  if [[ -z "$compile_receipts" ]]; then
    echo "Runtime lowering verification requires compile receipts." >&2
    exit 1
  fi
elif [[ "$lowering" == "compiler" ]]; then
  if [[ -n "$compile_receipts" ]]; then
    echo "Compiler lowering verification does not accept runtime compile receipts." >&2
    exit 1
  fi
else
  echo "Expected lowering must be 'compiler' or 'runtime'." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

"$dotnet" run \
  "$repo_root/prototypes/inspect-web/scripts/verify-async-lowering.cs" \
  -- \
  "$assembly" \
  "$lowering"

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

if [[ "$lowering" == "runtime" ]]; then
  graph="$scratch/browser-engine-restore-graph.json"
  "$dotnet" msbuild \
    "$repo_root/prototypes/inspect-web/engine/InspectWeb.Engine.csproj" \
    -t:GenerateRestoreGraphFile \
    -p:RestoreGraphOutputPath="$graph" \
    -p:Configuration=Release \
    -p:Features=runtime-async=on \
    -p:MSBuildEnableWorkloadResolver=false \
    -nologo \
    -v:q
  "$node" \
    "$repo_root/prototypes/inspect-web/scripts/verify-runtime-async-project-graph.ts" \
    "$repo_root" \
    "$graph" \
    "$compile_receipts"
fi

mkdir -p "$(dirname "$receipt")"
"$node" --input-type=module - "$assembly" "$site" "$lowering" \
  "$repo_root/prototypes/inspect-web/src/inspect-web-engine.d.ts" \
  "$receipt" <<'JS'
import { createHash } from "node:crypto";
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

const [assembly, site, lowering, contract, receipt] = process.argv.slice(2);
if (!assembly || !site || !lowering || !contract || !receipt) {
  throw new Error("missing async deployment receipt argument");
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
    schema: 1,
    method: "InspectionEngine.AsyncLoweringCanary",
    lowering,
    result: "inspect-web-async-lowering-ok",
    publish_assembly_sha256: sha256(assembly),
    published_webcil_file: webcil[0],
    published_webcil_sha256: sha256(resolve(site, "_framework", webcil[0])),
    contract_sha256: sha256(contract),
  })}\n`);
JS

echo "InspectWeb $lowering-async deployment evidence passed."
