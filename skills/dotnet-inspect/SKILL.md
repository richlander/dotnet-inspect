---
name: dotnet-inspect
version: 0.10.5
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
| Inspect a library or assembly | `library Foo` or `library path/to.dll` | Add `--platform`, `--package`, `-S Signals` when source matters, or `-S Integrations` when ecosystem support matters. |
| Inspect a type | `type Type --package Foo` | Add `--all` for non-public, hidden, and extra members. |
| Inspect members and overloads | `member Type --package Foo -m Name --show-index` | Use `Name:N` selectors for a specific overload. |
| Compare API versions | `diff --package Foo@old..new --breaking` | Use `--additive` for new APIs or `-t Type` to narrow. |
| Locate source or implementation | `source Type --package Foo` | For a selected overload use `member Type Member:1 -S "Original Source"`, `-S Calls`, `-S Callers`, `-S "Call Graph"`, or `-S IL`. |
| Audit unsafe calls | `library MyLib.dll -S @Audit` | Drill into a selected member with `member Type Method:N --library MyLib.dll -S @Audit`. |
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
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize -D Methods --tsv
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize -S Methods --columns "Name;Signature"
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --rows -n 10
```

`@` represents a category or grouping of sections. Bare `-S` renders `@Default`, a curated high-density view; `-S @All` renders an exhaustive document with all sections. Workflow categories such as `@Audit`, plus focused categories such as `@Integrations` and `@Switches`, expand to related sections. Sections marked opt-in must be selected explicitly with `-S`. Focused library/member `-S Section` output keeps a compact context row before the selected section.

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

## API lookup workflow

Use `find` when you do not know the package, library, or exact namespace.

```bash
dnx dotnet-inspect -y -- find JsonSerializer
dnx dotnet-inspect -y -- type System.Text
dnx dotnet-inspect -y -- type JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize --show-index
dnx dotnet-inspect -y -- depends Stream
dnx dotnet-inspect -y -- extensions HttpClient --reachable
dnx dotnet-inspect -y -- implements IJsonTypeInfoResolver --package System.Text.Json
```

Default type output is a compact type shape with inheritance, interfaces, logical member groups, and overload counts. For single-type output, `-v:n` and `-v:d` grow the tree to show overload leaves; use `--markdown -v:q` for compact Markdown section output. Narrow member-name views render overload rows with full signatures and stable `Name:N` selectors. Relationship scopes include installed platform libraries by default, `--package Foo`, curated `--aspnetcore`/`--extensions`, and `--project ./App.csproj`. The `extensions` command reports extension methods and C# extension properties. Add `--mermaid` to `depends` when a diagram is more useful than a table.

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

Use `source` for SourceLink URLs, source text, or token/IL-offset mapping. Use `member Type Member:N -S "Decompiled Source"` when you need a selected member's lowered C# body, `-S "Original Source"` for SourceLink-backed source text, `-S Calls` for direct call-site evidence, `-S Callers` for the reverse edges (methods in the assembly that call the selected member), `-S "Call Graph"` for the bounded outbound call tree (callees, depth/node-capped, in-assembly), `-S "Unsafe*"` for unsafe API-member and operation evidence, or `-S IL` / `-S "IL (Annotated)"` for IL.

```bash
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S "Decompiled Source"
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S Calls
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S Callers
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S "Call Graph"
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
```

A selected overload defaults to `Signature`; use bare `-S` for `Signature` plus `Decompiled Source`, or select `Original Source`, `IL`, or `IL (Annotated)` when you need specific implementation evidence.

To read a whole type instead of one member, use `type Name -S "Decompiled Source"`: it renders the entire type as one C# listing — declaration, fields (including non-public, for context), and every member body. Add `--raw` to print only the bare listing (no headings or code fences), suitable for redirecting to a file:

```text
dnx dotnet-inspect -y -- type Stack --platform System.Collections -S "Decompiled Source" --raw > Stack.cs
```

Fidelity expectations: `Original Source` is the SourceLink-backed original source when available. `Decompiled Source` is lowered C#, a best-effort readable reconstruction from IL that helps explain intent; it uses PDB debug information such as local names when available, but is not guaranteed to match original syntax or compiler transformations. Raw IL and annotated IL are the highest-fidelity displays for exact opcodes, offsets, branches, tokens, and member calls; use them to confirm behavior when precision matters.

For crash/stack diagnostics that include a MethodDef token plus IL offset, `source --il-offset 0x06000001+0x5` can map the offset to source. This is a niche deep-debugging path; do not start there for normal API lookup.

## Unsafe call audit workflow

Start with the library/type roll-up, then drill into a selected overload for exact evidence.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S @Audit
dnx dotnet-inspect -y -- member MyType MyMethod:1 --library MyLib.dll -S @Audit
dnx dotnet-inspect -y -- member MyType MyMethod:1 --library MyLib.dll -S "Calls,IL"
```

At library/type scope, `@Audit` surfaces unsafe members, P/Invoke, and switch evidence. On a selected member, `@Audit` expands to signature, direct calls, unsafe operations, and IL; use the `IL` offsets and metadata tokens to confirm the exact binary evidence.

## Package, library, integrations, and Signals workflow

Use `package` for NuGet package structure and registry-backed signals. Use `library` for assembly metadata, APIs, PDB/SourceLink evidence, direct references, and unsafe-member audits.

```bash
dnx dotnet-inspect -y -- package System.Text.Json -S Signals
dnx dotnet-inspect -y -- package System.Text.Json -S "Library Files"
dnx dotnet-inspect -y -- package Aspire.Azure.AI.OpenAI --library -S @Integrations
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S Switches
dnx dotnet-inspect -y -- library System.Diagnostics.DiagnosticSource -S OpenTelemetry
```

`Signals` reports observations, not a safety or trust verdict. Library Signals include SourceLink presence, SourceLink availability, determinism, trim/AOT markers, async kind (`Runtime`, `State machine`, `Mixed`, or `None`), memory-safety metadata, unsafe/PInvoke observations, and direct references. Package Signals include TFMs, manifest, readme/license, dependencies, package signature, local provenance, vulnerabilities, package age, dependency vulnerability/deprecation counts, and dependency age.

Use `package Foo --library` to inspect the package's primary DLL when it is unambiguous; add a DLL name when a package contains multiple libraries. Use `package Foo --all-libraries` when a package contains multiple relevant DLLs or a tool package carries libraries under `tools/`; aggregate Markdown sections such as `@Integrations` include library provenance when needed. For row modes such as `--tsv`/`--jsonl`, select one concrete section such as `Integrations` or `OpenTelemetry`, not a category like `@Integrations`. Use `-S Integrations` for the ecosystem roll-up, `-S @Integrations` for roll-up plus focused sections, or a focused section such as `OpenTelemetry`. Integration sections cover AI, ASP.NET Core, Aspire resources, Authentication, Configuration, Dependency Injection, Logging, Options, Hosting, Health Checks, HTTP Client, OpenAPI, and OpenTelemetry. Focused sections list package-owned starter APIs, support types, and telemetry controls, not raw assembly references.

Use `-S Switches` when runtime feature switches or compatibility switches may affect behavior.

`library X -S Signals` resolves SourceLink by acquiring a missing PDB. Per-source-file reachability is opt-in: add `-S "SourceLink Availability"` and `-S "SourceLink Missing Files"` for HTTP HEAD checks, or `-S "SourceLink Integrity"` to download source files and compare checksums. For .NET tool packages, inspect the tool DLL through the package context, for example `library dotnet-inspect.dll --package dotnet-inspect@<version> -S "SourceLink Integrity"`. Tool v2 pointer/RID packages resolve to their inspectable framework-dependent payload.

For BCL/runtime-pack assemblies that are misleading as standalone packages, prefer `library --platform Lib --version <version>` or a direct DLL path.
