#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
canary="$repo_root/prototypes/inspect-web/multi-facade-canary"
host="$canary/Host/TsJsExport.MultiFacade.BrowserCanary.csproj"
verifier="$repo_root/prototypes/inspect-web/scripts/verify-multi-facade-canary.ts"
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
  "$repo_root/eng/generate-inspect-web-multi-facade-canary.sh" --check

mkdir -p "$scratch/stale-facades" "$scratch/missing-facade"
cp "$canary/facades/"*.ts "$scratch/stale-facades/"
printf '\n// stale\n' >> "$scratch/stale-facades/alpha.ts"
expect_failure \
  stale-alpha-facade \
  "alpha.ts is stale" \
  env \
  CANARY_FACADE_OUTPUT_DIR="$scratch/stale-facades" \
  DOTNET="$dotnet" \
  NODE="$node" \
  TSC="$tsc" \
  "$repo_root/eng/generate-inspect-web-multi-facade-canary.sh" \
  --check
cp "$canary/facades/alpha.ts" "$scratch/missing-facade/"
expect_failure \
  missing-beta-facade \
  "beta.ts is stale" \
  env \
  CANARY_FACADE_OUTPUT_DIR="$scratch/missing-facade" \
  DOTNET="$dotnet" \
  NODE="$node" \
  TSC="$tsc" \
  "$repo_root/eng/generate-inspect-web-multi-facade-canary.sh" \
  --check

echo "Independent Alpha-stale and Beta-missing facade drift mutations were rejected."

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
cp "$canary/facades/"*.ts "$scratch/source/facades/"
cp "$canary/coordinator.ts" "$canary/exercise.ts" "$scratch/source/"
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
  "include": ["facades/*.ts", "coordinator.ts", "exercise.ts"]
}
JSON
"$tsc" -p "$scratch/source/tsconfig.json"

"$dotnet" publish \
  "$host" \
  -c Release \
  --output "$scratch/publish" \
  -p:CanaryModulesDir="$scratch/modules" \
  --nologo
site="$scratch/publish/wwwroot"
dotnet_module=$(
  find "$site/_framework" \
    -maxdepth 1 \
    -type f \
    -name 'dotnet.*.js' \
    ! -name 'dotnet.native.*' \
    ! -name 'dotnet.runtime.*' \
    -print -quit
)
if [[ -z "$dotnet_module" ]]; then
  echo "Published Browser/Wasm runtime module was not found." >&2
  exit 1
fi
ln -s "$(basename "$dotnet_module")" "$site/_framework/dotnet.js"
test -f "$site/coordinator.js"
test -f "$site/exercise.js"
test -f "$site/facades/alpha.js"
test -f "$site/facades/beta.js"
printf '{ "type": "module" }\n' > "$site/package.json"

"$node" "$verifier" "$site" baseline

cp "$scratch/modules/facades/alpha.js" "$site/facades/alpha.js"
sed -i \
  's/TsJsExport\.MultiFacade\.Alpha/TsJsExport.MultiFacade.Beta/' \
  "$site/facades/alpha.js"
expect_failure \
  wrong-assembly-root \
  "Alpha primary identity returned beta:primary" \
  "$node" "$verifier" "$site" baseline

cp "$scratch/modules/facades/alpha.js" "$site/facades/alpha.js"
cp "$scratch/modules/facades/beta.js" "$site/facades/beta.js"
sed -i \
  's#../_framework/dotnet\.js#../_framework/dotnet.js?duplicate-runtime#' \
  "$site/facades/beta.js"
expect_failure \
  duplicate-runtime-module \
  "Expected exactly one live SDK runtime" \
  "$node" "$verifier" "$site" baseline

cp "$scratch/modules/facades/beta.js" "$site/facades/beta.js"
sed -i \
  's#import \* as beta from "./facades/beta\.js"#import * as beta from "./facades/alpha.js"#' \
  "$site/exercise.js"
expect_failure \
  cross-root-routing \
  "Beta primary identity returned alpha:primary" \
  "$node" "$verifier" "$site" baseline

cp "$scratch/modules/exercise.js" "$site/exercise.js"
expect_failure \
  skipped-beta-initialization \
  "The .NET runtime facade is not initialized." \
  "$node" "$verifier" "$site" skip-beta-initialization
expect_failure \
  skipped-beta-operation \
  "Beta facade did not invoke every canary operation exactly once." \
  "$node" "$verifier" "$site" skip-beta-operation

echo "ts-jsexport multi-facade Browser/Wasm mutations were rejected."
