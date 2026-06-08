# Release Notes

## v0.10.1

### Member output

- Splits logical method summaries into `Method Groups`, reserving `Methods` for actual method rows and overload signatures.
- Makes `member Type -m Name` render overload rows by default, with full signatures and optional `--show-index` selectors.
- Includes method generic parameter lists, such as `Serialize<TValue>(...)`, in rendered signatures.
- Aligns `--table` and `--tsv` selected-section output with Markdown so narrowed `-S Methods` renders overload rows.

## v0.10.0

### Table and TSV output

- Adds `--table` for compact pretty-printed rows and `--tsv` for normalized tab-separated rows.
- Treats `--table` and `--tsv` as single-table formats; select one section with `-S` or use Markdown/JSON for multi-section output.
- Keeps `--oneline` as a hidden compatibility alias for `--table`.
- Normalizes Markdown table cell pipe characters to `&#124;` instead of escaped pipes.

### Type shape output

- Collapses overload-heavy default single-type trees by logical member name, while leaving full overload signatures available through `-v:n`, `-v:d`, and targeted member queries.

## v0.9.4

### Cache

- Cleans obsolete versioned cache categories, such as older package-index schema caches, in the background after cache misses.
- Cache deletion paths are guarded so cache clearing and cleanup refuse to delete outside the active or legacy dotnet-inspect cache roots.

### Lowered C# output

- Recovers more recent C# lowering patterns, including `lock` statements, null-conditional assignments, and span collection expressions backed by inline-array helpers.
- Renders null-conditional property compound assignments such as `target?.Count += value` when the compiler-lowered shape is safe to fold.

## v0.9.2

### Package resolution

- `--preview`/`--prerelease` now opt latest package resolution into prerelease versions, including `library <dll> --package <package> --preview`.

### Output

- `Signals` no longer includes a SourceLink CR/LF placeholder row; CR/LF diagnostics are reported only by the `SourceLink Integrity` section.
- Library `Signals` now owns the `Async Kind` roll-up (`Runtime`, `State machine`, `Mixed`, or `None`); `Library Info` no longer duplicates it.
- Library output with explicit section selection now keeps a compact context row with key fields such as version and source.
- Symbol lookup misses are cached to avoid repeated network probes; 403 symbol-server misses are cached for 7 days.

## v0.9.1

### Fixes

- `library <dll> --package <tool-package> -S "SourceLink Integrity"` now resolves Tool v2 pointer/RID packages to their inspectable framework-dependent payload package.
- CI smoke tests now write directly to files again after the .NET 11 stdout redirection fix.

## v0.9.0

### Signals

- Replaced the top-level `audit` command with explicit `Signals` section selection for package and library signal reports.
- Added SourceLink availability and CR/LF mismatch diagnostics to library Signals.
- Package Signals now include symbol/source evidence grouped by PDB source, including `msdl.microsoft.com`, `.snupkg`, embedded, and in-package PDBs.

### Package inspection

- Added `Library Files` to list all files under `lib/` across target frameworks.
- Added package manifest version output and removed the redundant manifest schema row.
- Added `-S All` to select all sections, including opt-in sections.

### SourceLink

- SourceLink Integrity now treats CR/LF-only checksum differences as verified with a diagnostic row.
- Removed duplicate `source --audit`; use `library <target> -S "SourceLink Availability"` for full SourceLink reachability.

## v0.8.1

### Skill guidance

- Embedded `dotnet-inspect skill` guidance now includes a compact Modern .NET / preview workflow for runtime async classification, runtime-pack/platform assemblies, extension properties, and implementation-lowering inspection.

## v0.8.0

### Highlights

- `source --il-offset` maps MethodDef token + IL offset pairs to source file locations, with Markdown, table/TSV, and JSON output.
- `--count` returns a single integer row count when exactly one table section is selected.
- `library -S "Async*"` lists async methods and classifies them as runtime async or classic state-machine async.
- Platform assembly resolution is SemVer/prerelease-aware and resolves runtime-only assemblies such as `System.Private.CoreLib`.
- `type` and `member` discovery now default to effective `-D` output; use `--schema` for the static schema.
- Obsolete members are shown by default with an obsolete marker and message when available.

### Improvements

- `-S`, `--columns`, and `--fields` accept semicolon-separated lists in addition to comma-separated lists.
- Effective discovery and field projection are wired through API type/member routing and markdown output.
- Package-backed `type`, `member`, and related commands preserve package/library context more reliably for multi-library packages.
- NuGet configs containing local folder or `file://` sources no longer block later HTTP feeds during package resolution.
- Assembly public key tokens are computed from the full public key using the ECMA-335 SHA-1 algorithm.

### Documentation

- README.md is now a concise capability inventory.
- SKILL.md is now workflow-oriented for agents: upgrade triage, find-to-member drill-in, source/IL lookup, platform release notes, package/library audit, structured queries, and relationship exploration.
