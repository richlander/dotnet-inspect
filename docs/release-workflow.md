# Release workflow

This document explains how a tested commit becomes published packages.
Repository development, worktree, build, and test rules live in
[AGENTS.md](../AGENTS.md). The executable source of truth for publishing is
[release.yml](../.github/workflows/release.yml); update this document when that
workflow changes.

## Release boundary

CI and publishing have separate responsibilities:

| Workflow | Trigger | Responsibility |
| --- | --- | --- |
| `ci.yml` | Pull requests and pushes to `main` | Validate the changed commit |
| `release.yml` | Manual dispatch | Rebuild, verify, and publish one selected commit |

The publish workflow accepts a CI run ID only to resolve the exact commit SHA.
It does not publish or otherwise trust packages produced by that CI run. Every
release package is built fresh from the resolved commit in `release.yml`.

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

### What is packable

Pack and publish flows are separate from the normal build and build
`src/dotnet-inspect` directly. Packaging is off by default (`IsPackable=false`
in the root `Directory.Build.props`). Every CLI project declares `IsTool`, and
`Directory.Build.targets` opts it into `IsPackable` and the SDK tool layout
while excluding API documentation from publish output. This enables
`dotnet pack`; it does not add a project to a publishing workflow. Internal
libraries remain non-packable and may still generate XML documentation in
their own build outputs. `PackagingSurfaceTests` pins the default, tool census,
and centralized overrides.

## Prerequisites

Before dispatching a release:

1. Select a successful CI run for the exact commit to publish.
2. Confirm that the commit contains the intended `VersionPrefix` and release
   notes.
3. Confirm that the version has not already been published.
4. Reconcile the shipped documentation with what the release actually does —
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
  `SkillCommand.Skills`. Nothing enforces this: every test in
  `SkillCommandTests` iterates `SkillCommand.Skills`, so a skill directory that
  was never registered is invisible to the suite and ships as nothing at all,
  with a green build. Compare `skills/*/SKILL.md` on disk against both lists by
  hand, and confirm with `dotnet-inspect skill list`.
- Record the outcome either way. If neither needed a change, say so; silence
  reads the same as an unchecked box.

## Dispatching

1. Open the selected run in GitHub Actions and copy its run ID.
2. Open the **Publish** workflow and choose **Run workflow**.
3. Enter the run ID and type `publish` in the confirmation field.
4. Confirm that the resolve job reports the expected commit SHA before the
   package jobs proceed.

The workflow then:

1. Runs the full publish-time product, CLI, decompiler, Analysis, and IL
   round-trip checks against the resolved commit.
2. Builds each Native AOT package on its supported host.
3. Builds the pointer and managed fallback packages.
4. Validates that the managed fallback retains its supported runtime reach and
   that the pointer remains TFM-agnostic.
5. Publishes Native AOT packages, then the managed fallback, then the pointer.
6. Creates a GitHub release from the package version and attaches all packages.

The pointer is deliberately published last because it references the
runtime-specific packages.

## Failure handling

- **Run resolution fails:** verify the run ID belongs to this repository and
  still exists.
- **Resolved SHA is wrong:** cancel the workflow; do not publish a nearby run.
- **Publish-time tests fail:** fix the product on a new commit, obtain a new
  successful CI run, and dispatch again with that run ID.
- **Reach validation fails:** fix the package shape rather than bypassing the
  guard.
- **A package version already exists:** advance `VersionPrefix`; published
  package versions are immutable.
- **A partially published release is retried:** the workflow uses
  `--skip-duplicate`, but verify the complete package set and GitHub release
  before treating the release as complete.
