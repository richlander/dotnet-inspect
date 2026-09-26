#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -lt 4 ] || [ "$#" -gt 8 ]; then
  echo "usage: $0 <candidate-run-id> <candidate-attempt> <accept-certification-concerns> <output> [expected-sha] [expected-artifact-id] [expected-digest] [expected-concerns-accepted]" >&2
  exit 2
fi

candidate_run_id=$1
candidate_attempt=$2
accept_certification_concerns=$3
output=$4
expected_sha=${5:-}
expected_artifact_id=${6:-}
expected_digest=${7:-}
expected_concerns_accepted=${8:-}

if [[ ! "$candidate_run_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "candidate run ID must be a positive decimal run ID." >&2
  exit 1
fi
if [[ ! "$candidate_attempt" =~ ^[1-9][0-9]*$ ]]; then
  echo "candidate attempt must be a positive integer." >&2
  exit 1
fi
if [ "$accept_certification_concerns" != true ] &&
   [ "$accept_certification_concerns" != false ]; then
  echo "accept-certification-concerns must be true or false." >&2
  exit 1
fi
if [ -n "$expected_sha" ] && [[ ! "$expected_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "expected SHA must be 40 hexadecimal characters." >&2
  exit 1
fi
if [ -n "$expected_artifact_id" ] &&
   [[ ! "$expected_artifact_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "expected artifact ID must be a positive integer." >&2
  exit 1
fi
if [ -n "$expected_digest" ] &&
   [[ ! "$expected_digest" =~ ^sha256:[0-9a-fA-F]{64}$ ]]; then
  echo "expected digest must be a SHA-256 digest." >&2
  exit 1
fi
if [ -n "$expected_concerns_accepted" ] &&
   [ "$expected_concerns_accepted" != true ] &&
   [ "$expected_concerns_accepted" != false ]; then
  echo "expected concerns-accepted value must be true or false." >&2
  exit 1
fi

: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must identify the repository}"
: "${RUNNER_TEMP:?RUNNER_TEMP must identify the runner temporary directory}"

scratch=$(mktemp -d "$RUNNER_TEMP/release-candidate.XXXXXX")
trap 'rm -rf "$scratch"' EXIT

run_json="$scratch/run.json"
jobs_json="$scratch/jobs.json"
artifacts_json="$scratch/artifacts.json"
checks_json="$scratch/checks.json"
validator_output="$scratch/validator-output"

gh api "repos/$GITHUB_REPOSITORY/actions/runs/$candidate_run_id" > "$run_json"
gh api \
  "repos/$GITHUB_REPOSITORY/actions/runs/$candidate_run_id/jobs?filter=latest&per_page=100" \
  > "$jobs_json"
gh api \
  "repos/$GITHUB_REPOSITORY/actions/runs/$candidate_run_id/artifacts?per_page=100" \
  > "$artifacts_json"

candidate_sha=$(jq -er .head_sha "$run_json")
if [[ ! "$candidate_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "candidate workflow returned an invalid commit SHA." >&2
  exit 1
fi
gh api \
  "repos/$GITHUB_REPOSITORY/commits/$candidate_sha/check-runs?check_name=ci-required&filter=latest&per_page=100" \
  > "$checks_json"

dotnet run eng/validate-release-candidate.cs -- \
  --run "$run_json" \
  --jobs "$jobs_json" \
  --artifacts "$artifacts_json" \
  --checks "$checks_json" \
  --repository "$GITHUB_REPOSITORY" \
  --expected-attempt "$candidate_attempt" \
  --accept-certification-concerns "$accept_certification_concerns" \
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
resolved_concerns_accepted=$(read_output concerns_accepted)

if [ "$resolved_attempt" != "$candidate_attempt" ]; then
  echo "validated attempt $resolved_attempt does not match selected attempt $candidate_attempt." >&2
  exit 1
fi
if [ -n "$expected_sha" ] && [ "$resolved_sha" != "$expected_sha" ]; then
  echo "revalidated SHA $resolved_sha does not match resolved SHA $expected_sha." >&2
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
if [ -n "$expected_concerns_accepted" ] &&
   [ "$resolved_concerns_accepted" != "$expected_concerns_accepted" ]; then
  echo "revalidated concern acceptance $resolved_concerns_accepted does not match $expected_concerns_accepted." >&2
  exit 1
fi

cat "$validator_output" >> "$output"
