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

Markout source is a **peer checkout**, a sibling directory of this repository's
worktree — not a directory inside it. Point at the Markout worktree carrying
the change; if that is the branch's own PR worktree, the reference is
read-only in practice, and a defect the adopter finds gets fixed on that branch
where it belongs.

Do **not** nest the checkout under this repository (for example at
`.worktrees/markout`). Nesting is the intuitive arrangement and it does not
work — see the footgun below.

Replace the Markout `PackageReference` with **two** project references, written
as **absolute paths**:

```xml
<ProjectReference Include="/home/you/git/markout/src/Markout/Markout.csproj" />
<ProjectReference Include="/home/you/git/markout/src/Markout.SourceGeneration/Markout.SourceGeneration.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Absolute, deliberately. A relative path looks like something that could be
committed; an absolute one names a directory that exists on exactly one
machine, so the edit advertises what it is every time anyone reads the file.
That matters because these lines are tracked while the thing they point at is
not — see the footgun below.

Three projects reference the package, and each one that must move needs both
lines:

- `src/dotnet-inspect/dotnet-inspect.csproj`
- `src/DotnetInspector.MetadataRendering/DotnetInspector.MetadataRendering.csproj`
- `src/ILInspector.Decompiler.Tests/ILInspector.Decompiler.Tests.csproj`

`ILInspector.Decompiler.Tests` also consumes `Markout.Templates`, which is a
separate package with its own version.

## Footguns

**A nested checkout inherits this repository's build configuration.** Putting
Markout source inside the tree — `.worktrees/markout` is the obvious spot — is
the arrangement that fails, and it fails at restore, before any of the
interesting problems below can be reached. Two mechanisms, neither fixable from
the Markout side:

- Markout has no `Directory.Packages.props`. MSBuild walks up from the project
  directory, finds *this* repository's, and switches central package management
  on for projects that pin their versions inline.
- NuGet configuration **merges** up the directory tree rather than stopping at
  the first file found, so Markout's projects acquire this repository's feeds.

```text
error NU1008: The following PackageReference items cannot define a value for
Version: Microsoft.CodeAnalysis.Analyzers, Microsoft.CodeAnalysis.CSharp.
error NU1507: There are 2 package sources defined in your configuration.
```

Both name Markout project files while being caused entirely by their location,
which is what makes the failure hard to read. A peer checkout has neither
problem: nothing above it belongs to this repository.

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

**Markout's transitive constraints move with Markout.** Markout depends on
`MarkdownTable.Formatting`, and raises its floor over time. NU1605 is
warning-as-error here, so when Markout's floor passes this repository's pin,
restore fails before anything compiles — and the error names a package that
neither side's change touches:

```text
error NU1605: Detected package downgrade: MarkdownTable.Formatting from 0.3.4 to 0.3.3
  dotnet-inspect -> Markout -> MarkdownTable.Formatting (>= 0.3.4)
  dotnet-inspect -> MarkdownTable.Formatting (>= 0.3.3)
```

This fires on a plain version bump as readily as on the project-reference swap;
the swap just reaches further, since source carries whatever Markout's `main`
requires *today* rather than what its last release required. Either way the fix
is to raise the pin in `Directory.Packages.props`. Do that as its own change,
not as scaffolding to be reverted with the project references: it is a real
dependency change that outlives the local phase.

**The edit is tracked; the thing it points at is not.** The Markout checkout is
outside this repository entirely, but the `.csproj` files are tracked. The one
artifact that can be committed by accident is the one with no safety net, and
nothing in the build will object — the branch stays green on the machine that
has the directory. Writing the reference as an absolute path is the cheap
mitigation: a reviewer or `git diff` reader sees a home directory and knows
immediately. Check for stray project references before every commit during the
local phase:

```bash
git diff --cached -G'ProjectReference.*Markout' --stat
```

**Project-closure tests see the peer repository.** Project references make
Markout's projects part of the build graph, and tests that deliberately inspect
that graph see them too. In particular,
`CommandErrorOwnershipTests.EveryProjectInTheCliClosureIsAnalyzedForTheStderrRule`
requires every dotnet-inspect project to carry this repository's banned-API
analyzer configuration. Markout correctly does not carry another repository's
policy, so that test fails during the source-reference phase even when every
product and output test passes.

Do not weaken the closure test or add dotnet-inspect analyzers to Markout.
Exclude that one test while proving the source-reference build, then run it
normally after step 4 restores package references. The failure should disappear
with the peer projects; if it does not, it is no longer co-development
scaffolding.

**The package version is not where you left it.** dotnet-inspect pins a Markout
version that can be well behind Markout's `main`. Returning to
`PackageReference` at step 4 may absorb unrelated drift along with the intended
change. Bump to current Markout **before** starting the local phase, as its own
change, so that a step-4 failure is unambiguous.

**One release can collapse a source-level stack.** Local adoption branches can
prove separate Markout changes against their exact intermediate heads. A
published package cannot: if several Markout PRs merge before one release, the
package contains all of them. The lowest downstream branch therefore sees the
whole released contract when it returns to `PackageReference`, including
behavior that its source-level proof deliberately left to a later branch.

Choose that boundary explicitly. Publish an intermediate package when the
downstream slices must retain independent package contracts; otherwise move
release-wide compatibility updates and expectations into the lowest downstream
package-bump branch. Do not point an earlier local slice at a later Markout head
just to imitate the future package — that weakens the source-level proof by
mixing in behavior outside the slice. In either case, rerun the complete suite
after the package handoff; the source-level results are not evidence for the
larger released contract.

**A version bump attracts false attribution.** Once the diff says "Markout
0.29.0 -> 0.33.0", every failure in the run looks like it belongs to the bump,
and a failure's own text is easy to read as corroboration. Baseline before
believing it: re-run the failing tests at the unmodified base commit. The
0.33.0 bump had seven decompiler tests fail, reported as rendering drift from
the bump; the identical seven failed at the base with Markout still at 0.29.0.
Their content already disagreed with that reading — assembly binding moving
between `System.Runtime` and `System.Private.CoreLib`, Roslyn closure slots
renumbering, `CS0246` on `int` — none of which a markdown renderer can reach.
Markout renders markdown, TSV, and JSONL; if a failure is not about rendered
output, suspect the SDK before the package.

**Local green does not mean CI green.** During the local phase, the build
proves the Markout change works against this consumer. It proves nothing about
the published package, which is built and packed separately. Step 4 is a real
validation step, not bookkeeping.
