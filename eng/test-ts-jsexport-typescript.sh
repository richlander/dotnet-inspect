#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

dotnet_root=${DOTNET_ROOT:-$(dirname "$(command -v dotnet)")}
dotnet_exe="$dotnet_root/dotnet"
if [[ ! -x "$dotnet_exe" ]]; then
  echo "The selected .NET executable was not found at $dotnet_exe." >&2
  exit 1
fi

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

"$dotnet_exe" build "$fixture_project" -c Release --nologo >/dev/null
"$dotnet_exe" run \
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

cat > "$scratch/union-usage.ts" <<'TS'
import {
  getBoxedCount,
  getBoxedWidget,
  getCollectionSelection,
  getDefaultSelection,
  getFlagSelection,
  getKindSelection,
  getOutcomeSelection,
  getSelectionEnvelopeAsync,
  getWidgetSelection,
  getWrappedBlob,
} from "./facade.js";
import type {
  Boxed,
  CollectionSelection,
  FlagSelection,
  KindSelection,
  OutcomeSelection,
  SelectionEnvelope,
  WidgetDto,
  WidgetKind,
  WidgetSelection,
  Wrapped,
} from "./facade.js";

// Reference entries in union-case collections stay nullable, so the consumer
// narrows them instead of asserting presence.
type SelectionEntries = Extract<CollectionSelection, ReadonlyArray<unknown>>;
type SelectionMap = Extract<
  CollectionSelection,
  Readonly<Record<string, unknown>>
>;
type GroupEntries = Extract<SelectionEnvelope["group"], ReadonlyArray<unknown>>;

export const missingSelectionEntry: SelectionEntries[number] = null;
export const missingMapEntry: SelectionMap[string] = null;
export const missingGroupEntry: GroupEntries[number] = null;

function isEntryArray(
  selection: CollectionSelection,
): selection is SelectionEntries {
  return Array.isArray(selection);
}

export function describeCollection(selection: CollectionSelection): string {
  if (selection === null) {
    return "none";
  }
  if (typeof selection === "number") {
    return `count-${selection}`;
  }
  if (isEntryArray(selection)) {
    const entries: SelectionEntries = selection;
    return entries
      .map((entry) => (entry === null ? "null" : entry.name))
      .join(",");
  }
  const named: SelectionMap = selection;
  return Object.keys(named)
    .map((key) => {
      const entry: WidgetDto | null = named[key] ?? null;
      return `${key}=${entry === null ? "null" : entry.name}`;
    })
    .join(",");
}

export function describeGroup(group: SelectionEnvelope["group"]): string {
  if (group === null) {
    return "none";
  }
  if (typeof group === "string") {
    return group;
  }
  return `group:${group
    .map((entry) => (entry === null ? "null" : entry.name))
    .join(",")}`;
}

export function describeSelection(selection: WidgetSelection): string {
  if (selection === null) {
    return "none";
  }
  if (typeof selection === "string") {
    return selection;
  }
  const widget: WidgetDto = selection;
  return `${widget.name}:${widget.count}`;
}

export function describeFlag(flag: FlagSelection): string {
  if (flag === null) {
    return "none";
  }
  if (typeof flag === "boolean") {
    return flag ? "true" : "false";
  }
  return flag.name;
}

export function describeOutcome(outcome: OutcomeSelection): string {
  if (typeof outcome === "boolean") {
    return outcome ? "yes" : "no";
  }
  return describeSelection(outcome);
}

export function describeKind(kind: KindSelection): string {
  if (kind === null) {
    return "none";
  }
  if (typeof kind === "string") {
    return kind;
  }
  const declared: WidgetKind = kind;
  return `kind-${declared}`;
}

export function describeBoxed(
  count: Boxed<number>,
  widget: Boxed<WidgetDto>,
): string {
  const left = count === null
    ? "none"
    : typeof count === "string" ? count : count.toFixed(0);
  const right = widget === null
    ? "none"
    : typeof widget === "string" ? widget : widget.name;
  return `${left}/${right}`;
}

export function selectWidget(widget: WidgetDto): WidgetSelection {
  return widget;
}

// A closed byte[] union argument lowers to the Base64 wire string, so the
// alias's own number alternative stays distinguishable from it.
export function describeBlob(blob: Wrapped<string>): string {
  if (blob === null) {
    return "none";
  }
  if (typeof blob === "number") {
    return `count-${blob}`;
  }
  return `blob:${blob}`;
}

export function probeSelections(): string {
  const selection: WidgetSelection = getWidgetSelection(true);
  const missing: WidgetSelection = getDefaultSelection();
  return [
    describeSelection(selection),
    describeSelection(missing),
    describeSelection(null),
    describeFlag(getFlagSelection(true)),
    describeOutcome(getOutcomeSelection(false)),
    describeKind(getKindSelection(true)),
    describeBoxed(getBoxedCount(11), getBoxedWidget("boxed")),
    describeSelection(selectWidget({ name: "literal", count: 9 })),
    describeBlob(getWrappedBlob()),
    describeCollection(getCollectionSelection(0)),
    describeCollection(getCollectionSelection(1)),
    describeCollection(getCollectionSelection(3)),
  ].join("|");
}

export async function summarizeEnvelope(): Promise<string> {
  const envelope: SelectionEnvelope =
    await getSelectionEnvelopeAsync("envelope");
  const items: ReadonlyArray<WidgetSelection> = envelope.items;
  const first: WidgetSelection | undefined = items[0];
  const named: WidgetSelection | undefined = envelope.byName["named"];
  return [
    describeSelection(envelope.result),
    describeSelection(first ?? null),
    describeSelection(named ?? null),
    describeOutcome(envelope.outcome),
    describeKind(envelope.kind),
    describeBoxed(envelope.count, envelope.widget),
    describeBlob(envelope.blob),
    describeGroup(envelope.group),
  ].join("|");
}
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
  "include": ["facade.ts", "callback-usage.ts", "union-usage.ts"]
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
"$dotnet_exe" run \
  "$repo_root/tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/union-payloads.cs" \
  -c Release \
  -- \
  "$scratch/union-payloads.json" \
  >/dev/null
node \
  "$repo_root/tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/runtime-probe.mjs" \
  "$scratch/out/facade.js" \
  "$scratch/union-payloads.json" \
  "$scratch/out/union-usage.js"

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

expect_union_facade_compile_failure() {
  local name=$1
  local expression=$2
  local replacement=$3
  local scope=$4
  local mutation="$scratch/$name"
  mkdir "$mutation"
  cp \
    "$scratch/dotnet.d.ts" \
    "$scratch/tsconfig.json" \
    "$scratch/union-usage.ts" \
    "$mutation/"
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

expect_union_usage_compile_failure() {
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
    "$scratch/union-usage.ts" > "$mutation/union-usage.ts"
  if cmp -s "$scratch/union-usage.ts" "$mutation/union-usage.ts"; then
    echo "$name mutation did not change union usage." >&2
    exit 1
  fi
  if "$tsc" -p "$mutation/tsconfig.json" >/dev/null 2>&1; then
    echo "$name mutation unexpectedly compiled." >&2
    exit 1
  fi
}

expect_union_facade_compile_failure \
  union-null-alternative \
  ' \| null' \
  '' \
  '^export type WidgetSelection'
expect_union_facade_compile_failure \
  union-dto-case \
  'WidgetDto \| string' \
  'string' \
  '^export type WidgetSelection'
expect_union_facade_compile_failure \
  union-closed-generic-argument \
  'Boxed<number>' \
  'Boxed<string>' \
  '^export function getBoxedCount'
expect_union_facade_compile_failure \
  union-closed-byte-array-argument \
  'Wrapped<string>' \
  'Wrapped<number>' \
  '^export function getWrappedBlob'

expect_union_facade_compile_failure \
  union-collection-entry-null \
  'ReadonlyArray<WidgetDto \| null>' \
  'ReadonlyArray<WidgetDto>' \
  '^export type CollectionSelection'
expect_union_facade_compile_failure \
  union-collection-map-entry-null \
  'Record<string, WidgetDto \| null>' \
  'Record<string, WidgetDto>' \
  '^export type CollectionSelection'
expect_union_facade_compile_failure \
  union-closed-generic-container-entry-null \
  'WidgetDto \| null' \
  'WidgetDto' \
  '^  readonly group:'

expect_union_usage_compile_failure \
  union-case-narrowing \
  'typeof selection === "string"' \
  'typeof selection === "number"'
expect_union_usage_compile_failure \
  union-readonly-array-snapshot \
  'const first: WidgetSelection \| undefined = items\[0\];' \
  'const first: WidgetSelection | undefined = (items[0] = null);'
expect_union_usage_compile_failure \
  union-readonly-member-snapshot \
  'const items: ReadonlyArray<WidgetSelection> = envelope.items;' \
  'const items: ReadonlyArray<WidgetSelection> = (envelope.items = []);'
expect_union_usage_compile_failure \
  union-alternative-mismatch \
  'describeKind\(getKindSelection\(true\)\)' \
  'describeFlag(getKindSelection(true))'
expect_union_usage_compile_failure \
  union-closed-generic-mismatch \
  'describeBoxed\(getBoxedCount\(11\), getBoxedWidget\("boxed"\)\)' \
  'describeBoxed(getBoxedWidget("boxed"), getBoxedCount(11))'

echo "ts-jsexport TypeScript compiler gates passed."
