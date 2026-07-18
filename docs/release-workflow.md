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

## Prerequisites

Before dispatching a release:

1. Select a successful CI run for the exact commit to publish.
2. Confirm that the commit contains the intended `VersionPrefix` and release
   notes.
3. Confirm that the version has not already been published.

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
