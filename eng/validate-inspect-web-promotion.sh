#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -lt 4 ] || [ "$#" -gt 8 ]; then
  echo "usage: $0 <staging-run-id> <max-age-hours> <output> <allow-manual-staging> [expected-sha] [expected-attempt] [expected-artifact-id] [expected-digest]" >&2
  exit 2
fi

staging_run_id=$1
max_age_hours=$2
output=$3
allow_manual_staging=$4
expected_sha=${5:-}
expected_attempt=${6:-}
expected_artifact_id=${7:-}
expected_digest=${8:-}

if [[ ! "$staging_run_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "staging run ID must be a positive decimal run ID." >&2
  exit 1
fi
if [[ ! "$max_age_hours" =~ ^[1-9][0-9]*$ ]]; then
  echo "max-age-hours must be a positive integer." >&2
  exit 1
fi
if [ "$allow_manual_staging" != true ] && [ "$allow_manual_staging" != false ]; then
  echo "allow-manual-staging must be true or false." >&2
  exit 1
fi
if [ -n "$expected_sha" ] && [[ ! "$expected_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "expected SHA must be 40 hexadecimal characters." >&2
  exit 1
fi
if [ -n "$expected_attempt" ] && [[ ! "$expected_attempt" =~ ^[1-9][0-9]*$ ]]; then
  echo "expected attempt must be a positive integer." >&2
  exit 1
fi
if [ -n "$expected_artifact_id" ] && [[ ! "$expected_artifact_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "expected artifact ID must be a positive integer." >&2
  exit 1
fi
if [ -n "$expected_digest" ] &&
   [[ ! "$expected_digest" =~ ^sha256:[0-9a-fA-F]{64}$ ]]; then
  echo "expected digest must be a SHA-256 digest." >&2
  exit 1
fi

: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must identify the repository}"
: "${RUNNER_TEMP:?RUNNER_TEMP must identify the runner temporary directory}"

scratch=$(mktemp -d "$RUNNER_TEMP/inspect-web-promotion.XXXXXX")
trap 'rm -rf "$scratch"' EXIT

run_json="$scratch/run.json"
jobs_json="$scratch/jobs.json"
artifacts_json="$scratch/artifacts.json"
validator_output="$scratch/validator-output"

gh api "repos/$GITHUB_REPOSITORY/actions/runs/$staging_run_id" > "$run_json"
gh api \
  "repos/$GITHUB_REPOSITORY/actions/runs/$staging_run_id/jobs?filter=latest&per_page=100" \
  > "$jobs_json"
gh api \
  "repos/$GITHUB_REPOSITORY/actions/runs/$staging_run_id/artifacts?per_page=100" \
  > "$artifacts_json"

dotnet run eng/validate-inspect-web-promotion.cs -- \
  --run "$run_json" \
  --jobs "$jobs_json" \
  --artifacts "$artifacts_json" \
  --repository "$GITHUB_REPOSITORY" \
  --allow-manual-staging "$allow_manual_staging" \
  --max-age-hours "$max_age_hours" \
  --github-output "$validator_output"

read_output() {
  local name=$1
  awk -F= -v key="$name" '
    $1 == key {
      count++
      value = substr($0, length(key) + 2)
    }
    END {
      if (count != 1) exit 1
      print value
    }
  ' "$validator_output"
}

resolved_sha=$(read_output sha)
resolved_attempt=$(read_output run_attempt)
resolved_artifact_id=$(read_output artifact_id)
resolved_digest=$(read_output artifact_digest)

if [ -n "$expected_sha" ] && [ "$resolved_sha" != "$expected_sha" ]; then
  echo "revalidated SHA $resolved_sha does not match resolved SHA $expected_sha." >&2
  exit 1
fi
if [ -n "$expected_attempt" ] &&
   [ "$resolved_attempt" != "$expected_attempt" ]; then
  echo "revalidated attempt $resolved_attempt does not match resolved attempt $expected_attempt." >&2
  exit 1
fi
if [ -n "$expected_artifact_id" ] &&
   [ "$resolved_artifact_id" != "$expected_artifact_id" ]; then
  echo "revalidated artifact $resolved_artifact_id does not match resolved artifact $expected_artifact_id." >&2
  exit 1
fi
if [ -n "$expected_digest" ] && [ "$resolved_digest" != "$expected_digest" ]; then
  echo "revalidated digest $resolved_digest does not match resolved digest $expected_digest." >&2
  exit 1
fi

cat "$validator_output" >> "$output"
