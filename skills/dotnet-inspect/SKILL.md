---
name: dotnet-inspect
version: 0.8.1
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
| Audit package/library contents | `package Foo`, `library Foo` | Add `-S Section`, `--fields`, `--source-link-audit`, or `--count`. |
| Explore relationships | `depends Type`, `extensions Type`, `implements Interface` | Add package/platform scope as needed. |
| Keep output small | `--oneline`, `--json`, `-S Section`, `--count`, `-n N` | Prefer built-in limits over shell pipes. |

## Modern .NET and preview workflow

LLM training may miss .NET 10+ runtime/library features. Prefer metadata inspection over web search.

| Feature | Description | Use | Watch for |
| ------- | ----------- | --- | --------- |
| Runtime async | .NET 11+ libraries may use runtime async instead of compiler-generated state machines. | `library --platform Lib --framework runtime@<version> -S "Async*"` | `Kind` distinguishes runtime async from state-machine async; use `--count` only for totals. |
| Runtime-pack assemblies | Many BCL libraries ship only as installed platform/runtime-pack assemblies, not standalone packages. | `library --platform Lib --framework runtime@<version>` or direct DLL path | Prefer platform/direct DLL inspection when package lookup is misleading. |
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

## Package and library audit workflow

Use `package` for NuGet metadata and package layout. Use `library` for assembly identity, references, symbols, SourceLink, resources, async methods, and public key token.

```bash
dnx dotnet-inspect -y -- package System.Text.Json --versions
dnx dotnet-inspect -y -- library System.Text.Json --source-link-audit
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
```

`package` supports custom feeds (`--source`, `--add-source`, `--nugetconfig`) and local `.nupkg` files. `library -S "Async*"` classifies async methods as runtime async or classic state-machine async.

## Output and query workflow

Discover sections, then select or project fields.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -D
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json -S Methods --columns "Name;Signature;Obsolete"
dnx dotnet-inspect -y -- library System.Text.Json -S "Async*" --count
```

For `type` and `member`, `-D` reports the effective schema by default: only sections and columns that can render for that query. Add `--schema` for the static schema. `-S`, `--columns`, and `--fields` accept comma-separated or semicolon-separated lists. Use `--count` only when exactly one selected section should produce a row count.

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
- Use `--all` for non-public, hidden, and extra members; obsolete members are already shown by default.
