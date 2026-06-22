---
name: dotnet-inspect
version: 0.12.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, SourceLink/source, and API version diffs.
---

# dotnet-inspect

Use dotnet-inspect when you need evidence instead of guesses for .NET packages, platform libraries, local assemblies, APIs, dependencies, SourceLink/source, or version-to-version API changes.

```bash
dnx dotnet-inspect -y -- <command>
```

## Common starts

| Goal | Command |
| ---- | ------- |
| Find an API | `find Pattern`, then reuse the reported `--platform`, `--package`, or `--library`. |
| Inspect overloads | `member Type --platform Lib -m Name -S "Member Index"` |
| Select an overload | `member Type --platform Lib Name:1` or `Name~digest` |
| Source/implementation | `member Type Name:1 -S @Source`, `-S Calls`, `-S Callers`, `-S IL` |
| Inspect a type | `type Type --package Foo`; add `--all` for non-public/hidden/extra members. |
| Compare APIs | `diff --package Foo@old..new --breaking`; use `--additive` for new APIs. |
| Inspect packages | `package Foo -S Signals`, `-S "Library Files"`, `--library` |
| Inspect libraries | `library Foo` or `library path/to.dll`; add `--platform`, `--package`, `-S Signals`. |
| Relationships | `depends Type`, `extensions Type`, `implements Interface`; add package/platform/project scope. |

## Member lookup workflow

Member lookup is a common flow. Use `find` when scope is unknown, then inspect the type, then use `Member Index` to find the overload to select. The bare router also accepts source-qualified member syntax when the source is obvious.

```bash
dnx dotnet-inspect -y -- find JsonSerializer
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- System.Text.Json.JsonSerializer.Serialize:1 -S Signature
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S Signature
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize~1dc14dd1fb -S @Source
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S Calls
dnx dotnet-inspect -y -- member string IndexOf:7 -S Callers --caller-package System.Text.Json@9.0.0 --tfm net9.0
```

Selector syntax: first run `member Type --platform Lib -m Name -S "Member Index"` (or the package/library source `find` reported). Then pass either `Name:N` (1-based, for the current index) or `Name~digest` (stable, from the `Stable` column) as the positional member selector. `Canonical Signature` is the printed digest input. Prefer `Name~digest` in notes, scripts, issues, and handoffs; use `Name:N` for immediate drill-in. `--show-index` is an alias for `-S "Member Index"`.

A selected overload defaults to `Signature`; bare `-S` adds `Decompiled Source`. Use `-S @Source` for source and IL evidence: `Decompiled Source` (raised C#), `Annotated Source` (C# with hidden-fact comments and interleaved IL), `Original Source`, and `IL`. Use `Annotated Source` or `IL` when exact opcodes, offsets, branches, tokens, or calls matter.

Use `-S Calls` for direct call-site evidence, `-S Callers` for reverse edges (widen with `--bin`, `--project`, or `--caller-package`), `-S "Call Graph"` for a bounded outbound tree, `-S "Unsafe Operations"` for unsafe evidence, and `-S Facts --tsv` for structured hidden facts.

## Query and output

Default output is Markdown. Use `--table` for compact aligned rows, `--tsv` for stable snake_case headers with no embedded tabs/newlines, `--jsonl` for one JSON object per row, `--json` for structured documents, and `--mermaid` for graph-shaped output.

Use `-D` to discover sections/columns, `-S Section` to select sections by name or wildcard, and `--columns`/`--fields` to project values. Discover first instead of guessing names.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
```

`@` names a category: `-S @All`, `-S @Source`, `-S @Audit`, `-S @Integrations`, `-S @Switches`. Row formats (`--tsv`/`--jsonl`/`--table`) work best with one concrete section, not a category.

## Limits

Prefer built-in limits to shell pipes. `-n N` and numeric shorthand like `-6` cap output lines; `--tail N` shows the end; `--rows` makes `-n` cap Markdown table data rows; `--count` counts rows in one selected table. Command-specific caps: `-t N` for type/find rows, `-m N` for members, and `--versions N` for package versions.

## Package docs, libraries, and signals

For agent-readable package docs, use `--path @readme --content`; the resolver exposes the best README content for agents, preferring `AGENTS.md` over `README.md` when present. For multi-package doc surveys, pass multiple package IDs with `--path @readme --jsonl` for metadata rows or `--path @readme --content --jsonl` for content rows. Add `--frontmatter`/`--yaml-header` or `--body` with `--content`; keep `--readme` for single-package reads.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI -S Signals
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --path @readme --content --frontmatter
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI Microsoft.Extensions.AI.OpenAI --path @readme --content --jsonl
dnx dotnet-inspect -y -- project ./App --agents-index --jsonl
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --library -S @Integrations
dnx dotnet-inspect -y -- library System.Text.Json -S Switches
```

Use `package Foo --library` for the primary DLL when unambiguous; add a DLL name or use `--all-libraries` for multi-library packages. `Signals` reports observations, not trust: SourceLink, determinism, trim/AOT, memory-safety metadata, unsafe/PInvoke, references, TFMs, manifest/docs, license, vulnerabilities, package age, and dependency risk.

## Other workflows

Use `diff --package Foo@old..new --breaking` for migration work, `--additive` for release-note work, and `-t Type` to narrow. For platform APIs, compare individual libraries: `diff --platform System.Runtime@9.0.0..10.0.0 --additive`.

For unsafe audits, start with `library MyLib.dll -S @Audit`, then drill into `member Type Method:1 --library MyLib.dll -S "Unsafe Operations,IL"`.

Use `type Name -S "Decompiled Source" --raw` for a whole-type C# listing. Use `source --il-offset 0x06000001+0x5` for crash diagnostics with MethodDef token plus IL offset. If decompiled output looks wrong, capture `Decompiled Source`, `Annotated Source`, `Original Source`, and `IL`; maintainers diagnose pipeline state with DecompilerHarness.

## General tips

- Built-in aliases and common BCL types resolve without scope: `type string`, `type 'List<T>'`.
- Quote generic type names and shell patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- After `find`, reuse the package/library it reports; add `--library` for multi-library packages.
- `type` uses `-t` for type filters; `member` uses `-m` for member filters.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.
- If behavior is surprising, rerun with `--trace-mermaid` and include stderr in bug reports.
