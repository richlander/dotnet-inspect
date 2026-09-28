---
name: dotnet-inspect-relationships
version: 0.1.0
description: Map how code connects — implementors and subclasses, extension methods, dependency graphs, reverse callers, and ecosystem integrations. Many outputs are graph-shaped (add --mermaid).
---

# dotnet-inspect: relationships and dependency graphs

Use this skill to map how code connects: what implements or extends a type, what
it depends on, and who calls it. Dependency graphs and Member Call Graphs share
the same graph gestures: add `--tree` for a standalone path view, `--mermaid`
for a standalone diagram, or `--markdown --mermaid` to embed one.

```bash
dnx dotnet-inspect -y -- <command>
```

With a positional type, scope relationship commands with
`--project path/to.csproj`, repeatable `--package Foo`, `--library path.dll`,
`--platform`, `--extensions`, `--aspnetcore`, and `--tfm net9.0`. Without a
positional type, `depends` treats repeatable `--package`, `--nuspec`,
`--library`, and `--project` options as explicit roots; exclusive
`--package-prefix PREFIX` admits up to 500 package roots by default and can be
bounded explicitly with `--max-packages`.

`--project` reads existing restored assets; restore/build first if dependencies
changed.

## What does the root declare directly?

`depends -S Dependencies` reports normalized direct declarations without
requesting transitive traversal. Select `Dependencies,Failures` when a mixed
root request should retain usable declaration rows while also reporting roots
that could not be inspected.

```bash
dnx dotnet-inspect -y -- depends \
  --package Newtonsoft.Json --tfm net8.0
dnx dotnet-inspect -y -- depends \
  --project ./src/App/App.csproj \
  --nuspec ./artifacts/App.nuspec \
  -S "Dependencies,Failures"
dnx dotnet-inspect -y -- depends \
  --package-prefix Microsoft.Extensions --tfm net10.0 -v:n
```

## Would the platform supply a direct dependency?

`depends -S Pruning` explicitly compares each selected direct declaration's
source-authorized package candidate with one exact installed platform
inventory. It requires `--tfm`; the default family is `runtime`, and
`--platform-family aspnetcore` selects ASP.NET Core. The section does not
traverse or remove graph edges.

```bash
dnx dotnet-inspect -y -- depends \
  --package System.Text.Json@9.0.0 \
  --tfm net11.0 \
  -S Pruning
```

Read `Candidate` as the selected package version and `Platform Provides` as
separate comparison evidence. An older platform-provided version produces
`PackageRetained`; it is not selected as a downgrade.

## What implements or extends it?

`implements Interface` finds concrete implementors and subclasses;
`extensions Type` finds extension methods. Add `--reachable` (with `--depth N`)
to include extensions on types reachable through properties and methods.

```bash
dnx dotnet-inspect -y -- implements IDisposable --platform
dnx dotnet-inspect -y -- extensions HttpClient --platform --reachable
dnx dotnet-inspect -y -- implements ILogger --package-prefix Microsoft.Extensions
dnx dotnet-inspect -y -- implements IEquatable --project ./src/App/App.csproj -v:q
dnx dotnet-inspect -y -- extensions string --project ./src/App/App.csproj -v:n
```

## What does it depend on?

`depends Type` walks a type hierarchy inside its search scopes. Asset mode
omits the positional type and walks explicit package manifests, restored
projects, and library references. `--depth 1` includes direct edges only;
omitting it follows the complete authorized graph. Shared targets remain
distinct incoming edges and appear as revisits in tree output. `-D`, `-S`,
`--table`, `--tsv`, `--jsonl`, `--json`, `--count`, `--rows`, and `-n` address
the existing section and logical-row contracts. For positional type mode only,
unprojected `--json` is now the complete camelCase
`TypeDependencySectionResult`, not the former flattened presentation graph.

```bash
dnx dotnet-inspect -y -- depends JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- depends MyType --library MyLib.dll --mermaid
dnx dotnet-inspect -y -- depends Command --project ./src/App/App.csproj -v:q
dnx dotnet-inspect -y -- depends Int128 --table --rows 1..10
dnx dotnet-inspect -y -- depends -Q "Dependency Graph"
dnx dotnet-inspect -y -- depends Int128 \
  --where "Kind=Interface" \
  --order-by "Target desc" \
  --top 5 --table
dnx dotnet-inspect -y -- depends NpgsqlOptionsExtension \
  --package Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4 \
  --tfm net8.0 \
  --envelope
dnx dotnet-inspect -y -- depends NpgsqlOptionsExtension \
  --package Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4 \
  --tfm net8.0 \
  --json
dnx dotnet-inspect -y -- depends \
  --project ./src/App/App.csproj \
  --depth 2 \
  -S "Dependency Hierarchy,Dependencies"
dnx dotnet-inspect -y -- depends \
  --package Microsoft.Extensions.Hosting@10.0.0 \
  --depth 1 \
  --tree
dnx dotnet-inspect -y -- package Microsoft.Extensions.Hosting@10.0.0 \
  -S Dependencies
dnx dotnet-inspect -y -- package Microsoft.Extensions.Hosting@10.0.0 \
  -S "Dependency Hierarchy" --depth 1 --tree
dnx dotnet-inspect -y -- library System.Text.Json \
  -D @Dependencies --details
dnx dotnet-inspect -y -- library System.Text.Json \
  -D "Reference Hierarchy" --details
dnx dotnet-inspect -y -- library System.Text.Json -S References
dnx dotnet-inspect -y -- library System.Text.Json \
  -S "Reference Hierarchy" --tree
```

For asset roots, `Dependency Hierarchy` preserves one occurrence per
root-relative parent relationship. Use `Dependencies` for direct declaration
evidence; use hierarchy table or JSON output when repeated targets and their
parent context matter. On `package`, selecting `Dependency Hierarchy` invokes
the same Depends operation, while `--tree` only chooses its projection.
Both entrances inherit the same Depth capability. For positional type mode,
`-Q "Dependency Graph"` reports the Source, Target, and Kind predicates plus
field and Traversal ordering. `--top` requires a Source, Target, or Kind field
order; Traversal cannot rank.
On `library`, `References` remains direct assembly metadata and
`Reference Hierarchy` invokes that same occurrence-addressed Depends operation
for the exact assembly; `--depth` controls traversal and `--tree` remains only
a projection choice. Use `-D @Dependencies --details` to compare the complete
category with its members before choosing the exact hierarchy section for tree
or Mermaid output.

`--envelope` is a presence-only service-output selector implemented only for
positional `depends <type>`. It implies JSON and emits
`schema_version: 1`, `result_kind: "type-dependencies"`, the same Content as
the paired `--json` command, Share, and ordered diagnostics. The Content keeps
the complete dependency relationships plus the selected
`rowSelection.relationships`; dependency enums remain numeric.
The service constructs Share for both JSON modes, but `--json` emits Content
only; `--envelope` exposes Share.

For one exact NuGet.org package version and TFM, `share.kind` is normally
`available`; its `full_url` opens the analogous Dependencies view in Inspect
Web and `packet` is the canonical replay state. Mixed dependency sources and
explicit `--depth` remain valid Content requests but make Share
`nonProjectable`, because the published browser cannot preserve them. This is
the high-value envelope path when a dependency answer should include a
user-drillable graph.

With `--envelope`, use `--compact` for minified JSON. `--depth` remains
traversal, and `--rows` or `-n`/`--head`/`--tail` remain semantic relationship
selection. Do not combine it with `--json`, another format, Discover or schema
modes, `-S`, explicit `-v`, Count, fields/columns, decoration, projections, or
rendered-line clipping. `--verbose` and `--tips` remain on stderr;
explicit `--share` retains the existing final-line URL/packet policy. Asset
mode, Discover, Count, Library Diff, and `--evidence-envelope` have not adopted
this transport.

## Who calls it? (reverse edges)

`member Type -m Method:1 -S Calls` lists what a method calls; `-S Callers` lists
the call sites that reach it. With an explicit source, widen the caller search
with `--bin`, `--project`, or `--caller-package`. With no explicit source, the
first `--project` is the source context; repeated `--project` values after it
remain caller scopes.

With exactly `-S Callers`, `-n`, `--tail`, and strict `--rows A..B` select
complete deduplicated call-site rows after every authorized caller scope has
been scanned. Markdown, table, TSV, JSONL, JSON, and Count consume that same
selected vector. Add `--lines` only for rendered-text clipping; `@Calls`, mixed
sections, and scope-implied Callers retain rendered-line fallback.

With exactly `-S Calls`, the same selectors operate on complete direct
call-site occurrences after the selected method and its generated evidence
methods have been analyzed. Repeated calls remain distinct, all output formats
consume the same selected vector, and neighboring `@Calls`, mixed-section,
discovery, and verbosity-implied modes retain rendered-line fallback.

```bash
dnx dotnet-inspect -y -- member Type -m Method:1 -S Calls -n 1 --tail --json
dnx dotnet-inspect -y -- member string -m IndexOf~147d84bbd7 -S Callers --caller-package System.Text.Json@9.0.0 --tfm net9.0
```

`Call Graph` is the bounded bidirectional view centered on one member: inbound
callers toward entry points plus outbound calls. Its default Markdown view is
an edge table. Select `--tree` for a standalone path-oriented view,
`--mermaid` for a standalone diagram, or `--markdown --mermaid` for a diagram
inside the Markdown document. For scripts, `--tsv` and `--jsonl` expose the
same ordered edges. Machine fields `from` and `to` are always present;
`from_group`, `to_group`, and `label` appear only when the whole graph uses
them. A row window does not change that schema. `--count` and `--rows` address
edge rows consistently across these views.

For a type-level dependency summary, `Called Types` groups direct calls by
target type, assembly, members, and call kinds.

`Call Graph` has no `--envelope` route. Its graph, row selection, and
completeness evidence belong to Content, so use the graph views or structured
row formats above. A separate exact-member `--share url` projects the public
API Overview, not the Call Graph; do not present that URL as a replay of the
graph analysis.

```bash
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph"
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --tree
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --mermaid
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --markdown --mermaid
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --jsonl
dnx dotnet-inspect -y -- type Type --library MyLib.dll -S "Called Types"
```

For an integration-style explanation of calls leaving one package assembly,
use `graph calls`. It resolves one exact member in `--root-package`,
automatically follows that root's authorized dependency graph, and retains
only external boundary calls plus their shortest local connectors. Root asset
selection stays exact; dependency traversal independently uses `--tfm` or the
product default. This is always external-focused; it does not change the
general bidirectional `member -S "Call Graph"` view. Dependency members with
unique ownership retain their exact package id, version, and selected
framework. In Inspect Web, selecting such a node loads that exact package
through the ordinary package path before opening the member; ambiguous
ownership publishes no package coordinate.

```bash
dnx dotnet-inspect -y -- graph calls \
  Microsoft.Extensions.DependencyInjection.ProviderBuilderServiceCollectionExtensions \
  AddOpenTelemetrySharedProviderBuilderServices~4d95928639 \
  --root-package OpenTelemetry@1.18.0 \
  --root-tfm net10.0 \
  --tfm net10.0 \
  --all
```

Each logical edge has a typed `connector`, `boundary`, or
`unclassified-boundary` role. Table, TSV, JSONL, and JSON retain physical call
receipts; Markdown, Mermaid, and plaintext lower the same Markout graph. Calls
into assemblies outside the explicit package context remain visible as
unclassified boundaries with an incompleteness warning. `-n`, `--tail`, and
`--rows` select complete logical edges after bounded graph construction.
By default, the supply-chain baseline highlights third-party Package
boundaries while keeping the root Package and registered .NET ecosystems as
connectors. `--first-party-prefix PREFIX` adds a known first-party Package
family to the baseline. Use `--baseline self` to keep only the root and explicit
first-party prefixes as connectors, or `--baseline nothing` to highlight every
known dependency Package; the latter rejects first-party prefixes. These
choices change highlighting, not dependency traversal.

For an exact pair of Libraries, `graph libraries --library ./Consumer.dll
--library ./Provider.dll` reports physical cross-Library calls in both
directions. Select `-S "Direct Use Clusters"` to group connected source and
target methods with their physical call sites. Reopen one cluster with
`--where "Cluster=N"`, then select `-S "Public Root Paths"` to see bounded
paths from public consumer entrypoints to that cluster. Use `-Q "Call Sites"`
to discover the cluster predicate before opening the Libraries.

## What does it integrate with? (ecosystem)

All integrations are enabled by default. Discover and narrow the ordinary
Integration result with canonical concept or ecosystem identities:

```bash
dnx dotnet-inspect -y -- library -Q Integrations
dnx dotnet-inspect -y -- library Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S Integrations --where "ecosystem=ecosystem.aspire"
dnx dotnet-inspect -y -- library MyLibrary.dll -S Integrations \
  --where "integration=integration.aspire" --jsonl
```

Omitting `-S` with either predicate selects `Integrations`. The two facets
intersect when combined. An empty filtered result is not absence of all
Integration support; full-library presence and Census remain unchanged.
Unsupported IDs and combinations fail explicitly. Do not combine either
predicate with Performance Triage or Body Shapes queries. These options belong
to `library`, not `package --library` or `graph`.

`graph integrations` compares an explicit package set inside one
binding-consistent target. Repeat `--package name[@version]`, provide the shared
`--tfm`, and add `--relationship <id>` only when the default Integration family
should be narrowed. This is an induced set, not a traversal: it has no direction
or depth. Markdown is an edge table by default; `--tree`, `--mermaid`, `--json`,
`--jsonl`, and `--count` project the same logical relationships. `-n`, bare
`-N`, `--tail`, and strict `--rows` select complete logical edges after the
graph is built and before those formats; use `--lines` only for explicit
rendered-line clipping. Row selection does not reduce package acquisition or
hide retained graph failures. `graph libraries` remains a separate
multi-section command: its default and exact `Call Sites` views apply the same
semantic gestures to complete physical call sites, and exact
`Direct Use Clusters` applies them to complete deterministic cluster rows.
Its independent summary, path, wildcard, category, and multi-section views
retain rendered-line `-n`.
Missing `api.extension` or `integration.observed` endpoints whose assemblies are
absent from the explicit package set remain outside the induced graph; add the
owning package to admit those relationships. A missing
`integration.opportunity` target and other binding failures -- unavailable,
ambiguous, rejected, or selected outside the active context -- remain visible
and produce a nonzero exit.

```bash
dnx dotnet-inspect -y -- graph integrations \
  --package Microsoft.Extensions.DependencyInjection.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging@10.0.0 \
  --package Microsoft.Extensions.Http@10.0.0 \
  --tfm net10.0 \
  --relationship integration.observed
```

`-S @Integrations` on `library` or `package --library` rolls up the ecosystem
frameworks a library plugs into — DI, hosting, ASP.NET Core, AI, OpenTelemetry,
configuration, logging, and more — plus `Integration Opportunities` and
language/runtime integration signals like C# union types.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --library -S @Integrations
dnx dotnet-inspect -y -- library MyLibrary.dll -S "Union Types" --tsv
dnx dotnet-inspect -y -- library --platform System.Text.Json -S "Union Types" --tsv
```

Use `Union Types` when looking for C# union adoption in libraries. It reports
types annotated with `System.Runtime.CompilerServices.UnionAttribute`, whether
they implement `IUnion`, and constructor-derived case types. Current platform
libraries may expose the runtime infrastructure before any production library
declares union types, so an empty table is still a useful signal.
