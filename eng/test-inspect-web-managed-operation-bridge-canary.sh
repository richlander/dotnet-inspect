#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
canary="$repo_root/prototypes/inspect-web/managed-operation-bridge-canary"
host="$canary/Host/InspectWeb.ManagedOperationBridge.BrowserCanary.Host.csproj"
verifier="$repo_root/prototypes/inspect-web/scripts/verify-managed-operation-bridge-canary.ts"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}
tsc=${TSC:-"$repo_root/prototypes/inspect-web/node_modules/.bin/tsc"}

expect_failure() {
  local name=$1
  local expected=$2
  shift 2
  if "$@" >"$scratch/$name.stdout" 2>"$scratch/$name.stderr"; then
    echo "$name mutation unexpectedly passed." >&2
    exit 1
  fi
  if ! grep -Fq \
      "$expected" \
      "$scratch/$name.stdout" \
      "$scratch/$name.stderr"; then
    echo "$name mutation failed for an unexpected reason." >&2
    cat "$scratch/$name.stdout" "$scratch/$name.stderr" >&2
    exit 1
  fi
}

DOTNET="$dotnet" NODE="$node" TSC="$tsc" \
  "$repo_root/eng/generate-inspect-web-managed-operation-bridge-canary.sh" \
  --check

mkdir -p "$scratch/stale-facade"
cp "$canary/facades/bridge.ts" "$scratch/stale-facade/"
printf '\n// stale\n' >> "$scratch/stale-facade/bridge.ts"
expect_failure \
  stale-bridge-facade \
  "bridge.ts is stale" \
  env \
  CANARY_FACADE_OUTPUT_DIR="$scratch/stale-facade" \
  DOTNET="$dotnet" \
  NODE="$node" \
  TSC="$tsc" \
  "$repo_root/eng/generate-inspect-web-managed-operation-bridge-canary.sh" \
  --check

runtime_pack_directory=$(
  "$dotnet" msbuild \
    "$host" \
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

mkdir -p "$scratch/source/facades" "$scratch/source/_framework"
cp "$canary/facades/bridge.ts" "$scratch/source/facades/"
cp "$canary/initialize.ts" "$canary/exercise.ts" "$scratch/source/"
cp \
  "$runtime_pack_directory/runtimes/browser-wasm/native/dotnet.d.ts" \
  "$scratch/source/_framework/"
cat > "$scratch/source/tsconfig.json" <<'JSON'
{
  "compilerOptions": {
    "exactOptionalPropertyTypes": true,
    "lib": ["DOM", "ES2022"],
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "newLine": "lf",
    "noImplicitReturns": true,
    "noUncheckedIndexedAccess": true,
    "outDir": "../modules",
    "strict": true,
    "target": "ES2022",
    "types": [],
    "verbatimModuleSyntax": true
  },
  "include": ["facades/*.ts", "initialize.ts", "exercise.ts"]
}
JSON
"$tsc" -p "$scratch/source/tsconfig.json"

clear_canary_build_outputs() {
  rm -rf \
    "$canary/Bridge/bin/Release/net11.0" \
    "$canary/Bridge/obj/Release/net11.0" \
    "$canary/Host/bin/Release/net11.0" \
    "$canary/Host/obj/Release/net11.0"
}

publish_canary() {
  local runtime_name=$1
  local use_mono_runtime=$2
  local output="$scratch/publish-$runtime_name"
  local dotnet_module
  local expected_runtime_pack
  local resolved_runtime_pack
  local runtime_properties=()

  if [[ "$use_mono_runtime" == false ]]; then
    expected_runtime_pack="Microsoft.NETCore.App.Runtime.browser-wasm"
    runtime_properties=(
      -p:WasmBuildNative=false
      -p:WasmNestedPublishAppDependsOn=
      -p:WasmEnableExceptionHandling=true
    )
  else
    expected_runtime_pack="Microsoft.NETCore.App.Runtime.Mono.browser-wasm"
  fi

  resolved_runtime_pack=$(
    "$dotnet" msbuild \
      "$host" \
      -nologo \
      -p:UseMonoRuntime="$use_mono_runtime" \
      -target:ProcessFrameworkReferences \
      -getItem:RuntimePack \
    | "$node" -e '
const data = JSON.parse(require("fs").readFileSync(0, "utf8"));
const matches = (data.Items?.RuntimePack ?? []).filter(
  pack => pack.Identity === "Microsoft.NETCore.App.Runtime.Mono.browser-wasm"
    || pack.Identity === "Microsoft.NETCore.App.Runtime.browser-wasm");
if (matches.length !== 1) {
  console.error(`Expected one browser-wasm runtime pack; found ${matches.length}.`);
  process.exit(1);
}
process.stdout.write(matches[0].Identity);
'
  )
  if [[ "$resolved_runtime_pack" != "$expected_runtime_pack" ]]; then
    echo \
      "Expected $runtime_name runtime pack $expected_runtime_pack; resolved $resolved_runtime_pack." \
      >&2
    exit 1
  fi

  clear_canary_build_outputs
  "$dotnet" publish \
    "$host" \
    -c Release \
    --output "$output" \
    -p:CanaryModulesDir="$scratch/modules" \
    -p:UseMonoRuntime="$use_mono_runtime" \
    "${runtime_properties[@]}" \
    --nologo
  published_site="$output/wwwroot"
  dotnet_module=$(
    find "$published_site/_framework" \
      -maxdepth 1 \
      -type f \
      -name 'dotnet.*.js' \
      ! -name 'dotnet.native.*' \
      ! -name 'dotnet.runtime.*' \
      -print -quit
  )
  if [[ -z "$dotnet_module" ]]; then
    echo "Published $runtime_name Browser/Wasm runtime module was not found." >&2
    exit 1
  fi
  ln -s \
    "$(basename "$dotnet_module")" \
    "$published_site/_framework/dotnet.js"
  test -f "$published_site/initialize.js"
  test -f "$published_site/exercise.js"
  test -f "$published_site/facades/bridge.js"
  printf '{ "type": "module" }\n' > "$published_site/package.json"

  "$node" "$verifier" "$published_site" baseline
  echo "Managed-operation bridge $runtime_name Browser/Wasm canary passed."
}

published_site=
publish_canary mono true
mono_site=$published_site
publish_canary coreclr false

expect_failure \
  wrong-cancellation-target \
  "User cancellation result kind returned Failed instead of Canceled." \
  "$node" "$verifier" "$mono_site" wrong-cancellation-target
expect_failure \
  skipped-expected-failure \
  "did not execute every baseline scenario exactly once" \
  "$node" "$verifier" "$mono_site" skip-expected-failure
expect_failure \
  skipped-retained-progress \
  "did not execute every baseline scenario exactly once" \
  "$node" "$verifier" "$mono_site" skip-retained-progress
expect_failure \
  split-shared-neighbor \
  "Shared initial waiter count returned 1 instead of 2." \
  "$node" "$verifier" "$mono_site" split-shared-neighbor
expect_failure \
  early-shared-finalization \
  "Shared finalization physical completion returned true instead of false." \
  "$node" "$verifier" "$mono_site" early-shared-finalization
expect_failure \
  skipped-final-natural \
  "did not execute every shared scenario exactly once" \
  "$node" "$verifier" "$mono_site" skip-final-natural

echo "Managed-operation bridge Browser/Wasm mutations were rejected."
