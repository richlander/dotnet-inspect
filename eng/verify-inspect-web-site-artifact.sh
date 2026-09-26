#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <publish-root>" >&2
  exit 2
fi

root=$1
site="$root/wwwroot"
api="$root/api"
index="$site/index.html"
receipt="$root/async-lowering.json"

test -f "$index"
jq -e '
  .schema == 5
  and .method == "InspectionEngine.AsyncLoweringCanary"
  and .lowering == "compiler"
  and .result == "inspect-web-async-lowering-ok"
  and .facade_count == 8
  and .assembly_count == 8
  and .js_export_method_count > 0
  and .async_method_count > 0
  and .compiler_async_method_count == .async_method_count
  and .runtime_async_method_count == 0
  and .repository_project_count > 0
  and (.repository_projects | length) == .repository_project_count
  and .repository_projects == (.repository_projects | sort | unique)
  and (.repository_project_sha256 | test("^[0-9a-f]{64}$"))
  and ([.assemblies[].name] == [
    "DotnetInspect.Web",
    "DotnetInspect.Web.Interop.Analysis",
    "DotnetInspect.Web.Interop.CallGraph",
    "DotnetInspect.Web.Interop.Catalog",
    "DotnetInspect.Web.Interop.Library",
    "DotnetInspect.Web.Interop.Metadata",
    "DotnetInspect.Web.Interop.Package",
    "DotnetInspect.Web.Interop.Source"
  ])
  and ([.assemblies[].module] == [
    "inspect-web-host",
    "inspect-web-analysis",
    "inspect-web-call-graph",
    "inspect-web-catalog",
    "inspect-web-library",
    "inspect-web-metadata",
    "inspect-web-package",
    "inspect-web-source"
  ])
  and all(.assemblies[];
    .file == (.name + ".dll")
    and (.publish_assembly_sha256 | test("^[0-9a-f]{64}$"))
    and .generated_source_file == (.name + ".ts")
    and (.generated_source_sha256 | test("^[0-9a-f]{64}$"))
    and .declaration_file == (.module + ".d.ts")
    and (.declaration_sha256 | test("^[0-9a-f]{64}$"))
    and .published_js_file == (.module + ".js")
    and (.published_js_sha256 | test("^[0-9a-f]{64}$"))
    and .webcil_assembly == .name
    and (. as $assembly | $assembly.published_webcil_file
      | startswith($assembly.name + "."))
    and (.published_webcil_file
      | test("^DotnetInspect\\.Web(\\.[A-Za-z0-9]+)*\\.[A-Za-z0-9]+\\.wasm$"))
    and (.published_webcil_sha256 | test("^[0-9a-f]{64}$"))
    and .js_export_method_count > 0
    and .async_method_count > 0
    and .compiler_async_method_count == .async_method_count
    and .runtime_async_method_count == 0)
  and ([.assemblies[].js_export_method_count] | add) == .js_export_method_count
  and ([.assemblies[].async_method_count] | add) == .async_method_count
  and ([.assemblies[].compiler_async_method_count] | add)
    == .compiler_async_method_count
  and ([.assemblies[].runtime_async_method_count] | add)
    == .runtime_async_method_count
  and .smoke.initialized_facades == [.assemblies[] | {
    assembly: .name,
    module: .module
  }]
  and .smoke.sdk_create_count == 1
  and .smoke.sdk_runtime_count == 1
  and .smoke.entry_point_count == 0
  and .smoke.async_lowering_canary == "inspect-web-async-lowering-ok"
' "$receipt" >/dev/null

expected_modules=$(jq -r '.assemblies[].published_js_file' "$receipt" | sort)
published_modules=$(
  find "$site" -maxdepth 1 -type f -name 'inspect-web-*.js' -printf '%f\n' \
    | sort
)
test "$published_modules" = "$expected_modules"
while IFS=$'\t' read -r js_file js_sha webcil_file webcil_sha; do
  test "$(sha256sum "$site/$js_file" | awk '{print $1}')" = "$js_sha"
  test "$(sha256sum "$site/_framework/$webcil_file" | awk '{print $1}')" \
    = "$webcil_sha"
done < <(
  jq -r '
    .assemblies[]
    | [
        .published_js_file,
        .published_js_sha256,
        .published_webcil_file,
        .published_webcil_sha256
      ]
    | @tsv
  ' "$receipt"
)

test ! -f "$site/inspect-web-engine.js"
test ! -f "$site/inspect-web-engine.ts"
test -f "$site/staticwebapp.config.json"
test -f "$api/host.json"
test -f "$api/functions.metadata"
test -f "$api/worker.config.json"
test -f \
  "$api/.azurefunctions/Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll"
jq -e '
  any(.[];
    .name == "MsdlProxy"
    and .language == "dotnet-isolated"
    and any(.bindings[];
      .type == "httpTrigger"
      and .authLevel == "Anonymous"
      and .methods == ["get"]
      and .route == "msdl/{pdbFileName}/{symbolKey}"))
' "$api/functions.metadata" >/dev/null

manifest="$site/manifest.json"
test -f "$manifest"
jq -e '
  . as $manifest
  | type == "object"
  and (.["index.html"] | type == "object")
  and all(to_entries[];
    (.value | type == "object")
    and all(((.value.imports // []) + (.value.dynamicImports // []))[];
      . as $key | $manifest | has($key)))
' "$manifest" >/dev/null
vite_assets=$(
  jq -er '
    [to_entries[].value | .file, (.css[]?), (.assets[]?)]
    | unique
    | if length > 0
      then join("\n")
      else error("empty Vite manifest")
      end
  ' "$manifest"
)
while IFS= read -r asset; do
  [[ "$asset" =~ ^assets/([A-Za-z0-9_-][A-Za-z0-9._-]*/)*[A-Za-z0-9_-][A-Za-z0-9._-]*$ ]]
  test -f "$site/$asset"
done <<< "$vite_assets"

vite_entry=$(jq -er '.["index.html"].file' "$manifest")
grep -Fq "src=\"/$vite_entry\"" "$index"
vite_stylesheets=$(
  jq -er '
    .["index.html"].css
    | if length > 0
      then join("\n")
      else error("missing Vite stylesheet")
      end
  ' "$manifest"
)
while IFS= read -r stylesheet; do
  grep -Fq "href=\"/$stylesheet\"" "$index"
done <<< "$vite_stylesheets"

dotnet_module=$(
  sed -n \
    's#.*"\./_framework/dotnet\.js": "\./_framework/\([^"]*\.js\)".*#\1#p' \
    "$index" \
    | head -n 1
)
test -n "$dotnet_module"
test -f "$site/_framework/$dotnet_module"
import_map_line=$(grep -n -m1 '<script type="importmap">' "$index" | cut -d: -f1)
module_line=$(grep -n -m1 '<script type="module"' "$index" | cut -d: -f1)
test "$import_map_line" -lt "$module_line"
