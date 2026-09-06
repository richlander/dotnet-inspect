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
