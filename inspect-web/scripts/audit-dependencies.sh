#!/usr/bin/env bash
set -euo pipefail

reports="${1:?Usage: bash scripts/audit-dependencies.sh REPORT_DIRECTORY}"
mkdir -p "$reports"

report() {
  printf '### npm audit: %s\n' "$1" | tee -a "${GITHUB_STEP_SUMMARY:-/dev/null}"
}

attempt=0
# npm's HTTP retry settings do not retry its audit POST requests.
for delay in 0 10 30; do
  if [ "$delay" -gt 0 ]; then
    sleep "$delay"
  fi
  attempt=$((attempt + 1))
  json="$reports/attempt-$attempt.json"
  stderr="$reports/attempt-$attempt.stderr"

  if npm audit --package-lock-only --include=dev --audit-level=info --json > "$json" 2> "$stderr"; then
    report "no known advisories"
    exit 0
  fi

  if jq -e '.error == null and (.metadata.vulnerabilities.total | numbers > 0)' "$json" > /dev/null 2>&1; then
    report "advisories found"
    echo "::error title=npm audit advisories::See the npm-audit artifact for npm's report."
    exit 1
  fi

  echo "npm audit attempt $attempt did not complete; preserving its report and stderr."
done

report "incomplete after three attempts"
echo "::error title=npm audit incomplete::The audit could not complete. This is not a clean dependency report; see the npm-audit artifact."
exit 2
