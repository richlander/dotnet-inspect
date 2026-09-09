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
dotnet run --project tests/Inspector.Text.Tests -c Release
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

### Analysis tests

Build the solution before running the analysis suite so every FixtureCatalog
binary is available, and always use Release because compiler-generated IL is
part of the evidence:

```bash
dotnet build dotnet-inspect.slnx -c Release
dotnet run --project tests/ILInspector.Analysis.Tests -c Release
```

This is a Microsoft Testing Platform executable. Required PR lanes exclude
`Speed=Slow` after `--`; Deep Inspect runs the complete suite. Compiler-produced
runtime-async specimens remain inside the test assembly, while independently
compiled analysis inputs remain under `fixtures/analysis/`. See
[repository layout](fixture-governance.md#repository-layout).

### Decompiler tests

Build the solution before running the decompiler suite so its cataloged fixture
binaries and tool harness are current, and always use Release because the
compiler-produced IL is test evidence:

```bash
dotnet build dotnet-inspect.slnx -c Release
source eng/activate-iltools.sh --mdv
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --gate no-corpus
```

The custom xUnit executable retains its native selectors and `--gate` presets.
Its compiler-produced specimens stay with the host under `tests/`; independent
inputs remain under `fixtures/`, and linked harness sources remain owned by
`tools/DecompilerHarness`. Run the separate `--gate corpus` lane only when the
multi-hour corpus sweep is required. See
[decompiler correctness](decompiler-correctness-pipeline.md) and
[repository layout](fixture-governance.md#repository-layout).

### Inspection query tests

Build the solution before running the inspection-query suite so every
FixtureCatalog binary is available:

```bash
dotnet build dotnet-inspect.slnx -c Release
dotnet run --project tests/DotnetInspector.Queries.Tests -c Release
```

This is a Microsoft Testing Platform executable. Use `--filter-class` and
`--filter-method` after `--` for focused selections. Its source and embedded
resources live under `tests/`; the independently compiled binaries it inspects
remain under `fixtures/`. See
[repository layout](fixture-governance.md#repository-layout).

### Shared services tests

Build the solution before running the shared-services suite so its route-learning
FixtureCatalog binaries are available:

```bash
dotnet build dotnet-inspect.slnx -c Release
dotnet run --project tests/DotnetInspector.Services.Tests -c Release
```

This is a Microsoft Testing Platform executable. Use `--filter-class` and
`--filter-method` after `--` for focused selections. Its source lives under
`tests/`; independently compiled route-learning inputs and static signed-package
archives remain under `fixtures/services/`. See
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
