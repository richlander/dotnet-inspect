---
name: dotnet-inspect
version: 0.10.0
description: Find evidence instead of guessing for .NET packages, platform libraries, local assemblies, APIs, dependencies, SourceLink/symbol provenance, and version-to-version API changes.
---

# dotnet-inspect

Use dotnet-inspect when you need evidence instead of guesses for .NET packages, platform libraries, local assemblies, APIs, dependencies, SourceLink/symbol provenance, or version-to-version API changes.

Invoke with `dnx`:

```bash
dnx dotnet-inspect -y -- <command>
```

Default output is Markdown. Use Markdown for readable evidence with headings, section boundaries, table headers, and code fences that are easy to quote. Use `--table` for compact pretty-printed rows, and `--tsv` for normalized tab-separated rows when agents or shell tools need stable field splitting. Use `--json` for structured automation. Verbosity controls document breadth: default views stay compact, bare `-S` gives a curated high-density view, and `-v:n`/`-v:d` expand fuller section detail such as overload signatures and docs. For selected overload implementation bodies, use `-S "Decompiled Source"`, `-S "Original Source"`, `-S IL`, or `-S "IL (Annotated)"`. Markdown and JSON can represent multi-section documents. Table and TSV are single-table formats; when a query matches multiple sections, select one with `-S` or use Markdown/JSON.

Format promises:

- Markdown table cell values do not contain escaped pipes (`\|`); pipe characters in values are normalized.
- `--tsv` table headers are stable snake_case keys, and cells never contain embedded tabs or newlines.
- `--table` renders the same projection as `--tsv`, with each column starting at a uniform position across rows.

Start with the default Markdown view for readable evidence, or bare `-S` for the curated high-density view. Use the query system when you need to drill into specific detail that those views do not expose: `-D` discovers sections/columns; `-S Section` selects sections by name or wildcard, such as `-S "Async*"`; `--columns` and `--fields` project values. This query system serves a similar role to Go templates, but you discover the available shape first instead of guessing field names.

Use built-in limiters before shell pipes. `-n N` and numeric shorthand like `-6` work like `head`; `--tail N` works like `tail`; add `--rows` to make head counts cap Markdown table data rows instead of output lines, for example `--rows -n 10` or `--rows -10`. Use `--count` to count rows in one selected table section. Command-specific limiters also matter: `-t N` limits type/find results, `-m N` limits member results, and `--versions N` limits package version lists.

The query system, output modes, and limiters compose as independent axes: first choose a shape with `-D`, `-S`, `--columns`, or `--fields`; then choose a renderer with Markdown, `--table`, `--tsv`, or `--json`; then bound the result with `-n`, `--tail`, `--rows`, or `--count`. Markdown and JSON can carry multiple sections; `--table` and `--tsv` are single-table renderers, so pair them with one selected section. For example, `-D Section --tsv` returns the section schema as stable tab-separated rows, while `-S Section --columns Name,Signature --rows -10` renders the same projected shape as a bounded Markdown table.

## Workflow map

| Goal | Start with | Drill in |
| ---- | ---------- | -------- |
| Find the right API | `find Pattern --table` | `type Type --package Foo`, then `member Type --package Foo`. |
| Fix upgrade breaks | `diff --package Foo@old..new --breaking` | Inspect replacement members with `member`. |
| Learn what changed | `diff --package Foo@old..new --additive` or `diff --platform Lib@old..new` | Use `-t Type` to narrow. |
| Locate source | `source Type --package Foo` | Add `-m Member`; for a selected overload use `member Type Member:1 -S "Original Source"` when you need SourceLink-backed source text. |
| Inspect package/library signals | `library Foo -S Signals` or `package Foo -S Signals` | `Signals` resolves SourceLink for libraries; add `-S "SourceLink Availability"` for source reachability or `-S "SourceLink Integrity"` for slow content verification. |
| Inventory package library files | `package Foo -S "Library Files"` | Lists all files under `lib/` across TFMs; use paths from this section with `library <file> --package Foo` for specific assemblies. |
| Explore relationships | `depends Type`, `extensions Type`, `implements Interface` | Add package/platform scope as needed. |
| Keep output small | `--table`, `--tsv`, `--json`, `-S Section`, `--count`, `-n N`, `--tail N`, `--rows -n N` or `--rows -6` | Prefer built-in limits over shell pipes. `--rows` requires a head count and cannot combine with `--tail`. |

## Modern .NET and preview workflow

LLM training may miss .NET 10+ runtime/library features. Prefer metadata inspection over web search.

| Feature | Description | Use | Watch for |
| ------- | ----------- | --- | --------- |
| Runtime async | .NET 11+ libraries may use runtime async instead of compiler-generated state machines. | `library --platform Lib --version <version> -S "Async*"` | `Kind` distinguishes runtime async from state-machine async; use `--count` only for totals. |
| Runtime-pack assemblies | Many BCL libraries ship only as installed platform/runtime-pack assemblies, not standalone packages. | `library --platform Lib --version <version>` or direct DLL path | Prefer platform/direct DLL inspection when package lookup is misleading. |
| Memory-safety metadata | Newer compilers may stamp updated memory-safety rules and caller-unsafe members in metadata. | `library Lib --version <version> -S Signals` | Compare `MemorySafetyRules` v2+ with the `RequiresUnsafe` member count; unsafe signatures and P/Invoke remain separate signals. |
| Extension properties | C# extension blocks can expose properties in addition to extension methods. | `extensions Type --reachable` | Results include extension methods and C# extension properties. |
| Implementation detail | Compiler/runtime implementation can differ from API signatures. | `member Type Member:1 -S "Decompiled Source"` | `Decompiled Source` is lowered C# and works broadly; `Original Source` is SourceLink-backed source when available; IL/annotated IL show exact instructions. |

For preview sweeps, resolve the version once, prove one library end-to-end, then fan out to the rest. Unpinned packages use the latest stable by default; add `--preview` to include prerelease versions in latest resolution, including `package Foo --latest-version --preview` and `library <dll> --package Foo --preview`.

## API lookup workflow

Use `find` when you do not know the package, library, or exact namespace.

```bash
dnx dotnet-inspect -y -- find JsonSerializer --table
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json
```

Carry resolved context forward. Bare names use the router: platform-looking names are tried as installed platform libraries first, then fall back to NuGet packages if platform resolution fails. Use explicit `--platform`, `--package`, or `--library` when the source matters; for multi-library packages, include the `--library` value shown by `find`.

For full public signature or overload inventories, start with `type Type --package Foo --shape`; it gives the clean declaration shape with parameter names, nullable annotations, defaults, and generic parameters. Use `member Type --package Foo -m Name --show-index` when you need docs or stable `Name:N` overload selectors. A selected overload defaults to `Signature`; use bare `-S` for `Signature` plus `Decompiled Source`, or select `Original Source`, `IL`, or `IL (Annotated)` when you need that specific implementation evidence.

## Upgrade and compatibility workflow

Start with `diff`, then inspect the affected API.

```bash
dnx dotnet-inspect -y -- diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json@10.0.0
```

Use `--breaking` for migration work, `--additive` for release-note work, and `-t TypeName` to narrow noisy diffs. Obsolete members are shown by default with an obsolete marker/message when available.

For .NET platform APIs, compare individual framework libraries:

```bash
dnx dotnet-inspect -y -- diff --platform System.Runtime@9.0.0..10.0.0 --additive
```

## Source and implementation workflow

Use `source` for SourceLink URLs, source text, or token/IL-offset mapping. Use `member Type Member:N -S "Decompiled Source"` when you need a selected member's lowered C# body, `-S "Original Source"` for SourceLink-backed source text, or `-S IL` / `-S "IL (Annotated)"` for IL.

```bash
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json --table
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -S "Decompiled Source"
```

For crash/stack diagnostics that include a MethodDef token plus IL offset, `source --il-offset 0x06000001+0x5` can map the offset to source. This is a niche deep-debugging path; do not start there for normal API lookup.

Fidelity expectations: `Original Source` is the SourceLink-backed original source when available. `Decompiled Source` is lowered C#, a best-effort readable reconstruction from IL that helps explain intent; it uses PDB debug information such as local names when available, but is not guaranteed to match original syntax or compiler transformations. Raw IL and annotated IL are the highest-fidelity displays for exact opcodes, offsets, branches, tokens, and member calls; use them to confirm behavior when precision matters.

## Package and library Signals workflow

Use `Signals` for metadata and provenance observations. It reports observations, not a safety or trust verdict. Cost follows verbosity and explicit selection: `library X -S Signals` reports metadata plus the shared Signals section (acquiring a missing library PDB to resolve SourceLink); add `SourceLink Availability` and `SourceLink Missing Files` for the per-source-file reachability pass. The exhaustive content check is the opt-in `SourceLink Integrity` section.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S "Signals,SourceLink Availability,SourceLink Missing Files"
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink Integrity"
dnx dotnet-inspect -y -- package System.Text.Json -S Signals
```

For .NET tool packages, inspect the tool DLL through the package context, for example `library dotnet-inspect.dll --package dotnet-inspect@<version> -S "SourceLink Integrity"`. Tool v2 pointer/RID packages resolve to their inspectable framework-dependent payload.

Library Signals include assembly metadata such as SourceLink presence, SourceLink availability, determinism, trim/AOT markers, async kind (`Runtime`, `State machine`, `Mixed`, or `None`), updated memory-safety model, `RequiresUnsafe` member count, unsafe signatures, P/Invoke, and direct references. Package Signals use the same shape for package metadata/assets, dependencies, signature provenance, and NuGet registry observations. `library X -S Signals` resolves SourceLink by acquiring a missing PDB. The per-source-file reachability pass — SourceLink Availability and SourceLink Missing Files, which issue one HTTP HEAD per tracked source URL — is opt-in: select it explicitly with `-S "SourceLink Availability"`. It does not run in a plain `library X -v:d` flow because its cost scales with source-file count. To verify source *content*, select `library X -S "SourceLink Integrity"`: it downloads each tracked source file and compares its hash to the PDB checksum, exits non-zero on true content mismatch, and reports `CR/LF Mismatch` when checksums match after line-ending normalization.

Package Signals include TFMs, manifest, readme/license, direct dependencies, package signature, local provenance, and registry-backed signals such as vulnerabilities, package age, dependency vulnerability/deprecation counts, and dependency age. Symbol/SourceLink package evidence names the PDB source (`embedded`, `in-package`, `.snupkg`, `msdl.microsoft.com`). Custom feeds (`--nuget-source`, `--add-source`, `--nugetconfig`) and local `.nupkg` files are supported.

For package structure, use `package X -S Manifest` to see manifest version/package/tool rows, `package X -S "Library Files"` to list all files under `lib/` across TFMs, and `package X -S All` to include opt-in sections such as Signals.

## Output and query workflow

Discover sections, then select or project fields. Use `--tsv` for discovery when another tool or agent will consume the schema; the output is small, but the delimiter and stable keys prevent ambiguity.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D Methods --tsv
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -S Methods --columns "Name;Signature;Obsolete"
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --rows -n 10
```

For target-based queries, `-D` reports the effective schema by default: only sections and columns that can render for that query. Add `--schema` for the static schema. Bare `-S` renders a curated high-density view (`Package Info`/`Library Files`, `Library Info` with counts, compact type/member summaries, or selected-overload `Signature`/`Decompiled Source`). Minimal/default and bare `-S` views favor summaries, counts, and one row per logical item; use named sections or `-S All` for long lists. `-S`, `--columns`, and `--fields` accept comma-separated or semicolon-separated lists. In section output, `section (opt-in)` means the section never runs from normal verbosity or `-v:d`; select it explicitly with `-S` when needed. Focused library/member `-S Section` output keeps a compact context row before the selected section. `-S All` produces an exhaustive document: default section first, remaining sections alphabetically, no compact context row.

`-n N` and shorthand values like `-6` normally limit output lines. Add `--rows` to reinterpret that head count as data rows per rendered Markdown table; this preserves headings/table headers and applies independently to each table. `--rows` requires `-n/--head` or numeric shorthand and cannot be combined with `--tail`. Prefer `--rows` over shell `head` when you need parseable Markdown tables.

## Relationship workflow

```bash
dnx dotnet-inspect -y -- depends Stream
dnx dotnet-inspect -y -- extensions HttpClient --reachable
dnx dotnet-inspect -y -- implements IJsonTypeInfoResolver --package System.Text.Json
```

Scopes include installed platform libraries by default, `--package Foo`, curated `--aspnetcore`/`--extensions`, and `--project ./App.csproj`. Add `--mermaid` to `depends` when a diagram is more useful than a table.

## Syntax guardrails

- Quote generic type names: `'Option<T>'`, `'INumber<TSelf>'`.
- Use `<T>` rather than `<>` for generic type queries.
- `type` uses `-t` for type filters; `member` uses `-m` for member filters.
- Dotted member syntax works: `-m JsonSerializer.Deserialize`.
- Diff ranges use `..`: `--package Foo@1.0.0..2.0.0`.
- In API/member queries, use `--all` for non-public, hidden, and extra members; obsolete members are already shown by default.
