---
name: dotnet-inspect
version: 0.10.2
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
| Inspect a package | `package Foo` | Add `-S Signals`, `-S Manifest`, or `-S "Library Files"`. |
| Inspect a library or assembly | `library Foo` or `library path/to.dll` | Add `--platform`, `--package`, or `-S Signals` when source matters. |
| Inspect a type | `type Type --package Foo` | Add `--all` for non-public, hidden, and extra members. |
| Inspect members and overloads | `member Type --package Foo -m Name --show-index` | Use `Name:N` selectors for a specific overload. |
| Compare API versions | `diff --package Foo@old..new --breaking` | Use `--additive` for new APIs or `-t Type` to narrow. |
| Locate source or implementation | `source Type --package Foo` | For a selected overload use `member Type Member:1 -S "Original Source"` or `-S IL`. |
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

Bare `-S` renders a curated high-density view; `-S All` renders an exhaustive document. Sections marked opt-in must be selected explicitly with `-S`. Focused library/member `-S Section` output keeps a compact context row before the selected section.

## General tips

- Built-in aliases and common BCL types such as `string`, `int`, and `List<T>` resolve without `--package`, `--platform`, or `--library`; start with `type string` or `type 'List<T>'`.
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
dnx dotnet-inspect -y -- type JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -m Serialize --show-index
dnx dotnet-inspect -y -- depends Stream
dnx dotnet-inspect -y -- extensions HttpClient --reachable
dnx dotnet-inspect -y -- implements IJsonTypeInfoResolver --package System.Text.Json
```

Default type output is a compact type shape with inheritance, interfaces, logical member groups, and overload counts. Narrow member-name views render overload rows with full signatures and stable `Name:N` selectors. Relationship scopes include installed platform libraries by default, `--package Foo`, curated `--aspnetcore`/`--extensions`, and `--project ./App.csproj`. The `extensions` command reports extension methods and C# extension properties. Add `--mermaid` to `depends` when a diagram is more useful than a table.

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

Use `source` for SourceLink URLs, source text, or token/IL-offset mapping. Use `member Type Member:N -S "Decompiled Source"` when you need a selected member's lowered C# body, `-S "Original Source"` for SourceLink-backed source text, or `-S IL` / `-S "IL (Annotated)"` for IL.

```bash
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S "Decompiled Source"
```

A selected overload defaults to `Signature`; use bare `-S` for `Signature` plus `Decompiled Source`, or select `Original Source`, `IL`, or `IL (Annotated)` when you need specific implementation evidence.

Fidelity expectations: `Original Source` is the SourceLink-backed original source when available. `Decompiled Source` is lowered C#, a best-effort readable reconstruction from IL that helps explain intent; it uses PDB debug information such as local names when available, but is not guaranteed to match original syntax or compiler transformations. Raw IL and annotated IL are the highest-fidelity displays for exact opcodes, offsets, branches, tokens, and member calls; use them to confirm behavior when precision matters.

For crash/stack diagnostics that include a MethodDef token plus IL offset, `source --il-offset 0x06000001+0x5` can map the offset to source. This is a niche deep-debugging path; do not start there for normal API lookup.

## Package, library, and Signals workflow

Use `package` for NuGet package structure and registry-backed signals. Use `library` for assembly metadata, APIs, PDB/SourceLink evidence, and direct references.

```bash
dnx dotnet-inspect -y -- package System.Text.Json -S Signals
dnx dotnet-inspect -y -- package System.Text.Json -S "Library Files"
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
```

`Signals` reports observations, not a safety or trust verdict. Library Signals include SourceLink presence, SourceLink availability, determinism, trim/AOT markers, async kind (`Runtime`, `State machine`, `Mixed`, or `None`), memory-safety metadata, unsafe/PInvoke observations, and direct references. Package Signals include TFMs, manifest, readme/license, dependencies, package signature, local provenance, vulnerabilities, package age, dependency vulnerability/deprecation counts, and dependency age.

`library X -S Signals` resolves SourceLink by acquiring a missing PDB. Per-source-file reachability is opt-in: add `-S "SourceLink Availability"` and `-S "SourceLink Missing Files"` for HTTP HEAD checks, or `-S "SourceLink Integrity"` to download source files and compare checksums. For .NET tool packages, inspect the tool DLL through the package context, for example `library dotnet-inspect.dll --package dotnet-inspect@<version> -S "SourceLink Integrity"`. Tool v2 pointer/RID packages resolve to their inspectable framework-dependent payload.

For BCL/runtime-pack assemblies that are misleading as standalone packages, prefer `library --platform Lib --version <version>` or a direct DLL path.
