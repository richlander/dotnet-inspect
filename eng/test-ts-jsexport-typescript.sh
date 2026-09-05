#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

dotnet_root=${DOTNET_ROOT:-$(dirname "$(command -v dotnet)")}
dotnet_dts=$(
  find "$dotnet_root/packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm" \
    -path '*/runtimes/browser-wasm/native/dotnet.d.ts' \
    -print \
    | sort -V \
    | tail -n 1
)
if [[ -z "$dotnet_dts" ]]; then
  echo "SDK-owned browser dotnet.d.ts was not found under $dotnet_root/packs." >&2
  exit 1
fi

tsc=${TSC:-"$repo_root/prototypes/inspect-web/node_modules/.bin/tsc"}
if [[ ! -x "$tsc" ]]; then
  echo "TypeScript compiler not found at $tsc; run npm ci in prototypes/inspect-web." >&2
  exit 1
fi

fixture_project="$repo_root/fixtures/js-export/ILInspector.JsExportSurface.TypeScriptFixtures/ILInspector.JsExportSurface.TypeScriptFixtures.csproj"
fixture_dll="$repo_root/artifacts/bin/ILInspector.JsExportSurface.TypeScriptFixtures/release/ILInspector.JsExportSurface.TypeScriptFixtures.dll"

dotnet build "$fixture_project" -c Release --nologo >/dev/null
dotnet run \
  --project "$repo_root/src/ts-jsexport" \
  -c Release \
  -- \
  "$fixture_dll" \
  --runtime-module ./dotnet.js \
  --output "$scratch/facade.ts"

cat > "$scratch/callback-usage.ts" <<'TS'
import { observeValue, transformValue } from "./facade.js";

const observed: number[] = [];
const observe = (value: number): undefined => {
  observed.push(value);
  return undefined;
};
observeValue(observe);

const transformed: boolean = transformValue(
  (value, text) => value === 42 && text === "answer",
);
void transformed;
TS

cat > "$scratch/tsconfig.json" <<'JSON'
{
  "compilerOptions": {
    "declaration": true,
    "exactOptionalPropertyTypes": true,
    "lib": ["DOM", "ES2022"],
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "noImplicitReturns": true,
    "noUncheckedIndexedAccess": true,
    "outDir": "out",
    "strict": true,
    "target": "ES2022",
    "types": [],
    "verbatimModuleSyntax": true
  },
  "include": ["facade.ts", "callback-usage.ts"]
}
JSON
cp "$dotnet_dts" "$scratch/dotnet.d.ts"
"$tsc" -p "$scratch/tsconfig.json"

grep -F 'from "./dotnet.js"' "$scratch/out/facade.js" >/dev/null
if grep -E 'RuntimeAPI|dotnet(\.js)?' "$scratch/out/facade.d.ts" >/dev/null; then
  echo "Generated public declaration leaked an SDK runtime type." >&2
  exit 1
fi

cp \
  "$repo_root/tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/dotnet.js" \
  "$scratch/out/dotnet.js"
printf '{ "type": "module" }\n' > "$scratch/out/package.json"
node \
  "$repo_root/tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/runtime-probe.mjs" \
  "$scratch/out/facade.js"

expect_compile_failure() {
  local name=$1
  local expression=$2
  local replacement=$3
  local scope=$4
  local mutation="$scratch/$name"
  mkdir "$mutation"
  cp "$scratch/dotnet.d.ts" "$scratch/tsconfig.json" "$mutation/"
  sed -E "/$scope/ s/$expression/$replacement/" \
    "$scratch/facade.ts" > "$mutation/facade.ts"
  if cmp -s "$scratch/facade.ts" "$mutation/facade.ts"; then
    echo "$name mutation did not change the generated source." >&2
    exit 1
  fi
  if "$tsc" -p "$mutation/tsconfig.json" >/dev/null 2>&1; then
    echo "$name mutation unexpectedly compiled." >&2
    exit 1
  fi
}

expect_compile_failure \
  raw-parameter \
  'name: string' \
  'name: number' \
  'readonly "GetWidgetAsync\.[-0-9]+":'
expect_compile_failure \
  raw-return \
  'Promise<string>' \
  'Promise<number>' \
  'readonly "GetWidgetAsync\.[-0-9]+":'
expect_compile_failure \
  public-parameter \
  'name: string' \
  'name: number' \
  'export async function getWidgetAsync'
expect_compile_failure \
  public-return \
  'Promise<WidgetDto>' \
  'Promise<number>' \
  'export async function getWidgetAsync'
expect_compile_failure \
  runtime-api \
  'getAssemblyExports' \
  'missingGetAssemblyExports' \
  'const exports: unknown'

expect_callback_compile_failure() {
  local name=$1
  local expression=$2
  local replacement=$3
  local mutation="$scratch/$name"
  mkdir "$mutation"
  cp \
    "$scratch/dotnet.d.ts" \
    "$scratch/tsconfig.json" \
    "$scratch/facade.ts" \
    "$mutation/"
  sed -E "s/$expression/$replacement/" \
    "$scratch/callback-usage.ts" > "$mutation/callback-usage.ts"
  if cmp -s \
      "$scratch/callback-usage.ts" \
      "$mutation/callback-usage.ts"; then
    echo "$name mutation did not change callback usage." >&2
    exit 1
  fi
  if "$tsc" -p "$mutation/tsconfig.json" >/dev/null 2>&1; then
    echo "$name callback mutation unexpectedly compiled." >&2
    exit 1
  fi
}

expect_callback_compile_failure \
  async-action-callback \
  'const observe = \(value: number\): undefined =>' \
  'const observe = async (value: number): Promise<void> =>'
expect_callback_compile_failure \
  void-action-callback \
  'const observe = \(value: number\): undefined =>' \
  'const observe = (value: number): void =>'

echo "ts-jsexport TypeScript compiler gates passed."
