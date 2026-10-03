#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet=${DOTNET:-dotnet}
site="${INSPECT_WEB_SOURCE_DIFF_SITE:-$repo_root/artifacts/inspect-web-publish/wwwroot}"
resolver="$repo_root/tools/InspectWebFixtureResolver"
resolver_command=("$dotnet" run --project "$resolver" -c Release)
if [[ "${INSPECT_WEB_FIXTURE_RESOLVER_NO_BUILD:-}" == "1" ]]; then
  resolver_command+=(--no-build)
fi

if [[ ! -f "$site/inspect-web-source.js" ]]; then
  echo "Published Source facade not found at $site." >&2
  exit 1
fi

resolved=$(
  "${resolver_command[@]}" -- \
    inspect-web.source-comparison.v1:package \
    inspect-web.source-comparison.v2:package \
    inspect-web.source-comparison.v1:source \
    inspect-web.source-comparison.v2:source
)
before_package=$(awk -F'\t' '$1 == "inspect-web.source-comparison.v1:package" {print $2}' <<<"$resolved")
after_package=$(awk -F'\t' '$1 == "inspect-web.source-comparison.v2:package" {print $2}' <<<"$resolved")
before_source=$(awk -F'\t' '$1 == "inspect-web.source-comparison.v1:source" {print $2}' <<<"$resolved")
after_source=$(awk -F'\t' '$1 == "inspect-web.source-comparison.v2:source" {print $2}' <<<"$resolved")
for asset in "$before_package" "$after_package" "$before_source" "$after_source"; do
  if [[ ! -f "$asset" ]]; then
    echo "Source comparison fixture asset did not resolve: $asset" >&2
    exit 1
  fi
done

system_text_json_version="11.0.0-preview.7.26381.103"
system_text_json_assets="$repo_root/artifacts/inspect-web-source-comparison"
system_text_json_package="$system_text_json_assets/system.text.json.$system_text_json_version.nupkg"
system_text_json_pdb="$system_text_json_assets/System.Text.Json.$system_text_json_version.pdb"
system_text_json_source="$system_text_json_assets/JsonDocument.Parse.$system_text_json_version.cs"
mkdir -p "$system_text_json_assets"

download_immutable() {
  local url=$1
  local destination=$2
  if [[ -f "$destination" ]]; then
    return
  fi
  curl -fsSL --retry 3 \
    "$url" \
    -o "$destination.download"
  mv "$destination.download" "$destination"
}

download_immutable \
  "https://api.nuget.org/v3-flatcontainer/system.text.json/$system_text_json_version/system.text.json.$system_text_json_version.nupkg" \
  "$system_text_json_package"
download_immutable \
  "https://msdl.microsoft.com/download/symbols/System.Text.Json.pdb/6132D78C38A10F8E2CED16E2EFEAC925FFFFFFFF/System.Text.Json.pdb" \
  "$system_text_json_pdb"
download_immutable \
  "https://raw.githubusercontent.com/dotnet/dotnet/e2c1e00b3d0f96afb892fb261d5921565b400246/src/runtime/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonDocument.Parse.cs" \
  "$system_text_json_source"

cd "$repo_root/inspect-web"
INSPECT_WEB_SOURCE_DIFF_SITE="$site" \
INSPECT_WEB_SOURCE_DIFF_BEFORE_PACKAGE="$before_package" \
INSPECT_WEB_SOURCE_DIFF_AFTER_PACKAGE="$after_package" \
INSPECT_WEB_SOURCE_DIFF_BEFORE_SOURCE="$before_source" \
INSPECT_WEB_SOURCE_DIFF_AFTER_SOURCE="$after_source" \
INSPECT_WEB_SOURCE_DIFF_SYSTEM_TEXT_JSON_PACKAGE="$system_text_json_package" \
INSPECT_WEB_SOURCE_DIFF_SYSTEM_TEXT_JSON_PDB="$system_text_json_pdb" \
INSPECT_WEB_SOURCE_DIFF_SYSTEM_TEXT_JSON_SOURCE="$system_text_json_source" \
  node_modules/.bin/playwright test \
    --config playwright.source-comparison.config.ts --project=firefox \
    --grep "cataloged Source-only|production Worker|System.Text.Json TryParseValue"
