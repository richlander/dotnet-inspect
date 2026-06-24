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
| Restored projects | `project ./src/App --agents-index`, `project ./src/App --readme Markout` | Resolves direct package references from `project.assets.json` for compact package grounding manifests and version-pinned docs. |
| Platform libraries | `library System.Private.CoreLib`, `library System.Text.Json --version 10.0.0`, `diff --platform System.Runtime@9.0.0..10.0.0` | Resolves installed SDK/runtime assemblies, including runtime-only implementation assemblies with no NuGet package. |
| Local assets | `library ./bin/MyLib.dll`, `package ./pkg/MyLib.nupkg` | Useful for auditing builds before publishing. |

Bare names are routed automatically: platform-looking names (`System.*`, `Microsoft.AspNetCore.*`) resolve to installed platform libraries; other names resolve as NuGet packages. In API commands, common CoreLib aliases and simple type names such as `string`, `int`, `DateTime`, and `Guid` resolve to `System.Private.CoreLib`. Use explicit commands and `--package`, `--platform`, or `--library` when you need a specific source.

## Capability inventory

| Capability | Commands | Highlights |
| ---------- | -------- | ---------- |
| Package inventory | `package` | Metadata, versions, TFMs, file layout, dependency tree, metadata audit, vulnerability data, custom feeds, NuGet config support. |
| Project grounding | `project` | Direct dependency `AGENTS.md` frontmatter indexes and version-resolved package docs from restored projects. |
| Library audit | `library` | Assembly identity, public key token, trim/AOT metadata, unsafe/interoperability signals, OpenTelemetry support, symbols/PDBs, SourceLink and determinism audit, references, resources, async method classification. |
| API discovery | `type`, `member`, `find` | Type search, member tables, docs, overload selection, generics, obsolete-member markers, direct calls and callers, source/decompiled/IL drill-in. |
| API compatibility | `diff` | Version ranges, package or platform diffs, breaking/additive/potentially-breaking classification, type filters. |
| Relationships | `depends`, `extensions`, `implements` | Type hierarchies, package dependencies, library reference graphs, extension methods/properties, implementors and subclasses. |
| Source mapping | `type`/`library`/`package -S "Source Files"`, `member -S "Source Locations"` / `"Original Source"` | SourceLink URLs, member file/line locations, source fetching, URL verification, token+IL-offset to source-line resolution. |
| Agent-friendly output | global flags | Markdown by default, compact `--table`, normalized `--tsv`, `--jsonl`, `--plaintext`, `--json`, Mermaid diagrams, section/field projection, `--count`, table row limiting, built-in head/tail limiting. |

## Command inventory

| Command | Purpose |
| ------- | ------- |
| `package X` | Inspect NuGet metadata, versions, dependencies, TFMs, layout, and vulnerabilities. |
| `project [path]` | Inspect restored direct package references for grounding indexes and package docs. |
| `library X` | Inspect assembly metadata, symbols, SourceLink, references, resources, and async methods. |
| `type X` | Discover types or render a single type shape. |
| `member X` | Inspect members, docs, overloads, decompiled/lowered C#, SourceLink-backed original source, and IL. |
| `find X` | Search for types across packages, frameworks, projects, and local assets. |
| `diff X` | Compare API surfaces between versions. |
| `extensions X` | Find extension methods and C# extension properties for a type. |
| `implements X` | Find concrete implementors or subclasses. |
| `depends X` | Walk type, package, or library dependency graphs; emits Mermaid diagrams. |
| `cache` | Inspect or clear dotnet-inspect caches. |
| `skill` | Print the embedded LLM skill definition. |

Single-type `type X` output is tree-shaped by default. Use `-v:n` or `-v:d`
to grow that tree to overload leaves; use `--markdown -v:q` when you want the
compact Markdown section view instead.
When an exact type is not found, `type` treats the query as a best-effort
namespace/type prefix browse within the resolved package, library, or platform
scope.

## Signals

`Signals` is an evidence report, not a safety certification. Select it with `-S Signals`. For libraries, Signals reports metadata/provenance observations and acquires a missing PDB when selected to resolve SourceLink. For packages, Signals reports package metadata/assets, dependencies, signature provenance, and NuGet registry observations. The per-source-file reachability pass (`SourceLink Availability`, `SourceLink Missing Files`) is selected explicitly with `-S` because its cost scales with source-file count. The slow, exhaustive content check (`SourceLink Integrity`) is opt-in only.

| Command | Scope | Signals |
| ------- | ----- | ------- |
| `library X -S Signals` | Metadata + provenance | Library metadata/provenance signals; a missing library PDB is acquired to resolve SourceLink. |
| `library X -S "Signals,SourceLink Availability,SourceLink Missing Files"` | Detailed SourceLink reachability | Adds the opt-in per-file HEAD pass and reports embedded-source coverage. |
| `library X -S "SourceLink Integrity"` | Content verification (slow, opt-in) | Downloads every tracked source file and compares its hash to the PDB checksum; a mismatch exits non-zero. Never runs in a default flow. |
| `package X -S Signals` | Full package signals | Package and dependency signals, including known vulnerabilities, package age, dependency vulnerability/deprecation counts, and dependency age. |

## Integrations

`Integrations` is a library section for ecosystem support such as AI, ASP.NET
Core, Aspire, Authentication, Configuration, Dependency Injection, Logging, Options, Hosting,
Health Checks, HTTP Client, OpenAPI, and OpenTelemetry. It is a usability index, not a raw evidence report: focused
integration sections list package-owned actionable types and starter APIs rather
than assembly references.

Use `package Foo --library` to inspect one package DLL, or `package Foo
--all-libraries` when the package contains multiple relevant libraries. In
all-library mode, singular sections such as `Library Info` are rendered per
library while aggregate sections such as `@Integrations` roll up rows across
libraries and include library provenance when needed. Row formats (`--table`,
`--tsv`, `--jsonl`) require one concrete section, such as `Library Info`,
`Integrations`, `Switches`, or a focused integration section; use Markdown for
category selectors such as `@Integrations`.

`Switches` is a peer library section for feature, compatibility, and runtime
configuration switches such as `FeatureSwitchDefinitionAttribute` and
`AppContext` switch names.

## Output and querying

Default output is Markdown. Use Markdown for evidence and narrative, `--table` for compact human scanning, `--tsv` for normalized tab-separated rows for agents and scripts, `--jsonl` for one JSON object per table row, and `--json` for structured object graphs. Use `--plaintext` for plain text, `--bare` for one undecorated payload without changing the selected shape, `--rows -n N` to cap rendered table rows, `--count` to reduce a selected section/vector to a single row count, and `--mermaid` on `depends` for diagrams. Verbosity is `-v:q`, `-v:m`, `-v:n`, or `-v:d`. Markdown and JSON can represent multi-section documents; `--table`, `--tsv`, and `--jsonl` render one table/section at a time, so pair them with a specific `-S` selection when querying sectioned output.

Sections and fields are queryable without a template language:

```bash
dotnet-inspect library System.Net.Security -S "Async*"
dotnet-inspect member JsonSerializer --package System.Text.Json -D
dotnet-inspect member JsonSerializer --package System.Text.Json -D --schema
dotnet-inspect type --package System.Text.Json --columns Kind,Name
dotnet-inspect library System.Text.Json -S Symbols --fields "PDB*;SourceLink"
dotnet-inspect library System.Text.Json -S @Audit
dotnet-inspect library System.Text.Json -S "Async*" --count
dotnet-inspect package Microsoft.Extensions.Logging.Abstractions --library -S Integrations
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions -S Integrations
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions -S Logging
dotnet-inspect library System.Diagnostics.DiagnosticSource -S OpenTelemetry
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect package System.Text.Json --path @readme --content --frontmatter
dotnet-inspect project ./src/App --agents-index --jsonl
dotnet-inspect project ./src/App --readme Markout
```

For target-based queries, `-D` reports the effective schema by default: only sections and columns that can actually render for that query. Add `--schema` for the static schema. Bare `-S` renders `@Default`, a curated high-density view; type/member summaries use `Method Groups`, while `member Type -m Name` uses `Methods` overload rows. Lists for `-S`, `--columns`, and `--fields` accept commas or semicolons. Use `-S @All` to select all sections; it renders the default section first, then remaining sections alphabetically. Workflow categories such as `@Source` and `@Audit` expand to scenario-focused section groups.

## Common examples

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect package Microsoft.Extensions.Logging.Abstractions --library -S Integrations
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions -S Integrations
dotnet-inspect library System.Diagnostics.DiagnosticSource -S OpenTelemetry
dotnet-inspect library System.Text.Json -S "Signals,SourceLink Availability,SourceLink Missing Files"
dotnet-inspect library System.Text.Json -S "SourceLink Integrity"
dotnet-inspect package System.Text.Json -S Signals
dotnet-inspect package System.Text.Json --versions
dotnet-inspect project ./src/App --agents-index
dotnet-inspect project ./src/App --readme Markout
dotnet-inspect type string --shape
dotnet-inspect type --package System.Text.Json --table
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize -S "Member Index"
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize -S "Source Locations"
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S @Source
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Calls
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Callers
dotnet-inspect member string IndexOf:7 -S Callers --caller-package System.Text.Json@9.0.0 --tfm net9.0
dotnet-inspect member MyApi.Helper Run:1 --library MyLib.dll --bin ./app/bin/Release/net10.0
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Call Graph"
dotnet-inspect member MyType MyMethod:1 --library MyLib.dll -S "Unsafe*"
dotnet-inspect library System.Text.Json --il-offset 0x06000004+0x15
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 --breaking
dotnet-inspect depends Stream --markdown --mermaid
```

## Requirements

.NET 10.0 SDK or later.

## LLM integration

dotnet-inspect is [designed for LLM-driven development](docs/llm-design.md). The embedded skill (`dotnet-inspect skill`) is also distributed through the [dotnet/skills](https://github.com/dotnet/skills) marketplace.

## Contributor and agent docs

Start with [docs/overview.md](docs/overview.md) for system context. `AGENTS.md` is the agent resolver for this repo, and [taste/skill-guidance.md](taste/skill-guidance.md) captures good and bad examples for maintaining the embedded skill.

## License

MIT
