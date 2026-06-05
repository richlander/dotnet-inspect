---
name: dotnet-inspect
version: 0.9.1
description: Query .NET APIs across NuGet packages, platform libraries, and local files. Use for factual answers about package contents, API signatures, compatibility changes, relationships, SourceLink, and assembly metadata.
---

# dotnet-inspect

Use dotnet-inspect when you need evidence about .NET libraries instead of guessing: API signatures, package contents, extension methods, implementors, SourceLink URLs, dependencies, or version-to-version API changes.

Invoke through `dnx` unless the tool is already installed:

```bash
dnx dotnet-inspect -y -- <command>
```

Default output is Markdown. Use `--oneline` to scan, `--json` for structured data, `--count` to count one selected table section, and `-v:d` when you need source/decompiled C#/IL detail.

## Workflow map

| Goal | Start with | Drill in |
| ---- | ---------- | -------- |
| Find the right API | `find Pattern --oneline` | `type Type --package Foo`, then `member Type --package Foo`. |
| Fix upgrade breaks | `diff --package Foo@old..new --breaking` | Inspect replacement members with `member`. |
| Learn what changed | `diff --package Foo@old..new --additive` or `diff --platform Lib@old..new` | Use `-t Type` to narrow. |
| Locate source | `source Type --package Foo` | Add `-m Member` or use `member Type Member:1 -v:d`. |
| Inspect package/library signals | `library Foo -S Signals` or `package Foo -S Signals` | `Signals` resolves SourceLink for libraries; add `-S "SourceLink Availability"` for source reachability or `-S "SourceLink Integrity"` for slow content verification. |
| Inventory package library files | `package Foo -S "Library Files"` | Lists all files under `lib/` across TFMs; use paths from this section with `library <file> --package Foo` for specific assemblies. |
| Explore relationships | `depends Type`, `extensions Type`, `implements Interface` | Add package/platform scope as needed. |
| Keep output small | `--oneline`, `--json`, `-S Section`, `--count`, `-n N` | Prefer built-in limits over shell pipes. |

## Modern .NET and preview workflow

LLM training may miss .NET 10+ runtime/library features. Prefer metadata inspection over web search.

| Feature | Description | Use | Watch for |
| ------- | ----------- | --- | --------- |
| Runtime async | .NET 11+ libraries may use runtime async instead of compiler-generated state machines. | `library --platform Lib --version <version> -S "Async*"` | `Kind` distinguishes runtime async from state-machine async; use `--count` only for totals. |
| Runtime-pack assemblies | Many BCL libraries ship only as installed platform/runtime-pack assemblies, not standalone packages. | `library --platform Lib --version <version>` or direct DLL path | Prefer platform/direct DLL inspection when package lookup is misleading. |
| Memory-safety metadata | Newer compilers may stamp updated memory-safety rules and caller-unsafe members in metadata. | `library Lib --version <version> -S Signals` | Compare `MemorySafetyRules` v2+ with the `RequiresUnsafe` member count; unsafe signatures and P/Invoke remain separate signals. |
| Extension properties | C# extension blocks can expose properties in addition to extension methods. | `extensions Type --reachable` | Results include extension methods and C# extension properties. |
| Lowering changes | Compiler/runtime implementation can differ from API signatures. | `member Type Member:1 -v:d` | Inspect Source, Lowered C#, IL, and annotated IL before inferring behavior. |

For preview sweeps, resolve the version once, prove one library end-to-end, then fan out to the rest.

## API lookup workflow

Use `find` when you do not know the package, library, or exact namespace.

```bash
dnx dotnet-inspect -y -- find JsonSerializer --oneline
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json
```

Carry resolved context forward. Bare names use the router: platform-looking names are tried as installed platform libraries first, then fall back to NuGet packages if platform resolution fails. Use explicit `--platform`, `--package`, or `--library` when the source matters; for multi-library packages, include the `--library` value shown by `find`.

Use `type` for type shape and summaries; use `member` for signatures, overloads, docs, and implementation detail. Add `--show-index` when you need stable `Name:N` overload selectors.

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

Use `source` for SourceLink URLs or source text. Use `member -v:d` when you need a selected member's source, lowered C#, raw IL, or annotated IL.

```bash
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json --oneline
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -v:d
```

For crash/stack diagnostics that include a MethodDef token plus IL offset, `source --il-offset 0x06000001+0x5` can map the offset to source. This is a niche deep-debugging path; do not start there for normal API lookup.

## Package and library Signals workflow

Use `Signals` for metadata and provenance observations. It reports observations, not a safety or trust verdict. Cost follows verbosity and explicit selection: `library X -S Signals` reports metadata plus the shared Signals section (acquiring a missing library PDB to resolve SourceLink); add `SourceLink Availability` and `SourceLink Missing Files` for the per-source-file reachability pass. The exhaustive content check is the opt-in `SourceLink Integrity` section.

```bash
dnx dotnet-inspect -y -- library System.Text.Json -S Signals
dnx dotnet-inspect -y -- library System.Text.Json -S "Signals,SourceLink Availability,SourceLink Missing Files"
dnx dotnet-inspect -y -- library System.Text.Json -S "SourceLink Integrity"
dnx dotnet-inspect -y -- package System.Text.Json -S Signals
```

Library Signals include assembly metadata such as SourceLink presence, SourceLink availability, SourceLink CR/LF diagnostics, determinism, trim/AOT markers, updated memory-safety model, `RequiresUnsafe` member count, unsafe signatures, P/Invoke, and direct references. Package Signals use the same shape for package metadata/assets, dependencies, signature provenance, and NuGet registry observations. `library X -S Signals` resolves SourceLink by acquiring a missing PDB. The per-source-file reachability pass — SourceLink Availability and SourceLink Missing Files, which issue one HTTP HEAD per tracked source URL — is opt-in: select it explicitly with `-S "SourceLink Availability"`. It does not run in a plain `library X -v:d` flow because its cost scales with source-file count. To verify source *content*, select `library X -S "SourceLink Integrity"`: it downloads each tracked source file and compares its hash to the PDB checksum, exits non-zero on true content mismatch, and reports `CR/LF Mismatch` when checksums match after line-ending normalization.

Package Signals include TFMs, manifest, readme/license, direct dependencies, package signature, local provenance, and registry-backed signals such as vulnerabilities, package age, dependency vulnerability/deprecation counts, and dependency age. Symbol/SourceLink package evidence names the PDB source (`embedded`, `in-package`, `.snupkg`, `msdl.microsoft.com`). Custom feeds (`--nuget-source`, `--add-source`, `--nugetconfig`) and local `.nupkg` files are supported.

For package structure, use `package X -S Manifest` to see manifest version/package/tool rows, `package X -S "Library Files"` to list all files under `lib/` across TFMs, and `package X -S All` to include opt-in sections such as Signals.

## Output and query workflow

Discover sections, then select or project fields.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -S Methods --columns "Name;Signature;Obsolete"
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
```

For target-based queries, `-D` and bare `-S` report the effective schema by default: only sections and columns that can render for that query. Add `--schema` for the static schema. `-S`, `--columns`, and `--fields` accept comma-separated or semicolon-separated lists. In `--schema` section output, `section (opt-in)` means the section never runs from normal verbosity; select it explicitly with `-S` when needed, or use `-S All` to include all sections including opt-in sections. Use `--count` only when exactly one selected section should produce a row count.

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
