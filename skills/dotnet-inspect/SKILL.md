---
name: dotnet-inspect
version: 0.12.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, SourceLink/symbol provenance, and version-to-version API changes.
---

# dotnet-inspect

Use dotnet-inspect when you need evidence instead of guesses for .NET packages, platform libraries, local assemblies, APIs, dependencies, SourceLink/symbol provenance, or version-to-version API changes.

Invoke with `dnx`:

```bash
dnx dotnet-inspect -y -- <command>
```

## Start with a command

| Goal | Start with | Drill in |
| ---- | ---------- | -------- |
| Find the right API | `find Pattern` | `type Type --package Foo`, then `member Type --package Foo`. |
| Inspect a package | `package Foo` | Add `-S Signals`, `-S Manifest`, `-S "Library Files"`, or `--library` to inspect the package DLL. |
| Load project package grounding | `project ./App --agents-index` | Use `--readme PackageId` only after the index identifies a dependency whose full docs matter. |
| Inspect a library or assembly | `library Foo` or `library path/to.dll` | Add `--platform`, `--package`, `-S Signals` when source matters, or `-S Integrations` when ecosystem support matters. |
| Inspect a type | `type Type --package Foo` | Add `--all` for non-public, hidden, and extra members. |
| Inspect members and overloads | `member Type --package Foo -m Name` | Add `-S "Member Index"` or `--show-index` for copyable `Name:N` and digest selectors. |
| Compare API versions | `diff --package Foo@old..new --breaking` | Use `--additive` for new APIs or `-t Type` to narrow. |
| Locate source or implementation | `source Type --package Foo` | For a selected overload use `member Type Member:1 -S @Source`, `-S Calls`, `-S Callers`, `-S "Call Graph"`, or `-S "Recovered IL"`. |
| Audit unsafe calls | `library MyLib.dll -S @Audit` | Drill into a selected member with `member Type Method:N --library MyLib.dll -S "Unsafe Operations,Recovered IL"`. |
| Explore relationships | `depends Type`, `extensions Type`, `implements Interface` | Add package, platform, or project scope as needed. |

## Output modes

Default output is Markdown. Use Markdown for readable evidence with headings, section boundaries, tables, and code fences. Use `--table` for compact human scanning, `--tsv` for stable field splitting, `--jsonl` for one JSON object per table row, `--json` for structured object graphs, and `--mermaid` for graph-shaped output such as `depends`.

Markdown and JSON can represent multi-section documents. Table, TSV, and JSONL are single-table formats for commands or projections that produce one table. Mermaid is diagram output for commands that produce graph-shaped results.

Format promises:

- Markdown table cell values do not contain escaped pipes (`\|`); pipe characters in values are normalized.
- `--tsv` table headers are stable snake_case keys, and cells never contain embedded tabs or newlines.
- `--jsonl` emits one compact JSON object per table row with stable snake_case property names.
- `--table` renders the same projection as `--tsv` and `--jsonl`, with each column starting at a uniform position across rows.

## Limits

Prefer built-in limiters to shell pipes. `-n N` and numeric shorthand like `-6` work like `head`; `--tail N` works like `tail`; add `--rows` to make head counts cap Markdown table data rows instead of output lines. Use `--count` to count rows in one selected table section. Use command-specific limiters for command-specific result sets: `-t N` limits type/find results, `-m N` limits member results, and `--versions N` limits package version lists.

## Query system

Use the query system when default views do not expose the detail you need. `-D` discovers available sections/columns; `-S Section` selects sections by name or wildcard; `--columns` and `--fields` project values. Discover first instead of guessing field names.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D "Method Groups" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --rows -n 10
```

`@` represents a category or grouping of sections. Bare `-S` renders `@Default`, a curated high-density view; `-S @All` renders an exhaustive document with all sections. Workflow categories such as `@Source`, plus focused categories such as `@Audit`, `@Integrations`, and `@Switches`, expand to related sections. Sections marked opt-in must be selected explicitly with `-S`. Focused library/member `-S Section` output keeps a compact context row before the selected section.

## General tips

- Built-in aliases and common BCL types such as `string`, `int`, and `List<T>` resolve without `--package`, `--platform`, or `--library`; start with `type string` or `type 'List<T>'`.
- `type` supports URL-like namespace probing: unresolved namespace-ish names such as `System.Text` produce best-effort prefix matches, while exact package/library/platform matches keep normal precedence.
- After `find`, reuse the package/library it reports in follow-up commands. Use explicit `--platform`, `--package`, or `--library` when the source matters; for multi-library packages, include the `--library` value shown by `find`.
- Always quote generic type names in shell commands: `type 'List<T>'`, `member 'Dictionary<TKey,TValue>'`, or `type 'INumber<TSelf>'`. Use `<T>` rather than `<>` for generic type queries.
- Wildcards are supported for type names and section/schema selection; quote shell patterns, such as `type 'Json*' --package System.Text.Json`, `-S "Async*"`, or `-D "SourceLink*"`.
- `type` uses `-t` for type filters; `member` uses `-m` for member filters. Dotted member syntax works: `-m JsonSerializer.Deserialize`.
- Member `Signature` values are single-line C# declarations and may include high-signal attributes such as `[Obsolete]`.
- Diff ranges use `..`: `--package Foo@1.0.0..2.0.0`. Obsolete members are shown by default; use `--all` for non-public, hidden, and extra members.
- Unpinned packages use the latest stable by default; add `--preview` when prerelease APIs matter.
- If a command behaves unexpectedly, rerun it with `--trace-mermaid` and include the stderr Mermaid request trace in bug reports.

## API lookup workflow

Use `find` when you do not know the package, library, or exact namespace.

```bash
dnx dotnet-inspect -y -- find JsonSerializer
dnx dotnet-inspect -y -- type System.Text
dnx dotnet-inspect -y -- type JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- depends Stream
dnx dotnet-inspect -y -- extensions HttpClient --reachable
dnx dotnet-inspect -y -- implements IJsonTypeInfoResolver --package System.Text.Json
```

Default type output is a compact type shape with inheritance, interfaces, logical member groups, and overload counts. For single-type output, `-v:n` and `-v:d` grow the tree to show overload leaves; use `--markdown -v:q` for compact Markdown section output. Narrow member-name views render overload rows with full signatures. Relationship scopes include installed platform libraries by default, `--package Foo`, curated `--aspnetcore`/`--extensions`, and `--project ./App.csproj`. The `extensions` command reports extension methods and C# extension properties. Add `--mermaid` to `depends` when a diagram is more useful than a table.

For overload selection, start with `-S "Member Index"`:

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S Signature
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize~1dc14dd1fb -S Signature
```

`Member Index` is terse and selector-only: `Selector` is the interactive `Name:N` form, `Stable` is the durable `Name~digest` form, and `Canonical Signature` is the exact printed string used to compute the digest. Prefer `Stable` in notes, scripts, issues, and agent handoffs; use `Name:N` for immediate drill-in after reading the same index. `--show-index` is a compatibility alias for `-S "Member Index"`. The digest contract is documented in `docs/design/member-index.md`.

## Upgrade and compatibility workflow

Start with `diff`, then inspect the affected API.

```bash
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0 --additive
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json@10.0.0
```

Use `--breaking` for migration work, `--additive` for release-note work, and `-t TypeName` to narrow noisy diffs. For .NET platform APIs, compare individual framework libraries:

```bash
dnx dotnet-inspect -y -- diff --platform System.Runtime@9.0.0..10.0.0 --additive
```

## Source and implementation workflow

Use `source` for SourceLink URLs, source text, or token/IL-offset mapping. Use `member Type Member:N -S @Source` when you want source and IL evidence for a selected overload: `Decompiled Source` (best-effort raised C# without IL comments), `Annotated Source` (the same raised C# with hidden-fact comments and IL interleaved beneath each statement), `Original Source` (SourceLink-backed source text when available), and `Recovered IL` (raw IL). Use `-S Calls` for direct call-site evidence, `-S Callers` for reverse edges (defaults to the member's own assembly, widen with `--bin <dir>` / `--project <proj>` / `--caller-package <pkg>`), `-S "Call Graph"` for the bounded outbound call tree, `-S "Unsafe*"` for unsafe API-member and operation evidence, or `-S "Facts"` for hidden facts as a structured table (`--tsv` for agents).

```bash
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S @Source
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S Calls
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S Callers
dnx dotnet-inspect -y -- member string IndexOf:7 -S Callers --caller-package System.Text.Json@9.0.0 --tfm net9.0
dnx dotnet-inspect -y -- member MyApi.Helper Run:1 --library MyLib.dll --bin ./app/bin/Release/net10.0
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S "Call Graph"
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
```

A selected overload defaults to `Signature`; use bare `-S` for `Signature` plus `Decompiled Source`, or select `@Source`, `Annotated Source`, `Original Source`, or `Recovered IL` when you need specific implementation evidence. The code views are `Decompiled Source` (raised C# without IL comments), `Annotated Source` (raised C# with hidden-fact comments and interleaved IL), `Recovered IL` (raw IL), and `Original Source` (SourceLink-backed original C#).

To read a whole type instead of one member, use `type Name -S "Decompiled Source"`: it renders the entire type as one C# listing — declaration, fields (including non-public, for context), and every member body. Add `--raw` to print only the bare listing (no headings or code fences), suitable for redirecting to a file:

```text
dnx dotnet-inspect -y -- type Stack --platform System.Collections -S "Decompiled Source" --raw > Stack.cs
```

Fidelity expectations: `Original Source` is SourceLink-backed original source when available. `Decompiled Source` is a best-effort readable C# reconstruction from IL, idiomatically raised, that helps explain intent; it may use PDB local names but is not guaranteed to match original syntax or compiler transformations. `Annotated Source` and `Recovered IL` are the highest-fidelity displays for exact opcodes, offsets, branches, tokens, and member calls; use them to confirm behavior when precision matters.

For crash/stack diagnostics that include a MethodDef token plus IL offset, `source --il-offset 0x06000001+0x5` can map the offset to source. This is a niche deep-debugging path; do not start there for normal API lookup.

## Unsafe call audit workflow

Start with the library/type roll-up, then drill into a selected overload for exact evidence.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
dnx dotnet-inspect -y -- member MyType MyMethod:1 --library MyLib.dll -S "Unsafe Operations,Recovered IL"
dnx dotnet-inspect -y -- member MyType MyMethod:1 --library MyLib.dll -S "Calls,Recovered IL"
```

At library/type scope, `@Audit` surfaces unsafe members, P/Invoke, and switch evidence. On a selected member, choose exact evidence sections such as `Unsafe Operations`, `Calls`, `Callers`, and `Recovered IL`; use the IL offsets and metadata tokens to confirm the exact binary evidence.

## Package, library, integrations, and Signals workflow

Use `package` for NuGet package structure and registry-backed signals. Use `library` for assembly metadata, APIs, PDB/SourceLink evidence, direct references, and unsafe-member audits.

```bash
dnx dotnet-inspect -y -- package System.Text.Json -S Signals
dnx dotnet-inspect -y -- package System.Text.Json -S "Library Files"
dnx dotnet-inspect -y -- package System.Text.Json -S "Package README"
dnx dotnet-inspect -y -- package System.Text.Json -S "Markdown Files"
dnx dotnet-inspect -y -- package System.Text.Json --path "*.md" --jsonl
dnx dotnet-inspect -y -- package System.Text.Json --path @readme --content --frontmatter
dnx dotnet-inspect -y -- package Markout Polly --path @agents --path @readme --match first --content --jsonl
dnx dotnet-inspect -y -- project ./App --agents-index --jsonl
dnx dotnet-inspect -y -- project ./App --readme Markout
dnx dotnet-inspect -y -- package Aspire.Azure.AI.OpenAI --library -S @Integrations
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S Switches
dnx dotnet-inspect -y -- library System.Diagnostics.DiagnosticSource -S OpenTelemetry
```

`Signals` reports observations, not a safety or trust verdict. Library Signals include SourceLink presence, SourceLink availability, determinism, trim/AOT markers, async kind (`Runtime`, `State machine`, `Mixed`, or `None`), memory-safety metadata, unsafe/PInvoke observations, and direct references. Package Signals include TFMs, manifest, README and agent documentation, license, dependencies, package signature, local provenance, vulnerabilities, package age, dependency vulnerability/deprecation counts, and dependency age.

Use `package Foo --library` to inspect the package's primary DLL when it is unambiguous; add a DLL name when a package contains multiple libraries. Use `package Foo --all-libraries` when a package contains multiple relevant DLLs or a tool package carries libraries under `tools/`; aggregate Markdown sections such as `@Integrations` include library provenance when needed. For row modes such as `--tsv`/`--jsonl`, select one concrete section such as `Integrations` or `OpenTelemetry`, not a category like `@Integrations`. Use `-S Integrations` for the ecosystem roll-up, `-S @Integrations` for roll-up plus focused sections, or a focused section such as `OpenTelemetry`. Integration sections cover AI, ASP.NET Core, Aspire resources, Authentication, Configuration, Dependency Injection, Logging, Options, Hosting, Health Checks, HTTP Client, OpenAPI, and OpenTelemetry. Focused sections list package-owned starter APIs, support types, and telemetry controls, not raw assembly references.

Use `-S Switches` when runtime feature switches or compatibility switches may affect behavior.

Package file sections share one sparse-free schema: `Path` and uncompressed byte `Size`. `Library Files` shows files under `lib/`; `Package README` returns the best README candidate (`AGENTS.md` > `README.md` > `PACKAGE.md` > declared readme); `Markdown Files` shows all `.md` files at full package depth; explicit `Files` shows all package files at full depth. `--path` scopes the same file-resolution primitive: `/` lists root files only, `"lib/net8.0/"` a directory's immediate children, `"*.md"` globs across the package, `README.md` a single file, `@readme` the best README candidate, and `@agents` a root `AGENTS.md`. Repeat `--path` or separate selectors with commas/semicolons; `--match all` returns every hit and `--match first` uses selectors as an ordered fallback. Multi-package file rows add `package` and `version`; JSON/JSONL keep `size` numeric; empty rows are preserved unless `--skip-empty`.

Add `--content` to print selected file bodies instead of path rows. Default content output uses machine-splittable separator blocks; `--jsonl` emits one row per file with `package`, `version`, `path`, `found`, and `content`. Use `--frontmatter`/`--yaml-header` or `--body` with `--content` or `--readme` to scope markdown output.

For project-scoped grounding, run `project ./App --agents-index` after restore. It resolves direct package versions from `project.assets.json` and emits one compact row per direct dependency; `name` and `description` are populated from root `AGENTS.md` frontmatter when present. Fetch the full best package doc only when needed with `project ./App --readme PackageId`; the package version comes from the project.

`library X -S Signals` resolves SourceLink by acquiring a missing PDB. Per-source-file reachability is opt-in: add `-S "SourceLink Availability"` and `-S "SourceLink Missing Files"` for HTTP HEAD checks, or `-S "SourceLink Integrity"` to download source files and compare checksums. For .NET tool packages, inspect the tool DLL through the package context, for example `library dotnet-inspect.dll --package dotnet-inspect@<version> -S "SourceLink Integrity"`. Tool v2 pointer/RID packages resolve to their inspectable framework-dependent payload.

For BCL/runtime-pack assemblies that are misleading as standalone packages, prefer `library --platform Lib --version <version>` or a direct DLL path.
