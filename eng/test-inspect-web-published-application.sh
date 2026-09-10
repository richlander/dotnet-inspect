#!/usr/bin/env bash
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
site="${1:-$repo_root/artifacts/inspect-web-publish/wwwroot}"
frontend="$repo_root/prototypes/inspect-web"
resolver="$repo_root/tools/InspectWebFixtureResolver"
dotnet=${DOTNET:-dotnet}

if [[ $# -gt 1 || ! -f "$site/index.html" ]]; then
  echo "Usage: $0 [published-wwwroot]" >&2
  exit 1
fi

export INSPECT_WEB_WORKER_SITE="$site"
export INSPECT_WEB_WORKER_SOURCE_DLL="$repo_root/artifacts/bin/TsJsExport.Contracts/release/TsJsExport.Contracts.dll"
export INSPECT_WEB_WORKER_QUERY_DLL="$repo_root/artifacts/bin/ILInspector.Decompiler/release/ILInspector.Decompiler.dll"
if [[ ! -f "$INSPECT_WEB_WORKER_QUERY_DLL" ]]; then
  echo "Worker Package Query fixture assembly is missing: $INSPECT_WEB_WORKER_QUERY_DLL" >&2
  exit 1
fi
export INSPECT_WEB_PACKAGE_ADOPTION_SITE="$site"
export INSPECT_WEB_SOURCE_DIFF_SITE="$site"
export INSPECT_WEB_FIXTURE_RESOLVER_NO_BUILD=1

if ! "$dotnet" build "$resolver" -c Release --nologo; then
  echo "Fixture resolver build failed." >&2
  exit 1
fi

(
  cd "$frontend"
  node scripts/verify-published-engine-facades.ts "$site" production
) &
facade_pid=$!

(
  cd "$frontend"
  npm run inspect-web-worker-browser-binding \
    && npm run inspect-web-worker-cpu-isolation
) &
worker_pid=$!

"$repo_root/eng/test-inspect-web-package-adoption-gate.sh" &
package_pid=$!

"$repo_root/eng/test-inspect-web-source-comparison-gate.sh" &
source_pid=$!

"$dotnet" run "$repo_root/eng/validate-inspect-web-promotion.cs" -- --self-test &
promotion_pid=$!

failed=0
for gate in \
  "published facade:$facade_pid" \
  "Worker browser gates:$worker_pid" \
  "package adoption:$package_pid" \
  "Authored Source comparison:$source_pid" \
  "promotion validation:$promotion_pid"; do
  name="${gate%%:*}"
  pid="${gate##*:}"
  if wait "$pid"; then
    echo "$name passed."
  else
    echo "$name failed." >&2
    failed=1
  fi
done

exit "$failed"
