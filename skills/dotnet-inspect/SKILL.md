---
name: dotnet-inspect
version: 0.8.0
description: Query .NET APIs across NuGet packages, platform libraries, and local files. Search for types, list API surfaces, compare versions, find extension methods and implementors, inspect SourceLink and IL. Use whenever you need evidence about .NET library contents.
---

# dotnet-inspect

Use dotnet-inspect when you need factual information about .NET packages, platform libraries, or local assemblies. Prefer it over guessing signatures, package contents, inheritance, SourceLink URLs, or version-to-version API changes.

Invoke through `dnx` unless the tool is already installed:

```bash
dnx dotnet-inspect -y -- <command>
```

Default output is Markdown. Use `--oneline` to scan, `--json` for structured data, `--count` for one selected section's row count, and `-v:d` when you need source/decompiled C#/IL detail.

## Workflow map

| Goal | Start with | Then drill in |
| ---- | ---------- | ------------- |
| Fix code after a package upgrade | `diff --package Foo@old..new --breaking` | `member Type --package Foo@new` |
| Learn what changed in a .NET preview | `diff --platform System.Runtime@prev..next --additive` | Repeat per framework library. |
| Find where a type lives | `find Pattern --oneline` | Carry forward the returned package version and library name. |
| Inspect members and docs | `member Type --package Foo` | Add `-m Name`, `--show-index`, or `Name:N`. |
| Get source, lowered C#, or IL | `member Type --package Foo Name:N -v:d` | Use `source` when you only need SourceLink URLs. |
| Map stacktrace token+IL offset to source | `source --library App.dll --il-offset 0x06000001+0x5` | Use `--json` if another tool consumes the result. |
| Audit package or assembly metadata | `package Foo`, `library Foo` | Add `--source-link-audit`, `--dependencies`, `-S Symbols`, or `-S "Async*"`. |
| Explore relationships | `depends Type`, `extensions Type`, `implements Interface` | Add `--mermaid` for diagrams. |
| Count a section | `library Foo -S "Async*" --count` | Requires exactly one selected section; returns one integer. |
| Query exact sections/fields | `command ... -D`, then `-S Section --fields Names` | Add `--schema` only when you need the static schema. |

## Upgrade and compatibility workflow

Use `diff` first, then inspect the replacement API.

```bash
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.3 --breaking
dnx dotnet-inspect -y -- member Command --package System.CommandLine@2.0.3
dnx dotnet-inspect -y -- member Command --package System.CommandLine@2.0.3 --show-index
```

Notes:

- `diff` classifies breaking, additive, and potentially-breaking changes.
- Use `--additive` for "what is new?" release-note work.
- Obsolete members are visible by default and include an obsolete marker/message when available.

## Find -> inspect -> source workflow

Use `find` to discover the source, then keep the resolved context in follow-up commands.

```bash
dnx dotnet-inspect -y -- find RegexOptions --package Microsoft.NETCore.App.Ref@11.0.0-preview.4.26251.6 --oneline
dnx dotnet-inspect -y -- member RegexOptions \
  --package Microsoft.NETCore.App.Ref@11.0.0-preview.4.26251.6 \
  --library System.Text.RegularExpressions
dnx dotnet-inspect -y -- source RegexOptions \
  --package Microsoft.NETCore.App.Ref@11.0.0-preview.4.26251.6 \
  --library System.Text.RegularExpressions
```

For multi-library packages, add the `--library` value shown by `find`. For framework libraries, prefer `--platform <LibraryName>` when you do not specifically need a NuGet package. Platform resolution is SemVer/prerelease-aware and can resolve runtime-only assemblies such as `System.Private.CoreLib`.

## Source, decompilation, and IL workflow

Use `member -v:d` when you need implementation detail for a selected member.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json --show-index
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json Serialize:1 -v:d
```

The detailed member view can include original source via SourceLink, lowered C#, raw IL, and annotated IL. Use `source` for source URLs without a full member detail view:

```bash
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json -m Serialize
dnx dotnet-inspect -y -- source JsonSerializer --package System.Text.Json --cat
dnx dotnet-inspect -y -- source --library ./bin/MyApp.dll --il-offset 0x06000001+0x5 --json
```

## Platform and release-note workflow

For .NET platform APIs, diff each framework library with `--platform`.

```bash
dnx dotnet-inspect -y -- diff --platform System.Runtime@9.0.0..10.0.0 --additive
dnx dotnet-inspect -y -- diff --platform System.Text.Json@9.0.0..10.0.0
dnx dotnet-inspect -y -- library System.Private.CoreLib
```

Use installed platform libraries for runtime/source fidelity. Use `--package` when a workflow specifically depends on a NuGet package, custom feed, or package layout.

## Package and library audit workflow

```bash
dnx dotnet-inspect -y -- package System.Text.Json --versions
dnx dotnet-inspect -y -- package System.Text.Json --tfms
dnx dotnet-inspect -y -- package System.Text.Json --dependencies
dnx dotnet-inspect -y -- library System.Text.Json --source-link-audit
dnx dotnet-inspect -y -- library System.Net.Security -S "Async*"
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
```

`package` supports custom feeds (`--source`, `--add-source`, `--nugetconfig`) and local `.nupkg` files. Non-HTTP sources in NuGet config are skipped for HTTP-only resolution instead of blocking later feeds. `library -S "Async*"` classifies async methods as runtime async or classic state-machine async.

## Structured query workflow

Discover sections, then select or project fields. For `type` and `member`, `-D` is effective by default: it resolves the source and lists only sections/columns that can render for that query.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D --schema
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -S Methods --columns "Name;Signature;Obsolete"
dnx dotnet-inspect -y -- library System.Text.Json -S Symbols --fields "PDB*;SourceLink"
```

Rules:

- `-S`, `--columns`, and `--fields` accept comma-separated or semicolon-separated lists.
- Use bare `-S` to list available sections.
- Use `--count` with exactly one selected section to return the rendered table row count.
- Use `--schema` when you need the cheap static schema instead of the effective one.
- Use built-in `-n N`, `--head N`, `-N`, or `--tail N`; do not pipe through shell `head`/`tail` if preserving headers matters.

## Relationship workflow

```bash
dnx dotnet-inspect -y -- depends Stream
dnx dotnet-inspect -y -- depends --package Markout --mermaid
dnx dotnet-inspect -y -- extensions HttpClient --reachable
dnx dotnet-inspect -y -- implements IJsonTypeInfoResolver --package System.Text.Json
```

Search scope flags for `find`, `extensions`, `implements`, and type-oriented `depends`:

| Scope | Meaning |
| ----- | ------- |
| no scope flag or `--platform` | Installed platform frameworks. |
| `--package Foo` | Specific NuGet package; repeatable for multi-package scans. |
| `--aspnetcore` | Curated ASP.NET Core packages. |
| `--extensions` | Curated Microsoft.Extensions packages. |
| `--project ./App.csproj` | Project dependencies. |

## Syntax guardrails

- Quote generic type names: `'Option<T>'`, `'INumber<TSelf>'`.
- Use `<T>` rather than `<>` for generic type queries.
- `type` uses `-t` for type-name filters; `member` uses `-m` for member filters.
- Dotted member syntax works: `-m JsonSerializer.Deserialize`.
- Diff ranges use `..`: `--package Foo@1.0.0..2.0.0`.
- Use `--all` for non-public, hidden, and extra members; obsolete members are already shown by default.

## Command inventory

| Command | Use |
| ------- | --- |
| `package` | NuGet metadata, versions, TFMs, layout, dependencies, vulnerabilities. |
| `library` | Assembly identity, public key token, symbols, SourceLink, references, resources, async methods. |
| `type` | Type discovery or single-type shape. |
| `member` | Member lists, docs, overloads, source, lowered C#, IL. |
| `find` | Type search across selected scopes. |
| `diff` | API comparison between versions. |
| `source` | SourceLink URLs, source fetching, IL-offset source mapping. |
| `depends` | Type, package, or library dependency graphs. |
| `extensions` | Extension methods and extension properties. |
| `implements` | Implementors and subclasses. |
