# dotnet-inspect

CLI tool for inspecting .NET libraries and NuGet packages. It is for .NET what `docker inspect` and `kubectl describe` are for container land: view package metadata, API surfaces, dependencies, source provenance, and version-to-version changes.

## Install or run

```bash
dotnet tool install -g dotnet-inspect
dotnet-inspect <command>
```

Run without installing:

```bash
dnx dotnet-inspect -y -- <command>
```

## What it inspects

| Source | Examples | Notes |
| ------ | -------- | ----- |
| NuGet packages | `package System.Text.Json`, `type --package Markout` | Supports versions, custom sources, `nuget.config`, TFMs, package layout, dependencies, and vulnerabilities. |
| Platform libraries | `library System.Private.CoreLib`, `library System.Text.Json --version 10.0.0`, `diff --platform System.Runtime@9.0.0..10.0.0` | Resolves installed SDK/runtime assemblies, including runtime-only implementation assemblies with no NuGet package. |
| Local assets | `library ./bin/MyLib.dll`, `package ./pkg/MyLib.nupkg` | Useful for auditing builds before publishing. |

Bare names are routed automatically: platform-looking names (`System.*`, `Microsoft.AspNetCore.*`) resolve to installed platform libraries; other names resolve as NuGet packages. Use explicit commands and `--package`, `--platform`, or `--library` when you need a specific source.

## Capability inventory

| Capability | Commands | Highlights |
| ---------- | -------- | ---------- |
| Package inventory | `package` | Metadata, versions, TFMs, file layout, dependency tree, metadata audit, vulnerability data, custom feeds, NuGet config support. |
| Library audit | `library` | Assembly identity, public key token, trim/AOT metadata, unsafe/interoperability signals, symbols/PDBs, SourceLink and determinism audit, references, resources, async method classification. |
| API discovery | `type`, `member`, `find` | Type search, member tables, docs, overload selection, generics, obsolete-member markers, source/decompiled/IL drill-in. |
| API compatibility | `diff` | Version ranges, package or platform diffs, breaking/additive/potentially-breaking classification, type filters. |
| Relationships | `depends`, `extensions`, `implements` | Type hierarchies, package dependencies, library reference graphs, extension methods/properties, implementors and subclasses. |
| Source mapping | `source`, `member -v:d` | SourceLink URLs, member line numbers, source fetching, URL verification, token+IL-offset to source-line resolution. |
| Agent-friendly output | global flags | Markdown by default, compact `--oneline`, `--plaintext`, `--json`, Mermaid diagrams, section/field projection, `--count`, built-in head/tail limiting. |

## Command inventory

| Command | Purpose |
| ------- | ------- |
| `package X` | Inspect NuGet metadata, versions, dependencies, TFMs, layout, and vulnerabilities. |
| `library X` | Inspect assembly metadata, symbols, SourceLink, references, resources, and async methods. |
| `audit X` | Report package/library audit signals with explicit network scope flags. |
| `type X` | Discover types or render a single type shape. |
| `member X` | Inspect members, docs, overloads, source, decompiled C#, and IL. |
| `find X` | Search for types across packages, frameworks, projects, and local assets. |
| `diff X` | Compare API surfaces between versions. |
| `extensions X` | Find extension methods and C# extension properties for a type. |
| `implements X` | Find concrete implementors or subclasses. |
| `depends X` | Walk type, package, or library dependency graphs; emits Mermaid diagrams. |
| `source X` | Resolve SourceLink URLs or map method token + IL offset to source. |
| `cache` | Inspect or clear dotnet-inspect caches. |
| `skill` | Print the embedded LLM skill definition. |

## Audit signals

`audit` is a signal report, not a safety certification. By default it reports facts discoverable from package and assembly metadata. Use `--full` (alias: `--all`) for broad target-appropriate enrichment. Use `-v:d` when you want detailed audit sections, including exhaustive SourceLink coverage.

| Command | Scope | Signals |
| ------- | ----- | ------- |
| `audit X` | Metadata | Package or library metadata signals only; no network enrichment. |
| `audit X --full` | Broad target-appropriate scope | Libraries allow symbol/PDB enrichment; packages include NuGet registry and dependency expansion. |
| `audit X -v:d` | Detailed library audit | Audit plus SourceLink Audit section; verifies tracked source-file URLs and embedded-source coverage. |
| `audit X --source-audit` | Explicit SourceLink verification | Alias: `--source`; equivalent to the detailed library source coverage check. |
| `audit X --symbols` | Symbol/PDB enrichment | Library audit plus PDB acquisition for SourceLink presence and deterministic evidence without source URL verification. |
| `audit package X --nuget` | NuGet registry expansion | Package audit plus known vulnerabilities, resolved dependency closure, max dependency depth, package age, and direct dependency age. |

## Output and querying

Default output is Markdown. Use `--oneline` for compact rows, `--plaintext` for plain text, `--json` for structured data, `--count` to count table rows in one selected section, and `--mermaid` on `depends` for diagrams. Verbosity is `-v:q`, `-v:m`, `-v:n`, or `-v:d`.

Sections and fields are queryable without a template language:

```bash
dotnet-inspect library System.Net.Security -S "Async*"
dotnet-inspect member JsonSerializer --package System.Text.Json -D
dotnet-inspect member JsonSerializer --package System.Text.Json -D --schema
dotnet-inspect type --package System.Text.Json --columns Kind,Name
dotnet-inspect library System.Text.Json -S Symbols --fields "PDB*;SourceLink"
dotnet-inspect library System.Text.Json -S "Async*" --count
dotnet-inspect audit System.Text.Json
```

For `type` and `member`, `-D` reports the effective schema by default: only sections and columns that can actually render for that query. Add `--schema` for the static schema. Lists for `-S`, `--columns`, and `--fields` accept commas or semicolons.

## Common examples

```bash
dotnet-inspect audit System.Text.Json
dotnet-inspect audit System.Text.Json --full
dotnet-inspect audit System.Text.Json -v:d
dotnet-inspect audit package System.Text.Json --full
dotnet-inspect package System.Text.Json --versions
dotnet-inspect type --package System.Text.Json --oneline
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -v:d
dotnet-inspect source JsonSerializer --package System.Text.Json --il-offset 0x06000004+0x15
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dotnet-inspect depends Stream --markdown --mermaid
```

## Requirements

.NET 10.0 SDK or later.

## LLM integration

dotnet-inspect is [designed for LLM-driven development](docs/llm-design.md). The embedded skill (`dotnet-inspect skill`) is also distributed through the [dotnet/skills](https://github.com/dotnet/skills) marketplace.

## License

MIT
