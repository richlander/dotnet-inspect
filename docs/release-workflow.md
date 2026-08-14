# Release workflow

This document explains how a tested commit becomes published packages.
Repository development, worktree, build, and test rules live in
[AGENTS.md](../AGENTS.md). The executable source of truth for publishing is
[release.yml](../.github/workflows/release.yml); update this document when that
workflow changes.

## Release boundary

PR validation, release certification, and publishing have separate
responsibilities:

| Workflow | Trigger | Responsibility |
| --- | --- | --- |
| `ci.yml` | Pull requests and pushes to `main` | Validate the changed commit |
| `deep-inspect.yml` | Daily schedule or manual `lane=test` dispatch | Certify one `main` commit with the full slow test and corpus gates |
| `release.yml` | Manual dispatch | Verify certification, rebuild packages, and publish one selected commit |

The publish workflow accepts a successful `main` CI run ID to resolve the exact
commit SHA and a Deep Inspect run ID as its heavy-validation evidence. It does
not publish or otherwise trust packages produced by either run. Every release
package is built fresh from the resolved commit in `release.yml`.

Deep Inspect certifies `main` daily at 06:00 UTC. Dispatch
`deep-inspect.yml` with `lane=test` when a fresh result is needed during the
day. Certification remains valid for 36 hours.

By default, the certified and published commits must be identical. An operator
may explicitly enable `allow_later_commit` to publish a later `main` commit
with successful CI. The workflow proves that the target descends from the
certified commit, but the operator owns the decision that the intervening
changes do not require another slow run. Divergent and older commits are never
accepted.

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
3. If publishing a later commit, review every intervening commit and decide
   whether carrying the ancestor's certification is justified.
4. Confirm that the commit contains the intended `VersionPrefix` and release
   notes.
5. Confirm that the version has not already been published.
6. Reconcile the shipped documentation with what the release actually does —
   see [Shipped documentation](#shipped-documentation).

## Shipped documentation

`README.md` and the skills are not repository-side notes. They are release
artifacts:

- `README.md` is the package readme (`PackageReadmeFile` in
  `src/dotnet-inspect/dotnet-inspect.csproj`, packed via the `None Include`
  entry beside it), so it is the first thing a consumer of the published
  package reads.
- Every shipped `SKILL.md` is embedded into the tool binary as an
  `EmbeddedResource` and served by `dotnet-inspect skill`, so the published tool
  teaches agents whatever those files said at build time. The embeds are
  enumerated one line per skill in `src/dotnet-inspect/dotnet-inspect.csproj`,
  not globbed.

A change to `VersionPrefix` is therefore a documentation checkpoint. Consult
both before dispatching, and expect to update them:

- Do the commands, flags, defaults, and example output in `README.md` still
  match the tool? Re-run any example whose command surface changed rather than
  eyeballing it.
- Does each `SKILL.md` still describe capabilities the release actually has,
  and is a new capability discoverable from the skill that owns it? A skill's
  YAML frontmatter `description:` is the single source of truth for the
  generated listing, so a stale description ships as a stale listing.
- **Does every skill added since the last release appear in both places?** A
  skill needs an `EmbeddedResource` line in
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
3. Open the **Publish** workflow and choose **Run workflow**.
4. Enter both run IDs and type `publish` in the confirmation field.
5. Leave `allow_later_commit` disabled for an exact certification. Enable it
   only after reviewing the commits between the certified and target SHAs.
6. Confirm that the resolve job reports both expected SHAs before the package
   jobs proceed.

The workflow then:

1. Verifies the normal CI run, the fresh Deep Inspect certification, and their
   commit relationship.
2. Builds each Native AOT package on its supported host.
3. Builds the TFM-agnostic pointer without inner RID packages, then builds the
   managed fallback. Dedicated native jobs own RID-specific packages; the
   pointer contains only their mapping under `tools/any/any`.
4. Validates that the managed fallback retains its supported runtime reach and
   that the pointer remains TFM-agnostic.
5. Publishes Native AOT packages, then the managed fallback, then the pointer.
6. Creates a GitHub release from the package version at the resolved CI commit
   and attaches all packages.

The pointer is deliberately published last because it references the
runtime-specific packages.

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
