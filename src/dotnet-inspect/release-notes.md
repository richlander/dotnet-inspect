# Release Notes

## v0.14.0

### Grounding and skill workflow

- Renames the `package` README section to `Grounding` and adds a `--print`
  flag for emitting grounding/content payloads directly (#1659, #1672).
- Turns `dotnet-inspect skill` into a router to focused scenario sub-skills
  (`skill list`, `skill source`, `skill performance`, and more), with
  one-line descriptions sourced from each skill's YAML frontmatter
  (#1559, #1577).

### Performance analysis (experimental)

- Renames the `Optimization Opportunities` section to `Performance Triage`
  and ranks rows by triage priority (hot pay-dirt first) (#1530, #1545).
- Adds allocation-hotspot rows to `Performance Triage` and loop-aware
  allocation-regression detection to Analysis Diff (#1558, #1582).

### Decompiler (experimental)

- Large body of method-body raise, structuring, and printer fidelity
  improvements across the C# decompiler, plus honest `DEC####` degradation
  rather than plausible-but-wrong output. These remain experimental and are
  surfaced through `member -S @Source`.

### CLI fixes

- Fixes CLI batch processing bugs (#1679).
- Preserves `ref readonly` return signatures and function-pointer signature
  modifiers in rendered API surfaces (#1678, #1537).

## v0.13.0

### SourceLink section consolidation

- Renames the undecorated single-section output mode from `--raw` to `--bare`;
  `--raw` now names the default raw/fetchable GitHub URL shape and pairs with
  `--blob`.
- Clarifies that `--bare` is a presentation-only modifier for already-selected
  payloads, while `--count` remains the reduction that collapses a selected
  section/vector to a single row count.
- Generalizes `--bare` beyond code sections to package README/content payloads
  and one-column SourceLink URL output.
- Normalizes GitHub file links in package README/content output from `blob` to
  raw URLs in the default agent-friendly URL mode.
- Removes the standalone `source` command. Use `package`, `library`, and `type`
  `-S "Source Files"` for type-to-SourceLink URL rows, and use `member -S
  "Source Locations"` / `member -S "Original Source"` for member-level source
  evidence.
- Adds `library --il-offset` for MethodDef token + IL offset source
  symbolication.
- Adds `--blob` as the GitHub browser URL toggle for SourceLink URL sections.
- Adds `-t` type filtering to `package`/`library -S "Source Files"`.

### SourceLink member locations

- Adds a `Source Locations` section for member groups and selected signatures,
  reporting SourceLink-backed file/line/URL rows without fetching source bodies.
- Resolves SourceLink rows for unpinned NuGet packages whose symbols are only in
  `.snupkg` packages by reusing the resolved package version during PDB
  acquisition.
- Repeats the start line in the `End Line` column for single-line member source
  locations so blank cells only mean the end line is unknown.
- Keeps library SourceLink audit sections discoverable via `-D` when their
  render data is produced only after the section runs.
- Keeps `Member Index` focused on selector/query columns while moving
  source-location evidence to the dedicated source section.

### Package documentation and project grounding

- Adds package file and documentation views for the best package README, Markdown files, explicit file listings, scoped content, and frontmatter/body extraction.
- Adds opt-in `Source Files` sections to `type`, `library`, and `package` for SourceLink type-to-URL rows.
- Verifies portable PDB identity before using SourceLink rows so multi-TFM package symbol PDBs cannot be paired with the wrong assembly.
- Extends package README/content output with JSON/JSONL and frontmatter/body-scoped modes.
- Supports multi-package `package` surveys with package/version provenance and optional `--skip-empty`.
- Adds `project [path] --agents-index` for direct dependency grounding manifests and `project [path] --readme <package-id>` for version-resolved dependency docs.
- Reports selected package README provenance in `--info`.

### Member lookup and source sections

- Adds the `Member Index` section with copyable `Name:N` selectors, stable `Name~digest` selectors, and printed canonical signatures.
- Removes the older `--params` and `-of` overload selector options.
- Keeps `--show-index` as a compatibility alias for `-S "Member Index"`.

### Member source views

- Replaces selected-member `@Audit` with `@Source` for coherent source-view discovery.
- Splits `Decompiled Source` into plain raised C# and `Annotated Source` into the mixed C#+IL view.
- Keeps one readable decompiled C# section and makes `@Source` include `IL`.
- Removes the production `IR (Stages)`/`--dump-stages` decompiler-debugging surface; per-pass IR remains available through `DecompilerHarness`.
- Documents the source-view model in repo docs and the embedded skill guidance.

### Output polish

- Uses alphabetical field ordering for `Package Info`.

### Bare-name routing

- Keeps exact platform libraries such as `System.Text.Json` on the library view while routing exact NuGet-only package IDs such as `System.CommandLine` to package inspection.
- Suggests likely command names for bare-token typos such as `packag` before falling through to NuGet package lookup.

## v0.10.5

### Library workflows

- Adds `Switches` for feature, compatibility, and runtime configuration switch action points.
- Adds focused integration coverage for ASP.NET Core, Authentication, and OpenAPI.
- Broadens integration detection for package-owned starter APIs across DI, Logging, Health Checks, Hosting, OpenTelemetry, and ASP.NET Core middleware/endpoints.

### Type and implementation inspection

- Keeps single-type verbosity in the tree-shaped type view, with `-v:n` and `-v:d` expanding overload leaves.
- Adds whole-type decompiled source output and improves lowered C# readability for common compiler patterns.

## v0.10.4

### AI integration fixes

- Detects `Microsoft.Extensions.AI.OpenAI` AI adapter APIs such as `AsIChatClient`, `AsIEmbeddingGenerator`, and related modality adapters.
- Includes package-owned OpenAI realtime client support types in the AI integration section.
- Renames the `Integrations` roll-up count column to `APIs`.

## v0.10.3

### Library integrations

- Adds library integration discovery for AI, Aspire, Dependency Injection, Logging, Options, Hosting, Health Checks, HTTP Client, and OpenTelemetry.
- Adds `package <id> --library` to inspect the primary DLL in a package when it is unambiguous.
- Adds section categories such as `@Integrations` so agents can discover or render related library sections together.
- Refines focused integration sections to show package-owned starter APIs and user-facing support types instead of raw referenced assemblies.
- Adds OpenTelemetry telemetry-control rows for public `DisableTracing` and `DisableMetrics` APIs.
- Adds HTTP Client sub-kinds such as HTTP Logging, HTTP Latency, and HTTP Diagnostics.

### Decompiled source

- Improves lowered C# rendering for loops, conditional returns, generic element loads, operator sugar, lambdas, local functions, enum cases, and compound assignments.
- Reduces unnecessary goto labels and unsigned casts while preserving clearer control flow.

### Cleanup

- Removes the stale `demo` command.

## v0.10.2

### Full member signatures

- Makes member `Signature` values full single-line C# declarations with accessibility, modifiers, and high-signal attributes such as `[Obsolete]`.
- Improves overload documentation matching for XML docs.
- Warns when requested projection columns are not available and points to `-D` discovery.

## v0.10.1

### Member output

- Splits logical method summaries into `Method Groups`, reserving `Methods` for actual method rows and overload signatures.
- Makes `member Type -m Name` render overload rows by default, with full signatures and optional `--show-index` member selectors.
- Includes method generic parameter lists, such as `Serialize<TValue>(...)`, in rendered signatures.
- Aligns `--table` and `--tsv` selected-section output with Markdown so narrowed `-S Methods` renders overload rows.
- Adds first-class `Operators`, `Explicit Interface Implementations`, and local `Extension Methods` sections to type/member views.

### JSONL output

- Adds `--jsonl` for one JSON object per table row using the same stable projection as `--tsv`.

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
- Added `-S @All` to select all sections, including opt-in sections.

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
