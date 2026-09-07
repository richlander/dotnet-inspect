#!/usr/bin/env bash
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
site="${1:-$repo_root/artifacts/inspect-web-publish/wwwroot}"
frontend="$repo_root/prototypes/inspect-web"

if [[ $# -gt 1 || ! -f "$site/index.html" ]]; then
  echo "Usage: $0 [published-wwwroot]" >&2
  exit 1
fi

export INSPECT_WEB_WORKER_SITE="$site"
export INSPECT_WEB_PACKAGE_ADOPTION_SITE="$site"
export INSPECT_WEB_SOURCE_DIFF_SITE="$site"

(
  cd "$frontend"
  node scripts/verify-published-engine-facades.ts "$site" production
) &
facade_pid=$!

(
  cd "$frontend"
  npm run inspect-web-worker-browser-binding
) &
worker_pid=$!

"$repo_root/eng/test-inspect-web-package-adoption-gate.sh" &
package_pid=$!

dotnet run "$repo_root/eng/validate-inspect-web-promotion.cs" -- --self-test &
promotion_pid=$!

failed=0
for gate in \
  "published facade:$facade_pid" \
  "Worker browser binding:$worker_pid" \
  "package adoption:$package_pid" \
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

# Both fixture gates build the same resolver outputs. Run Source after Package
# so their independent dotnet hosts never race over those build artifacts.
if "$repo_root/eng/test-inspect-web-source-comparison-gate.sh"; then
  echo "Authored Source comparison passed."
else
  echo "Authored Source comparison failed." >&2
  failed=1
fi

exit "$failed"
