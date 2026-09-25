#!/usr/bin/env bash
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
site="${1:-$repo_root/artifacts/inspect-web-publish/wwwroot}"
frontend="$repo_root/inspect-web"
resolver="$repo_root/tools/InspectWebFixtureResolver"
dotnet=${DOTNET:-dotnet}

if [[ $# -gt 1 || ! -f "$site/index.html" ]]; then
  echo "Usage: $0 [published-wwwroot]" >&2
  exit 1
fi

export INSPECT_WEB_WORKER_SITE="$site"
export INSPECT_WEB_WORKER_SOURCE_DLL="$repo_root/artifacts/bin/TsJsExport.Contracts/release/TsJsExport.Contracts.dll"
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

(
  cd "$frontend"
  INSPECT_WEB_PUBLISHED_BENCHMARK_SITE="$site" \
    npm run inspect-web-published-benchmark
) &
benchmark_pid=$!

"$dotnet" run "$repo_root/eng/validate-inspect-web-promotion.cs" -- --self-test &
promotion_pid=$!

failed=0
for gate in \
  "published facade:$facade_pid" \
  "Worker browser gates:$worker_pid" \
  "published benchmark bridge:$benchmark_pid" \
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

# These broad Firefox/Wasm gates share runner CPU. Run them sequentially so
# their timeouts measure product work rather than cross-gate contention.
if "$repo_root/eng/test-inspect-web-package-adoption-gate.sh"; then
  echo "package adoption passed."
else
  echo "package adoption failed." >&2
  failed=1
fi

if "$repo_root/eng/test-inspect-web-source-comparison-gate.sh"; then
  echo "Authored Source comparison passed."
else
  echo "Authored Source comparison failed." >&2
  failed=1
fi

exit "$failed"
