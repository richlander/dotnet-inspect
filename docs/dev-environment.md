# Local development environment

Supplementary notes for [Building and testing](../AGENTS.md#building-and-testing)
that most contributors won't need but should be able to find.

## Which dotnet-inspect to run

For routine repository development and investigation, use the latest production
dotnet-inspect:

```bash
dnx dotnet-inspect -y -- <command>
```

The production tool is normally current and its Native AOT executable starts
much faster than `dotnet run`. Prefer it for inspecting packages, platform
libraries, local artifacts, and existing product behavior while developing.

Use the source version primarily to test behavior from the current worktree:

```bash
dotnet run --project src/dotnet-inspect -c Release -- <command>
```

The source command is required when the evidence depends on an unmerged change,
when reproducing or validating a source-only fix, or when checking output that
the production release does not yet contain. Do not cite the production tool as
evidence for worktree behavior, and do not pay the source-build startup cost for
routine development queries that the production tool can answer.

## Build warnings and dependency auditing

Warnings-as-errors is the repository default for normal builds and tests. A
test may suppress a specific diagnostic only when its intended input cannot be
produced with that diagnostic enabled; document the reason at the suppression.
Prefer that narrow exception to disabling warnings-as-errors for a project.

`NU1507` (multiple package sources without source mapping under central package
management) stays enabled and is an error. Configure package-source mapping or
use an explicit source for the restore; do not hide it with `NoWarn`.

NuGet Audit is different: vulnerability advisories can change without a source
change. It is off for ordinary local, PR, and release builds. The separate
`nuget-audit-scheduled.yml` workflow runs nightly at 05:37 UTC and supports manual
reruns. It audits direct and transitive dependencies at every severity; findings
fail that workflow rather than `ci-required`.

The audit restores the solution and the separately hosted inspect-web engine
tests, MSDL proxy tests, and IL round-trip tests, including each root's project
references. It audits the tooling's restored dependencies, not the package
contents acquired as inspection or corpus inputs. Standalone projects outside
those restore graphs are not covered by this scheduled audit.

To reproduce one audit root locally:

```bash
dotnet restore dotnet-inspect.slnx --force-evaluate \
  -p:Configuration=Release -p:NuGetAudit=true \
  -p:NuGetAuditMode=all -p:NuGetAuditLevel=low
```

The root build properties own these defaults. Corpus scripts and test launchers
inherit them instead of supplying their own warning or audit overrides.

## Package acquisition when nuget.org is disabled

If a machine-level proxy lacks an exact pinned version and restore reports
`NU1603`, do not edit machine configuration or commit a clearing
`nuget.config`. Override sources for one restore:

```bash
dotnet restore dotnet-inspect.slnx -s https://api.nuget.org/v3/index.json
```

Prefer `--source` to `--add-source`; the package cache then satisfies later
restores. Repeat after clearing the cache or changing to an uncached pin. Tool
acquisition accepts the same override:

```bash
dotnet tool install -g dotnet-inspect --source https://api.nuget.org/v3/index.json
dnx dotnet-inspect --source https://api.nuget.org/v3/index.json
```

## Additional library suites

### Text-library tests

Run the complete text-library suites from the repository root:

```bash
dotnet run --project tests/InertText.Tests -c Release
dotnet run --project tests/ILInspector.Text.Tests -c Release
```

Both are xUnit in-process executables. Their source lives under `tests/`, while
their built outputs remain under `artifacts/`. The in-process corpus data stays
with its test host; it is not an independently compiled inspected fixture.
See [repository layout](fixture-governance.md#repository-layout).

### Ecosystem tests

Run both the dedicated catalog suite and the separate public consumer suite:

```bash
dotnet run --project tests/DotnetInspector.Ecosystems.Tests -c Release
dotnet run --project tests/DotnetInspector.Ecosystems.Consumer.Tests -c Release
```

Both are xUnit in-process executables. Keep the consumer project separately
compiled without friend access; only the dedicated catalog suite is an assembly
friend. See the [ecosystem boundary](design/ecosystem-packs.md#dependency-boundary) and
[package-set registry gates](design/package-set-registry.md#required-gates).

### IL substrate and diff tests

Run the instruction-substrate and IL comparison suites:

```bash
dotnet run --project tests/ILInspector.Instructions.Tests -c Release
dotnet run --project tests/ILInspector.ILDiff.Tests -c Release
```

Both are xUnit in-process executables and retain separate assemblies and
`artifacts/` outputs. Their compiler-produced sample types stay with their test
hosts; the ILDiff suite also retains its test-only Roslyn dependency for source
inspection. See the [instruction substrate](../src/ILInspector.Instructions/README.md)
and [IL comparison boundary](../src/ILInspector.ILDiff/README.md).

### Model-bound C# tests

Run the C# formatting, declaration, and type-shell suite:

```bash
dotnet run --project tests/ILInspector.CSharp.Tests -c Release
```

This is an xUnit in-process executable with its built output under `artifacts/`.
Its compiler-produced sample types stay with the test host, including the types
inspected through its own assembly. Keep this suite distinct from the model-free
`tests/CSharpText.Tests` suite. See
[repository layout](fixture-governance.md#repository-layout).

## Test tooling activation

The CLI and decompiler suites skip `ilasm`/`ildasm` checks when those tools are
missing; metadata tests do the same for `mdv`. Activate all three with
`source eng/activate-iltools.sh --mdv` before relying on a clean run — source
the wrapper rather than assembling `PATH` by hand. CI restores the same pinned
tools and fails its lane if acquisition fails.

The IL round-trip project has separate dependency restore and fast/full test
commands; follow `tests/DotnetInspector.ILRoundtrip.Tests/README.md`.
`ILInspector.Decompiler.Tests` composes `Speed` and `Area` traits and offers a
`--gate <preset>` flag (`--gate list` prints the table); the taxonomy and
per-change targeting advice live in `docs/decompiler-correctness-pipeline.md`.

Only tool projects set `IsPackable=true`; `IsTool` also makes them available to
solution-level publish. Internal library APIs are not external compatibility
surfaces. Changing `VersionPrefix` is a coordinated package-and-site release:
follow `docs/release-workflow.md`, publish dotnet-inspect and
`https://dotnet-inspect.net` from the same commit, and update the shipped
`README.md` and skills.

## File-based apps

For throwaway probes, use .NET file-based apps under `/tmp/` unless a specific
Python library is required. Do not use `.csx`, `dotnet-script`, `dotnet script`,
or `dotnet-fsi`.

```bash
dotnet run /tmp/check.cs
```
