# dotnet-inspect

A tool for inspecting .NET libraries and NuGet packages. It is for .NET what
`docker inspect` and `kubectl describe` are for containers: view package
metadata, API surfaces, dependencies, and source.

The .NET ecosystem uses a standardized binary format for managed assemblies
([ECMA-335](https://ecma-international.org/publications-and-standards/standards/ecma-335/)
`.dll` files). That's why NuGet packages primarily distribute binaries instead
of source. That's where `dotnet-inspect` fits. It does for .NET binaries what
LSP-based tools do for source. `dotnet-inspect` reads .NET binaries to answer
basic questions about types and members and unlocks deeper insights, like call
graphs and seeing what really changed across two binary versions

## Install or run

```bash
dotnet tool install -g dotnet-inspect
dotnet-inspect <command>
```

Run without installing:

```bash
dnx dotnet-inspect -y -- <command>
```

## Repository development SDK

Published tool users can install or run `dotnet-inspect` with the commands
above. Contributors building this repository should use the current .NET 11
preview SDK.

Check the selected SDK first:

```bash
command -v dotnet
dotnet --version
```

Build from source:

```bash
dotnet build dotnet-inspect.slnx -c Release
```

See [AGENTS.md](AGENTS.md) for contributor workflow, targeted test commands,
and repository-specific guidance.

## What it inspects

| Source | Examples | Notes |
| ------ | -------- | ----- |
| NuGet packages | `package System.Text.Json`, `type --package Markout` | Supports versions, custom sources, `nuget.config`, TFMs, package layout, dependencies, and vulnerabilities. |
| Restored projects | `type Command --project ./src/dotnet-inspect`, `project ./src/dotnet-inspect -S Skills --print` | Uses an existing `project.assets.json` as restored-assets context for API lookup, relationship search, and dependency package skills; restore/build first if dependencies changed. dotnet-inspect does not restore or build. |
| Platform libraries | `library System.Private.CoreLib`, `library System.Text.Json --version 10.0.0`, `diff --platform System.Runtime@9.0.0..10.0.0` | Resolves installed SDK/runtime assemblies, including runtime-only implementation assemblies with no NuGet package. |
| Local assets | `library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll`, `package ./artifacts/MyLib.nupkg` | Useful for auditing local builds before publishing. |

Windows Metadata (`.winmd`) is not a supported input format, and rejection is
only partially enforced. Directory and package scans select `*.dll`, so a
`.winmd` beside them is skipped without comment. A `.winmd` named explicitly —
`library ./Foo.winmd`, `find --library ./Foo.winmd` — is rejected rather than
inspected, though not every surface names the reason: `find` reports
"Windows Metadata is not a supported metadata format", while `library` reports
only "Could not read library". Owners that have not yet adopted the admission
contract do not classify at all. Treat any Windows Metadata result as
unsupported output regardless of how confident it looks. Tracked by
[#5559](https://github.com/richlander/dotnet-inspect/issues/5559).

Bare names are routed automatically: platform-looking names (`System.*`,
`Microsoft.AspNetCore.*`) resolve to installed platform libraries; other names
resolve as NuGet packages. In API commands, common CoreLib aliases and simple
type names such as `string`, `int`, `DateTime`, and `Guid` resolve to
`System.Private.CoreLib`. Use explicit commands and `--package`, `--platform`,
or `--library` when you need a specific source.

Use `-D --schema` to inspect the syntax-selected structural view without
acquiring or loading the target. Package `--library` and `--all-libraries`
queries expose their route-specific schemas before package resolution, while
ambiguous commandless or dotted-member targets return separately labeled
alternatives rather than a lookup-chosen union. A commandless
`<target> --all-libraries` gesture always selects the package aggregate view.

## Demo: query rendered C# body shapes

Discover the stable IDs accepted by body queries:

```bash
dnx dotnet-inspect -y -- vocabulary -S "C# Body Kinds" \
  --columns "ID;Label" --rows 5 --table
```

Then use one as a typed predicate. `Kind=...` auto-selects `Body Shapes`, while
ordinary section query options still control columns and rows:

```bash
dnx dotnet-inspect -y -- library System.Text.Json \
  --where "Kind=ObjectCreationExpression" \
  --columns "Member;Token;Match" --rows 3
```

```text
# System.Text.Json.dll

## Body Shapes

| Member | Token | Match |
| ------ | ----- | ----- |
| `System.Text.Json.JsonDocument.RootElement~2810741072:1` | `0x06000260` | `new JsonElement(this, 0)` |
| `System.Text.Json.JsonDocumentOptions.CommentHandling~4fc3b6f99d:2` | `0x060002DC` | `new ArgumentOutOfRangeException("value", SR.JsonDocumentDoesNotSupportComments)` |
| `System.Text.Json.JsonElement.GetProperty~b07c7787dc` | `0x060002EA` | `new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, propertyName))` |
```

Use `--jsonl` for one machine-readable row per match or `--count` for the row
count. Bodies that cannot be reconstructed at full fidelity are reported on
stderr rather than mixed into structured output.

## Capability inventory

| Capability | Commands | Highlights |
| ---------- | -------- | ---------- |
| Package inventory | `package` | Metadata, versions, TFMs, file layout, dependency tree, vulnerability data, custom feeds, and NuGet config support. |
| Project package skills and docs | `project` | Direct dependency `Skills` rows from valid package `skills/**/SKILL.md` files plus version-resolved package docs in a restored project context. Skill inventory values and complete documents that require containment become `[Text omitted: required containment]`. |
| Query vocabulary | `vocabulary` | Product-owned stable values, operators, defaults, and applicability for rich queries. |
| Library audit | `library` | Assembly identity, public key token, trim/AOT metadata, unsafe/interoperability signals, SourceLink, PDBs, references, resources, async methods, and body-shape search. |
| API and package discovery | `type`, `member`, `find` | Type search, member tables, docs, overload selection, generics, direct calls/callers, source, decompiled C#, IL, and package-prefix discovery. |
| API compatibility | `diff` | Package, platform, and library diffs with breaking/additive classification plus opt-in C#/IL and selected-member authored-source evidence. |
| Timeline correlation | `timeline` | Correlate API or member-body Findings across a package version range, with evaluation and transition views. |
| Implementation matching | `match` | Identity-agnostic structural equivalence for two unambiguously named methods, plus `--similar` seeded discovery that ranks structural candidates for one seed. |
| Relationships | `graph`, `depends`, `extensions`, `implements` | Integration graphs, type hierarchies, package dependencies, reference graphs, extension methods/properties, implementors, and subclasses. |
| Direct dependency evidence | `dependency-evidence` | One normalized snapshot of the direct dependencies declared by named package, nuspec, restored-project, or package-prefix roots, with framework scopes, version constraints, restored resolution evidence, and root-set completion. Unlike `depends`, it does not walk the transitive tree. |
| Source mapping | `library`/`package -S "SourceLink: Files"`, `type -S "Source Files"`, `member -S "Source Locations"` / `"PDB Source"` | SourceLink URLs, member file/line locations, and token+IL-offset to source-line resolution. `PDB Source` is checksum-verified source acquired from the PDB-recorded local path, a caller-supplied Git clone (`--repo`), or remote SourceLink, in that order. |
| Performance analysis *(experimental)* | `library -S @Performance`, `type`/`member -S "Performance Triage"`, `"Top Leverage"`, `"Resource Triage"`, `"Call Graph"` | Whole-assembly leverage ranking, actionable rewrite-shape detection, and exception-path resource-lifecycle candidates. |
| Decompiler *(experimental)* | `member -S @Source`, `member -S "Fidelity Causes"`, `member`/`type`/`library --where "Kind=<ID>"` | Decompiled C#, annotated source, IL, body-shape queries, and typed `DEC####` fidelity causes. |
| Raw metadata | `library -S @Metadata`, `--heap "#Strings:0x1a4"` | Decoded ECMA-335 metadata tables and heap addressing. |
| Workspace package occurrences | `workspace --package X --tfm TFM` | Render the exact ordered package occurrences of one runtime Workspace through the same product-owned view used by Inspect Web. Repeat `--package` to compose the Workspace. |
| Workspace sharing | `workspace-state encode` / `decode` | Convert the canonical browser/CLI base64url workspace packet to or from its bounded JSON shape without acquisition or execution. |
| Agent-friendly output | global flags | Markdown by default, compact `--table`, normalized `--tsv`, `--jsonl`, `--json`, Mermaid diagrams, section/field projection, `--count`, and row limiting. |

## Command inventory

| Command | Purpose |
| ------- | ------- |
| `package X` | Inspect NuGet metadata, versions, dependencies, TFMs, layout, and vulnerabilities. |
| `project [path]` | Inspect restored project package skills and package docs. |
| `library X` | Inspect assembly metadata, symbols, SourceLink, references, resources, async methods, and rendered body shapes. |
| `type X` | Discover types or render a single type shape. |
| `member X` | Inspect members, docs, overloads, decompiled/lowered C#, rendered body shapes, checksum-verified PDB source, and IL. |
| `find [X]` | Search for types across packages, frameworks, projects, and local assets. Add `--members` (or lead the query with `.`, such as `.Serialize`) to search member names instead. Omit `X` with `--package-prefix PREFIX` to discover latest NuGet package manifests. |
| `diff X` | Compare API surfaces by default; opt into analysis or implementation evidence. |
| `timeline X` | Correlate API or member-body Findings across a package version range. |
| `graph integrations` | Induce extension, observed Integration, and Integration-opportunity relationships over an explicit package set. |
| `depends X` | Walk type, package, or library dependency graphs; can emit Mermaid diagrams. |
| `dependency-evidence` | Report the normalized direct dependencies declared by explicitly named `--package`, `--nuspec`, `--project`, or `--package-prefix` roots. Reports declarations and restored resolution evidence for those roots only; use `depends` to traverse. |
| `extensions X` | Find extension methods and C# extension properties for a type. |
| `implements X` | Find concrete implementors or subclasses. |
| `match A B` | Compare two unambiguous `Type.Member` names by identity-agnostic structural equivalence; add `--implementation` for side-by-side decompiled C# and IL. |
| `match A --similar` | Rank structural candidates for one seed method, within a single assembly. Ranks candidates only; it establishes no relation. |
| `vocabulary` | Discover product-owned query vocabularies such as `Accessibility`, `C# Style Choices`, and `C# Body Kinds`. |
| `workspace` | Render an ordered runtime Workspace package-occurrence view for packages with selected managed assemblies. Repeat `--package ID@VERSION` coordinates and supply `--tfm`; omit packages for a typed empty Workspace. |
| `workspace-state encode` / `decode` | Convert validated workspace-state JSON and canonical base64url packets; pass `-` for stdin or use `--file`. |
| `skill` | Print the base LLM skill and route to focused built-in guidance (`skill list`, `skill query`, `skill decompiler`, `skill relationships`, and more). |
| `demo [id]` | List or run product-home inspection demos backed by real section output. |
| `cache` | Inspect or clear dotnet-inspect caches. |

## Signals, integrations, and focused guidance

`Signals` is an evidence report, not a safety certification. Use `-S Signals`
for a compact package or library overview, then opt into deeper audits only when
you need them.

Integration support is exposed through `@Integrations` or focused
`Integration: ...` sections such as `Integration: Logging` or
`Integration: OpenTelemetry`.

For deeper how-to guidance, use the embedded skills instead of relying on a very
long README:

```bash
dotnet-inspect skill list
dotnet-inspect skill query
dotnet-inspect skill signals
dotnet-inspect skill decompiler
dotnet-inspect skill performance
dotnet-inspect skill relationships
```

## Experimental features

These features are under active development. Their output shapes, section names,
and signal sets may change between releases.

### Performance analysis

Use `library -S @Performance` for a whole-assembly triage pass, `Top Leverage`
for ranking, `Resource Triage` for exception-path pool-churn candidates, and
`Call Graph` to drill one selected member. The performance skill covers the full
workflow in more depth.

```bash
dotnet-inspect library System.Text.Json -S @Performance
dotnet-inspect library System.Text.Json -S @Performance --count
dotnet-inspect library System.Text.Json -S "Performance: Boxing" --json -T q
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Call Graph"
```

### Decompiler

Use `member -S @Source` for decompiled C#, annotated source, PDB source, source
diff, and IL. Use `Fidelity Causes` when a body cannot be raised faithfully.

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S @Source
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Fidelity Causes"
dotnet-inspect library System.Text.Json --il-offset 0x060002EA+0x0
```

### Raw metadata

Metadata sections are opt-in only. Use `@Metadata` to discover or render decoded
table rows, and `--heap` for one exact heap address.

```bash
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -D @Metadata
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -S @Metadata --count
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -S "Metadata: TypeRef" --rows 20
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll --heap "#Strings:0x1a4"
```

## Output and querying

Default output is Markdown. For compact human scanning use `--table`; for
machine-friendly rows use `--tsv` or `--jsonl`; for structured graphs use
`--json`; for plain text use `--plaintext`; and for diagrams use `--mermaid`.
Use `-T q` to suppress tips in script-oriented commands.

| Goal | Flags |
| ---- | ----- |
| Discover available sections and fields | `-D`, `-D --schema` |
| Discover query facets and operators | `-Q`, `-Q "Body Shapes"`, `-Q @Performance` on library/type/member/package/find |
| Select sections or categories | `-S`, wildcards such as `-S "Async*"`, authored categories such as `-S @Source` or `-S @Audit` |
| Project columns/fields | `--columns`, `--fields` |
| Limit rows | `--rows`, `-n`, `--head`, `--tail` |
| Count results | `--count` |
| Materialize one payload | `--print`, `--row`, `--value`, `--bare`, `--paths`, `--urls`, `--json-array` |
| Control document verbosity | `-v:q`, `-v:m`, `-v:n`, `-v:d` |
| Control tip verbosity | `-T q`, `-T m`, `-T d` |
| Control package sources | `--offline`, `--source`, `--add-source`, `--nugetconfig`, `--http-timeout` |

`--table`, `--tsv`, and `--jsonl` render one section at a time, so pair them
with a concrete `-S` when querying sectioned output. Markdown and JSON can
represent multi-section documents.

Useful discovery and projection patterns:

```bash
dotnet-inspect library System.Text.Json -D
dotnet-inspect library -Q
dotnet-inspect type -Q "Body Shapes"
dotnet-inspect library -Q "Performance: Arrays" --json
dotnet-inspect member JsonSerializer --package System.Text.Json -D --schema
dotnet-inspect vocabulary -D
dotnet-inspect vocabulary -S "C# Body Kinds" --rows 10
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S @Audit
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions -S Integrations
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions -S "Integration: Logging"
dotnet-inspect library System.Diagnostics.DiagnosticSource -S "Integration: OpenTelemetry"
dotnet-inspect package System.Text.Json --path @readme --content --frontmatter
dotnet-inspect package Newtonsoft.Json -S "Package Info" --fields Version --value
dotnet-inspect project ./src/dotnet-inspect -S Skills --jsonl -T q
```

## Common examples

### Packages and feeds

```bash
dotnet-inspect package System.Text.Json
dotnet-inspect package System.Text.Json --versions
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --versions
dotnet-inspect package System.Text.Json -S Signals
dotnet-inspect package System.Text.Json -S "Signals,Audit: Artifact Text"
dotnet-inspect package System.Text.Json -S "Signals,Audit: Findings"
dotnet-inspect find --package-prefix Azure.AI -t 100 --tsv
```

Patternless `find --package-prefix PREFIX` streams latest listed package
metadata and exact `.nuspec` manifests without downloading package archives.
`-t` limits packages rather than flattened dependency rows. Supplying a pattern
keeps API-search behavior and may acquire package archives:

```bash
dotnet-inspect find JsonSerializer --package-prefix System.Text
```

### Projects and local assets

```bash
dotnet-inspect project ./src/dotnet-inspect -S Skills
dotnet-inspect project ./src/dotnet-inspect -S Skills --print --row 1
dotnet-inspect type Command --project ./src/dotnet-inspect
dotnet-inspect member Command --project ./src/dotnet-inspect -S "Member Index"
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -S Signals
```

For API and relationship commands, `--project` means an existing
`project.assets.json` restored-assets context. Passing a `.csproj` or project
directory only locates that file; dotnet-inspect does not restore or build.

### Types, members, and source

```bash
dotnet-inspect type string --shape
dotnet-inspect find JsonSerializer --platform System.Text.Json
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S @Source
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Finding Census" --json
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Calls
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Callers
dotnet-inspect type JsonSerializer --platform System.Text.Json -S "Source Files" --urls --json-array -T q
dotnet-inspect library System.Text.Json --il-offset 0x060002EA+0x0
```

### Compatibility and change tracking

```bash
dotnet-inspect diff --package Markout@0.33.0..0.35.2
dotnet-inspect diff --platform System.Runtime@9.0.0..10.0.0 --breaking
dotnet-inspect timeline --package Markout@0.33.0..0.35.2 --type Markout.MarkoutWriterOptions --members --at all
dotnet-inspect timeline --package System.Text.Json@8.0.0..9.0.0 --type System.Text.Json.JsonSerializer --members --at all
```

### Structural matching

```bash
dotnet-inspect match Left.Compute Right.Compute --library ./app.dll
dotnet-inspect match Left.Compute Right.Compute --library ./app.dll --implementation
dotnet-inspect match Sample.Encode --similar --library ./app.dll
dotnet-inspect match Sample.Encode --similar --library ./app.dll --assembly-wide --top 10
```

`--similar` ranks structural candidates for one seed method. It is a discovery
step, not a verdict: a rank establishes no relation, no semantic equivalence,
and no authorship or copying claim. Within one image, confirm a candidate by
re-running the pairwise form on the selected pair.

The default candidate population is the seed's declaring type. `--assembly-wide`
opts into whole-assembly retrieval, which costs materially more. `--top` bounds
rendered rows only; `--json` retains every candidate, per-method outcome,
blocker, and receipt regardless. `--max-results` and `--max-methods` move the
product retrieval limits themselves. In `--table`, `--tsv`, and `--jsonl`, the
ranked candidates are the only row shape; the seed, scope, disposition, receipt,
blockers, and disclosure are written to stderr so stdout stays single-shaped and
parseable.

Every ranked row prints a `Token` column holding the candidate's metadata token,
which the pairwise form accepts directly as the second operand. That keeps every
row addressable even when a name is ambiguous across overloads or property
accessors:

```bash
dotnet-inspect match 'Sample.Encode' 0x06000CF8 --library ./app.dll
```

A metadata token is a table row index, not a portable identity, so it addresses
a member only in the assembly that defines it. `match` resolves a token against
the image named by `--library` and fails when that image does not define the
row, rather than binding it to an unrelated member.

That distinction is visible when `--library` names a facade. If the seed's type
is forwarded, the ranked rows come from the assembly that actually defines them,
so the run names that assembly and the exact `--library` value to pass when
confirming a candidate:

```bash
dotnet-inspect match System.String.IsNullOrEmpty --similar --library ./System.Runtime.dll
```

Comparing candidates drawn from two different assemblies is not supported;
inspect each side on its own.

### Relationships and graphs

```bash
dotnet-inspect depends Stream --markdown --mermaid
dotnet-inspect dependency-evidence --package Newtonsoft.Json --tfm net8.0
dotnet-inspect dependency-evidence \
  --project ./src/dotnet-inspect \
  --nuspec ./artifacts/package.nuspec \
  -v:n
dotnet-inspect implements IEquatable --project ./src/dotnet-inspect -v:q
dotnet-inspect extensions string --project ./src/dotnet-inspect -v:q
dotnet-inspect graph integrations \
  --package Microsoft.Extensions.DependencyInjection.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging@10.0.0 \
  --package Microsoft.Extensions.Http@10.0.0 \
  --tfm net10.0 \
  --relationship integration.observed
```

### Workspace sharing and built-in guidance

```bash
dotnet-inspect workspace-state decode "$w"
dotnet-inspect workspace-state decode "$w" | jq
dotnet-inspect workspace-state encode --file workspace-state.json
dotnet-inspect skill list
dotnet-inspect demo list
```

## Requirements

.NET 10.0 SDK or later.

## LLM integration

dotnet-inspect is [designed for LLM-driven development](docs/llm-design.md).
The embedded skill (`dotnet-inspect skill`) is also distributed through the
[dotnet/skills](https://github.com/dotnet/skills) marketplace.

## Contributor and agent docs

Start with [AGENTS.md](AGENTS.md) for repository-wide engineering and workflow
rules. Use [docs/overview.md](docs/overview.md) when a change crosses subsystem
ownership boundaries, and [taste/skill-guidance.md](taste/skill-guidance.md)
when maintaining the embedded skill.

## License

MIT
