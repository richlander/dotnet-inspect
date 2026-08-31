#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
canary="$repo_root/prototypes/inspect-web/multi-facade-canary"
host="$canary/Host/TsJsExport.MultiFacade.BrowserCanary.csproj"
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
      echo "Usage: generate-inspect-web-multi-facade-canary.sh [--check]" >&2
      exit 1
    fi
    mode=check
    ;;
  *)
    echo "Usage: generate-inspect-web-multi-facade-canary.sh [--check]" >&2
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

generate_facade() {
  local name=$1
  local assembly=$2
  local output="$scratch/facades/$name.ts"
  local body="$scratch/$name.body.ts"

  "$dotnet" run \
    --project "$repo_root/src/ts-jsexport" \
    -c Release \
    -- \
    "$assembly" \
    --runtime-module ../_framework/dotnet.js \
    --output "$body"
  {
    printf '%s\n' \
      '// GENERATED FILE - DO NOT EDIT BY HAND.' \
      '//' \
      "// Generated independently from $(basename "$assembly") by:" \
      '//   eng/generate-inspect-web-multi-facade-canary.sh' \
      '// CI fails if this facade drifts.' \
      ''
    cat "$body"
  } > "$output"
}

generate_facade \
  alpha \
  "$canary/Alpha/bin/Release/net11.0/TsJsExport.MultiFacade.Alpha.dll"
generate_facade \
  beta \
  "$canary/Beta/bin/Release/net11.0/TsJsExport.MultiFacade.Beta.dll"

sed \
  's/TsJsExport\.MultiFacade\.Alpha/TsJsExport.MultiFacade.Assembly/g' \
  "$scratch/alpha.body.ts" > "$scratch/alpha.normalized.ts"
sed \
  's/TsJsExport\.MultiFacade\.Beta/TsJsExport.MultiFacade.Assembly/g' \
  "$scratch/beta.body.ts" > "$scratch/beta.normalized.ts"
if ! cmp -s "$scratch/alpha.normalized.ts" "$scratch/beta.normalized.ts"; then
  echo "Alpha and Beta generated contracts differ beyond assembly identity." >&2
  diff -u "$scratch/alpha.normalized.ts" "$scratch/beta.normalized.ts" >&2 || true
  exit 1
fi

cp "$canary/coordinator.ts" "$canary/exercise.ts" "$scratch/"
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
  "include": ["facades/*.ts", "coordinator.ts", "exercise.ts"]
}
JSON
"$tsc" -p "$scratch/tsconfig.json"
if grep -E 'RuntimeAPI|dotnet(\.js)?' "$scratch/out/facades/"*.d.ts >/dev/null; then
  echo "A generated public declaration leaked an SDK runtime type." >&2
  exit 1
fi

drifted=0
for name in alpha beta; do
  generated="$scratch/facades/$name.ts"
  committed="$facade_output_dir/$name.ts"
  if [[ "$mode" == check ]]; then
    if ! diff -q "$generated" "$committed" >/dev/null 2>&1; then
      echo "error: $committed is stale. Run $0 and commit the result." >&2
      diff -u "$committed" "$generated" >&2 || true
      drifted=1
    fi
  else
    mkdir -p "$(dirname "$committed")"
    cp "$generated" "$committed"
    echo "Wrote $committed"
  fi
done
if [[ "$drifted" != 0 ]]; then
  exit 1
fi
if [[ "$mode" == check ]]; then
  echo "inspect-web multi-facade canary contracts are up to date."
fi
