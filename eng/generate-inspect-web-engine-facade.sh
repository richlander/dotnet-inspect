#!/usr/bin/env bash
# Regenerates inspect-web's canonical C# contract snapshots and proves that the
# complete facade set compiles into the transient declarations and JavaScript
# consumed by the frontend and published Browser/Wasm application.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
inspect_web="$repo_root/inspect-web"
engine_csproj="$inspect_web/DotnetInspect.Web/DotnetInspect.Web.csproj"
engine_output="$inspect_web/DotnetInspect.Web/bin/Release/net11.0"
engine_dll="$engine_output/DotnetInspect.Web.dll"
context_type="DotnetInspect.Web.InspectWebJsExportContext"

# The consumer map. Each canonical context artifact becomes exactly one public module; the
# map's domain must equal the recipe's output set before any TypeScript is compiled, so it
# can neither add nor omit a facade. `context_artifacts` and `facade_modules` are read as
# one ordered map, and every derived path is spelled from `facade_modules`.
context_artifacts=(
  "DotnetInspect.Web.ts"
  "DotnetInspect.Web.Interop.Package.ts"
  "DotnetInspect.Web.Interop.Library.ts"
  "DotnetInspect.Web.Interop.Metadata.ts"
  "DotnetInspect.Web.Interop.Analysis.ts"
  "DotnetInspect.Web.Interop.Source.ts"
  "DotnetInspect.Web.Interop.CallGraph.ts"
  "DotnetInspect.Web.Interop.Catalog.ts"
)
facade_modules=(
  "inspect-web-host"
  "inspect-web-package"
  "inspect-web-library"
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

ts_output_directory="$inspect_web/DotnetInspect.Web/facades"
dts_output_directory="$inspect_web/src/facades"
js_output_directory="$inspect_web/DotnetInspect.Web/wwwroot"
compiler="$inspect_web/scripts/compile-engine-facades.ts"
stale_msbuild_module="$js_output_directory/inspect-web-stale.js"

scratch="$(mktemp -d)"
cleanup() {
  rm -rf "$scratch"
  rm -f "$stale_msbuild_module"
}
trap cleanup EXIT
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}

usage="Usage: generate-inspect-web-engine-facade.sh [--compile | --fast-check | --check | --contract <assembly> <declaration-output-directory> <version-prefix>]"

mode=write
source_assembly="$engine_dll"
contract_output=
contract_version_prefix=
case "${1:-}" in
  "")
    ;;
  --compile)
    if [[ "$#" != 1 ]]; then
      echo "$usage" >&2
      exit 1
    fi
    exec "$node" "$compiler" --install
    ;;
  --fast-check)
    if [[ "$#" != 1 ]]; then
      echo "$usage" >&2
      exit 1
    fi
    mode=fast-check
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

if [[ ! -f "$compiler" ]]; then
  echo "Facade compiler not found at $compiler." >&2
  exit 1
fi

if [[ "$mode" != contract ]]; then
  "$dotnet" build "$engine_csproj" -c Release >&2
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
  ${generator_build_properties[@]+"${generator_build_properties[@]}"} \
  -- \
  "$source_assembly" \
  --context "$context_type" \
  --assembly-search-path "$source_assembly_directory" \
  --runtime-module ./runtime-loader.js \
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
    ${generator_build_properties[@]+"${generator_build_properties[@]}"} \
    -- \
    "$root_assembly" \
    --assembly-search-path "$source_assembly_directory" \
    --runtime-module ./runtime-loader.js \
    --output "$scratch/direct/$artifact"
  if ! cmp "$context_output/$artifact" "$scratch/direct/$artifact"; then
    echo "The JsExportRoot recipe differs from direct generation for $artifact." >&2
    exit 1
  fi
  cp "$context_output/$artifact" "$scratch/sources/$module.ts"
done

compiled="$scratch/compiled"
"$node" "$compiler" \
  --sources "$scratch/sources" \
  --output "$compiled"

expected_sources="$(printf '%s.ts\n' "${facade_modules[@]}" | sort)"
expected_declarations="$(printf '%s.d.ts\n' "${facade_modules[@]}" | sort)"
expected_modules="$(printf '%s.js\n' "${facade_modules[@]}" | sort)"

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

seed_stale_msbuild_module() {
  printf 'export const stale = true;\n' > "$stale_msbuild_module"
}

verify_msbuild_facade_build() {
  seed_stale_msbuild_module
  "$dotnet" build \
    "$engine_csproj" \
    -c Release \
    --no-restore \
    "$@" >&2
  if [[ -e "$stale_msbuild_module" ]]; then
    echo "error: the .NET build left the stale facade module in place." >&2
    exit 1
  fi
  assert_directory_inventory \
    "$js_output_directory" 'inspect-web-*.js' "$expected_modules"
}

verify_msbuild_facade_publish() {
  local publish_output="$scratch/publish"
  seed_stale_msbuild_module
  "$dotnet" publish \
    "$engine_csproj" \
    -c Release \
    --no-restore \
    --output "$publish_output" \
    "$@" >&2
  if [[ -e "$stale_msbuild_module" ]]; then
    echo "error: the .NET publish left the stale facade module in place." >&2
    exit 1
  fi
  assert_directory_inventory \
    "$js_output_directory" 'inspect-web-*.js' "$expected_modules"
  assert_directory_inventory \
    "$publish_output/wwwroot" 'inspect-web-*.js' "$expected_modules"
}

install_compiled_outputs() {
  rm -rf "$dts_output_directory"
  mkdir -p "$dts_output_directory" "$js_output_directory"
  shopt -s nullglob
  local old_modules=("$js_output_directory"/inspect-web-*.js)
  shopt -u nullglob
  if [[ "${#old_modules[@]}" != 0 ]]; then
    rm -f "${old_modules[@]}"
  fi
  for module in "${facade_modules[@]}"; do
    cp "$compiled/$module.d.ts" "$dts_output_directory/$module.d.ts"
    cp "$compiled/$module.js" "$js_output_directory/$module.js"
  done
  assert_directory_inventory \
    "$dts_output_directory" '*.d.ts' "$expected_declarations"
  assert_directory_inventory \
    "$js_output_directory" 'inspect-web-*.js' "$expected_modules"
}

typecheck_consumers() {
  (
    cd "$inspect_web"
    npm run typecheck:authored
  )
}

if [[ "$mode" == contract ]]; then
  mkdir -p "$contract_output"
  for module in "${facade_modules[@]}"; do
    cp "$compiled/$module.d.ts" "$contract_output/$module.d.ts"
  done
  assert_directory_inventory "$contract_output" '*.d.ts' "$expected_declarations"
  echo "Wrote the facade declaration set to $contract_output"
elif [[ "$mode" == check || "$mode" == fast-check ]]; then
  install_compiled_outputs
  consumer_failed=0
  if ! typecheck_consumers; then
    consumer_failed=1
  fi

  drifted=0
  for module in "${facade_modules[@]}"; do
    generated="$scratch/sources/$module.ts"
    committed="$ts_output_directory/$module.ts"
    if ! diff -q "$generated" "$committed" > /dev/null 2>&1; then
      echo "error: $committed is stale. Run eng/generate-inspect-web-engine-facade.sh and commit the canonical source." >&2
      diff "$committed" "$generated" >&2 || true
      drifted=1
    fi
  done
  assert_directory_inventory \
    "$ts_output_directory" '*.ts' "$expected_sources" || drifted=1
  if [[ "$drifted" != 0 || "$consumer_failed" != 0 ]]; then
    exit 1
  fi

  if [[ "$mode" == check ]]; then
    version_prefix=$(
      "$dotnet" msbuild \
        "$repo_root/src/DotnetInspect.Cli/DotnetInspect.Cli.csproj" \
        -getProperty:VersionPrefix \
        -nologo
    )
    if [[ -z "$version_prefix" ]]; then
      echo "The authoritative product VersionPrefix is empty." >&2
      exit 1
    fi
    verify_msbuild_facade_build "-p:VersionPrefix=$version_prefix"
    versioned_contract="$scratch/versioned-declarations"
    "$0" \
      --contract \
      "$engine_dll" \
      "$versioned_contract" \
      "$version_prefix" >&2
    for module in "${facade_modules[@]}"; do
      if ! cmp "$versioned_contract/$module.d.ts" "$compiled/$module.d.ts"; then
        echo "The deployment-version context changed the $module declaration." >&2
        exit 1
      fi
    done
    verify_msbuild_facade_publish "-p:VersionPrefix=$version_prefix"
  else
    verify_msbuild_facade_build
  fi

  echo "inspect-web canonical TypeScript facades are current and consumer-compatible."
else
  mkdir -p "$ts_output_directory"
  shopt -s nullglob
  old_sources=("$ts_output_directory"/*.ts)
  shopt -u nullglob
  if [[ "${#old_sources[@]}" != 0 ]]; then
    rm -f "${old_sources[@]}"
  fi
  for module in "${facade_modules[@]}"; do
    cp "$scratch/sources/$module.ts" "$ts_output_directory/$module.ts"
    echo "Wrote $ts_output_directory/$module.ts"
  done
  assert_directory_inventory "$ts_output_directory" '*.ts' "$expected_sources"
  install_compiled_outputs
  typecheck_consumers
fi
