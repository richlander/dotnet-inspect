#!/usr/bin/env bash
# Executes inspect-web's compiled JsExportRoot recipe once, then compiles the complete
# generated facade set into the declarations consumed by Vite and the JavaScript modules
# published beside _framework/. The recipe emits one canonical artifact per rooted managed
# export assembly; the consumer map below names the public module each artifact becomes.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
inspect_web="$repo_root/prototypes/inspect-web"
engine_csproj="$inspect_web/engine/InspectWeb.Engine.csproj"
engine_output="$inspect_web/engine/bin/Release/net11.0"
engine_dll="$engine_output/InspectWeb.Engine.dll"
context_type="InspectWeb.Engine.InspectWebJsExportContext"

# The consumer map. Each canonical context artifact becomes exactly one public module; the
# map's domain must equal the recipe's output set before any TypeScript is compiled, so it
# can neither add nor omit a facade. `context_artifacts` and `facade_modules` are read as
# one ordered map, and every derived path is spelled from `facade_modules`.
context_artifacts=(
  "InspectWeb.Engine.ts"
  "InspectWeb.Engine.PackageExports.ts"
  "InspectWeb.Engine.MetadataExports.ts"
  "InspectWeb.Engine.AnalysisExports.ts"
  "InspectWeb.Engine.SourceExports.ts"
  "InspectWeb.Engine.CallGraphExports.ts"
  "InspectWeb.Engine.CatalogExports.ts"
)
facade_modules=(
  "inspect-web-host"
  "inspect-web-package"
  "inspect-web-metadata"
  "inspect-web-analysis"
  "inspect-web-source"
  "inspect-web-call-graph"
  "inspect-web-catalog"
)
if [[ "${#context_artifacts[@]}" != "${#facade_modules[@]}" ]]; then
  echo "The consumer map is malformed: artifact and module counts differ." >&2
  exit 1
fi

ts_output_directory="$inspect_web/engine/facades"
dts_output_directory="$inspect_web/src/facades"
js_output_directory="$inspect_web/engine/wwwroot"

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}

usage="Usage: generate-inspect-web-engine-facade.sh [--check | --contract <assembly> <declaration-output-directory> <version-prefix>]"

mode=write
source_assembly="$engine_dll"
contract_output=
contract_version_prefix=
case "${1:-}" in
  "")
    ;;
  --check)
    if [[ "$#" != 1 ]]; then
      echo "$usage" >&2
      exit 1
    fi
    mode=check
    ;;
  --contract)
    if [[ "$#" != 4 ]]; then
      echo "$usage" >&2
      exit 1
    fi
    mode=contract
    source_assembly="$2"
    contract_output="$3"
    contract_version_prefix="$4"
    if [[ ! -f "$source_assembly" ]]; then
      echo "Assembly not found: $source_assembly" >&2
      exit 1
    fi
    if [[ -z "$contract_version_prefix" ]]; then
      echo "Version prefix must not be empty." >&2
      exit 1
    fi
    ;;
  *)
    echo "$usage" >&2
    exit 1
    ;;
esac

tsc=${TSC:-"$inspect_web/node_modules/.bin/tsc"}
if [[ ! -x "$tsc" ]]; then
  echo "TypeScript compiler not found at $tsc; run npm ci in prototypes/inspect-web." >&2
  exit 1
fi

if [[ "$mode" != contract ]]; then
  "$dotnet" build "$engine_csproj" -c Release >&2
fi
runtime_pack_directory=$(
  "$dotnet" msbuild \
    "$engine_csproj" \
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
  echo "SDK-owned browser dotnet.d.ts was not found in resolved runtime pack $runtime_pack_directory." >&2
  exit 1
fi

# One invocation of the compiled recipe, into a destination that does not exist yet: the
# whole facade set is emitted as one operation or not at all.
context_output="$scratch/context-facades"
source_assembly_directory="$(dirname "$source_assembly")"
generator_build_properties=()
if [[ -n "$contract_version_prefix" ]]; then
  generator_build_properties+=("-p:VersionPrefix=$contract_version_prefix")
fi
"$dotnet" run \
  --project "$repo_root/src/ts-jsexport" \
  -c Release \
  "${generator_build_properties[@]}" \
  -- \
  "$source_assembly" \
  --context "$context_type" \
  --assembly-search-path "$source_assembly_directory" \
  --runtime-module ./_framework/dotnet.js \
  --output "$context_output"

expected_artifacts="$(printf '%s\n' "${context_artifacts[@]}" | sort)"
shopt -s nullglob
emitted_paths=("$context_output"/*)
shopt -u nullglob
emitted_artifacts="$(printf '%s\n' "${emitted_paths[@]##*/}" | sort)"
if [[ "$emitted_artifacts" != "$expected_artifacts" ]]; then
  echo "The JsExportRoot recipe emitted a different facade set than the consumer map:" >&2
  diff <(printf '%s\n' "$expected_artifacts") <(printf '%s\n' "$emitted_artifacts") >&2 || true
  exit 1
fi

# Each rooted assembly is also generated on its own. The recipe decides membership; this
# proves it changes no artifact, so the checked-in source of one facade stays the handoff
# for exactly one managed export assembly.
mkdir -p "$scratch/sources" "$scratch/direct"
for index in "${!context_artifacts[@]}"; do
  artifact="${context_artifacts[$index]}"
  module="${facade_modules[$index]}"
  root_assembly="$source_assembly_directory/${artifact%.ts}.dll"
  if [[ ! -f "$root_assembly" ]]; then
    echo "Rooted export assembly not found: $root_assembly" >&2
    exit 1
  fi
  "$dotnet" run \
    --project "$repo_root/src/ts-jsexport" \
    -c Release \
    --no-build \
    "${generator_build_properties[@]}" \
    -- \
    "$root_assembly" \
    --runtime-module ./_framework/dotnet.js \
    --output "$scratch/direct/$artifact"
  if ! cmp "$context_output/$artifact" "$scratch/direct/$artifact"; then
    echo "The JsExportRoot recipe differs from direct generation for $artifact." >&2
    exit 1
  fi
  # The checked-in source is a byte-identical copy of its canonical artifact; the consumer
  # map renames it and nothing else.
  cp "$context_output/$artifact" "$scratch/sources/$module.ts"
done

mkdir -p "$scratch/sources/_framework"
cp "$dotnet_dts" "$scratch/sources/_framework/dotnet.d.ts"
{
  cat <<'JSON'
{
  "compilerOptions": {
    "declaration": true,
    "exactOptionalPropertyTypes": true,
    "lib": ["DOM", "ES2022"],
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "newLine": "lf",
    "noImplicitReturns": true,
    "noUncheckedIndexedAccess": true,
    "outDir": "out",
    "strict": true,
    "target": "ES2022",
    "types": [],
    "verbatimModuleSyntax": true
  },
JSON
  printf '  "include": ['
  for index in "${!facade_modules[@]}"; do
    [[ "$index" == 0 ]] || printf ', '
    printf '"%s.ts"' "${facade_modules[$index]}"
  done
  printf ']\n}\n'
} > "$scratch/sources/tsconfig.json"
"$tsc" -p "$scratch/sources/tsconfig.json"

compiled="$scratch/sources/out"
expected_declarations="$(
  printf '%s.d.ts\n' "${facade_modules[@]}" | sort)"
expected_modules="$(printf '%s.js\n' "${facade_modules[@]}" | sort)"
shopt -s nullglob
compiled_declaration_paths=("$compiled"/*.d.ts)
compiled_module_paths=("$compiled"/*.js)
shopt -u nullglob
compiled_declarations="$(
  printf '%s\n' "${compiled_declaration_paths[@]##*/}" | sort)"
compiled_modules="$(printf '%s\n' "${compiled_module_paths[@]##*/}" | sort)"
if [[ "$compiled_declarations" != "$expected_declarations" \
    || "$compiled_modules" != "$expected_modules" ]]; then
  echo "Compiling the facade set produced a different artifact set than the consumer map." >&2
  exit 1
fi

for module in "${facade_modules[@]}"; do
  if grep -E 'RuntimeAPI|dotnet(\.js)?' "$compiled/$module.d.ts" >/dev/null; then
    echo "Generated public declaration $module.d.ts leaked an SDK runtime type." >&2
    exit 1
  fi
done

printf '{ "type": "module" }\n' > "$compiled/package.json"
"$node" \
  "$inspect_web/scripts/verify-engine-facade-runtime.ts" \
  "$compiled"

# The checked-in trees are exact inventories: a stale module left behind by a removed
# facade fails here rather than shipping beside the current set.
assert_directory_inventory() {
  local directory="$1"
  local pattern="$2"
  local expected="$3"
  local present
  shopt -s nullglob
  local paths=("$directory"/$pattern)
  shopt -u nullglob
  present="$(printf '%s\n' "${paths[@]##*/}" | sort)"
  if [[ "$present" != "$expected" ]]; then
    echo "error: $directory holds a different $pattern set than the consumer map." >&2
    diff <(printf '%s\n' "$expected") <(printf '%s\n' "$present") >&2 || true
    return 1
  fi
}

expected_sources="$(printf '%s.ts\n' "${facade_modules[@]}" | sort)"

if [[ "$mode" == contract ]]; then
  mkdir -p "$contract_output"
  for module in "${facade_modules[@]}"; do
    cp "$compiled/$module.d.ts" "$contract_output/$module.d.ts"
  done
  assert_directory_inventory "$contract_output" '*.d.ts' "$expected_declarations"
  echo "Wrote the facade declaration set to $contract_output"
elif [[ "$mode" == check ]]; then
  drifted=0
  for module in "${facade_modules[@]}"; do
    for pair in \
      "$scratch/sources/$module.ts:$ts_output_directory/$module.ts" \
      "$compiled/$module.d.ts:$dts_output_directory/$module.d.ts" \
      "$compiled/$module.js:$js_output_directory/$module.js"; do
      generated="${pair%%:*}"
      committed="${pair##*:}"
      if ! diff -q "$generated" "$committed" > /dev/null 2>&1; then
        echo "error: $committed is stale. Run eng/generate-inspect-web-engine-facade.sh and commit the result." >&2
        diff "$committed" "$generated" >&2 || true
        drifted=1
      fi
    done
  done
  assert_directory_inventory \
    "$ts_output_directory" '*.ts' "$expected_sources" || drifted=1
  assert_directory_inventory \
    "$dts_output_directory" '*.d.ts' "$expected_declarations" || drifted=1
  assert_directory_inventory \
    "$js_output_directory" '*.js' "$expected_modules" || drifted=1
  if [[ "$drifted" != 0 ]]; then
    exit 1
  fi

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
  "$dotnet" build \
    "$engine_csproj" \
    -c Release \
    -p:VersionPrefix="$version_prefix" >&2
  versioned_contract="$scratch/versioned-declarations"
  "$0" \
    --contract \
    "$engine_dll" \
    "$versioned_contract" \
    "$version_prefix" >&2
  for module in "${facade_modules[@]}"; do
    if ! cmp "$versioned_contract/$module.d.ts" "$dts_output_directory/$module.d.ts"; then
      echo "The deployment-version context changed the $module declaration." >&2
      exit 1
    fi
  done

  echo "inspect-web TypeScript facades and compiler-derived artifacts are up to date."
else
  mkdir -p "$ts_output_directory" "$dts_output_directory" "$js_output_directory"
  for module in "${facade_modules[@]}"; do
    cp "$scratch/sources/$module.ts" "$ts_output_directory/$module.ts"
    cp "$compiled/$module.d.ts" "$dts_output_directory/$module.d.ts"
    cp "$compiled/$module.js" "$js_output_directory/$module.js"
    echo "Wrote $ts_output_directory/$module.ts"
    echo "Wrote $dts_output_directory/$module.d.ts"
    echo "Wrote $js_output_directory/$module.js"
  done
  assert_directory_inventory "$ts_output_directory" '*.ts' "$expected_sources"
  assert_directory_inventory "$dts_output_directory" '*.d.ts' "$expected_declarations"
  assert_directory_inventory "$js_output_directory" '*.js' "$expected_modules"
fi
