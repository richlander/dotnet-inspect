---
name: release
description: Use when shipping a new dotnet-inspect version; coordinate certification, NuGet and GitHub publication, and the matching dotnet-inspect.net production deployment.
---

# Release dotnet-inspect

Use this maintainer skill when the user asks to ship, publish, or release a new
dotnet-inspect version. Read
[`docs/release-workflow.md`](../../../docs/release-workflow.md) first; it owns
the release contract and failure handling. This skill is an operator playbook,
not a substitute for that document or the workflows.

A release is one exact `(commit SHA, VersionPrefix)` pair shared by the NuGet
packages, GitHub release, and `https://dotnet-inspect.net`. Never advance only
the packages or only the site.

## Collect the release evidence

From `main`, collect:

1. The successful `ci.yml` main-push run ID that selects the release commit.
2. A fresh successful Deep Inspect `test` run ID that certifies that commit or
   an explicitly accepted ancestor.
3. The successful `deploy-inspect-web.yml` run ID for the exact release commit.
   Use the main-push run by default. An operator-dispatched main staging run
   requires explicit authorization during promotion.

Compare the full CI and staging SHAs before dispatching:

```bash
set -euo pipefail
: "${ci_run_id:?set ci_run_id to the successful main CI run ID}"
: "${staging_run_id:?set staging_run_id to the matching staging run ID}"

ci_sha=$(gh run view "$ci_run_id" --json headSha --jq .headSha)
site_sha=$(gh run view "$staging_run_id" --json headSha --jq .headSha)

[[ "$ci_sha" =~ ^[0-9a-f]{40}$ ]] || {
  printf 'invalid CI SHA: %s\n' "$ci_sha" >&2
  exit 1
}
[[ "$site_sha" =~ ^[0-9a-f]{40}$ ]] || {
  printf 'invalid staging SHA: %s\n' "$site_sha" >&2
  exit 1
}
if [ "$ci_sha" != "$site_sha" ]; then
  printf 'release SHA mismatch: CI=%s staging=%s\n' \
    "$ci_sha" "$site_sha" >&2
  exit 1
fi

printf 'release SHA: %s\n' "$ci_sha"
```

The standard path uses exact certification and leaves both overrides disabled.
For urgent relief, a person may set `allow_later_commit` to ship a later `main`
descendant whose target commit is not itself certified. This never relaxes site
identity: select a staging run for that same exact SHA. If that staging run was
operator-dispatched, the person must also set `allow_manual_staging`.

After relief ships, keep package and site together on that commit and wait for
the next exact certification before the next ordinary release. Neither override
is standing authorization.

The SHA comparison is the last dependable veto before dispatch. The `nuget`
environment has no approval gate; package publication proceeds automatically
after its builds.

Confirm that the release commit contains the intended `VersionPrefix`, release
notes, package `README.md`, and embedded product skills. Record the documentation
checkpoint even when no edits were required.

When reconciling the shipped skill corpus (`skills/*/SKILL.md`, embedded in the
tool binary and registered in `SkillCommand.Skills`), check scope as well as
accuracy: each shipped skill must describe a genuine end-user capability of the
published tool, not a repository-internal process, CI/certification harness, or
maintainer workflow (that content belongs under `.github/skills/` instead, and
is never embedded or registered). Adding a new skill to the shipped corpus, or
moving a skill into or out of it, is a product-surface change and needs the
repository owner's explicit approval before landing — do not add or relocate a
shipped skill unilaterally while doing routine release reconciliation.

## Publish package and site together

Open `release.yml` and `promote-inspect-web.yml` together:

1. Dispatch `release.yml` with the CI run ID, certification run ID, the intended
   `allow_later_commit` value, and `confirm=publish`.
2. Dispatch `promote-inspect-web.yml` with the matching staging run ID and
   `confirm=promote`. Leave `allow_manual_staging=false` for a push run; set it
   only with explicit authorization for an operator-dispatched staging run.
3. Confirm immediately that both resolve jobs report the same full release SHA.
   If either is wrong, cancel both workflow runs before package publication
   starts. Do not leave a stale promotion run waiting for approval.
4. Monitor the package builds and automatic NuGet publication.
5. Wait for the package workflow and GitHub release to succeed, then approve
   the production-site environment. Never promote the site first.

Do not substitute a newer run after the SHA comparison. The release is complete
only when both workflows succeed.

## Verify and recover

Verify the package version and commit in NuGet and the GitHub release. Then
check the production site's status bar for the same version and linked commit.

If the package workflow fails, leave site production unapproved and retry with
the same CI and certification run IDs. Package retries tolerate
already-published artifacts with `--skip-duplicate`. If site promotion fails
after package publication, retry with the same staging run ID; site retries
revalidate and promote the same staged artifact. A different SHA, ancestry-only
relationship, or matching version string is not a valid substitute.

If a newer `main` push cancels the release commit's staging run, wait for active
staging work to finish and rerun the original push-triggered run:

```bash
gh run rerun "$staging_run_id" --failed
```

Rerun only failed or cancelled jobs so a successful build keeps its artifact
while deployment retries. If GitHub reruns a cancelled build after upload, the
workflow replaces the retained same-name artifact;
`PromotionWorkflowContract` gates that promotion still sees exactly one.
Promote the successful failed-job rerun. Do not substitute a manual staging
dispatch by default. If exceptional circumstances require one, obtain explicit
authorization, confirm its SHA still equals the package target, and enable
`allow_manual_staging` during promotion.
