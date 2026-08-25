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
3. The successful `deploy-inspect-web.yml` **push** run ID for the exact release
   commit. A manual staging run cannot be promoted.

Compare the full CI and staging SHAs before dispatching:

```bash
ci_sha=$(gh run view "$ci_run_id" --json headSha --jq .headSha)
site_sha=$(gh run view "$staging_run_id" --json headSha --jq .headSha)
test "$ci_sha" = "$site_sha"
printf 'release SHA: %s\n' "$ci_sha"
```

`allow_later_commit` changes the package target; it never relaxes site
identity. When using it, select a staging run for the later exact SHA.

Confirm that the release commit contains the intended `VersionPrefix`, release
notes, package `README.md`, and embedded product skills. Record the documentation
checkpoint even when no edits were required.

## Publish package and site together

Open `release.yml` and `promote-inspect-web.yml` together:

1. Dispatch `release.yml` with the CI run ID, certification run ID, the intended
   `allow_later_commit` value, and `confirm=publish`.
2. Dispatch `promote-inspect-web.yml` with the matching staging run ID and
   `confirm=promote`.
3. Confirm that both resolve jobs report the same full release SHA.
4. Only then approve the protected NuGet and production-site environments.

Do not substitute a newer run after the SHA comparison. The release is complete
only when both workflows succeed.

## Verify and recover

Verify the package version and commit in NuGet and the GitHub release. Then
check the production site's status bar for the same version and linked commit.

If one workflow fails after the other publishes, retry the failed workflow with
the same run IDs. Package retries tolerate already-published artifacts with
`--skip-duplicate`; site retries revalidate and promote the same staged
artifact. A different SHA, ancestry-only relationship, or matching version
string is not a valid substitute.
