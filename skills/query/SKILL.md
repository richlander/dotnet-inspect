---
name: dotnet-inspect-query
version: 0.1.0
description: Output formats, -D/-S discovery and selection, -Q query-capability discovery, value projection, @ categories, and output limits shared across commands.
---

# dotnet-inspect: query and output system

The query system is like Go templates, without a DSL: inspection commands emit
structured sections, with the broadest shared query surface on `type`, `member`,
`package`, and `library`. `project` supports `-D` and `-S` but not general
field/column projection. `find` supports `-D` discovery and field/column
projection but not `-S` selection. Pairwise `diff` supports `-D` and `-S` but
not field/column projection. `diff --history` supports section selection and
projection but not `-D` discovery. `workspace` supports output formats,
`--count`, and `--rows`, but not discovery, section selection, or field
projection.
`depends` supports `-D`, `-S`, categories, row windows, count, and field/column
projection across its dependency graph and evidence sections. Several commands
also expose a separate complete-service `--envelope` path described below.
Other relationship commands may still expose fixed output. Discover the shape
first where available, then select and project.

```bash
dnx dotnet-inspect -y -- <command>
```

Use this skill to shape the result the user needs. Start by identifying its
result space. If the intent or space is unclear, use a bare target and let the
router choose. Otherwise, enter the matching space directly:

- API-symbol space: `find <pattern>` searches type names, or member names with
  `--members`.
- Package space: `package query <exact-id>` or `package query '<prefix>*'`
  discovers package IDs; `package <exact-id>` then inspects one.
- Library space: `library query <pattern-or-scope>` discovers Libraries, then
  `library <source>` inspects a known Library.

## Output formats

Default output is Markdown. Pick a machine or compact shape when you need one:

- `--table` — compact aligned rows.
- `--tsv` — stable snake_case headers, no embedded tabs/newlines.
- `--jsonl` — one JSON object per row.
- `--json-array` — one JSON array for projected rows (`--urls`, `--paths`, `--value`, `--print`).
- `--json` — structured documents.
- `--bare` — one undecorated payload or URL list.
- `--count` — a bare row count.
- `--value` / `--urls` / `--paths` — project one selected section to scalar, URL, or path payloads.
- `--print` — print one document behind a selected section row; use `--row N|first|last` when the section renders multiple rows.
- `--tree` — a standalone tree for graph sections that support tree lowering.
- `--mermaid` — a standalone diagram; combine it with `--markdown` to embed
  the diagram in a Markdown document.

`--envelope` normally implies JSON and emits the complete service value with
`schema_version`, `result_kind`, `content`, `share`, and `diagnostics`.
Workspace coordinate replacement is the exception: request
`--json --envelope` together. An envelope is not a presentation format above
`--json`: use it only when Share or service diagnostics are part of the answer.

| Route | Why the envelope can matter |
| ----- | --------------------------- |
| `type` / `member ... --match` | Carries the exact API-coordinate correspondence outcome and diagnostics; ordered match endpoints currently make Share non-projectable. |
| `depends <type>` | Carries complete dependency Content, semantic relationship selection, diagnostics, and an available Share for an exact projectable NuGet.org package/TFM request. |
| Single-Library API `diff` | Carries the complete typed comparison outcome and diagnostics; ordered comparison endpoints currently make Share non-projectable. |
| `package activity` | Carries the complete ecosystem change report and diagnostics; Share may be non-projectable. |
| `package query` | Carries one complete `PackageQueryDocument`, including selected-library semantic context when `library-literal` is active, plus diagnostics; Package Query Share is currently non-projectable. |
| `library query` | Carries complete Library-grain query Content, population/evaluation failures, and completion; Library Query Share is currently non-projectable. |
| Exact package-backed Type or Library API `type` | Carries the complete `exact-type` or `exact-library-api` Content and diagnostics; quiet/minimal output is admitted. |
| Online package version population | Unlike projected version JSON, carries the complete directed population Document and source/completion evidence; `--count --envelope` uses the scalar Count as Content. |
| Workspace coordinate replacement (`--json --envelope`) | Carries the derived Share, actual Scope outcome, retention/fallback decision, and diagnostics. |

For adopted routes whose unprojected `--json` is complete Content, that JSON is
semantically identical to `--envelope`'s `content`; whitespace and property
order may differ. Package version JSON is the exception noted above.
`--compact` minifies supported JSON boundaries. Post-service presentation
formats, sections, fields, projections, and row/count controls are generally
incompatible unless the route explicitly defines them as semantic inputs.
Asset-mode `depends`, Member `Call Graph`, other command routes, internal
sub-operations, and `--evidence-envelope` remain unadopted.

On `find`, plain `--json` retains the typed root result array. Adding
`--columns` or `--fields` requests projected JSON instead: the result is a
JSON document containing the same selected rows and snake_case fields as the
`--tsv` and `--jsonl` formats.

For `member -S "Call Graph"`, default Markdown is an edge table. Choose the
view for the task without changing the graph or its ordered edge rows:

```bash
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph"
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --tree
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --mermaid
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --markdown --mermaid
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --tsv
```

Use the Markdown table when edge evidence belongs in a document, `--tree` when
call paths are the natural reading order, Mermaid for a diagram, and
`--tsv`/`--jsonl` for one machine-readable edge row per relationship.
`from` and `to` are always present; `from_group`, `to_group`, and `label`
appear only when the whole graph uses them. A row window can therefore retain
an optional field even when its selected values are empty. `--tree` and
standalone `--mermaid` do not mix with another explicitly selected output
format.

## Discover and select sections

`-D`, `-S`, and `-Q` are the uppercase cross-command query namespace. Use `-D` to
discover sections and fields, `-S` to select exact names, categories, compatible
aliases, or wildcards, `-Q` to discover query facets and operators, and
`--columns`/`--fields` to project values. Discover first instead of guessing names.
On `library`, add `--details` to bare `-D` or one exact category or section to
include supported presentation modes without acquiring the target.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
dnx dotnet-inspect -y -- library System.Text.Json -D --details
dnx dotnet-inspect -y -- library System.Text.Json -D @Dependencies --details
dnx dotnet-inspect -y -- library System.Text.Json -D "Reference Hierarchy" --details
```

Structural discovery describes authored membership without running producers;
effective discovery probes for data. Package and library differ:

| Goal | `package` | `library` |
| ---- | --------- | --------- |
| Orient to a target | `package X -D` — effective base catalog. | `library X -D` — cheap target-aware base catalog. |
| Inspect a category | `package X -D @Category` — effective members. | `library X -D @Category` — structural members; add `--effective` for populated members. |
| Inspect section fields | `package X -D Section` — effective fields. | `library X -D Section` — structural fields; add `--effective` for rendered fields. |
| Inspect output formats | Not yet adopted. | `library X -D --details` or `library X -D <exact-category-or-section> --details` — structural complete-selection capabilities. |
| Read the static graph | `package -D --schema` | `library -D --schema` |

On library, `-D --effective` runs full probes and remains scoped to base
evidence unless a category is named.

For an exact category, detailed discovery reports both the complete category
and its members. It does not choose a compatible member or narrow the category.
For an exact section, `--details` reports the section row; omit it to drill into
fields or columns. After discovery, use exact `-S` section selection for
single-result formats such as tree or Mermaid.

| Command | Base categories | Domain categories |
| ------- | --------------- | ----------------- |
| `package` | `@Package`, `@Files` | `@Dependencies`, `@Audit`, `@SourceLink` |
| `library` | `@Library`, `@Surface` | `@Audit`, `@Performance`, `@SourceLink`, `@Integrations`, `@Metadata`, `@Context` |
| `type` listing | `@Surface` | none |
| `member` and exact type | `@Member` | `@Audit`, `@Calls`, `@Decompiler`, `@Performance`, `@Source`, `@SourceLink` |
| `diff` | `@Diff` | none |
| `project` | `@Project` | none |
| `vocabulary` | `@Vocabulary` | `@API`, `@Decompiler` |
| `ecosystem` | `@Ecosystem` | none |
| `graph libraries` | `@Libraries` | none |
| `package query` | `@Query` | none |
| `library query` | `@Query` | none |

`@Package` groups `Package Info`, `Signals`, `Statistics`, `Target Frameworks`,
`Signature`, `Dependencies`, `Vulnerabilities`, `Manifest`, `Runtime
Dependencies`, and the unbounded `Package files` listing. `@Files` groups the
curated nuspec, README, and skill-file sections. The `type` listing's
`@Surface` category groups `API Info`, public type-kind and type-forwarder
inventories, and `Inspection Failures`. Member `@Member` follows the resolved
view: member-kind summaries for a type, the matching overload inventory for a
member name, or signature and local implementation evidence for one selected
overload. Use its domain doors for audit, call, decompiler, performance, source,
or SourceLink evidence. Diff `@Diff` composes `Changes`, `Analysis Diff`, and
`Implementation Diff`; select the non-composable `Finding Transitions` section
by exact name. Project `@Project` composes restored dependency `Skills` and
`Package README file` inventories. Vocabulary `@Vocabulary` composes the
complete product-owned vocabulary document; use `@API` or `@Decompiler` for
the corresponding query family. Ecosystem `@Ecosystem` composes every section
available after the optional focus operand chooses the route; select exact
`Integrations` for configured Integration bindings. Graph `@Libraries`
composes the pair-wide call-site, summary, and direct-use cluster projections;
coordinate-gated `Public Root Paths` remains exact-name-only. `Switches` is a
section. Package Query `@Query` composes `Packages` and `Query Summary`;
ordinary output remains adaptive and bare `-S` retains `Packages`.
Library Query `@Query` composes `Libraries` and `Query Summary`; ordinary
output remains adaptive and bare `-S` retains `Libraries`.
There are no user-facing `@All`, `@Default`, or `@Hidden` categories.

Library `Unsafe Members` is intentionally standalone rather than category
owned. Select it directly with `-S "Unsafe Members"`; use `-D "Unsafe Members"`
for its fields or `-D --schema` to find it in the complete static graph.

Bare `-S` returns high-value, fixed-length, network-free sections from the
package or library base categories. Sections without evidence are omitted.
`-S --count` returns the candidate count map, including zero rows. Explicit
sections/categories override base scope and may authorize expensive work.
Focused selection omits identity; include `Package Info` or `Library Info` when
needed.

Some large families expose only their category door in the top-level catalog.
Use `library X -D @Performance` or `-D @Metadata`; add `--effective` for
populated members. Row formats require a concrete section or homogeneous
family. Heterogeneous categories use Markdown/JSON; `Performance:*` flattens
kinds and adds `Kind` when multiple kinds have rows.

## Discover query capabilities

`-Q` (alias `--query-help`) is structural and does not acquire or inspect a
target. It is available on `library`, `type`, `member`, `package`,
`package query`, `library query`, `find`, `depends`, and `graph libraries`.
Use it before constructing filters; displayed columns do not imply support for
`--where`, `--order-by`, or `--top`.

```bash
dnx dotnet-inspect -y -- library -Q
dnx dotnet-inspect -y -- type -Q "Body Shapes"
dnx dotnet-inspect -y -- library -Q "Performance: Arrays" --json
dnx dotnet-inspect -y -- library -Q @Performance
dnx dotnet-inspect -y -- depends -Q "Dependency Graph"
dnx dotnet-inspect -y -- package -Q "Dependency Hierarchy"
```

Bare `-Q` lists query-capable sections. Named `-Q` lists exact facet keys,
operators, comparisons, and values; `-v:d` adds examples. JSON retains typed
arrays of operators and legal values. Named TSV/JSONL output requires one
section. `--columns`, `--fields`, `--rows`, and `--count` shape the discovery
rows, not inspected data.

Do not combine `-Q` with `-S`, `-D`, `--where`, `--order-by`, or `--top`.
Each named description is a companion section called `Query: <Section>`;
`-S "Query: Body Shapes"` selects it directly, but normal output and data
wildcards omit companions. `-D "Query: Body Shapes"` describes one companion's
columns; companion schema discovery requires one resolved section.
A known section with no implemented query bindings
says so; for example, `find -Q Results` does not advertise Package Query terms
as API-search predicates.

`package query -Q Packages` exposes the product-owned Package Query term keys,
operators, values, and examples admitted by the CLI:

```bash
dnx dotnet-inspect -y -- package query -Q Packages --json
dnx dotnet-inspect -y -- package query Azure.Mcp \
  --where "tool=true"
dnx dotnet-inspect -y -- package query 'Azure.Mcp*' \
  --where "tool-format=v2" --take 20 -n 5 --jsonl
dnx dotnet-inspect -y -- package query wix \
  --where "license=OSMF" --nuspec-only
dnx dotnet-inspect -y -- package query Newtonsoft.Json \
  --where "license=MIT" --nuspec-only
dnx dotnet-inspect -y -- package query 'Polly.*' \
  --where "depends=System.Threading.Tasks.Extensions" \
  --where "dependency-target=netstandard2.0"
dnx dotnet-inspect -y -- package query Microsoft.Extensions.Http \
  --where "depends starts-with Microsoft.Extensions." \
  --where "dependency-target=net10.0"
dnx dotnet-inspect -y -- package query 'Azure.*' \
  --where "dependencies=cross-prefix"
dnx dotnet-inspect -y -- package query 'Microsoft.Extensions.*' \
  --where "references=Microsoft.Extensions.DependencyInjection.Abstractions" \
  --take 20 -n 5
dnx dotnet-inspect -y -- package query Aspire.Hosting.PostgreSQL \
  --where "depends-ecosystem=ecosystem.aspire"
dnx dotnet-inspect -y -- package query Microsoft.Extensions.Http \
  --where "depends-transitive=Microsoft.Extensions.Primitives" \
  --where "dependency-target=net10.0" \
  --where "dependency-depth=2" --take 1
```

`library query -Q Libraries` exposes direct assembly-reference qualification
for an explicit top-level DLL directory or platform reference pack:

```bash
dnx dotnet-inspect -y -- library query -Q Libraries --json
dnx dotnet-inspect -y -- library query ./bin \
  --where "references=System.Text.Json"
dnx dotnet-inspect -y -- library query --platform runtime \
  --where "references=System.Text.Json" --take 256 -n 10
```

Repeated `references` terms are ANDed at Library grain. `--take` bounds
candidate Metadata inspection; `-n` and `--rows` select matching Library rows
afterward. Missing or malformed Metadata remains visible and can prevent an
exact Count.

The positional-Type Dependency route exposes Source, Target, and Kind
predicates, field and Traversal ordering, Top ranking, row stages, and Depth:

```bash
dnx dotnet-inspect -y -- depends Int128 \
  --where "Kind=Interface" --order-by "Target desc" --top 5
dnx dotnet-inspect -y -- package System.Text.Json@10.0.0 \
  -S "Dependency Hierarchy" --depth 2
```

Traversal is a sequence order and cannot rank `--top`. Asset-mode `depends`
and Package `Dependency Hierarchy` share the rooted-hierarchy profile: Depth
limits traversal work, while row selection is applied afterward.

`--where` repeats select product terms, not arbitrary package-field
expressions. Independent terms are ANDed; the broad `tool=true` term identifies
the .NET tool package type from manifest evidence. Use `tool-format=v1` or
`tool-format=v2` for settings-based format classification; those specific
formats are ORed. `license=any|MIT|OSMF` is nuspec-only: `any` tests declaration
presence, `MIT` matches the exact SPDX expression, and `OSMF` matches the
declared `OSMFEULA.*` basename without reading the file. Query rows represent
individual packages with exact versions and semantic answers. Structured
evidence remains available in unprojected JSON and the inspection envelope.
Queries retrieve values and counts; hosts render any explanatory text.
Dependency predicates inspect all nuspec groups
by default; use `dependency-target=<TFM>` to select one compatible group, or
`dependency-target=all` to spell the default explicitly. The query scope
`all` remains distinct from a manifest's `any` group and does not request
traversal. `dependencies=cross-prefix` matches a direct declaration whose first
dot-delimited package-ID segment differs from the package's own segment.
`depends starts-with <literal-package-id-prefix>` matches direct dependencies
using case-insensitive literal prefix semantics. Include a trailing `.` to
require a dot-delimited family boundary; repeat the term to require every
prefix.
`depends-ecosystem=<ecosystem-id>` classifies direct dependencies against the
registered exact packages and package prefixes for one canonical ecosystem;
repeat it to require every named ecosystem.
`depends-transitive=<package-id>` requires an exact
`dependency-target=<TFM>` and `dependency-depth=2|3|4`. It matches only
source-authorized declaration reachability at depth 2 or greater, not direct
dependencies or a NuGet restore graph. The query admits at most five package
candidates; incomplete traversal remains a visible failure.
`references=<simple-assembly-name>` scans the managed `ref/` and `lib/`
assemblies from every target-framework group, matches `AssemblyRef` simple
names case-insensitively, and reports framework/path evidence without resolving
or traversing the reference.
`--take` bounds candidate work, while `-n` and `--rows` select final
matched-package rows. Without explicit `--take`, a simple `-n N` is pushed into
execution: direct package rows use an effective candidate bound of N, while
filtered queries scan until N matches or their default candidate bound.
Pushdown is capped at 1,000 candidates; larger semantic heads remain valid and
are applied after bounded execution. Selecting a package-content term is
itself approval for archive acquisition and permits at most 20 candidates; use
`--nuspec-only` to reject such a query. `--count` observes selected rows and
succeeds only when completion or a satisfied finite row selection proves that
count exact. Reached candidate bounds and failures remain visible.
Default non-count output shows `Packages` when at least one package matched and
`Query Summary` otherwise. The summary separates candidate, match, and
evaluation-failure counts; select a stable shape with `-S Packages` or
`-S "Query Summary"`. Bare `-S` selects the non-adaptive `Packages` preset, and
explicit `Packages` preserves its empty schema. Select `@Query` to compose both
sections in Markdown or JSON.
Package Query does not accept API-search scopes, source overrides, or ranking.
Query-execution flags cannot be combined with `-Q`.

`library -Q Integrations` describes the concept and ecosystem facets for the
whole Integration family. All integrations are enabled by default; use
`library MyLibrary.dll -S Integrations --where "ecosystem=ecosystem.aspire"`
to enable an ecosystem's registered concepts, or
`--where "integration=integration.aspire"` to select one concept. Aspire
currently enables the complete configured Integration catalog, and an
`integration` predicate narrows within that set. Query discovery lists every
supported concept identity.
`Integrations` supports TSV/JSONL. Neither facet combines with Body Shapes or
Performance Triage filters/rankings.

## Correlate one member's Findings

Select `Finding Census` by its exact name for one body-backed method or
accessor. It returns one indivisible envelope containing the census receipt,
raw Facts, annotated-source document, and document-local fact-to-instance
sidecar. The receipt scopes every instance key so display-identical Findings
remain distinct.

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json Serialize:1 \
  -S "Finding Census" --json
```

The section is explicit-only: categories, broad wildcards, and non-exact
selectors omit or reject it. Markdown and exact singleton JSON preserve the
envelope. Table, TSV, JSONL, count, row-window, field, and column projections
fail because they cannot preserve the correlation document.

## Query rendered body shapes

At library scope, select exact rendered C# syntax occurrences with the stable
IDs from the `C# Body Kinds` vocabulary. A `Kind=...` predicate auto-selects
the explicit-only `Body Shapes` section when no `-S` selection is present:

```bash
dnx dotnet-inspect -y -- vocabulary -S "C# Body Kinds"
dnx dotnet-inspect -y -- library MyLib.dll \
  --where "Kind=ObjectCreationExpression" --jsonl
dnx dotnet-inspect -y -- library MyLib.dll \
  --where "Kind=InvocationExpression" \
  --where "Finding=analysis.call-site" \
  --where "Shape=sync-call-in-async" \
  --where "Confidence>=medium" --jsonl
dnx dotnet-inspect -y -- member Widget Render:1 --library MyLib.dll \
  --where "Kind=InvocationExpression" --jsonl
dnx dotnet-inspect -y -- type Widget --library MyLib.dll \
  --where "Kind=InvocationExpression" --jsonl
```

At library scope, repeated Performance Triage predicates are ANDed before
decompilation. The matching opportunities are mapped through their typed source
owner identities and only those MethodDef bodies are searched for `Kind`.
`Body Shapes` is the default occurrence section. Explicitly select
`-S "Body Shape Summary"` for exact Kind/Match groups with a Count column;
`--columns "Match;Count"` hides the already-known kind. Summary windows select
groups without reducing their occurrence counts. `--count` counts the surviving
rows in the selected view, and hiding columns never aggregates. Occurrence
Member/Token and start/end coordinates locate matches in rendered C# method
bodies, not original source files or IL.

Select a Performance section separately
when the canonical candidate/evidence/IL rows are also needed. Performance
`--top` and `--order-by` do not compose with Body Shapes; use `--rows` to limit
rendered matches.

Type scope requires one exact type and searches only its MethodDef and accessor
bodies. Member scope requires one exact member name or stable selector and
decompiles only the selected MethodDef body. An unambiguous method or
single-accessor member is auto-selected; overloaded names require `Name:N` or
`Name~digest`.
A property or event with multiple body accessors requires an accessor selector;
use `Name~digest:1`/`Name~digest:2` when the owner is overloaded. Every body
query requires exactly one case-sensitive `Kind=...` predicate. Type and member
scope do not yet compose it with Performance Triage predicates.

## Filter and order performance rows

On type/member `Performance Triage` or one concrete library
`Performance: <Kind>` section, use `--where` with a discovered field name and
repeat it to combine predicates. Use `--order-by "Field desc,Other asc"` before
applying output limits.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance: Arrays" \
  --where "Finding=analysis.allocation" --order-by "RootReach desc" --jsonl
```

`Performance:*` orders within each `Kind` group before flattening, while
`--rows` caps the flattened sequence. Do not combine those flags expecting a
global field-ranked prefix; use `--top N` for the curated global rank, or
select one concrete kind when a specific field controls the order.

## Select and count items

Prefer product selection to shell pipes. Every active command or lens has one
effective item sequence:

1. A declared semantic sequence uses complete packages, types, dependencies,
   graph edges, or other domain rows.
2. A route without semantic adoption uses rendered lines.
3. `--lines` explicitly selects rendered lines even when semantic rows exist.

`-n N` and bare `-N` keep the first N items. Add `--tail` for the last N;
`--head` makes the default direction explicit. `--tail-lines` is the compact
rendered-line form.

On adopted semantic routes, `--rows` is a strict one-based inclusive window:
`A..B`, `A..`, or `..B`. A missing required position fails instead of silently
shortening the result. `-n` and `--rows` are ordered stages, so argument order
is observable. `--head` and `--tail` modify `-n`, not the range.

Legacy `--rows` composes with an inferred or explicit rendered-line `-n`, but
not in argument order: the command-owned row window runs before outer line
clipping.

Member `Call Graph` is the current exception: its legacy command-owned
`--rows` window clamps an unavailable end to the available edges. It produces
an empty edge table only when the requested start is beyond the available rows.

`find`, `implements`, `extensions`, `depends`, `ecosystem`, `vocabulary`,
`diff --history`, `match --similar`, `package query`, `library query`, package
activity, package `--versions` / `--versions-with-feed`, `demo list`, Workspace inventory,
Integration graph edges, selected package file/SourceLink inventories,
selected Project document inventories, explicit-source Type catalogs, and
exact Member `Calls` and `Callers` have semantic adoption in their supported
modes.
Partially adopted modes fall back to rendered lines.

Where a route supports it, `--count` is a terminal projection over the selected
semantic rows. On sectioned output, select one concrete table for a scalar
count. Count does not mean “count the unselected input,” and it does not by
itself authorize unbounded work; incomplete population evidence can prevent an
exact count.

`--row` is not a window. With `--print`, `--value`, `--urls`, or `--paths`, it
selects one displayed row; `first` and `last` mean the rendered endpoints.
Missing payloads fail rather than sliding to another row.

Keep work bounds and ranking separate: Package Query `--take N` bounds
candidate work before final row selection, and Library Query `--take N` bounds
its explicit Library population before final row selection, while `--top N`
requires a ranking order. None is another spelling of `-n`.

## Use a URL as part of the answer

Inspect Web consumes the same inspection and portable-query contracts. For a
supported envelope, inspect `share.kind`; `available` means the request is
projectable, not that the browser has adopted its packet format. Do not turn
`nonProjectable` into an approximate link.

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json@10.0.0 Serialize:1 \
  --tfm net10.0 --share url
dnx dotnet-inspect -y -- depends \
  --package Newtonsoft.Json@13.0.4 --tfm net6.0 --share url
```

These browser-restorable format-1 URLs carry canonical datapackets, not
captured output. The receiving Inspect Web host re-runs the represented member
or package-dependency operation. Format-1 packets are not accepted by CLI
complete restoration. For formats 2–4, pass opaque packet text to
`workspace --packet "$packet"`; it rejects URL input. The CLI can issue
Workspace format-3 and derived-Type format-4 packet or URL Shares, but current
Inspect Web rejects both formats. Keep them as packet strings for supported
CLI workflows. Package Query Share is currently `nonProjectable`, and Inspect
Web does not yet restore query-bearing packets. Keep Package Query answers in
Content rather than manufacturing a link. Inspect Web directly adopts Library
Query in the current package's Library navigation by sending the same portable
`references` intent to the shared envelope and filtering with returned asset
IDs; it does not infer matches from display names.
Offer a URL only for a browser-restorable scenario selection.
