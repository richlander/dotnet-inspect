---
name: dotnet-inspect
version: 0.14.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, and API version diffs. Router to focused source and performance skills.
---

# dotnet-inspect

Use dotnet-inspect when you need evidence instead of guesses for .NET packages,
platform libraries, local assemblies, APIs, dependencies, or version-to-version
API changes.

```bash
dnx dotnet-inspect -y -- <command>
```

## Skills

This is the base skill: the everyday lookup, query, and output workflows. Deeper
topics live in focused skills you can print on demand:

| Skill | Print it with | Use it for |
| ----- | ------------- | ---------- |
| source | `dotnet-inspect skill source` | Decompiled C#, IL, `@Source`/`Annotated Source`, SourceLink original source, unsafe/IL audits. |
| performance | `dotnet-inspect skill performance` | Whole-assembly call-graph leverage ranking and performance triage (experimental). |

Run `dotnet-inspect skill list` to list available skills. Load a focused skill
only when the task needs it; keep this base skill for routine lookups.

## Common starts

| Goal | Command |
| ---- | ------- |
| Find an API | `find Pattern`, then reuse the reported `--platform`, `--package`, or `--library`. |
| Inspect overloads | `member Type --platform Lib -m Name -S "Member Index"` |
| Select an overload | `member Type --platform Lib Name:1` or `Name~digest` |
| Inspect a type | `type Type --package Foo`; add `--all` for non-public/hidden/extra members. |
| Compare APIs | `diff --package Foo@old..new --breaking`; use `--additive` for new APIs. |
| Inspect packages | `package Foo -S Signals`, `-S "Library Files"`, `--library` |
| Inspect libraries | `library Foo` or `library path/to.dll`; add `--platform`, `--package`, `-S Signals`. |
| Relationships | `depends Type`, `extensions Type`, `implements Interface`; add package/platform/project scope. |
| Source or IL | see `dotnet-inspect skill source`. |

## Member lookup workflow

Member lookup is a common flow. Use `find` when scope is unknown, then inspect
the type, then use `Member Index` to find the overload to select. The bare router
also accepts source-qualified member syntax when the source is obvious.

```bash
dnx dotnet-inspect -y -- find JsonSerializer
dnx dotnet-inspect -y -- type JsonSerializer --platform System.Text.Json
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- System.Text.Json.JsonSerializer.Serialize:1 -S Signature
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json Serialize:1 -S Signature
```

Selector syntax: first run `member Type --platform Lib -m Name -S "Member Index"`
(or the package/library source `find` reported). Then pass either `Name:N`
(1-based, for the current index) or `Name~digest` (stable, from the `Stable`
column) as the positional member selector. `Canonical Signature` is the printed
digest input. Prefer `Name~digest` in notes, scripts, issues, and handoffs; use
`Name:N` for immediate drill-in. `--show-index` is an alias for
`-S "Member Index"`.

A selected overload defaults to `Signature`. For decompiled source, IL,
annotated source, and SourceLink evidence (`-S @Source`, `-S "Source
Locations"`, `Annotated Source`, `IL`), use `dotnet-inspect skill source`.

## Query and output

Default output is Markdown. Use `--table` for compact aligned rows, `--tsv` for
stable snake_case headers with no embedded tabs/newlines, `--jsonl` for one JSON
object per row, `--json` for structured documents, `--bare` for one undecorated
payload or URL list, `--count` for a bare row count, and `--mermaid` for
graph-shaped output.

Use `-D` to discover sections/columns, `-S Section` to select sections by name or
wildcard, and `--columns`/`--fields` to project values. Discover first instead of
guessing names.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
```

`@` names a category: `-S @All`, `-S @Source`, `-S @Audit`, `-S @Integrations`,
`-S @Switches`. Row formats (`--tsv`/`--jsonl`/`--table`) work best with one
concrete section, not a category.

## Limits

Prefer built-in limits to shell pipes. `-n N` and numeric shorthand like `-6`
cap output lines; `--tail N` shows the end; `--rows` makes `-n` cap Markdown
table data rows; `--count` counts rows in one selected table. Command-specific
caps: `-t N` for type/find rows, `-m N` for members, and `--versions N` for
package versions.

## Package docs, libraries, and signals

For agent-readable package docs, use `--path @readme --content`; the resolver
exposes the best README content for agents, preferring `AGENTS.md` over
`README.md` when present. For multi-package doc surveys, pass multiple package
IDs with `--path @readme --jsonl` for metadata rows or `--path @readme --content
--jsonl` for content rows. Add `--frontmatter`/`--yaml-header` or `--body` with
`--content`; keep `--readme` for single-package reads.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI -S Signals
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --path @readme --content --frontmatter
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI Microsoft.Extensions.AI.OpenAI --path @readme --content --jsonl
dnx dotnet-inspect -y -- project ./App --agents-index --jsonl
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --library -S @Integrations
```

Use `package Foo --library` for the primary DLL when unambiguous; add a DLL name
or use `--all-libraries` for multi-library packages. `Signals` reports
observations, not trust: SourceLink, determinism, trim/AOT, memory-safety
metadata, unsafe/PInvoke, references, TFMs, manifest/docs, license,
vulnerabilities, package age, and dependency risk.

## Other workflows

Use `diff --package Foo@old..new --breaking` for migration work, `--additive`
for release-note work, and `-t Type` to narrow. For platform APIs, compare
individual libraries: `diff --platform System.Runtime@9.0.0..10.0.0 --additive`.

Use `--bare` to extract one undecorated payload: `package Foo --readme --bare` or
`type Name -S Signature --bare`. For decompiled source, IL, SourceLink, and
unsafe audits, load `dotnet-inspect skill source`. For call-graph leverage and
performance triage, load `dotnet-inspect skill performance`.

## General tips

- Built-in aliases and common BCL types resolve without scope: `type string`, `type 'List<T>'`.
- Quote generic type names and shell patterns: `member 'Dictionary<TKey,TValue>'`, `-S "Async*"`.
- After `find`, reuse the package/library it reports; add `--library` for multi-library packages.
- `type` uses `-t` for type filters; `member` uses `-m` for member filters.
- Unpinned packages use latest stable; add `--preview` for prerelease APIs.
- If behavior is surprising, rerun with `--trace-mermaid` and include stderr in bug reports.
