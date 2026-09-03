#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}

if [[ "${1:-}" == "--compare" ]]; then
  if [[ "$#" -ne 3 ]]; then
    echo "Usage: verify-inspect-web-async-deployment.sh --compare <compiler-receipt.json> <runtime-receipt.json>" >&2
    exit 1
  fi
  "$node" --input-type=module - "$2" "$3" <<'JS'
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const [compilerPath, runtimePath] = process.argv.slice(2);
const compiler = JSON.parse(readFileSync(compilerPath, "utf8"));
const runtime = JSON.parse(readFileSync(runtimePath, "utf8"));
assert.equal(compiler.schema, 5);
assert.equal(runtime.schema, 5);
assert.equal(compiler.lowering, "compiler");
assert.equal(runtime.lowering, "runtime");

const commonTopLevel = receipt => ({
  method: receipt.method,
  result: receipt.result,
  facade_count: receipt.facade_count,
  assembly_count: receipt.assembly_count,
  js_export_method_count: receipt.js_export_method_count,
  async_method_count: receipt.async_method_count,
  repository_projects: receipt.repository_projects,
  repository_project_count: receipt.repository_project_count,
  repository_project_sha256: receipt.repository_project_sha256,
  smoke: receipt.smoke,
});
assert.deepEqual(
  commonTopLevel(compiler),
  commonTopLevel(runtime),
  "compiler/runtime async deployment domains or smoke outcomes differ");

const commonAssembly = assembly => ({
  name: assembly.name,
  file: assembly.file,
  module: assembly.module,
  generated_source_file: assembly.generated_source_file,
  generated_source_sha256: assembly.generated_source_sha256,
  declaration_file: assembly.declaration_file,
  declaration_sha256: assembly.declaration_sha256,
  published_js_file: assembly.published_js_file,
  published_js_sha256: assembly.published_js_sha256,
  webcil_assembly: assembly.webcil_assembly,
  js_export_method_count: assembly.js_export_method_count,
  async_method_count: assembly.async_method_count,
});
assert.deepEqual(
  compiler.assemblies.map(commonAssembly),
  runtime.assemblies.map(commonAssembly),
  "compiler/runtime facade contracts differ");

for (const receipt of [compiler, runtime]) {
  const isCompiler = receipt.lowering === "compiler";
  assert.equal(
    receipt.compiler_async_method_count,
    isCompiler ? receipt.async_method_count : 0);
  assert.equal(
    receipt.runtime_async_method_count,
    isCompiler ? 0 : receipt.async_method_count);
  for (const assembly of receipt.assemblies) {
    assert.equal(
      assembly.compiler_async_method_count,
      isCompiler ? assembly.async_method_count : 0);
    assert.equal(
      assembly.runtime_async_method_count,
      isCompiler ? 0 : assembly.async_method_count);
  }
}
console.log("InspectWeb compiler/runtime async deployment receipts are paired.");
JS
  exit 0
fi

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
if [[ ! -f "$assembly" ]]; then
  echo "Published host assembly was not found: $assembly" >&2
  exit 1
fi

scratch="$repo_root/artifacts/inspect-web-async-deployment-$lowering-$$"
rm -rf "$scratch"
mkdir -p "$scratch"
trap 'rm -rf "$scratch"' EXIT
census="$scratch/async-census.json"
domain="$scratch/facade-domain.json"
graph="$scratch/browser-engine-restore-graph.json"
graph_result="$scratch/async-project-graph.json"
smoke_result="$scratch/published-smoke.json"
context_output="$scratch/context-facades"
declarations="$scratch/declarations"
compiled_sources="$scratch/compiled-sources"
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
  "$lowering" \
  "$census"

"$node" --input-type=module - "$census" "$domain" <<'JS'
import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";

const [censusPath, domainPath] = process.argv.slice(2);
const census = JSON.parse(readFileSync(censusPath, "utf8"));
const expected = [
  "InspectWeb.Engine",
  "InspectWeb.Engine.AnalysisExports",
  "InspectWeb.Engine.CallGraphExports",
  "InspectWeb.Engine.CatalogExports",
  "InspectWeb.Engine.MetadataExports",
  "InspectWeb.Engine.PackageExports",
  "InspectWeb.Engine.SourceExports",
];
assert.deepEqual(
  census.assemblies.map(assembly => assembly.name),
  expected,
  "compiled InspectWebJsExportContext does not declare the exact facade set");
function moduleName(assembly) {
  if (assembly === "InspectWeb.Engine") return "inspect-web-host";
  const match = /^InspectWeb\.Engine\.([A-Z][A-Za-z0-9]*)Exports$/.exec(assembly);
  assert.ok(match, `context assembly ${assembly} has no public module mapping`);
  return `inspect-web-${match[1]
    .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
    .toLowerCase()}`;
}
writeFileSync(
  domainPath,
  `${JSON.stringify(census.assemblies.map(entry => ({
    assembly: entry.name,
    module: moduleName(entry.name),
  })))}\n`);
JS

TMPDIR="$repo_root/artifacts" \
  "$repo_root/eng/generate-inspect-web-engine-facade.sh" \
  --contract \
  "$assembly" \
  "$declarations" \
  "$version_prefix"

assembly_directory="$(dirname "$assembly")"
TMPDIR="$repo_root/artifacts" \
  "$dotnet" run \
  --project "$repo_root/src/ts-jsexport" \
  -c Release \
  -p:VersionPrefix="$version_prefix" \
  -- \
  "$assembly" \
  --context InspectWeb.Engine.InspectWebJsExportContext \
  --assembly-search-path "$assembly_directory" \
  --runtime-module ./_framework/dotnet.js \
  --output "$context_output"

runtime_pack_directory=$(
  "$dotnet" msbuild \
    "$repo_root/prototypes/inspect-web/engine/InspectWeb.Engine.csproj" \
    -nologo \
    -target:ProcessFrameworkReferences \
    -getItem:RuntimePack \
  | "$node" -e '
const data = JSON.parse(require("fs").readFileSync(0, "utf8"));
const matches = (data.Items?.RuntimePack ?? []).filter(
  pack => pack.Identity === "Microsoft.NETCore.App.Runtime.Mono.browser-wasm");
if (matches.length !== 1 || !matches[0].PackageDirectory) {
  console.error(
    `Expected one resolved browser-wasm runtime pack; found ${matches.length}.`);
  process.exit(1);
}
process.stdout.write(matches[0].PackageDirectory);
'
)
dotnet_dts="$runtime_pack_directory/runtimes/browser-wasm/native/dotnet.d.ts"
if [[ ! -f "$dotnet_dts" ]]; then
  echo "SDK-owned browser dotnet.d.ts was not found in $runtime_pack_directory." >&2
  exit 1
fi

mkdir -p "$compiled_sources/_framework"
cp "$dotnet_dts" "$compiled_sources/_framework/dotnet.d.ts"
"$node" --input-type=module - \
  "$domain" \
  "$context_output" \
  "$compiled_sources" \
  "$repo_root/prototypes/inspect-web/engine/facades" <<'JS'
import assert from "node:assert/strict";
import {
  copyFileSync,
  readFileSync,
  readdirSync,
} from "node:fs";
import { resolve } from "node:path";

const [domainPath, contextOutput, compiledSources, checkedInSources] =
  process.argv.slice(2);
const domain = JSON.parse(readFileSync(domainPath, "utf8"));
const expected = domain.map(entry => `${entry.assembly}.ts`).sort();
assert.deepEqual(
  readdirSync(contextOutput).sort(),
  expected,
  "context generation output does not match the compiled facade domain");
for (const entry of domain) {
  const generated = resolve(contextOutput, `${entry.assembly}.ts`);
  const checkedIn = resolve(checkedInSources, `${entry.module}.ts`);
  assert.deepEqual(
    readFileSync(generated),
    readFileSync(checkedIn),
    `${entry.module}.ts differs from its freshly generated context source`);
  copyFileSync(generated, resolve(compiledSources, `${entry.module}.ts`));
}
JS

"$node" --input-type=module - "$domain" "$compiled_sources/tsconfig.json" <<'JS'
import { readFileSync, writeFileSync } from "node:fs";

const [domainPath, configPath] = process.argv.slice(2);
const domain = JSON.parse(readFileSync(domainPath, "utf8"));
writeFileSync(configPath, `${JSON.stringify({
  compilerOptions: {
    declaration: true,
    exactOptionalPropertyTypes: true,
    lib: ["DOM", "ES2022"],
    module: "ESNext",
    moduleResolution: "Bundler",
    newLine: "lf",
    noImplicitReturns: true,
    noUncheckedIndexedAccess: true,
    outDir: "out",
    strict: true,
    target: "ES2022",
    types: [],
    verbatimModuleSyntax: true,
  },
  include: domain.map(entry => `${entry.module}.ts`),
}, null, 2)}\n`);
JS

tsc="$repo_root/prototypes/inspect-web/node_modules/.bin/tsc"
if [[ ! -x "$tsc" ]]; then
  echo "TypeScript compiler not found at $tsc." >&2
  exit 1
fi
"$tsc" -p "$compiled_sources/tsconfig.json"

"$node" --input-type=module - \
  "$domain" \
  "$compiled_sources/out" \
  "$declarations" \
  "$site" \
  "$repo_root/prototypes/inspect-web/src/facades" <<'JS'
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { resolve } from "node:path";

const [domainPath, compiled, contract, site, checkedInDeclarations] =
  process.argv.slice(2);
const domain = JSON.parse(readFileSync(domainPath, "utf8"));
const expectedDeclarations = domain.map(entry => `${entry.module}.d.ts`).sort();
const expectedModules = domain.map(entry => `${entry.module}.js`).sort();
assert.deepEqual(
  readdirSync(contract).sort(),
  expectedDeclarations,
  "contract generation did not emit the context-issued declaration set");
assert.deepEqual(
  readdirSync(compiled).filter(name => name.endsWith(".d.ts")).sort(),
  expectedDeclarations,
  "pinned TypeScript compilation emitted an unexpected declaration set");
assert.deepEqual(
  readdirSync(compiled).filter(name => name.endsWith(".js")).sort(),
  expectedModules,
  "pinned TypeScript compilation emitted an unexpected JavaScript set");
for (const entry of domain) {
  const declaration = `${entry.module}.d.ts`;
  const javascript = `${entry.module}.js`;
  assert.deepEqual(
    readFileSync(resolve(compiled, declaration)),
    readFileSync(resolve(contract, declaration)),
    `${declaration} differs from the generator contract`);
  assert.deepEqual(
    readFileSync(resolve(compiled, declaration)),
    readFileSync(resolve(checkedInDeclarations, declaration)),
    `${declaration} differs from the checked-in consumer declaration`);
  assert.deepEqual(
    readFileSync(resolve(compiled, javascript)),
    readFileSync(resolve(site, javascript)),
    `${javascript} differs from the freshly compiled context source`);
}
JS

"$node" \
  "$repo_root/prototypes/inspect-web/scripts/verify-published-engine-facades.ts" \
  "$site" \
  deployment \
  "$domain" \
  "$smoke_result"

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
"$node" --input-type=module - \
  "$assembly_directory" \
  "$site" \
  "$lowering" \
  "$context_output" \
  "$compiled_sources/out" \
  "$census" \
  "$domain" \
  "$graph_result" \
  "$smoke_result" \
  "$receipt" <<'JS'
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { resolve } from "node:path";

const [
  assemblyDirectory,
  site,
  lowering,
  contextOutput,
  compiled,
  censusPath,
  domainPath,
  graphResultPath,
  smokeResultPath,
  receipt,
] = process.argv.slice(2);
const census = JSON.parse(readFileSync(censusPath, "utf8"));
const domain = JSON.parse(readFileSync(domainPath, "utf8"));
const graph = JSON.parse(readFileSync(graphResultPath, "utf8"));
const smoke = JSON.parse(readFileSync(smokeResultPath, "utf8"));
const frameworkFiles = readdirSync(resolve(site, "_framework"));
const sha256 = path =>
  createHash("sha256").update(readFileSync(path)).digest("hex");
const byAssembly = new Map(
  domain.map(entry => [entry.assembly, entry.module]));
assert.equal(byAssembly.size, census.assembly_count);

const assemblies = census.assemblies.map(entry => {
  const module = byAssembly.get(entry.name);
  assert.ok(module, `context assembly ${entry.name} has no module association`);
  const webcilPattern = new RegExp(
    `^${entry.name.replaceAll(".", String.raw`\.`)}\\.[A-Za-z0-9]+\\.wasm$`);
  const webcil = frameworkFiles.filter(name => webcilPattern.test(name));
  assert.equal(
    webcil.length,
    1,
    `Expected one published ${entry.name} WebCIL image; found ${webcil.length}.`);
  const assemblyPath = resolve(assemblyDirectory, entry.file);
  const sourcePath = resolve(contextOutput, `${entry.name}.ts`);
  const declarationPath = resolve(compiled, `${module}.d.ts`);
  const javascriptPath = resolve(site, `${module}.js`);
  return {
    name: entry.name,
    file: entry.file,
    publish_assembly_sha256: sha256(assemblyPath),
    module,
    generated_source_file: `${entry.name}.ts`,
    generated_source_sha256: sha256(sourcePath),
    declaration_file: `${module}.d.ts`,
    declaration_sha256: sha256(declarationPath),
    published_js_file: `${module}.js`,
    published_js_sha256: sha256(javascriptPath),
    webcil_assembly: entry.name,
    published_webcil_file: webcil[0],
    published_webcil_sha256: sha256(
      resolve(site, "_framework", webcil[0])),
    js_export_method_count: entry.js_export_method_count,
    async_method_count: entry.async_method_count,
    compiler_async_method_count: entry.compiler_async_method_count,
    runtime_async_method_count: entry.runtime_async_method_count,
  };
});

const integerFields = [
  census.assembly_count,
  census.js_export_method_count,
  census.async_method_count,
  census.compiler_async_method_count,
  census.runtime_async_method_count,
  graph.repository_project_count,
];
assert.ok(integerFields.every(Number.isInteger), "receipt counts are not integers");
assert.equal(census.assembly_count, 7);
assert.equal(assemblies.length, 7);
assert.equal(census.js_export_method_count, 45);
assert.ok(census.async_method_count > 0);
assert.equal(
  census.async_method_count,
  census.compiler_async_method_count + census.runtime_async_method_count);
assert.equal(
  census.compiler_async_method_count,
  lowering === "compiler" ? census.async_method_count : 0);
assert.equal(
  census.runtime_async_method_count,
  lowering === "runtime" ? census.async_method_count : 0);
assert.equal(graph.repository_projects.length, graph.repository_project_count);
assert.match(graph.repository_project_sha256, /^[0-9a-f]{64}$/);
assert.deepEqual(smoke.initialized_facades, domain);
assert.equal(smoke.sdk_create_count, 1);
assert.equal(smoke.sdk_runtime_count, 1);
assert.equal(smoke.entry_point_count, 0);
assert.equal(smoke.async_lowering_canary, "inspect-web-async-lowering-ok");

writeFileSync(
  receipt,
  `${JSON.stringify({
    schema: 5,
    method: "InspectionEngine.AsyncLoweringCanary",
    lowering,
    result: "inspect-web-async-lowering-ok",
    facade_count: assemblies.length,
    assembly_count: assemblies.length,
    js_export_method_count: census.js_export_method_count,
    async_method_count: census.async_method_count,
    compiler_async_method_count: census.compiler_async_method_count,
    runtime_async_method_count: census.runtime_async_method_count,
    assemblies,
    repository_projects: graph.repository_projects,
    repository_project_count: graph.repository_project_count,
    repository_project_sha256: graph.repository_project_sha256,
    smoke,
  })}\n`);
JS

echo "InspectWeb $lowering-async deployment evidence passed."
