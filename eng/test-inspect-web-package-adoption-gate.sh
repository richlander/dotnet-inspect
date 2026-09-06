#!/usr/bin/env bash
set -euo pipefail

# Real-Wasm gate for artifact-backed package scope adoption (issue #5576). It
# reuses the engine artifact already published to artifacts/inspect-web-publish
# and the Firefox install from the inspect-web CI job. Fixture assemblies are
# genuinely valid, distinct-identity cataloged fixtures (diff-asm.lib-a and
# diff-asm.lib-b); tools/InspectWebFixtureResolver builds them through the
# normal solution graph and resolves their binaries by stable catalog ID via
# FixtureCatalog.AssemblyPath, so the gate never rediscovers binaries by
# scanning build outputs (docs/fixture-governance.md). The Playwright spec then
# composes those bytes into deterministic nupkg fixtures and drives the
# published production facades in a real browser.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
frontend="$repo_root/prototypes/inspect-web"
resolver="$repo_root/tools/InspectWebFixtureResolver/InspectWebFixtureResolver.csproj"
site="${INSPECT_WEB_PACKAGE_ADOPTION_SITE:-$repo_root/artifacts/inspect-web-publish/wwwroot}"
dotnet=${DOTNET:-dotnet}

if [[ ! -f "$site/inspect-web-package.js" ]]; then
  echo "Published engine artifact not found at $site." >&2
  echo "Publish it first (dotnet publish prototypes/inspect-web/engine/InspectWeb.Engine.csproj -c Release --output artifacts/inspect-web-publish)." >&2
  exit 1
fi

# Building the resolver materializes the cataloged fixtures (build-only project
# references) and prints "<id>\t<absolute-assembly-path>" for each requested ID.
resolved=$(
  "$dotnet" run --project "$resolver" -c Release -- \
    diff-asm.lib-a diff-asm.lib-b
)

liba_dll=$(awk -F'\t' '$1 == "diff-asm.lib-a" { print $2 }' <<<"$resolved")
libb_dll=$(awk -F'\t' '$1 == "diff-asm.lib-b" { print $2 }' <<<"$resolved")

if [[ -z "$liba_dll" || -z "$libb_dll" ]]; then
  echo "Fixture resolver did not return both cataloged fixture paths." >&2
  echo "$resolved" >&2
  exit 1
fi

cd "$frontend"
INSPECT_WEB_PACKAGE_ADOPTION_SITE="$site" \
INSPECT_WEB_PACKAGE_ADOPTION_LIBA_DLL="$liba_dll" \
INSPECT_WEB_PACKAGE_ADOPTION_LIBB_DLL="$libb_dll" \
  node_modules/.bin/playwright test \
    --config playwright.package-adoption.config.ts \
    --project=firefox

echo "Artifact-backed package scope adoption Browser/Wasm gate passed."
