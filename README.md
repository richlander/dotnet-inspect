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
above. Contributors building this repository should use .NET 11 RC1 SDK
`11.0.100-rc.1.26425.128`.

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
| Restored projects | `type Command --project ./src/DotnetInspect.Cli`, `project ./src/DotnetInspect.Cli -S Skills --print`, `project ./src/DotnetInspect.Cli -S "Package README file"` | Uses an existing `project.assets.json` as restored-assets context for API lookup, relationship search, dependency package skills, and root package README files; restore/build first if dependencies changed. dotnet-inspect does not restore, build, or acquire missing packages. |
| Platform libraries | `library System.Private.CoreLib`, `library System.Text.Json --version 10.0.0`, `diff --platform System.Runtime@9.0.0..10.0.0` | Resolves installed SDK/runtime assemblies, including runtime-only implementation assemblies with no NuGet package. |
| Local assets | `library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll`, `package ./artifacts/MyLib.nupkg` | Useful for auditing local builds before publishing. |

Platform packs have distinct package, Platform, and direct-library views. For
example, `package Microsoft.NETCore.App.Ref@10.0.0` inspects the targeting-pack
container as an exact NuGet package, while
`library ./packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0/System.Runtime.dll`
inspects one manually downloaded or extracted DLL directly. A Platform
selection unwraps authorized pack DLLs without publishing its source pack as a
Package participant. These entry paths preserve different provenance and do
not infer Platform identity from a package or file name.

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
  --columns "ID;Label" -n 5 --format table
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

For an overview, explicitly select `Body Shape Summary` to group identical
rendered matches with an occurrence count:

```bash
dnx dotnet-inspect -y -- type StringBuilder --platform System.Private.CoreLib \
  --where "Kind=ObjectCreationExpression" -S "Body Shape Summary" \
  --columns "Match;Count"
```

`Body Shapes` retains individual occurrences. Keep `Member`, `Token`, and the
start/end line and column fields to locate each match in the method's rendered
C# body; these coordinates are not IL offsets or original source locations.
Hiding columns never groups rows. Summary row limits select groups without
reducing their occurrence counts; `--count` counts the selected view's rows.
Both views are available on `library`, `type`, and `member`.

Use `--format jsonl` for one machine-readable row per match or `--count` for the row
count. Bodies that cannot be reconstructed at full fidelity are reported on
stderr rather than mixed into structured output.

## Capability inventory

| Capability | Commands | Highlights |
| ---------- | -------- | ---------- |
| Package inventory | `package` | Metadata, versions, TFMs, file layout, direct dependencies, recognized ecosystem dependencies, rooted dependency hierarchy, vulnerability data, custom feeds, and NuGet config support. |
| Project package skills and docs | `project` | Section-driven direct-dependency rows from valid `skills/**/SKILL.md` files and root `README.md` files in the restored package cache. Use `--print --row N` to emit one selected document. Skill inventory values and complete documents that require containment become `[Text omitted: required containment]`; selected documents also report bounded code-point locations on stderr. |
| Query vocabulary | `vocabulary` | Product-owned stable values, operators, defaults, and applicability for rich queries. |
| Ecosystem catalog | `ecosystem` | Product-configured ecosystem packs, namespace hints, core/tool packages, demos, and known Integration bindings without package acquisition. |
| Library audit | `library` | Assembly identity, public key token, trim/AOT metadata, unsafe/interoperability signals, SourceLink, PDBs, references, resources, async methods, and body-shape search. |
| API and package discovery | `type`, `member`, `find` | Type search, member tables, docs, overload selection, generics, direct calls/callers, source, decompiled C#, IL, and package-prefix discovery. |
| API compatibility | `diff` | Package, platform, and library diffs with breaking/additive classification plus opt-in C#/IL, selected-member authored-source, complexity, and structural-cohort context. |
| Timeline correlation | `timeline` | Correlate API or member-body Findings across a package version range, with evaluation and transition views. |
| Implementation matching | `match` | Identity-agnostic structural equivalence for two unambiguously named methods, plus `--similar` seeded discovery that ranks structural candidates for one seed. |
| Structural clone discovery | `library`/`type`/`member -S "Clone Candidates"` | Workspace-scoped structural candidate ranking for an exact Library, Type, or logical Member seed, with independent Breadth and Discovery facets. |
| Relationships | `graph`, `depends`, `extensions`, `implements` | Integration graphs, type hierarchies, explicit package/nuspec/library/restored-project dependency graphs, reference graphs, extension methods/properties, implementors, and subclasses. |
| Direct dependency evidence | `depends -S Dependencies` | `depends` combines explicit roots, traversal, and normalized declaration/restored evidence in one sectioned document. |
| Package pruning policy | `depends -S Pruning` | Explicitly compares source-authorized direct dependency candidates with an exact installed runtime or ASP.NET Core platform inventory, without changing graph traversal. |
| Source mapping | `library`/`package -S "SourceLink: Files"`, `type -S "Source Files"`, `member -S "Source Locations"` / `"PDB Source"` | SourceLink URLs, member file/line locations, and token+IL-offset to source-line resolution. `PDB Source` is checksum-verified source acquired from the PDB-recorded local path, a caller-supplied Git clone (`--repo`), or remote SourceLink, in that order. |
| Performance analysis *(experimental)* | `library -S @Performance`, `library -S "Performance: Strings"`, `type`/`member -S "Performance Triage"`, `"Top Leverage"`, `"Resource Triage"`, `"Call Graph"` | Whole-assembly leverage ranking, exact string-materialization operations, actionable rewrite-shape detection, and exception-path resource-lifecycle candidates. |
| Decompiler *(experimental)* | `member -S @Source`, `member -S "Fidelity Causes"`, `member`/`type`/`library --where "Kind=<ID>"` | Decompiled C#, annotated source, IL, body-shape queries, and typed `DEC####` fidelity causes. |
| Raw metadata | `library -S @Metadata`, `library coordinate "#Strings:0x1a4"` | Decoded ECMA-335 metadata tables and heap addressing. |
| Workspace definition, inventory, and navigation | `workspace --package X --tfm TFM --share packet` | Author a durable format-3 Workspace definition without acquisition, or omit `--share` to realize and render typed top-level inventory. Repeat `--package` to compose Package Scope; add `--register-library`, `--register-package-prefix`, or `--register-ecosystem` for registration intent. `--packet` accepts a canonical Base64URL packet string. Add `--active-package N` on the direct inventory route for structural Library, Type, Member, and lens descriptors. |
| Package Queries | `package query ID --library-literal TEXT --tfm TFM`, `workspace --root-request TOKEN` | Qualify exact package IDs or bounded package-ID prefixes by an ordinal decoded-`ldstr` substring in each selected primary implementation library. Results remain package-grain and carry typed occurrence evidence plus exact Root reopening tokens. |
| Workspace sharing | `workspace-state encode` / `decode` | Convert the canonical browser/CLI base64url workspace packet to or from its bounded JSON shape without acquisition or execution. |
| Agent-friendly output | global flags | Markdown by default, compact `--format table`, normalized `--format tsv`, `--format jsonl`, `--format json`, Mermaid diagrams, section/field projection, `--count`, and row limiting. |

## Command inventory

| Command | Purpose |
| ------- | ------- |
| `package X` | Inspect NuGet metadata, versions, dependencies, TFMs, layout, and vulnerabilities. |
| `package activity --ecosystem NAME` | Report bounded recent package activity for an ecosystem-selected package population, with source coverage and security evidence. |
| `project [path]` | Inspect restored project package skills and package docs. |
| `library X` | Inspect assembly metadata, symbols, SourceLink, references, resources, async methods, and rendered body shapes. |
| `library query DIR` | Query a directory or `--platform` reference pack as a bounded Library population; `references=NAME` qualifies direct assembly references. |
| `type X` | Discover types or render a single type shape. |
| `member X` | Inspect members, docs, overloads, decompiled/lowered C#, rendered body shapes, checksum-verified PDB source, and IL. |
| `find [X]` | Search for types across packages, frameworks, projects, and local assets. Add `--members` (or lead the query with `.`, such as `.Serialize`) to search member names instead. Use `--package-prefix PREFIX` with a type/member pattern to expand package scope. |
| `diff X` | Compare API surfaces by default; opt into analysis or implementation evidence. |
| `timeline X` | Correlate API or member-body Findings across a package version range. |
| `graph integrations` | Induce extension, observed Integration, and Integration-opportunity relationships over an explicit package set; `-n`, `--tail`, and `--rows` select complete logical edges after graph construction. |
| `graph calls TYPE MEMBER` | Explain one package member's outgoing calls that cross assembly boundaries, retaining only boundary calls and their shortest local connectors. |
| `graph libraries` | Show exact resolved cross-library calls, direct-use clusters, and public entrypoint paths to one selected cluster. |
| `depends [Type]` | With a positional type, walk its hierarchy inside `--package`, `--library`, `--project`, or platform search scopes. Without a positional type, combine repeatable explicit `--package`, `--nuspec`, `--library`, and `--project` roots, or exclusive `--package-prefix`, into one dependency graph and evidence document. |
| `extensions X` | Find extension methods and C# extension properties for a type. |
| `implements X` | Find concrete implementors or subclasses. |
| `match A B` | Compare two unambiguous `Type.Member` names by identity-agnostic structural equivalence; add `--body` for decompiled C# and IL body differences. |
| `match A --similar` | Rank structural candidates for one seed method, within a single assembly. Ranks candidates only; it establishes no relation. |
| `vocabulary` | Discover product-owned query vocabularies such as `Accessibility`, `C# Style Choices`, and `C# Body Kinds`. |
| `ecosystem [name]` | Inspect the ecosystem knowledge configured into this product build. Omit the name to list packs; use `-S Integrations` for configured Integration concepts, distinct from observations in a library. |
| `workspace` | Render the typed top-level inventory of one ephemeral Workspace: committed ordered Package occurrences first, then inert Exact Library, Package Prefix, and Ecosystem registrations. Repeat `--package ID@VERSION` coordinates and supply `--tfm`; add `--register-library PACKAGE@VERSION/ASSEMBLY@ASSEMBLY_VERSION`, `--register-package-prefix PREFIX`, or `--register-ecosystem ID`; filter with repeatable `--kind`. Restore a current-format canonical Workspace packet with `--packet PACKET`, or use `--root-request TOKEN` to reopen the exact Package Root a `package query --library-literal` result names. Add `--active-package N` on direct construction to evaluate the exact occurrence and expose its Navigation hierarchy, Library asset IDs, Type and Member inventories, lenses, and diagnostics. |
| `workspace-state encode` / `decode` | Convert validated workspace-state JSON and canonical base64url packets; pass `-` for stdin or use `--file`. |
| `skill` | Print the base LLM skill and route to focused built-in guidance (`skill list`, `skill query`, `skill decompiler`, `skill relationships`, and more). |
| `demo [id]` | List or run product-home inspection demos backed by real section output. |
| `cache` | Inspect or clear dotnet-inspect caches. |

## Signals, integrations, and focused guidance

`Signals` is an evidence report, not a safety certification. Use `-S Signals`
for a compact package or library overview, then opt into deeper audits only when
you need them.

Observed integration support is exposed through one `Integrations` section.
Use `integration=<canonical-concept-id>` to focus one concept, or
`ecosystem=<canonical-pack-id>` to enable the Integration concepts registered
to an ecosystem. The current Aspire registration enables the complete
configured Integration catalog; only concepts observed in the inspected
library produce rows. Add `integration=...` to narrow within that enabled set.
`@Integrations` also includes the separate `Integration Opportunities` section.

Use `ecosystem` to inspect which ecosystem packs and Integration bindings are
configured into this build. This is catalog knowledge, not evidence from an
acquired library:

```bash
dotnet-inspect ecosystem
dotnet-inspect ecosystem aspire
dotnet-inspect ecosystem aspire -D
dotnet-inspect ecosystem aspire -S @Ecosystem
dotnet-inspect ecosystem aspire -S Integrations
dotnet-inspect ecosystem ai -S "Core Packages"
dotnet-inspect ecosystem azure -S "Core Packages"
dotnet-inspect ecosystem blazor -S "Core Packages"
dotnet-inspect ecosystem maui -S "Core Packages"
dotnet-inspect ecosystem microsoft-extensions -S "Core Packages"
dotnet-inspect ecosystem runtime -S Pruning
```

Package Info and Library Info summarize ecosystems recognized from direct
dependencies. Select `Ecosystem Dependencies` to see the dependency/ecosystem
pairs, including one row per ecosystem when a dependency intentionally
overlaps multiple packs:

```bash
dotnet-inspect package Microsoft.Extensions.Http@10.0.0 \
  -S "Package Info"
dotnet-inspect library --platform System.Text.Json \
  -S "Library Info"
dotnet-inspect library --platform System.Text.Json \
  -S "Ecosystem Dependencies" \
  --columns "Ecosystem,Kind,Dependency,Declared By"
```

Library recognition classifies the selected Library's declared assembly
references. It does not resolve or traverse those references. Unrecognized
references do not become ecosystem rows, while JSON still reports the
recognition status as complete.

`Core Packages` are inert registered package roots. Catalog inspection performs
no source work; a later bounded operation that selects the ecosystem may resolve
those concrete packages and follow their ordinary dependencies. Package-prefix
matches remain discovery scope and are not substituted for those roots.

Use `package activity --ecosystem` to report package activity in one named
ecosystem's exact product-owned package set. The ecosystem option selects where
to look; `ecosystem` itself remains the acquisition-free vocabulary command.
This network-backed query defaults to the interval
`(reference time - 42 days, reference time]`, reports the exact UTC bounds and
source horizon, and overlays current GitHub-reviewed advisory context and
evidenced security releases:

```bash
dotnet-inspect package activity --ecosystem aspire
dotnet-inspect package activity --ecosystem aspnetcore --security-only
dotnet-inspect package activity --ecosystem microsoft-extensions -n 25 --format json
dotnet-inspect package activity --ecosystem aspire \
  --from 2026-02-01T00:00:00Z \
  --through 2026-03-01T00:00:00Z
```

`--from` is exclusive and `--through` is inclusive; specify both with explicit
UTC offsets, and keep the interval at 42 days or less. `--security-only` keeps
activity with positive current-advisory or exact security-release evidence.
Unavailable evidence is not treated as a negative. Human output uses the shared
report view; `--format json` emits the lossless schema-versioned report, while
`--envelope` emits the same report as Content with Share and diagnostics.
`--compact` minifies either JSON boundary. Use `--verbose` for bounded
acquisition progress on stderr. Single-table formats and catalog-only section
projections are not available with `package activity`.

`ecosystem runtime -S Pruning` is the exception to "catalog knowledge": it reads
the reference pack installed on this machine to list the package identities the
platform target supplies, so a reference to one resolves to the platform rather
than to the package. `Kind` separates a version that moves with the framework
(`live`) from one pinned to a release the framework has passed (`frozen`).

All integrations are enabled by default. Discover the supported ecosystem
predicate with `library -Q Integrations`, then narrow the ordinary result:

```bash
dotnet-inspect library -Q Integrations
dotnet-inspect library Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S Integrations --where "ecosystem=ecosystem.aspire"
dotnet-inspect library ./MyLibrary.dll -S Integrations \
  --where "integration=integration.aspire" --format jsonl
```

These facets filter Integration evidence and opportunities, not assembly-wide
presence or Census. When both are supplied, they intersect. Other query
families cannot be combined with either Integration facet.

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
dotnet-inspect library System.Text.Json -S "Performance: Boxing" --format json -T q
dotnet-inspect library System.Text.Json -S "Performance: Strings" --format json -T q
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Call Graph"
```

### Decompiler

Use `member -S @Source` for decompiled C#, annotated source, PDB source, source
diff, and IL. Use `Fidelity Causes` when a body cannot be raised faithfully.
In Inspect Web, **All** also reveals exact direct-call relationships at their
source locations; these remain outside the default Finding set. **Explore**
starts with one Relationships row per exact physical call, with explicit
call-site inspection and **Member** or **Source** target actions. An opt-in
**Diagram** groups repeated physical calls by their stable logical edge while
keeping every exact call site available through the table. Selecting a
recursive relationship shows its exact direct or mutual cycle witness and
whether the bounded focus-graph census was complete. Selecting a framework
`Task.Wait`, `Task<T>.Result`, or task-awaiter `GetResult` relationship also
shows the exact synchronous-completion structure without claiming that runtime
blocking was measured. Selecting a proven classic `await` explains its inline
and suspension/resume paths, while selecting an exception-related allocation
distinguishes a thrown value from an allocation inside a catch, filter, or
fault handler. A relationship can also show a bounded direct-call path to a
method containing an Analysis-proven local `throw new`, including its exception
type and physical construction and throw offsets. These are compiled-structure
claims only: they do not prove that a path ran, that a throw escapes, or that an
exception propagates to the selected method.

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S @Source
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Fidelity Causes"
dotnet-inspect library coordinate 0x060002EA+0x0 \
  --package System.Text.Json --library System.Text.Json.dll
dotnet-inspect library coordinate --file coordinates.txt \
  --library ./MyLibrary.dll
```

Coordinate files accept up to 1,024 significant records. Blank and comment
lines are ignored; valid and malformed records remain interleaved in source-file
order so row windows select the same records the producer supplied.

### ReadyToRun and raw metadata

ReadyToRun and metadata sections are opt-in only. Use `@ReadyToRun` for the
validated image header and section directory. Use `@Metadata` to discover or
render decoded ECMA-335 table rows, `--metadata-root r2r-manifest` to inspect
the ReadyToRun manifest metadata instead of the default CLI root, and
`library coordinate` for one exact heap address in the selected root.

```bash
dotnet-inspect library System.Private.CoreLib -S @ReadyToRun
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -D @Metadata
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -S @Metadata --count
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -S "Metadata: TypeRef" --rows 20
dotnet-inspect library coordinate "#Strings:0x1a4" \
  --library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll
dotnet-inspect library System.Private.CoreLib --metadata-root r2r-manifest -S "Metadata: Image"
```

## Output and querying

Default output is Markdown. For compact human scanning use `--format table`;
for machine-friendly rows use `--format tsv` or `--format jsonl`; for
structured documents use `--format json`; for plain text use
`--format plaintext`; and for standalone diagrams use `--format mermaid`.
Add `--mermaid` to Markdown output to embed supported diagrams. Use `-o` or
`--output` to write the selected output to a file instead of stdout; destination
does not select a format. `--format` is long-only; `-f` remains available for
framework selection where supported. Use `-T q` to suppress tips in
script-oriented commands. See
[CLI Output Format and Destination](docs/design/cli-output-format.md) for the
complete contract.

```bash
dotnet-inspect package System.Text.Json --format json
dotnet-inspect package System.Text.Json --format json \
  --output artifacts/system-text-json.json
```

Positional `depends <type>`, ordinary single-Library API `diff`, `package
activity`, ordinary and `--library-literal` Package Query, and online package
range-version population, and exact package-backed Type or Library API
inspection support the presence-only `--envelope` service-output selector. It
implies JSON. For `depends`, API Diff, Package Activity, Package Query, and
exact Type or Library API inspection, unprojected `--format json` emits the same
Content without the service frame. Package version `--format json` remains an explicit
row projection;
`--envelope` instead exposes the complete directed population Document, Share,
and diagnostics. Asset-mode `depends`, other Diff modes, Discover, Count outside
package population, projected output, other Type modes, and other commands have
not adopted this transport.

| Goal | Flags |
| ---- | ----- |
| Discover available sections and fields | `-D`, `-D --schema` |
| Add Library format details | `-D --details`, `-D <exact-name> --details` |
| Discover query facets and operators | `-Q` on library/type/member/package/find; e.g. `library -Q @Performance` or `type -Q "Body Shapes"` |
| Select sections or categories | `-S`, wildcards such as `-S "Async*"`, authored categories such as `-S @Source` or `-S @Audit` |
| Project columns/fields | `--columns`, `--fields` |
| Limit semantic rows or rendered lines | `--rows`, `-n`, `--head`, `--tail`, `--lines`, `--tail-lines` |
| Count results | `--count` |
| Materialize one payload | `--print`, `--row`, `--value`, `--bare`, `--paths`, package-file `--roots`, `--urls`, `--json-array` |
| Prefer browser views over fetchable URLs | `--prefer-rendered-urls` (keeps the original URL when no mapping is available) |
| Control document verbosity | `-v:q`, `-v:m`, `-v:n`, `-v:d` |
| Control tip verbosity | `-T q`, `-T m`, `-T d` |
| Control package sources | `--offline`, `--source`, `--add-source`, `--nugetconfig`, `--http-timeout` |

`--format table`, `--format tsv`, and `--format jsonl` render one section at a time, so pair them
with a concrete `-S` when querying sectioned output. Markdown and JSON can
represent multi-section documents.

Source URLs are fetchable by default. `--prefer-rendered-urls` prefers a browser
view when supported; it changes neither `--print` acquisition nor `--bare`
decoration. The old `--raw` and `--blob` flags are no longer accepted.

`-n N` selects the command's items. It selects semantic rows when the active
command or lens declares them; otherwise it selects the first N rendered lines.
Add `--tail` for the last N items. `--lines` explicitly selects rendered lines
on a semantic-row command, and `--tail-lines` selects rendered lines from the
end. Rendered-line clipping is not available with JSON document output.

Useful discovery and projection patterns:

```bash
dotnet-inspect library System.Text.Json -D
dotnet-inspect library System.Text.Json -D --details
dotnet-inspect library System.Text.Json -D @Dependencies --details
dotnet-inspect library System.Text.Json -D "Reference Hierarchy" --details
dotnet-inspect library -Q
dotnet-inspect type -Q "Body Shapes"
dotnet-inspect library -Q "Performance: Arrays" --format json
dotnet-inspect member JsonSerializer --package System.Text.Json -D --schema
dotnet-inspect vocabulary -D
dotnet-inspect vocabulary -S @Decompiler
dotnet-inspect vocabulary -S "C# Body Kinds" -n 10
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S @Audit
dotnet-inspect library System.Text.Json -S References
dotnet-inspect library System.Text.Json -S "Reference Hierarchy" --tree
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions -S Integrations
dotnet-inspect library Microsoft.Extensions.Logging.Abstractions \
  -S Integrations --where "integration=integration.logging"
dotnet-inspect library System.Diagnostics.DiagnosticSource \
  -S Integrations --where "integration=integration.opentelemetry"
dotnet-inspect package System.Text.Json --path @readme --content --frontmatter
dotnet-inspect package Newtonsoft.Json -S "Package Info" --fields Version --value
dotnet-inspect project ./src/DotnetInspect.Cli -S Skills --format jsonl -T q
```

Library `-D --details` is structural and does not acquire the target. It adds a
`Formats` column to the top-level catalog, or reports one exact category or
section in detail. A category reports the formats supported by its complete
expansion plus the formats of each member; it never selects or drops members to
satisfy a format. Use the result to choose an exact section before requesting a
single-result projection such as `--tree` or `--format mermaid`.

## Common examples

### Packages and feeds

```bash
dotnet-inspect package System.Text.Json
dotnet-inspect package System.Text.Json --versions -n 6
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --versions
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --versions --envelope
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --count
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --count --envelope
dotnet-inspect package Newtonsoft.Json@13.0.4 \
  --tfms -n 1 --tail --format json
dotnet-inspect package System.Text.Json -S Signals
dotnet-inspect package System.Text.Json -S "Signals,Audit: Artifact Text"
dotnet-inspect package System.Text.Json -S "Signals,Audit: Findings"
dotnet-inspect package Newtonsoft.Json@13.0.4 \
  --layout --tfm net6.0 -n 1 --tail --format json
dotnet-inspect package Markout@0.35.2 \
  --path "skills/*/SKILL.md" -n 1 --tail --paths
dotnet-inspect package Microsoft.Data.SqlClient@6.1.0 \
  --tfm net8.0 -S "Package files" --paths
dotnet-inspect package Microsoft.Data.SqlClient@6.1.0 \
  --tfm net8.0 -S "Package files" --roots
dotnet-inspect package Newtonsoft.Json@13.0.3 \
  -S "SourceLink: Files" -t JsonReader -n 1 --tail --urls
packet=$(dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --share packet)
dotnet-inspect package System.Text.Json --workspace "$packet"
dotnet-inspect package System.Text.Json \
  --workspace "$packet" --share packet
dotnet-inspect package query 'Azure.AI*' --take 100 --format tsv
```

`package ID[@VERSION] --workspace PACKET` inspects the matching direct Package
in the packet's selected context, independently of its focused tab, and reuses
the exact Package Root and target admitted during Workspace restoration.
Appending `--share` preserves ordinary stdout and writes a derived Package
packet or URL as the final stderr line. An exact selector can inspect a
currently resolved floating Package member, but Share refuses rather than
silently pinning that preserved definition.

For one package with exactly `Package files` selected, `-n`, `--tail`, and
`--rows A..B` select complete path/size rows after archive extraction, file
enumeration, optional exact directory-segment `--tfm` filtering, and optional
`--path` filtering. Count, table, TSV, JSONL, JSON, `--value`, and `--paths`
observe the same selected rows; `--roots` instead emits their ordered distinct
top-level package roots. Add `--lines` only to clip rendered text.

For one package with `--layout`, `-n`, `--tail`, and `--rows A..B` select
complete sorted file paths after archive extraction and `--lib`, `--tools`, or
layout-specific `--tfm` scoping. Count, JSONL, and JSON observe the same
selected paths; human output renders a tree derived from them. Add `--lines`
only to clip the rendered tree.

For one package with `--tfms`, `-n`, `--tail`, and `--rows A..B` select
complete target-framework rows after archive extraction, framework
de-duplication, and TFM-priority ordering. Count, table, TSV, JSONL, and JSON
observe the same selected rows; add `--lines` only to clip rendered text.

For one package with exactly `SourceLink: Files` selected, `-n`, `--tail`, and
`--rows A..B` select complete library/type/URL rows after SourceLink collection
and `--type` filtering. Count, table, TSV, JSONL, JSON, `--urls`, and `--bare`
observe the same selected rows; add `--lines` only to clip rendered text.

Online range-version population is metadata-only: it enumerates versions
without acquiring a package payload. `--count` projects the version Count as a
scalar, including with `--format json`; `--count --envelope` makes that same integer
the Content of `InspectionEnvelope<int>`. Without Count, `--envelope` retains
the complete directed version Document. `--preview`, `--include-unlisted`,
configured source options, and `--versions-with-feed` remain semantic
population inputs.

`package query ID` selects one exact package ID. A single terminal `*` selects
a literal package-ID prefix. Explicit `--take` bounds candidate work before
`-n` selects final package rows. Without explicit `--take`, a simple `-n N`
also bounds direct package-row acquisition to N, up to the 1,000-candidate
execution ceiling. Larger semantic heads remain valid and use that ceiling.
`find PATTERN --package-prefix PREFIX` remains API search across
packages matching the prefix:

```bash
dotnet-inspect find Serialize --members --type System.Text.Json.JsonSerializer \
  --package-prefix System.Text
```

Use `depends=<package-id>` to require a direct dependency. Package Query
considers all package manifest groups by default; add
`dependency-target=<TFM>` to select one applicable dependency group instead.
`dependency-target=all` spells the default explicitly and remains distinct
from a manifest's real `any` group. Repeat `depends` to require every named
dependency under the same scope. Use
`depends starts-with <literal-package-id-prefix>` to require a direct
dependency whose package ID begins with that prefix. The match is literal and
case-insensitive; include a trailing `.` to express a dot-delimited family.
Use
`depends-ecosystem=<canonical-ecosystem-id>` to match a direct dependency
against the ecosystem's registered exact packages and package prefixes:

```bash
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where "depends=Microsoft.Extensions.DependencyInjection"
dotnet-inspect package query 'Polly.*' \
  --where "depends=System.Threading.Tasks.Extensions" \
  --where "dependency-target=netstandard2.0"
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where "depends=Microsoft.Extensions.DependencyInjection" \
  --where "depends=Microsoft.Extensions.Configuration" --count
dotnet-inspect package query Microsoft.Extensions.Http \
  --where "depends starts-with Microsoft.Extensions." \
  --where "dependency-target=net10.0"
dotnet-inspect package query Aspire.Hosting.PostgreSQL \
  --where "depends-ecosystem=ecosystem.aspire"
```

Use `depends-transitive=<package-id>` for source-authorized declared-range
reachability beyond a direct dependency. It requires one exact
`dependency-target=<TFM>` and an explicit `dependency-depth=2|3|4`; the
expensive query is limited to five package candidates:

```bash
dotnet-inspect package query Microsoft.Extensions.Http \
  --where "depends-transitive=Microsoft.Extensions.Primitives" \
  --where "dependency-target=net10.0" \
  --where "dependency-depth=2" --take 1
```

The result is not a NuGet restore claim. Evidence counts matching declaration
edges and previews deterministic shortest paths built from declared ranges and
resolved exact package coordinates. The shared 160-character display budget
may shorten a preview, so it is not a complete path record or package
coordinate. A direct-only dependency does not satisfy the transitive term, and
incomplete traversal remains a visible failure.

Use `dependencies=cross-prefix` to find packages with a direct dependency from a
different first dot-delimited package-ID segment. It uses the same
`dependency-target` scope and remains nuspec-only:

```bash
dotnet-inspect package query 'Azure.*' \
  --where "dependencies=cross-prefix"
```

Use `references=<simple-assembly-name>` to find packages whose managed `ref/`
or `lib/` assemblies declare that `AssemblyRef` across any target-framework
group:

```bash
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where "references=Microsoft.Extensions.DependencyInjection.Abstractions" \
  --take 20 -n 5
```

This package-content term matches simple names case-insensitively and reports
the matching framework and archive path. It does not resolve or traverse the
reference.

Use the same key at Library grain to query top-level `*.dll` files in one
directory, or one installed or explicitly acquired platform reference pack:

```bash
dotnet-inspect library query ./artifacts/bin \
  --where "references=System.Text.Json"
dotnet-inspect library query --platform runtime \
  --where "references=System.Text.Json" --take 256 -n 10
```

Library Query tests each candidate Library's direct `AssemblyRef` table;
repeated `references` terms are ANDed. `--take` bounds candidates scanned,
while `-n`, `--head`, `--tail`, and `--rows` select matching Library rows
afterward. Use `library query -Q Libraries` to discover the current vocabulary.
Missing or malformed Metadata remains visible and makes unbounded Count
inexact rather than silently becoming a nonmatch.

License selection also stays at the manifest boundary. `license=any` matches
any nuspec license declaration. Closed semantic values match nuspec metadata
without reading a license document: SPDX expressions match their exact
expression, and `license=OSMF` recognizes a declared `OSMFEULA.*` file:

```bash
dotnet-inspect package query wix \
  --where "license=OSMF" --nuspec-only
dotnet-inspect package query Newtonsoft.Json \
  --where "license=MIT" --nuspec-only
```

Neither query opens the package archive. To inspect the license documents that
the package actually ships, use the separate package-file section:

```bash
dotnet-inspect package wix@7.0.0 -S "Package license files"
dotnet-inspect package wix@7.0.0 -S "Package license files" --count
dotnet-inspect package wix@7.0.0 -S "Package license files" --print --bare
```

Package Query places the semantic result in `Answer`: `MIT` for the
Newtonsoft.Json query, `OSMF` for the WiX query, and `true` for
`license=any`. Supporting nuspec
declaration kind and value remain separate evidence. `--count` already emits
only the scalar count; `--bare` is useful with `--print` when only the selected
document body is wanted without package or section framing.

The exact nuspec `<license type="file">` target is always included. The same
section also finds conventional text or Markdown license names and license
directories; notices remain a separate legal-document concern. Reading that
content is an explicit package projection and never informs license identity.

Add `--where "key=value"` to select product-owned Package Query terms, with one
matched package per row and semantic answers. Structured evidence remains
available in unprojected JSON and the inspection envelope. The initial CLI
vocabulary covers package metadata, direct and bounded transitive dependencies,
cross-prefix and ecosystem dependency classification, downloads, README presence, .NET tools
and their CLI v1/v2 format, assembly references, skill packages, and nuspec
license identity. Discover the admitted keys and values before constructing a
query. Discovery also reports the product-owned execution class independently
from the acquisition tier:

```bash
dotnet-inspect package query -Q Packages
dotnet-inspect package query Azure.Mcp \
  --where "tool=true"
dotnet-inspect package query 'Azure.Mcp*' \
  --where "tool-format=v2" --take 20 -n 5 --format jsonl
```

Repeat `--where` to combine terms; the engine rejects incompatible selections.
The broad `tool=true` term identifies the .NET tool package type from manifest
evidence. Use `tool-format=v1` or `tool-format=v2` for settings-based format
classification; the two specific formats are compatible filtering
alternatives. Selecting a package-content term authorizes the required archive
acquisition and defaults to at most 20 candidates. Use `--nuspec-only` to
reject a query that would require package content. Without explicit `--take`, a
simple `-n N` query pushes that semantic head into execution; explicit
`--take` instead fixes the candidate population before row selection. Reached
candidate limits and partial failures are reported explicitly. `--count`
counts selected matching package rows only when completion or the semantic
selection proves the count exact.

Package Query output adapts after execution. The default renders `Packages`
when at least one package matched and `Query Summary` otherwise. The summary
reports independent `Candidates`, `Matches`, and `Evaluation Failures` integer
columns, so a missing package (`Candidates=0`) remains distinct from an
existing package rejected by `--where` (`Candidates=1`, `Matches=0`). Select a
stable shape explicitly with `-S Packages` or `-S "Query Summary"`; explicit
`Packages` retains its empty table or array when no package matched. Bare `-S`
also requests the non-adaptive `Packages` preset. Select `@Query` to compose
`Packages` and `Query Summary` in Markdown or JSON; table, TSV, and JSONL remain
one-section formats.

**Breaking change:** `package search` and patternless
`find --package-prefix PREFIX` have been removed. Use `package query` with an
exact package ID or an explicit terminal-star prefix.

### Exact Find handoff

An exact-version Package or explicit Platform Library search with `--tfm`
uses one short-lived Workspace internally and preserves each declaration's
exact coordinate and observation context without changing Find's established
result presentation:

```bash
dotnet-inspect find System.Text.Json.JsonSerializer \
  --package System.Text.Json@10.0.0 \
  --platform System.Text.Json \
  --tfm net10.0
```

Default Markdown, tips, table formats, and plain type-search JSON retain their
existing shapes; plain JSON remains a root result array. When a selected
Package row feeds Type or Member inspection internally, the handoff preserves
the selected package-relative implementation asset and compatible TFM rather
than reopening a same-named reference assembly. Platform implementation-pack
observations decline exact internal reopening because public Platform syntax
resolves the reference view.

Repeat `--ecosystem ecosystem.ID` to register exactly those ecosystem packs in
caller order. Without it, Find registers every shipped ecosystem. Registration
is inert: it does not execute package-prefix discovery or add package content
to the search.

### Package Query over selected implementation libraries

`package query ... --library-literal TEXT` qualifies package Results using an
assembly-semantic query. An exact package ID selects its latest eligible listed
version. A terminal-star package-ID prefix evaluates at most five candidates by
default; use `--take 1..5` to choose the candidate bound. The query selects the
primary implementation library of each candidate for an explicit `--tfm`.

```bash
dotnet-inspect package query Newtonsoft.Json \
  --library-literal "Unexpected end when reading JSON" --tfm net6.0

dotnet-inspect package query 'Azure.Identity*' \
  --library-literal "DefaultAzureCredential" --tfm net8.0 --take 5
```

`TEXT` is a raw ordinal substring, not a type pattern: it is case-sensitive,
matches no wildcards, and preserves whitespace and Unicode spelling exactly.
The query covers selected primary implementation assemblies only, not every
assembly in a package. One output row is one matching package; occurrence count
and method-token/IL-offset previews are evidence on that package Result.
`-n` and `--rows` select package rows, while `--take` bounds package candidates.
Candidate failures remain visible and prevent an unqualified Count. Candidate
packages are disposable: they are never added to the package cache.

For both ordinary Package Query and `--library-literal`, unprojected `--format json`
emits the complete owner-issued Content. `--envelope` emits that same Content
with Share and diagnostics, using result kind `package-query` or
`package-assembly-semantic-query`. Query controls such as `--where`, `--take`,
`--tfm`, and `--library-literal` remain admitted; row selection, Count,
projection, discovery, section selection, and competing formats are rejected.
Typed incomplete or failed Content is still emitted before a nonzero exit.

Each evaluated candidate carries a `Root` reopening token. Hand that token back
to reopen exactly the Root the result came from:

```bash
dotnet-inspect workspace --root-request TOKEN
```

The token is opaque, credential-free, and exact. `workspace --root-request`
rejects a token this tool did not issue, and reports an unauthorized producer
or unavailable content instead of opening a different Root that happens to
share the package id and version.

### Workspace inventory and structural navigation

Append `--share packet` or `--share url` to author the complete durable
Workspace definition. Ordinary authoring performs no Package acquisition or
live Workspace construction. The selected scalar is the `workspace` command's
result on stdout, rather than the additive stderr side output used by noun
commands:

```bash
dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --register-package-prefix Microsoft.Extensions. \
  --share packet
```

Direct Package and registration inputs become one schema-version-3 definition
and canonical format-3 packet. Registration-only authoring also remains
resource-free. Normalized-equivalent Package coordinates are emitted once,
while registration options retain their authored cross-kind order. A
scanner-bearing Ecosystem fails visibly as non-projectable; the command never
drops its scanner to manufacture a packet.

Add `--make-package-dependencies-explicit` to acquire every direct Package
member, resolve its exact direct dependencies for the member's effective
target, and append those dependencies to the same context before emitting the
derived packet:

```bash
dotnet-inspect workspace \
  --package Microsoft.Extensions.Logging.Abstractions@10.0.0 \
  --tfm net10.0 \
  --make-package-dependencies-explicit \
  --share packet
```

The transformation also accepts a canonical Base64URL packet string through
`--packet`. Existing members retain their order; new members use owner-issued
dependency order and are deduplicated only within each context. The command
emits no packet unless every selected Package root completes and the complete
derived definition remains projectable. Unlike ordinary resource-free
`--share`, this explicit transformation admits `--preview` and NuGet source
policy because acquisition is part of the requested operation.

`--packet` accepts canonical Base64URL packet text. With `--share`, it validates
the input and emits the canonical packet or selected URL without complete
restoration. The `--share` selection governs this output scalar; inventory
output formats apply only when `--share` is absent.
Durable definition output cannot be combined with `--kind`, inventory row
controls, `--root-request`, or Package Navigation selectors. Explicit NuGet
source policy is accepted only for dependency enrichment or coordinate
replacement; `--preview` is accepted only for dependency enrichment.

To replace one direct Package coordinate in an existing scenario, use its
one-based **navigation-row order**, not inventory order. Unlike authoring,
this explicit transformation acquires and realizes the input and destination:

```bash
dotnet-inspect workspace --packet "$w" \
  --replace-package 1 --to-version 12.1.2 --share packet
dotnet-inspect workspace --packet "$w" \
  --replace-package 1 --to-tfm net9.0 --share url
dotnet-inspect workspace --packet "$w" \
  --replace-package 1 --to-version 12.1.2 --envelope
```

Use `--to-version`, `--to-tfm`, or both, and select either scalar `--share`
output or `--envelope`. The envelope includes the derived Share,
actual Scope outcome, retention/fallback decision and diagnostics. Scalar
output contains only the complete resulting packet/URL; fallback diagnostics
go to stderr. No input packet is emitted as a successful failure fallback.
Source options are allowed for this acquisition-backed route.

Replacement preserves unrelated contexts, registrations, row order, focus,
and committed views. Format 4 can retain an active Library, Type or Member
and its exact inspector. For example, changing `Avalonia@11.3.14` to `12.1.2`
on `net8.0` follows `Avalonia.Data.MultiBinding` from `Avalonia.Markup`
to its defining `Avalonia.Base` Library; its constructor can follow separately.
An explicitly active Library stays paired with that Library instead.
Both Versions must be exact pins. Changed TFMs require an unsubscribed
single-member context; shared-context TFM changes, floating selected sources,
ambiguous source positions and query-bearing scenarios are visibly refused.
Do not combine replacement with dependency enrichment, direct construction,
inventory controls, or
`--active-package`/Library/Type/Member/inspector selectors: the packet owns that
intent. Browser coordinate-control and capture adoption remain separate.

The default `workspace` output is the typed top-level inventory. Package
occurrences appear in committed Scope order, followed by inert registrations
in declaration order. Overlap is preserved because committed content and
registration intent are different facts:

```bash
dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 \
  --package Markout@0.35.2 \
  --tfm net10.0 \
  --register-library System.Text.Json@10.0.0/System.Text.Json@10.0.0.0 \
  --register-package-prefix Microsoft.Extensions. \
  --register-ecosystem aspire
```

Use repeatable `--kind package|exact-library|package-prefix|ecosystem` to
select inventory kinds without changing Workspace construction. `-n N`,
`--tail`, and `--rows A..B` select complete typed entries after that filter;
`--count` observes the selected entries, while `--lines` explicitly selects
rendered lines. JSON and JSONL retain the typed entry arms and their portable
details. `--verbose` adds each Package producer,
requested/selected/effective target, runtime identifier, and asset-selection
status to human output.

Restore one current-format canonical Base64URL Workspace packet string for
inventory instead of supplying direct construction options:

```bash
dotnet-inspect workspace --packet PACKET
```

Packet input is mutually exclusive with direct Package and registration
construction. Workspace Definitions performs complete restoration, including
group and non-Package context intent and retained Navigation state, before the
CLI enters the inventory operation. CLI refinement of that restored Navigation
state is intentionally deferred. Without `--replace-package`, `--packet`
combines only with inventory controls when `--share` is absent.

`workspace` never selects an occurrence implicitly, even when the Workspace
contains exactly one Package.

Add `--active-package N` to evaluate one exact occurrence by its one-based
Workspace order. The detailed result includes the active subject, complete
Workspace-to-Member hierarchy slots, Library asset IDs, bounded Type and Member
inventories, target-aware lens availability, and retained diagnostics.
For this CLI consumer, the current active catalog entries are available once
Navigation admits the exact subject because those entries execute on demand;
query and result non-success remains inside the selected entry. This does not
prevent another producer from supplying explicit unavailable or failed
availability evidence to the Navigation evaluator:

```bash
dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --active-package 1
```

Library asset IDs, Type full names, and Member stable selectors in that output
can drive an exact stateless descendant plus lens request. Copy those selector
fields verbatim: line separators, tabs, rendering hazards, and literal
backslashes, plus characters reserved by Markdown tables, use a reversible
backslash transport spelling that `workspace` decodes before exact selection:

```bash
dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --active-package 1 \
  --library compile:lib/net10.0/System.Text.Json.dll \
  --type System.Text.Json.JsonSerializer \
  --lens type.compare
```

Add `--member <stable-selector>` and use a `member.*` lens for a Type-to-Member
destination. `--all-libraries` uses the aggregate Library as the source while
`--library` still names the destination Type's exact defining Library. A
source-only `--all-libraries` request omits `--library`; supplying both without
a Type destination is rejected. Root-only and explicit-empty Packages have no
All-libraries subject and return a typed non-success snapshot instead of
throwing.

Selector misses retain the evaluated snapshot and diagnostics. The command
claims that a Type or Member is not present only when its scoped inventory is
complete; otherwise it reports incomplete evidence. Unavailable, failed,
inapplicable, and unknown destination lenses leave both requested halves
uninstalled and return nonzero with typed diagnostic data. JSON and JSONL Type
and Member rows carry the defining Library asset ID, and Member rows carry both
containing and declaring Type names. Process-local Workspace, occurrence,
generation, action, and authority identities are omitted.

### Projects and local assets

```bash
dotnet-inspect project ./src/DotnetInspect.Cli -S Skills
dotnet-inspect project ./src/DotnetInspect.Cli -S @Project
dotnet-inspect project ./src/DotnetInspect.Cli -S Skills -n 1 --tail
dotnet-inspect project ./src/DotnetInspect.Cli -S Skills --print --row 1
dotnet-inspect project ./src/DotnetInspect.Cli -S "Package README file"
dotnet-inspect project ./src/DotnetInspect.Cli -S "Package README file" --print --row 1
dotnet-inspect type Command --project ./src/DotnetInspect.Cli
dotnet-inspect member Command --project ./src/DotnetInspect.Cli -S "Member Index"
dotnet-inspect library ./artifacts/obj/ILInspector.Metadata/release/ILInspector.Metadata.dll -S Signals
```

For API and relationship commands, `--project` means an existing
`project.assets.json` restored-assets context. Passing a `.csproj` or project
directory only locates that file; dotnet-inspect does not restore or build.
The `project` command reads only valid package Skills and root `README.md`
documents listed by the existing restore output. It does not interpret package
`AGENTS.md` or `PROJECT.md` files. Select `@Project` to compose both document
inventories; bare `-S` retains the focused `Skills` overview. With exactly one
document section selected, `-n`, `--tail`, and `--rows A..B` select complete
document rows before Count, structured output, projection, or print/bare
lowering. Add `--lines` only to clip rendered text. Multi-section `@Project`
output retains its independent section row sets and rendered-line `-n`
fallback.

### Types, members, and source

```bash
dotnet-inspect type string --tree
dotnet-inspect type --platform System.Text.Json -n 1 --tail --format json
dotnet-inspect find JsonSerializer --platform System.Text.Json
dotnet-inspect member JsonSerializer --package System.Text.Json -m Serialize
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S @Source
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S "Finding Census" --format json
dotnet-inspect member JsonElement --package System.Text.Json DeepEquals:1 -S Facts --format json
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Calls
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Calls -n 1 --tail --format json
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 -S Callers
dotnet-inspect member System.ThrowHelper --platform System.Private.CoreLib --all \
  -m ThrowArgumentNullException:1 -S Callers -n 1 --tail --format json
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 --source-parts --format json
dotnet-inspect member JsonSerializer --package System.Text.Json Serialize:1 --print --part xml-docs
dotnet-inspect type JsonSerializer --platform System.Text.Json -S "Source Files" --urls --json-array -T q
dotnet-inspect library coordinate 0x060002EA+0x0 \
  --package System.Text.Json --library System.Text.Json.dll
```

For a Type catalog with an explicit package, library, platform, or project
source, including positional or `-t` Type globs, `-n`, `--tail`, and
`--rows A..B` select complete types after type, kind, and unsafe filtering.
Markdown, table, TSV, JSONL, and JSON observe the same selected types;
assembly-level companion evidence such as Type forwarders remains visible. Add
`--lines` only to clip rendered text. Exact-type, selected-section, discovery,
shape, match, and ambiguous commandless modes retain rendered-line fallback.
Numeric `-t` is a literal Type filter, not a row-count spelling.

With exact `member -S Calls`, `-n`, `--tail`, and strict `--rows A..B`
select complete direct call-site rows after analysis of the selected overload
and its generated evidence methods. Repeated calls to the same target remain
distinct. Markdown, table, TSV, JSONL, structured JSON, and Count observe the
same selected call sites in their existing IL-offset order. Add `--lines` only
to clip rendered text. `Callers`, `Call Graph`, `@Calls`, mixed sections,
discovery, and Calls included only by verbosity retain their existing row
contracts or rendered-line fallback.

With exact `member -S Callers`, `-n`, `--tail`, and strict `--rows A..B`
select complete deduplicated caller-site rows after the selected target
overload and all authorized caller scopes have been scanned. Markdown, table,
TSV, JSONL, structured JSON, and Count observe the same selected call sites,
including Source when the completed caller rows came from multiple assemblies.
Add `--lines` only to clip rendered text. `Calls`, `Call Graph`, `@Calls`,
mixed sections, discovery, and scope-implied Callers without the exact selector
retain their existing row contracts or rendered-line fallback.

Focused member `-S "Source Locations" --format json` reports `member`, `document`, and
`pdb_span` without fetching source text or adding generic section/row wrappers.
PDB spans describe executable source, not the entire declaration.
Opt in with `--source-parts` to acquire checksum-verified source and discover
lexical ranges. `--print --part member|xml-docs|attributes|signature|body`
prints the selected part; both gestures imply Source Locations when `-S` is
omitted. The full member includes attached XML documentation and attributes;
the body includes its delimiters. Missing parts fail visibly, and unqualified
`--print` still prints the whole source document. These are lexical source
parts, not parsed documentation or stronger physical-authorship evidence.
Human-readable part output restores the original first-line indentation;
structured JSON content remains the exact token-selected text.

Use a Workspace packet as reusable aggregate context when the Type may be
defined by any Library in its selected context:

```bash
packet=$(dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 \
  --tfm net10.0 \
  --share packet)

dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$packet"

# Given a schema-4 packet from a Type-capable producer:
dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$schema4_packet" \
  --share packet
```

`type --workspace` requires one exact Type and one canonical Base64URL
Workspace packet string; URL input is rejected. It uses the packet's selected
context independently of its focused tab. The packet is the sole location
source, while the receiving command still applies its own NuGet source,
credential, cache, and offline policy. Optional `--share` keeps ordinary Type
output on stdout and writes the derived schema-4 packet or URL as the final
stderr line when the input is schema 4. The current `workspace --share`
producer emits schema 3, which remains a valid inspection input but cannot
encode the derived Type scenario; requesting Share from that input fails
visibly without discarding the Type output.

### Compatibility and change tracking

```bash
dotnet-inspect diff --package Markout@0.33.0..0.35.2
dotnet-inspect diff --platform System.Runtime@9.0.0..10.0.0 --breaking
dotnet-inspect type System.Text.Json.Schema.JsonSchemaExporter --package System.Text.Json@9.0.0..8.0.6 --match
dotnet-inspect member System.Text.Json.JsonSerializer Deserialize:1 --package System.Text.Json@9.0.0..10.0.0 --match
dotnet-inspect timeline --package Markout@0.33.0..0.35.2 --type Markout.MarkoutWriterOptions --members --at all -S Transitions -n 10 --tail
dotnet-inspect timeline --package System.Text.Json@8.0.0..9.0.0 --type System.Text.Json.JsonSerializer --members --at all -S Evaluations --rows 2..
```

`type`/`member --match` follows one source API coordinate across exactly two
literal package versions, preserving the written direction. Member matching
resolves its selector only at the source; the destination selector comes from
API correspondence, so a moved overload ordinal is not independently replayed.
Matching is declaration-only: an ordinal selecting a singleton Property/Event
accessor is refused; omit the accessor ordinal to match that declaration.
Ordinals selecting among overloaded indexer declarations remain supported.
Use `--tfm` to select one API surface and optional `--library` to narrow the
source Library. `--all` widens source selection to the existing IncludeAll API
scope; destination declaration matching remains strict and does not apply a
second accessibility filter. Plain text and Markdown render the typed result;
`--format json` emits complete Content, and `--envelope` adds Share and diagnostics.
This is separate from root `match`, which compares implementation structure.

Ordinary API diffs with one Library at each endpoint consume the shared
[Library API Diff contract](docs/design/library-api-diff-presentation.md)
intended for website Compare. This includes single-Library packages, platform
libraries, and local DLL pairs. `--type` narrows the complete comparison;
`--all` widens its API scope. The endpoints must be versions of the same
logical Library (assembly name, culture, and public-key token).

On this route, unprojected `--format json` serializes the complete
`LibraryApiDiffOutcome`, replacing the former `{changes: ...}` presentation
view. Use `--envelope` for that same Content plus Share and diagnostics;
`--compact` controls whitespace for either complete JSON boundary, not projected
or other Diff operations. For example:

```bash
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 --tfm net8.0 --envelope --compact
```

Envelope output accepts `--all`, but rejects Type/classification filters,
section selection, explicit verbosity, row/line windows, and non-API modes.
Explicitly projected JSON, such as `-S Changes --format json`, retains its existing
presentation schema. Share is explicitly non-projectable for comparison
endpoints; it is not a replay URL.

The exact local-Library Implementation Diff route also exposes complete typed
Content. Exact section selection chooses the operation rather than projecting
its result; `--type` and `--member` remain semantic request inputs recorded in
the Content:

```bash
before=old/Foo.dll
after=new/Foo.dll
dotnet-inspect diff \
  --library "$before..$after" \
  -S "Implementation Diff" --type Foo.Widget --envelope
```

Unprojected `--json` emits the same `ImplementationDiffDocument` found at
`--envelope.content`; `--compact` controls whitespace. The document retains
Before/After assembly identity, MVID and provenance, structured C# and IL
evidence, complexity changes, and per-mechanism coverage. Share is currently
non-projectable for the ordered endpoint pair. Package/platform ranges, PDB
Source enrichment, row or field projection, alternate formats, and additional
sections remain on the existing rendered paths or are rejected for complete
transport.

Changes without a compatibility classification remain visible under
**Other API Changes**, or as `unclassified` rows in detailed output. They are
not classified as breaking or additive. An incomplete or rejected comparison
returns nonzero and says **not compared**, rather than claiming no changes.
Multi-Library packages, member-filtered diffs, Analysis Diff, Implementation
Diff, Finding Transitions, and mixed-section requests retain their existing
routes; this adoption does not add the website Compare UI.

Use `-S @Diff` to compose the `Changes`, `Analysis Diff`, and `Implementation
Diff` views. `Complexity Context`, `Structural Context`, and `Finding
Transitions` remain exact-name sections because their focused semantics do not
compose with those comparison views.

Select `Implementation Diff` directly to inspect body-level C#, IL, and
normal-flow complexity evidence. Select `Complexity Context` directly for a
focused view with nullable `Old`, `New`, `Delta`, `Population Size`, and
`Percentile Rank` fields. The rank is the inclusive percentage of
delta-bearing methods in this diff whose absolute complexity delta is no
greater than the row's. It is positional context, not an unusualness or
quality judgment; an all-equal population gives every row 100. Use column
projection with JSON Lines to emit dedicated cells instead of parsing
`Evidence`:

```bash
dotnet-inspect diff --package Markout@0.33.0..0.35.2 \
  --type Markout.MarkoutWriter \
  -S "Complexity Context" \
  --columns Member,State,Delta,PopulationSize,PercentileRank,Kind \
  --format jsonl
```

Select `Structural Context` directly to inspect signed instruction, complexity,
loop, exception-region, direct-call, allocation, and async deltas with their
direction signature and exact local cohort frequency. It includes complete,
unambiguous pairs, including all-Unchanged pairs; added, removed, incomplete,
and ambiguous pairs do not appear. `Cohort Size` is a local frequency, not an
outlier or quality judgment. Use the stable `Kind` value
`research.complexity.structural-cohort` and dedicated fields for structured
automation:

```bash
dotnet-inspect diff --package Markout@0.33.0..0.35.2 \
  --type Markout.MarkoutWriter \
  -S "Structural Context" \
  --columns Member,InstructionDelta,ComplexityDelta,InstructionDirection,CohortSize,PopulationSize,Kind \
  --format jsonl
```

### Structural matching

Use the `Clone Candidates` section for a globally ranked search from an exact
Library, Type, or logical Member seed. Breadth and candidate admission are
independent; the default is `Everything` plus `SimilarNames`.

```bash
dotnet-inspect type Cases.Widget --library ./app.dll -S "Clone Candidates"
dotnet-inspect member Cases.Widget --library ./app.dll -m Value \
  -S "Clone Candidates" \
  --where "Breadth=Self" \
  --where "Discovery=All"
dotnet-inspect type Cases.Widget --library ./app.dll \
  -S "Clone Candidates" -n 2
dotnet-inspect package ./app.nupkg --library app.dll \
  -S "Clone Candidates" -n 2
dotnet-inspect type Cases.Widget --library ./app.dll \
  -S "Clone Candidates" -n 1 --tail --count
dotnet-inspect type -Q "Clone Candidates"
```

`Breadth` accepts `Self`, `SelfAndRegisteredEcosystems`, or `Everything`;
`Discovery` accepts `SimilarNames` or `All`. The current CLI supplies the
selected exact library as its finite Workspace participant snapshot and
discloses that scope in tabular diagnostics and structured coverage. It does
not infer registered-ecosystem membership or silently narrow the requested
breadth. `-n`, `--tail`, and strict `--rows` windows select complete ranked
candidate pairs consistently across Markdown, tables, TSV, JSONL, projected
JSON, complete JSON, and `--count`. Coverage and the work receipt remain
complete evidence, so structured `receipt.returned_pairs` can exceed the
selected `rows` length.

Rows are retrieval candidates, not checked clone relations. They retain rank,
both exact method endpoints, the 0-10,000 total and component scores, and the
optional type/member name-similarity evidence. Plain `--format json` also retains the
portable seed, participant identity and provenance, coverage, failures, limits,
and work receipt. `--rows`, `--columns`, `--fields`, `--count`, `--format table`,
`--format tsv`, and `--format jsonl` operate at the declared candidate-row output seam.

Pairwise `match` remains the checked comparison path:

```bash
dotnet-inspect match Left.Compute Right.Compute --library ./app.dll
dotnet-inspect match Left.Compute Right.Compute --library ./app.dll --body
dotnet-inspect match Sample.Encode --similar --library ./app.dll
dotnet-inspect match Sample.Encode --similar --library ./app.dll --assembly-wide --top 10
```

`--body` adds decompiled C# and IL body differences alongside the independent
structural-match result. Both methods must resolve to the same physical
assembly. A bodyless or unavailable endpoint stays visible rather than implying
equal bodies. Use Markdown (the default) or `--format json`; body evidence is not
supported with `--format table`, `--format tsv`, `--format jsonl`, or `--similar`.
`--body --format json` returns a `match`/`body` envelope: the body document retains
per-producer native verdicts, physical endpoint addresses and availability,
structured C#/IL differences, diagnostics, and cleanup outcomes. Plain
`match --format json` keeps its existing flat structural document. `--body` returns
nonzero on query cancellation or failed body comparison while preserving the available output;
a valid bodyless endpoint is reported as `NoApplicableInput`, not a body match.
The [publication and CLI gates](docs/design/local-comparison-publication.md#demo-and-gates)
cover these distinctions.

`--similar` ranks structural candidates for one seed method. It is a discovery
step, not a verdict: a rank establishes no relation, no semantic equivalence,
and no authorship or copying claim. Within one image, confirm a candidate by
re-running the pairwise form on the selected pair.

The default candidate population is the seed's declaring type. `--assembly-wide`
opts into whole-assembly retrieval, which costs materially more. `-n`,
`--tail`, strict `--rows` windows, and `--top` select complete ranked candidate
rows after retrieval; `--top` uses the structural-similarity ranking already
issued by Analysis. Markdown, table, TSV, JSONL, JSON, and `--count` consume the
same selected candidate sequence. JSON retains the complete per-method
outcomes, blockers, and query receipt, with `row_selection` counts that
distinguish returned candidates from selected rows. `--max-results` and
`--max-methods` move the product retrieval limits themselves. In `--format table`,
`--format tsv`, and `--format jsonl`, the ranked candidates are the only row shape; the seed,
scope, disposition, receipt, blockers, and disclosure are written to stderr so
stdout stays single-shaped and parseable.

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
dotnet-inspect package Microsoft.Extensions.Logging@10.0.0 \
  -S Dependencies
dotnet-inspect package Microsoft.Extensions.Http@10.0.0 \
  -S "Package Info"
dotnet-inspect package Microsoft.Extensions.Http@10.0.0 \
  -S "Ecosystem Dependencies" \
  --columns "Ecosystem,Kind,Dependency,Declared By"
dotnet-inspect package Microsoft.Extensions.Logging@10.0.0 \
  -S "Dependency Hierarchy" --depth 2 --tree
dotnet-inspect depends Stream --format markdown --mermaid
dotnet-inspect depends Int128 --format table --rows 1..10
dotnet-inspect depends Int128 \
  --where "Kind=Interface" \
  --order-by "Target desc" \
  --top 5 --format table
dotnet-inspect depends NpgsqlOptionsExtension \
  --package Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4 \
  --tfm net8.0 \
  --envelope
dotnet-inspect depends NpgsqlOptionsExtension \
  --package Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4 \
  --tfm net8.0 \
  --format json
dotnet-inspect depends \
  --project ./src/App/App.csproj \
  --depth 2 \
  -S "Dependency Hierarchy,Dependencies"
dotnet-inspect depends \
  --package Microsoft.Extensions.Hosting@10.0.0 \
  --nuspec ./artifacts/local.nuspec \
  -v:n
dotnet-inspect depends \
  --package-prefix Microsoft.Extensions \
  --max-packages 100 \
  -S @Dependencies
dotnet-inspect depends --nuspec ./artifacts/local.nuspec -D --effective
dotnet-inspect depends --package Newtonsoft.Json --tfm net8.0 \
  -S Dependencies
dotnet-inspect depends --package System.Text.Json@9.0.0 --tfm net11.0 \
  -S Pruning
dotnet-inspect depends --package Microsoft.AspNetCore.Authentication.JwtBearer@10.0.0 \
  --tfm net11.0 --platform-family aspnetcore -S Pruning
dotnet-inspect depends \
  --project ./src/DotnetInspect.Cli \
  --nuspec ./artifacts/package.nuspec \
  -S "Dependencies,Failures" \
  -v:n
dotnet-inspect implements IEquatable --project ./src/DotnetInspect.Cli -v:q
dotnet-inspect extensions string --project ./src/DotnetInspect.Cli -v:q
dotnet-inspect graph integrations \
  --package Microsoft.Extensions.DependencyInjection.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging@10.0.0 \
  --package Microsoft.Extensions.Http@10.0.0 \
  --tfm net10.0 \
  --relationship integration.observed \
  -n 10 --tail --format table
dotnet-inspect graph calls \
  Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions \
  AddOpenTelemetrySharedProviderBuilderServices~4d95928639 \
  --root-package OpenTelemetry@1.18.0 \
  --package OpenTelemetry.Api@1.18.0 \
  --tfm net10.0 \
  --all
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  -S
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  -S "Provider API Types" \
  --format table
```

For asset roots, `Dependency Hierarchy` is the rooted explanatory result:
shared targets reached through different parents remain separate occurrences,
and tables, JSON, JSONL, row windows, and Count use that same occurrence
currency. On `package`, selecting this section invokes the same host-neutral
Depends operation; `--tree` only chooses its projection. `Dependencies`
remains the direct declaration evidence section and does not acquire transitive
packages. The removed package `--dependencies` spelling reports replacement
guidance rather than acting as a second hierarchy selector.
Positional `depends <type>` retains its existing `Dependency Graph` section
until Type relationships move to the general Graph operation. That route now
projects its existing Source, Target, and Kind predicates plus field and
Traversal ordering through `-Q "Dependency Graph"`. `--top` requires a Source,
Target, or Kind field order; Traversal is a sequence order. Asset-mode
`Dependency Hierarchy` and Package `Dependency Hierarchy` inherit the same
`--depth` capability from the Dependency operation.

For recursive package traversal, `--tfm` selects the root package dependency
group and configures the stable traversal target. When `--tfm` is omitted, the
root keeps its package-local selection while newly reached packages use the
product traversal default, currently `net12.0`; a compatible destination
selection does not replace that target on later edges.

For `graph integrations` and `graph calls`, one semantic row is one logical
graph edge in the completed typed document. Head/Tail and strict Window select
those edges before Markdown, table, TSV, JSONL, JSON, Mermaid, plaintext graph,
or Count lowering; selection does not reduce package acquisition or hide
retained graph failures. Add `--lines` only to clip rendered text explicitly.
`graph libraries` retains independent section row sets; its adopted Call Sites
and Direct Use Clusters cohorts are described below.

`graph calls` is the integration-style complement to the general
`member -S "Call Graph"` view. It starts from one exact member in
`--root-package`, treats repeatable `--package` values as explicit external
participants, and shows only calls crossing out of the focus assembly plus the
shortest local paths needed to reach them. Each edge is typed as `connector`,
`boundary`, or `unclassified-boundary`, and row-oriented output retains the
physical MVID, MethodDef token, IL offset, operand token, call kind, dispatch
kind, and loop state.

The OpenTelemetry example reduces the ordinary 28-edge bounded neighborhood to
nine explanatory edges. Two local connectors retain the path from
`AddOpenTelemetrySharedProviderBuilderServices` through
`Sdk.get_SuppressInstrumentation` and
`SuppressInstrumentationScope.get_IsSuppressed` to
`OpenTelemetry.Api`'s `RuntimeContextSlot<T>.Get`. Calls into assemblies not
declared by `--package` remain visible as `unclassified-boundary` edges with an
incompleteness warning instead of being silently dropped. Use `--format table`,
`--format jsonl`, or `--format json` for exact receipts; `-n`, `--tail`, and `--rows` select
complete logical edges after graph construction.

For positional type dependencies, `--format json` writes the complete
`TypeDependencySectionResult` Content, and `--envelope` writes the identical
camelCase value under `content` with `schema_version: 1`,
`result_kind: "type-dependencies"`, `share`, and `diagnostics`. The motivating
example resolves the full matched type
`Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension`.
The Content retains the complete dependency relationships and the separately
selected `rowSelection.relationships`; internal dependency enums remain
numeric.

Envelope framing and diagnostic member names use lower snake case. Share keeps
the owner values `available` and `nonProjectable`; diagnostic severity remains
`Information`, `Warning`, or `Error`, and absent correspondence is retained as
`null`. Baseline envelopes have no `evidence` member.

With `--envelope`, `--depth` remains a traversal input and `--rows`,
`-n`/`--head`/`--tail` remain semantic relationship selection. `--compact` is
accepted. Competing formats, `--format json`, Discover/schema/effective modes, `-S`,
explicit `-v`, Count, fields/columns, presentation projections or decoration,
and rendered-line clipping are rejected before acquisition. `--verbose`,
`--info`, and `--tips` remain on stderr. `--share` retains its existing policy
and emits its optional URL or packet as the final stderr line. There is no
`--evidence-envelope` support yet.

Asset-mode output, Discover, Count JSON, and ordinary non-JSON output are
unchanged. A service-issued empty or non-success Content value is still
serialized with its existing exit behavior; an acquisition failure that
produces no result emits no substitute envelope.

`Pruning` is explicit-only and evaluates direct declarations of the named
roots; it does not prune dependency-graph edges or run transitive traversal.
The default comparison family is `runtime`; use
`--platform-family aspnetcore` to select the ASP.NET Core inventory. In its
output, `Candidate` is the package version resolved from the declaration and
`Platform Provides` is separate platform-supply evidence. If those columns
show `4.3.2` and `4.3.1`, respectively, the disposition is
`PackageRetained`: the command does not select or downgrade to `4.3.1`.

`graph libraries` evaluates both directions in the pair; every row still names
its directed source and target. Omitting `-S` preserves the exact physical call
sites. In that default view, and with exact `-S "Call Sites"`, `-n`, bare
`-N`, `--tail`, and strict `--rows` select complete physical call sites before
Markdown, plaintext, table, TSV, JSONL, JSON, or Count lowering. Exact
`-S "Direct Use Clusters"` applies the same gestures to complete deterministic
cluster rows after optional `--where "Cluster=N"` scoping. Use `--lines` for
explicit rendered-line clipping. Bare `-S` shows `Consumer Use Sites` and
`Provider API Types`: the local
methods containing direct calls, and the provider declaring types selected by
those calls. These are direct-use surfaces, not semantic feature clusters,
public-entrypoint reachability, or a list of configured ecosystem Integrations.
Select `@Libraries` to compose `Call Sites`, `Consumer Use Sites`, `Direct Use
Clusters`, and `Provider API Types` in alphabetical section order. `Public Root
Paths` remains an exact-name section because its required cluster coordinate
does not compose with the pair-wide category. Summary, path, wildcard,
category, and multi-section views retain rendered-line `-n` because their
independent row schemas do not form one semantic sequence.

`-S "Direct Use Clusters"` partitions the exact directed call rows into
connected components of source and target methods. Each explicit row retains
its call-site references and separately counts source members, provider types,
target members, extension methods, and physical sites. A one-extension-method
row exposes a small direct-use footprint; it is not yet proof that the package
is removable or that copying source is safe. The section remains outside the
default and bare `-S` views.

Use the pair-wide cluster ordinal to reopen one component as exact calls:
Run `dotnet-inspect graph libraries -Q "Call Sites"` to discover the predicate
and its supported operator without inspecting a pair.

```bash
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  -S "Direct Use Clusters"

dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  --where "Cluster=3"

dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll \
  --where "Cluster=3" \
  -S "Public Root Paths"
```

The drill-down names every source member, source token, target member, target
token, call kind, evidence method, evidence token, and IL offset in that
cluster. Use source and target identities for ordinary `member` inspection.
Use the evidence token with the IL offset for `library coordinate`, because a
compiler-generated physical body can differ from the attributed source member.
The cluster remains structural evidence rather than a source-inlining verdict.

`Public Root Paths` traces the selected cluster's exact consumer methods back
to exhaustive public MethodDef roots in the consumer library. Each row reports
one deterministic shortest local static path, its public root and destination
tokens, and physical IL receipts for every logical step. The section must be
named explicitly and requires exactly one `Cluster=N` predicate; it is excluded
from defaults, bare `-S`, and wildcard section selection. A complete empty
section means no public root has a local static path to the selected use sites.
If pair, public-root, or path analysis is incomplete, retained positive paths
are still rendered and the command exits nonzero instead of asserting absence.

```bash
dotnet-inspect member "<SourceType>" \
  --library ./Consumer.dll \
  -m "<SourceMember>" \
  -S @Source

dotnet-inspect member "<TargetType>" \
  --library ./Provider.dll \
  -m "<TargetMember>" \
  -S @Source

dotnet-inspect library coordinate "<EvidenceToken>+<ILOffset>" \
  --library ./Consumer.dll
```

### Workspace sharing and built-in guidance

```bash
dotnet-inspect workspace-state decode "$w"
dotnet-inspect workspace-state decode "$w" | jq
dotnet-inspect workspace-state encode --file workspace-state.json
dotnet-inspect workspace-state encode --file workspace-state.json --url
dotnet-inspect member JsonConvert \
  --package Newtonsoft.Json@13.0.4 \
  SerializeObject:1 \
  --tfm net6.0 \
  --share
dotnet-inspect depends \
  --package Newtonsoft.Json \
  --tfm net6.0 \
  --share
dotnet-inspect skill list
dotnet-inspect demo list
dotnet-inspect demo list -n 3 --format json
```

`workspace-state encode --url` emits `https://dotnet-inspect.net/?w=<packet>`
for the existing share-packet JSON shape. Packet-only output remains the default.
This is not an encoder for `workspace --format json` inventory output. The packet's
existing limits and the browser's supported restoration shapes still apply.

Bare `--share` emits a complete Inspect Web URL; `--share url` spells that
default explicitly, while `--share packet` emits only the canonical packet.

`member --share[=url|packet]` projects one explicitly selected public member
overload from an exact NuGet.org package version and target framework. The URL
opens that member's API Overview in the published browser. Select an overload
with `Name:N`, `Name~digest`, or `--index N`. Local, project, platform,
private-feed, non-public, multi-library, and other rendering or analysis modes
fail visibly rather than producing a link the browser cannot restore.

`depends --package <id>[@<version>] --tfm <tfm> --share[=url|packet]`
projects the package Dependencies view without acquiring the package or
traversing the graph in the CLI. An omitted version or `latest` is resolved
from NuGet.org and pinned before emission; the published browser then acquires
that exact coordinate and lazily computes the dependency graph for the
selected target framework. Local archives, effective source policies that do
not authorize exactly one NuGet.org source, wildcard, range, or build-metadata
versions, omitted frameworks, the Browser-reserved `Microsoft.NETCore.App`
Platform id, row windows, counts, and other rendering formats fail visibly
rather than producing a non-reproducible link.

`depends <type> --package <id>@<version> --tfm <tfm> --share[=url|packet]`
keeps the type dependency result on stdout and appends its canonical
Dependencies URL or packet to stderr. Type sharing is limited to one exact
NuGet.org package coordinate, a valid target framework, and the requested type;
local, floating, ranged, private-feed, multi-source, platform, and
non-projectable requests fail visibly.

The service constructs Share for positional type JSON regardless of whether
stderr projection was requested. `--format json` emits Content only; `--envelope`
exposes Share alongside that Content. A `nonProjectable` Share does not change
otherwise successful Content or its exit status. Mixed dependency sources and
any explicit `--depth` are non-projectable because the published Browser
cannot preserve those requests.

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
