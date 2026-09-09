#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
site="${INSPECT_WEB_LIBRARY_API_DIFF_SITE:-$repo_root/artifacts/inspect-web-publish/wwwroot}"

if [[ ! -f "$site/inspect-web-metadata.js" ]]; then
  echo "Published Metadata facade not found at $site." >&2
  echo "Publish it first (dotnet publish prototypes/inspect-web/engine/InspectWeb.Engine.csproj -c Release --output artifacts/inspect-web-publish)." >&2
  exit 1
fi

for fixture in LibraryApiDiff.V1 LibraryApiDiff.V2; do
  asset="$repo_root/artifacts/bin/$fixture/release/LibraryApiDiffFixture.dll"
  if [[ ! -f "$asset" ]]; then
    echo "Library API diff fixture assembly did not resolve: $asset" >&2
    echo "Build it first (dotnet build dotnet-inspect.slnx -c Release)." >&2
    exit 1
  fi
done

cd "$repo_root/prototypes/inspect-web"
INSPECT_WEB_LIBRARY_API_DIFF_SITE="$site" \
  node_modules/.bin/playwright test \
    --config playwright.library-api-diff.config.ts --project=firefox
