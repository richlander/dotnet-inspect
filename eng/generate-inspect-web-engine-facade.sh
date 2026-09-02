#!/usr/bin/env bash
# Executes inspect-web's compiled JsExportRoot recipe to regenerate its checked-in
# TypeScript facade, then compiles that source into the declaration consumed by
# Vite and the JavaScript module published beside _framework/.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
engine_csproj="$repo_root/prototypes/inspect-web/engine/InspectWeb.Engine.csproj"
engine_dll="$repo_root/prototypes/inspect-web/engine/bin/Release/net11.0/InspectWeb.Engine.dll"
context_type="InspectWeb.Engine.InspectWebJsExportContext"
context_artifact_name="InspectWeb.Engine.ts"
ts_output_file="$repo_root/prototypes/inspect-web/engine/inspect-web-engine.ts"
dts_output_file="$repo_root/prototypes/inspect-web/src/inspect-web-engine.d.ts"
js_output_file="$repo_root/prototypes/inspect-web/engine/wwwroot/inspect-web-engine.js"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}

mode=write
source_assembly="$engine_dll"
contract_output=
case "${1:-}" in
  "")
    ;;
  --check)
    if [[ "$#" != 1 ]]; then
      echo "Usage: generate-inspect-web-engine-facade.sh [--check | --contract <assembly> <declaration-output>]" >&2
      exit 1
    fi
    mode=check
    ;;
  --contract)
    if [[ "$#" != 3 ]]; then
      echo "Usage: generate-inspect-web-engine-facade.sh --contract <assembly> <declaration-output>" >&2
      exit 1
    fi
    mode=contract
    source_assembly="$2"
    contract_output="$3"
    if [[ ! -f "$source_assembly" ]]; then
      echo "Assembly not found: $source_assembly" >&2
      exit 1
    fi
    ;;
  *)
    echo "Usage: generate-inspect-web-engine-facade.sh [--check | --contract <assembly> <declaration-output>]" >&2
    exit 1
    ;;
esac

tsc=${TSC:-"$repo_root/prototypes/inspect-web/node_modules/.bin/tsc"}
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

cat > "$scratch/header" <<'EOF'
// GENERATED FILE — DO NOT EDIT BY HAND.
//
// Generated from InspectWeb.Engine.dll's [JSExport] surface. Regenerate with:
//   eng/generate-inspect-web-engine-facade.sh
// CI fails if this facade or either compiler-derived artifact drifts.

EOF

context_output="$scratch/context-facades"
source_assembly_directory="$(dirname "$source_assembly")"
"$dotnet" run \
  --project "$repo_root/src/ts-jsexport" \
  -c Release \
  -- \
  "$source_assembly" \
  --context "$context_type" \
  --assembly-search-path "$source_assembly_directory" \
  --runtime-module ./_framework/dotnet.js \
  --output "$context_output"

shopt -s nullglob
context_artifacts=("$context_output"/*)
shopt -u nullglob
if [[ "${#context_artifacts[@]}" != 1 \
    || "${context_artifacts[0]##*/}" != "$context_artifact_name" ]]; then
  printf \
    'Expected the JsExportRoot recipe to produce only %s; found:' \
    "$context_artifact_name" >&2
  printf ' %s' "${context_artifacts[@]##*/}" >&2
  printf '\n' >&2
  exit 1
fi
context_artifact="${context_artifacts[0]}"

"$dotnet" run \
  --project "$repo_root/src/ts-jsexport" \
  -c Release \
  --no-build \
  -- \
  "$source_assembly" \
  --runtime-module ./_framework/dotnet.js \
  --output "$scratch/inspect-web-engine.direct.ts"
if ! cmp "$context_artifact" "$scratch/inspect-web-engine.direct.ts"; then
  echo "The one-root JsExportRoot recipe differs from direct generation." >&2
  exit 1
fi

cat \
  "$scratch/header" \
  "$context_artifact" \
  > "$scratch/inspect-web-engine.ts"

mkdir -p "$scratch/_framework"
cp "$dotnet_dts" "$scratch/_framework/dotnet.d.ts"
cat > "$scratch/tsconfig.json" <<'JSON'
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
  "include": ["inspect-web-engine.ts"]
}
JSON
"$tsc" -p "$scratch/tsconfig.json"

if grep -E 'RuntimeAPI|dotnet(\.js)?' "$scratch/out/inspect-web-engine.d.ts" >/dev/null; then
  echo "Generated public declaration leaked an SDK runtime type." >&2
  exit 1
fi

printf '{ "type": "module" }\n' > "$scratch/out/package.json"
"$node" \
  "$repo_root/prototypes/inspect-web/scripts/verify-engine-facade-runtime.ts" \
  "$scratch/out/inspect-web-engine.js"

if [[ "$mode" == contract ]]; then
  cp "$scratch/out/inspect-web-engine.d.ts" "$contract_output"
  echo "Wrote $contract_output"
elif [[ "$mode" == check ]]; then
  drifted=0
  for pair in \
    "$scratch/inspect-web-engine.ts:$ts_output_file" \
    "$scratch/out/inspect-web-engine.d.ts:$dts_output_file" \
    "$scratch/out/inspect-web-engine.js:$js_output_file"; do
    generated="${pair%%:*}"
    committed="${pair##*:}"
    if ! diff -q "$generated" "$committed" > /dev/null 2>&1; then
      echo "error: $committed is stale. Run eng/generate-inspect-web-engine-facade.sh and commit the result." >&2
      diff "$committed" "$generated" >&2 || true
      drifted=1
    fi
  done
  if [[ "$drifted" != 0 ]]; then
    exit 1
  fi
  echo "inspect-web TypeScript facade and compiler-derived artifacts are up to date."
else
  cp "$scratch/inspect-web-engine.ts" "$ts_output_file"
  cp "$scratch/out/inspect-web-engine.d.ts" "$dts_output_file"
  cp "$scratch/out/inspect-web-engine.js" "$js_output_file"
  echo "Wrote $ts_output_file"
  echo "Wrote $dts_output_file"
  echo "Wrote $js_output_file"
fi
