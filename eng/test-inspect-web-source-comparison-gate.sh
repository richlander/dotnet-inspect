#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet=${DOTNET:-dotnet}
site="${INSPECT_WEB_SOURCE_DIFF_SITE:-$repo_root/artifacts/inspect-web-publish/wwwroot}"

if [[ ! -f "$site/inspect-web-source.js" ]]; then
  echo "Published Source facade not found at $site." >&2
  exit 1
fi

resolved=$(
  "$dotnet" run --project "$repo_root/tools/InspectWebFixtureResolver" -c Release -- \
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

cd "$repo_root/prototypes/inspect-web"
INSPECT_WEB_SOURCE_DIFF_SITE="$site" \
INSPECT_WEB_SOURCE_DIFF_BEFORE_PACKAGE="$before_package" \
INSPECT_WEB_SOURCE_DIFF_AFTER_PACKAGE="$after_package" \
INSPECT_WEB_SOURCE_DIFF_BEFORE_SOURCE="$before_source" \
INSPECT_WEB_SOURCE_DIFF_AFTER_SOURCE="$after_source" \
  node_modules/.bin/playwright test \
    --config playwright.source-comparison.config.ts --project=firefox \
    --grep "cataloged Source-only"
