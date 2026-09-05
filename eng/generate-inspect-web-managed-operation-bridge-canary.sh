#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
canary="$repo_root/prototypes/inspect-web/managed-operation-bridge-canary"
host="$canary/Host/InspectWeb.ManagedOperationBridge.BrowserCanary.Host.csproj"
assembly="$canary/Bridge/bin/Release/net11.0/InspectWeb.ManagedOperationBridge.BrowserCanary.dll"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
dotnet=${DOTNET:-dotnet}
node=${NODE:-node}
tsc=${TSC:-"$repo_root/prototypes/inspect-web/node_modules/.bin/tsc"}
facade_output_dir=${CANARY_FACADE_OUTPUT_DIR:-"$canary/facades"}

mode=write
case "${1:-}" in
  "")
    ;;
  --check)
    if [[ "$#" != 1 ]]; then
      echo "Usage: generate-inspect-web-managed-operation-bridge-canary.sh [--check]" >&2
      exit 1
    fi
    mode=check
    ;;
  *)
    echo "Usage: generate-inspect-web-managed-operation-bridge-canary.sh [--check]" >&2
    exit 1
    ;;
esac

if [[ ! -x "$tsc" ]]; then
  echo "TypeScript compiler not found at $tsc; run npm ci in prototypes/inspect-web." >&2
  exit 1
fi

"$dotnet" build "$host" -c Release --nologo >&2
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
dotnet_dts="$runtime_pack_directory/runtimes/browser-wasm/native/dotnet.d.ts"
if [[ ! -f "$dotnet_dts" ]]; then
  echo "SDK-owned browser dotnet.d.ts was not found in $runtime_pack_directory." >&2
  exit 1
fi

mkdir -p "$scratch/facades" "$scratch/_framework"
cp "$dotnet_dts" "$scratch/_framework/dotnet.d.ts"
"$dotnet" run \
  --project "$repo_root/src/ts-jsexport" \
  -c Release \
  -- \
  "$assembly" \
  --runtime-module ../_framework/dotnet.js \
  --output "$scratch/bridge.body.ts"
{
  printf '%s\n' \
    '// GENERATED FILE - DO NOT EDIT BY HAND.' \
    '//' \
    '// Generated from InspectWeb.ManagedOperationBridge.BrowserCanary.dll by:' \
    '//   eng/generate-inspect-web-managed-operation-bridge-canary.sh' \
    '// CI fails if this facade drifts.' \
    ''
  cat "$scratch/bridge.body.ts"
} > "$scratch/facades/bridge.ts"
sed -i '${/^$/d;}' "$scratch/facades/bridge.ts"

cp "$canary/initialize.ts" "$canary/exercise.ts" "$scratch/"
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
  "include": ["facades/*.ts", "initialize.ts", "exercise.ts"]
}
JSON
"$tsc" -p "$scratch/tsconfig.json"
if grep -E 'RuntimeAPI|dotnet(\.js)?' "$scratch/out/facades/"*.d.ts >/dev/null; then
  echo "The generated public declaration leaked an SDK runtime type." >&2
  exit 1
fi

generated="$scratch/facades/bridge.ts"
committed="$facade_output_dir/bridge.ts"
if [[ "$mode" == check ]]; then
  if ! diff -q "$generated" "$committed" >/dev/null 2>&1; then
    echo "error: $committed is stale. Run $0 and commit the result." >&2
    diff -u "$committed" "$generated" >&2 || true
    exit 1
  fi
  echo "inspect-web managed-operation bridge canary contract is up to date."
else
  mkdir -p "$(dirname "$committed")"
  cp "$generated" "$committed"
  echo "Wrote $committed"
fi
