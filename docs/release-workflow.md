# Release workflow

This document explains how a tested commit becomes one coordinated release of
the dotnet-inspect packages and the production site at
`https://dotnet-inspect.net`. Repository development, worktree, build, and test
rules live in [AGENTS.md](../AGENTS.md). The executable sources of truth are
[release.yml](../.github/workflows/release.yml),
[deploy-inspect-web.yml](../.github/workflows/deploy-inspect-web.yml), and
[promote-inspect-web.yml](../.github/workflows/promote-inspect-web.yml); update
this document when those workflows change. The repo-local
[release skill](../.github/skills/release/SKILL.md) is the operator playbook and
must stay aligned with this contract.

## Release boundary

PR validation, release certification, and publishing have separate
responsibilities:

| Workflow | Trigger | Responsibility |
| --- | --- | --- |
| `ci.yml` | Pull requests and pushes to `main` | Validate the changed commit |
| `deep-inspect.yml` | Daily schedule or manual `lane=test` dispatch | Certify one `main` commit with the full slow test and corpus gates |
| `release.yml` | Manual dispatch | Verify certification, rebuild packages, and publish one selected commit |
| `deploy-inspect-web.yml` | Pushes to `main` | Build and deploy a staging site artifact for the pushed commit |
| `promote-inspect-web.yml` | Manual dispatch | Verify and promote one staged artifact to `https://dotnet-inspect.net` |

The publish workflow accepts a successful `main` CI run ID to resolve the exact
commit SHA and a Deep Inspect run ID as its heavy-validation evidence. It does
not publish or otherwise trust packages produced by either run. Every release
package is built fresh from the resolved commit in `release.yml`.

Deep Inspect certifies `main` daily at 06:00 UTC. Dispatch
`deep-inspect.yml` with `lane=test` when a fresh result is needed during the
day. Certification remains valid for 36 hours.

By default, the certified and published commits must be identical. An operator
may explicitly enable `allow_later_commit` to publish a later `main` commit
whose exact main-push `ci-required` result succeeded. Main-push CI identifies
the integrated target and runs lightweight repository checks; its substantive
test jobs are PR-only, so it does not certify the intervening changes. The
workflow proves that the target descends from the certified commit, but the
operator owns the decision that those changes do not require another slow run.
Divergent and older commits are never accepted.

## Lockstep release identity

A release is one exact pair: the full commit SHA and the `VersionPrefix` read
from that commit. The packages, GitHub release, and production site are one
release unit:

- `release.yml` rebuilds and publishes the packages from the commit selected by
  the successful `main` CI run.
- `deploy-inspect-web.yml` builds the staging site from a `main` push, embedding
  that commit's `VersionPrefix`, full source SHA, and build timestamp.
- `promote-inspect-web.yml` promotes that exact staged artifact without
  rebuilding it.

Publish and promote together. Do not publish a new package version without
promoting its matching site, and do not promote a site from a commit that is
not the package release commit. The staging run's `head_sha` must exactly equal
the target CI run's `head_sha`; ancestry is not sufficient. Enabling
`allow_later_commit` changes the package target and therefore requires a
staging run for that later exact commit.

The individual workflows validate their own evidence:
`validate-release-evidence.sh` fixes the package target SHA, and
`validate-inspect-web-promotion.sh` fixes the staging SHA, run attempt,
artifact ID, and digest. Comparing the two resolved SHAs remains an explicit
operator gate; no single workflow currently verifies the cross-workflow
equality. Perform that comparison before dispatch: the `nuget` environment has
no approval gate, so package publication proceeds automatically after its
builds.

## Packages

The tool is published as a pointer package, Native AOT packages for supported
runtime identifiers, and a managed fallback:

| Package | Role |
| --- | --- |
| `dotnet-inspect` | TFM-agnostic pointer to the runtime-specific packages |
| `dotnet-inspect.win-x64` | Windows Native AOT |
| `dotnet-inspect.win-arm64` | Windows Native AOT |
| `dotnet-inspect.linux-x64` | Linux Native AOT |
| `dotnet-inspect.linux-arm64` | Linux Native AOT |
| `dotnet-inspect.osx-arm64` | macOS Native AOT |
| `dotnet-inspect.any` | Managed fallback at the supported runtime floor |

The package version is owned by `VersionPrefix` in
`src/dotnet-inspect/dotnet-inspect.csproj`. Do not copy the current value into
guidance. The publish workflow reads it from the selected commit when creating
the GitHub release tag.

### What is packable and publishable

Pack and publish flows are separate from the normal build and build
`src/dotnet-inspect` directly. Packaging is off by default (`IsPackable=false`
in the root `Directory.Build.props`). Every CLI project declares `IsTool`, and
solution publishing is off by default there as well (`IsPublishable=false`).
Every CLI project declares `IsTool`; `Directory.Build.targets` uses that
property to enable solution publish and exclude API documentation from publish
output. Each tool explicitly declares `IsPackable` and `PackAsTool`; the latter
must be project-local because SDK runtime-identifier inference consumes it
before `Directory.Build.targets` is imported. These SDK properties do not add a
project to a release workflow. Internal libraries remain non-packable and
non-publishable, and may still generate XML documentation in their own build
outputs. `PackagingSurfaceTests` pins both defaults and the tool census.

## Prerequisites

Before dispatching a release:

1. Select a successful CI run for the exact commit to publish.
2. Select a successful Deep Inspect `test` run completed within the last 36
   hours. Prefer an exact-SHA match.
3. Select the successful `deploy-inspect-web.yml` **push** run for the exact
   commit to publish. Manual staging runs are not promotable.
4. Compare the CI and staging runs' full 40-character `head_sha` values. Stop
   if they differ.
5. If publishing a later commit, review every intervening commit and decide
   whether carrying the ancestor's certification is justified.
6. Confirm that the commit contains the intended `VersionPrefix` and release
   notes.
7. Confirm that the version has not already been published.
8. Reconcile the shipped documentation with what the release actually does —
   see [Shipped documentation](#shipped-documentation).

## Shipped documentation

`README.md` and the user-facing product skills under `skills/` are not
repository-side notes. They are release artifacts:

- `README.md` is the package readme (`PackageReadmeFile` in
  `src/dotnet-inspect/dotnet-inspect.csproj`, packed via the `None Include`
  entry beside it), so it is the first thing a consumer of the published
  package reads.
- Every shipped `SKILL.md` is embedded into the tool binary as an
  `EmbeddedResource` and served by `dotnet-inspect skill`, so the published tool
  teaches agents whatever those files said at build time. The embeds are
  enumerated one line per skill in `src/dotnet-inspect/dotnet-inspect.csproj`,
  not globbed.

Repo-local maintainer skills under `.github/skills/` and `.claude/skills/` are
not product release artifacts. Do not register or embed them.

A change to `VersionPrefix` is therefore a documentation checkpoint. Consult
both before dispatching, and expect to update them:

- Do the commands, flags, defaults, and example output in `README.md` still
  match the tool? Re-run any example whose command surface changed rather than
  eyeballing it.
- Does each product `skills/*/SKILL.md` still describe capabilities the release
  actually has, and is a new capability discoverable from the product skill
  that owns it? A product skill's YAML frontmatter `description:` is the single
  source of truth for the generated listing, so a stale description ships as a
  stale listing.
- **Does every product skill added under `skills/` since the last release appear
  in both places?** A product skill needs an `EmbeddedResource` line in
  `src/dotnet-inspect/dotnet-inspect.csproj` *and* an entry in
  `SkillCommand.Skills`.
  `SkillCommandTests.FocusedSkillFilesRegistryAndEmbeddedResourcesAgree`
  enforces equality between `skills/*/SKILL.md` on disk, the runtime registry,
  and embedded resources. The focused skill CI lane runs that gate for
  `SKILL.md`-only pull requests. Confirm the generated listing with
  `dotnet-inspect skill list`.
- Record the outcome either way. If neither needed a change, say so; silence
  reads the same as an unchecked box.

## Dispatching

1. Open the selected successful `main` CI run and copy its run ID.
2. Open the daily or manually dispatched Deep Inspect `test` run and copy its
   run ID.
3. Open the matching successful `deploy-inspect-web.yml` push run and copy its
   run ID.
4. Compare the CI and staging run `head_sha` values. Both must name the exact
   release commit:

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

5. Open the **Publish** and **Promote inspect-web** workflows in separate tabs
   and choose **Run workflow** for both.
6. In **Publish**, enter the CI and certification run IDs and type `publish` in
   the confirmation field.
7. In **Promote inspect-web**, enter the staging run ID and type `promote` in
   the confirmation field.
8. Leave `allow_later_commit` disabled for an exact certification. Enable it
   only after reviewing the commits between the certified and target SHAs.
9. Dispatch both workflows as one operator action. Do not substitute a newer
   run for either side after the exact-SHA comparison.
10. Confirm immediately that both resolve jobs report the expected release SHA.
    If either is wrong, cancel the package workflow before its publish job
    starts and leave the production-site environment unapproved.
11. Monitor the package builds and automatic NuGet publication. There is no
    NuGet environment approval after dispatch.
12. Wait for the package workflow and GitHub release to succeed, then approve
    the production-site environment. Never promote the site first.

The package workflow then:

1. Verifies the normal CI run, the fresh Deep Inspect certification, and their
   commit relationship.
2. Builds each Native AOT package on its supported host.
3. Builds the TFM-agnostic pointer without inner RID packages, then builds the
   managed fallback. Dedicated native jobs own RID-specific packages; the
   pointer contains only their mapping under `tools/any/any`.
4. Validates that the managed fallback retains its supported runtime reach and
   that the pointer remains TFM-agnostic.
5. Revalidates both source runs, freshness, and the resolved commit immediately
   before NuGet authentication and publication.
6. Publishes Native AOT packages, then the managed fallback, then the pointer.
7. Creates a GitHub release from the package version at the resolved CI commit
   and attaches all packages.

The pointer is deliberately published last because it references the
runtime-specific packages.

The site promotion workflow may validate its staging evidence while packages
build, but its production environment remains unapproved. After package
publication succeeds, approve the site workflow; it revalidates the staging run
and artifact identity, downloads the exact staged artifact with digest
verification, and deploys it to `https://dotnet-inspect.net`.

The release is complete only when both workflows succeed. Verify that the
published package and GitHub release use the intended version and commit, then
check the production site's status bar for the same version and linked commit.

## Failure handling

- **Run resolution fails:** verify the run ID belongs to this repository and
  still exists.
- **Resolved SHA is wrong:** cancel the workflow; do not publish a nearby run.
- **Certification is stale or red:** use a newer successful daily run or
  dispatch Deep Inspect with `lane=test`.
- **The target is later than the certification:** review the intervening
  commits, then either obtain exact-SHA certification or explicitly enable
  `allow_later_commit`.
- **The target is older or divergent:** select a certification that is the
  target or its ancestor; this relationship cannot be overridden.
- **Reach validation fails:** fix the package shape rather than bypassing the
  guard.
- **The release tag targets another commit:** move it to the resolved CI commit
  and fix the workflow before the next release. The
  `ReleaseWorkflow_TagsResolvedCiCommit` test gates this wiring.
- **A package version already exists:** advance `VersionPrefix`; published
  package versions are immutable.
- **A partially published release is retried:** the workflow uses
  `--skip-duplicate`, but verify the complete package set and GitHub release
  before treating the release as complete.
- **The package workflow fails before site approval:** leave the production-site
  environment unapproved and retry package publication with the same CI and
  certification run IDs.
- **The release commit's staging run was cancelled by a newer push:** wait for
  active staging work to finish, then rerun only the failed or cancelled jobs
  with `gh run rerun <staging-run-id> --failed`. This preserves a successful
  build's single uploaded artifact while retrying deployment; do not rerun all
  jobs. Use that successful rerun for promotion. A manually dispatched staging
  run is still not promotable.
- **Site promotion fails after package publication:** retry promotion with the
  same staging run ID. Do not advance the package version or staging SHA.
- **Either side resolves a different SHA:** cancel the package workflow
  immediately, leave the site unapproved, and select matching evidence. If
  package publication already started, audit the partial immutable package set
  before retrying. Do not treat ancestry or an equal version string as a match.
