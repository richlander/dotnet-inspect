# Markout co-development

Markout ships as a NuGet package, so a change that spans both repositories
cannot be validated by either one alone. This document describes the loop that
makes such a change developable: point dotnet-inspect at Markout **source**
until the Markout side is good, then ship a package and go back.

Repository, worktree, and review rules live in [AGENTS.md](../AGENTS.md). This
document only covers what is different when a change crosses the repository
boundary.

## When to use this

Use the local loop when a change needs new or altered Markout behavior. Do not
use it to consume Markout as it already exists — that is an ordinary
`PackageReference` bump.

The loop's purpose is **confidence, not delivery**. It exists so the Markout
change can be exercised against a real consumer before its API is frozen by a
release.

## The order of operations

The steps are sequential because a package release sits in the middle. Nothing
about the local loop changes that.

1. **Develop locally** against Markout source with project references, in both
   repositories at once.
2. **Get Markout to quality.** This is the goal of the whole local phase. The
   Markout change should be reviewable and mergeable on its own evidence.
3. **Land and release Markout.** Merge the Markout PR, then publish a package.
4. **Switch dotnet-inspect back** to `PackageReference` at the new version.
5. **Raise the dotnet-inspect PR.**

Stacked branches remain available on either side and are orthogonal to this
loop — see AGENTS.md.

### There is no dotnet-inspect PR before step 5

CI restores from the package feed. It has no access to a local Markout
worktree, so a branch carrying project references **cannot build in CI**. A PR
raised during the local phase does not fail for an interesting reason; it fails
because it is describing a build that only exists on one machine.

Do not push project-reference edits to a branch under review, and do not raise
a dotnet-inspect PR to "show progress" during the local phase. Progress during
the local phase belongs to the Markout PR.

## Setting up the local loop

Markout source is checked out as a git worktree at `.worktrees/markout`, which
is git-ignored (`.gitignore`, `.worktrees/`). Refresh it before starting; a
stale worktree is the most common cause of confusing local failures.

Replace the Markout `PackageReference` with **two** project references:

```xml
<ProjectReference Include="..\..\.worktrees\markout\src\Markout\Markout.csproj" />
<ProjectReference Include="..\..\.worktrees\markout\src\Markout.SourceGeneration\Markout.SourceGeneration.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Three projects reference the package, and each one that must move needs both
lines:

- `src/dotnet-inspect/dotnet-inspect.csproj`
- `src/DotnetInspector.MetadataRendering/DotnetInspector.MetadataRendering.csproj`
- `src/ILInspector.Decompiler.Tests/ILInspector.Decompiler.Tests.csproj`

`ILInspector.Decompiler.Tests` also consumes `Markout.Templates`, which is a
separate package with its own version.

## Footguns

**The source generator does not arrive with the library.** This is the reason
two project references are needed rather than one. `Markout.csproj` references
`Markout.SourceGeneration` with `PrivateAssets="all"` and
`ReferenceOutputAssembly="false"`, and packs it into `analyzers/dotnet/cs`. That
delivers the generator to *package* consumers only — `PrivateAssets="all"`
deliberately stops it from flowing through a project reference. Referencing
`Markout.csproj` alone therefore produces a build that compiles the library but
runs no generator. Nothing in the failure mentions a generator; it arrives as
unimplemented abstract members on every `MarkoutSerializerContext` subclass:

```text
error CS0534: 'TypeViewContext' does not implement inherited abstract member
'MarkoutSerializerContext.GetSchemaInfo<T>()'
```

Everything under `src/dotnet-inspect/Views/` depends on generated output, so
this appears dozens of times at once.

**Project references import Markout's current transitive constraints.** A
`PackageReference` pins one resolved graph; Markout source brings whatever its
`main` requires today, which can be ahead of what this repository pins. The
symptom is a downgrade error rather than anything naming Markout:

```text
error NU1605: Detected package downgrade: MarkdownTable.Formatting from 0.3.4 to 0.3.3
  dotnet-inspect -> Markout -> MarkdownTable.Formatting (>= 0.3.4)
  dotnet-inspect -> MarkdownTable.Formatting (>= 0.3.3)
```

The fix is to raise the pin in `Directory.Packages.props` to satisfy Markout.
Do this as a deliberate, separate step: it is a real dependency change that
outlives the local phase, not scaffolding to be reverted with the project
references.

**The edit is tracked; the thing it points at is not.** `.worktrees/` is
git-ignored, but the `.csproj` files are not. The one artifact that can be
committed by accident is the one with no safety net. Check for stray project
references before every commit during the local phase.

**The package version is not where you left it.** dotnet-inspect pins a Markout
version that can be well behind Markout's `main`. Returning to
`PackageReference` at step 4 may absorb unrelated drift along with the intended
change. Prefer bumping to current Markout **before** starting the local phase,
as its own change, so that a step-4 failure is unambiguous.

**Local green does not mean CI green.** During the local phase, the build
proves the Markout change works against this consumer. It proves nothing about
the published package, which is built and packed separately. Step 4 is a real
validation step, not bookkeeping.
