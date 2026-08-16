#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -lt 5 ] || [ "$#" -gt 6 ]; then
  echo "usage: $0 <certification-run-id> <target-run-id> <allow-later-commit> <max-age-hours> <output> [expected-sha]" >&2
  exit 2
fi

certification_run_id=$1
target_run_id=$2
allow_later_commit=$3
max_age_hours=$4
output=$5
expected_sha=${6:-}

if [[ ! "$certification_run_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "certification run ID must be a positive decimal run ID." >&2
  exit 1
fi
if [[ ! "$target_run_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "target run ID must be a positive decimal run ID." >&2
  exit 1
fi
if [ "$allow_later_commit" != true ] && [ "$allow_later_commit" != false ]; then
  echo "allow-later-commit must be true or false." >&2
  exit 1
fi
if [[ ! "$max_age_hours" =~ ^[1-9][0-9]*$ ]]; then
  echo "max-age-hours must be a positive integer." >&2
  exit 1
fi
if [ -n "$expected_sha" ] && [[ ! "$expected_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "expected SHA must be 40 hexadecimal characters." >&2
  exit 1
fi

: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must identify the repository}"
: "${RUNNER_TEMP:?RUNNER_TEMP must identify the runner temporary directory}"

scratch=$(mktemp -d "$RUNNER_TEMP/release-evidence.XXXXXX")
trap 'rm -rf "$scratch"' EXIT

certification_run="$scratch/certification-run.json"
certification_jobs="$scratch/certification-jobs.json"
target_run="$scratch/target-run.json"
target_jobs="$scratch/target-jobs.json"
comparison="$scratch/comparison.json"
validator_output="$scratch/validator-output"

gh api "repos/$GITHUB_REPOSITORY/actions/runs/$certification_run_id" \
  > "$certification_run"
gh api \
  "repos/$GITHUB_REPOSITORY/actions/runs/$certification_run_id/jobs?filter=latest&per_page=100" \
  > "$certification_jobs"
gh api "repos/$GITHUB_REPOSITORY/actions/runs/$target_run_id" \
  > "$target_run"
gh api \
  "repos/$GITHUB_REPOSITORY/actions/runs/$target_run_id/jobs?filter=latest&per_page=100" \
  > "$target_jobs"

certified_sha=$(jq -er .head_sha "$certification_run")
target_sha=$(jq -er .head_sha "$target_run")
if [[ ! "$certified_sha" =~ ^[0-9a-fA-F]{40}$ ]] ||
   [[ ! "$target_sha" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "workflow run returned an invalid commit SHA." >&2
  exit 1
fi

gh api "repos/$GITHUB_REPOSITORY/compare/$certified_sha...$target_sha" \
  > "$comparison"

dotnet run eng/validate-release-certification.cs -- \
  --certification-run "$certification_run" \
  --certification-jobs "$certification_jobs" \
  --target-run "$target_run" \
  --target-jobs "$target_jobs" \
  --comparison "$comparison" \
  --allow-later-commit "$allow_later_commit" \
  --max-age-hours "$max_age_hours" \
  --github-output "$validator_output"

mapfile -t resolved_sha_lines < <(grep '^sha=' "$validator_output")
if [ "${#resolved_sha_lines[@]}" -ne 1 ]; then
  echo "validator did not emit exactly one resolved SHA." >&2
  exit 1
fi
resolved_sha=${resolved_sha_lines[0]#sha=}
if [ -n "$expected_sha" ] && [ "$resolved_sha" != "$expected_sha" ]; then
  echo "revalidated SHA $resolved_sha does not match resolved SHA $expected_sha." >&2
  exit 1
fi

cat "$validator_output" >> "$output"
