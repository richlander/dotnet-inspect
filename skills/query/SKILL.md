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
projection but not `-S` selection. `diff` supports `-D` and `-S` but not
field/column projection. `timeline` supports section selection and projection
but not `-D` discovery. `workspace` supports output formats, `--count`, and
`--rows`, but not discovery, section selection, or field projection.
`depends` supports `-D`, `-S`, categories, row windows, count, and field/column
projection across its dependency graph and evidence sections. Positional
`depends <type>` also has a separate complete-service `--envelope` path
described below. Other relationship commands may still expose fixed output.
Discover the shape first where available, then select and project.

```bash
dnx dotnet-inspect -y -- <command>
```

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

Positional `depends <type>` and single-Library API `diff` support presence-only
`--envelope`. It
implies JSON and emits the complete service value with
`schema_version`, `result_kind`, `content`, `share`, and `diagnostics`.
For dependencies, `content` is semantically identical to the owner-issued camelCase
`TypeDependencySectionResult` selected by unprojected `depends <type> --json`;
whitespace and property order may differ. `--compact`, `--depth`, and semantic
relationship row selection remain available. Presentation formats,
Discover/schema/effective modes, `-S`, explicit `-v`, Count, field/column or
scalar projection, decoration, and rendered-line clipping are incompatible.
For Library API Diff, unprojected `--json` and envelope `content` both serialize
the complete `LibraryApiDiffOutcome` using result kind `library-api-diff`.
`--all` and `--compact` remain admitted; Type/classification filters, sections,
explicit verbosity, row/line controls, and non-API modes are incompatible with
envelope output. Explicitly projected Diff JSON retains its presentation
schema. Load `skill compatibility` for outcome and scope details.
Asset-mode `depends`, other commands, and `--evidence-envelope` remain unadopted.

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

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
```

Structural discovery describes authored membership without running producers;
effective discovery probes for data. Package and library differ:

| Goal | `package` | `library` |
| ---- | --------- | --------- |
| Orient to a target | `package X -D` — effective base catalog. | `library X -D` — cheap target-aware base catalog. |
| Inspect a category | `package X -D @Category` — effective members. | `library X -D @Category` — structural members; add `--effective` for populated members. |
| Inspect section fields | `package X -D Section` — effective fields. | `library X -D Section` — structural fields; add `--effective` for rendered fields. |
| Read the static graph | `package -D --schema` | `library -D --schema` |

On library, `-D --effective` runs full probes and remains scoped to base
evidence unless a category is named.

| Command | Base categories | Domain categories |
| ------- | --------------- | ----------------- |
| `package` | `@Package`, `@Files` | `@Dependencies`, `@Audit`, `@SourceLink` |
| `library` | `@Library`, `@Surface` | `@Audit`, `@Performance`, `@SourceLink`, `@Integrations`, `@Metadata`, `@Context` |
| `type` listing | `@Surface` | none |
| `member` and exact type | `@Member` | `@Audit`, `@Calls`, `@Decompiler`, `@Performance`, `@Source`, `@SourceLink` |
| `diff` | `@Diff` | none |
| `project` | `@Project` | none |
| `vocabulary` | `@Vocabulary` | `@API`, `@Decompiler` |

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
the corresponding query family. `Switches` is a section. There are no
user-facing `@All`, `@Default`, or `@Hidden` categories.

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
`package query`, and `find`.
Use it before constructing filters; displayed columns do not imply support for
`--where`, `--order-by`, or `--top`.

```bash
dnx dotnet-inspect -y -- library -Q
dnx dotnet-inspect -y -- type -Q "Body Shapes"
dnx dotnet-inspect -y -- library -Q "Performance: Arrays" --json
dnx dotnet-inspect -y -- library -Q @Performance
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
```

`--where` repeats select product terms, not arbitrary package-field
expressions. Independent terms are ANDed; the broad `tool=true` term identifies
the .NET tool package type from manifest evidence. Use `tool-format=v1` or
`tool-format=v2` for settings-based format classification; those specific
formats are ORed. `license=any|MIT|OSMF` is nuspec-only: `any` tests declaration
presence, `MIT` matches the exact SPDX expression, and `OSMF` matches the
declared `OSMFEULA.*` basename without reading the file. Query rows represent
individual packages with exact versions, semantic answers, and structured
evidence. Queries retrieve values and counts; hosts render any explanatory
text. Dependency predicates
inspect all nuspec groups
by default; use `dependency-target=<TFM>` to select one compatible group, or
`dependency-target=all` to spell the default explicitly. The query scope
`all` remains distinct from a manifest's `any` group and does not request
traversal. `--take` bounds candidate work, while `-n` and `--rows` select final
matched-package rows. Without explicit `--take`, a simple `-n N` is pushed into
execution: direct package rows use an effective candidate bound of N, while
filtered queries scan until N matches or their default candidate bound.
Pushdown is capped at 1,000 candidates; larger semantic heads remain valid and
are applied after bounded execution. Selecting a package-content term is
itself approval for archive acquisition and permits at most 20 candidates; use
`--nuspec-only` to reject such a query. `--count` observes selected rows and
succeeds only when completion or a satisfied finite row selection proves that
count exact. Reached candidate bounds and failures remain visible.
Package Query does not
accept API-search scopes, source overrides, or ranking. Query-execution flags
cannot be combined with `-Q`.

`library -Q Integrations` describes the ecosystem facet for the whole Integration
family. All integrations are enabled by default; use
`library MyLibrary.dll -S Integrations --where "ecosystem=ecosystem.aspire"`
to narrow the ordinary result. The initial supported value is
`ecosystem.aspire`. Use a concrete section such as `Integration: Aspire` for
TSV/JSONL. This predicate does not combine with Body Shapes or Performance
Triage filters/rankings.

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

## Limit output

Prefer built-in limits to shell pipes:

- `-n N` and numeric shorthand like `-6` select semantic rows on commands
  that declare them. Other commands reject `-n` alone.
- Add `--lines` for the first N rendered lines or `--tail-lines` for the last
  N. `--lines --tail` is equivalent to `--tail-lines`.
- `--rows N` takes the first N data rows per table on commands that retain the
  legacy row window, preserving headings and headers; add `--tail` for the last
  N. On adopted semantic-row surfaces, use `-n N` instead.
- On commands retaining the legacy row window, `--rows 2..10` is an absolute
  1-based inclusive range (nine rows), `2+10` means ten rows starting at row 2,
  and `10..` runs from row 10 to the end. These legacy ranges reject
  `--head`/`--tail`, and all legacy `--rows` forms reject `-n`.
- `--row` is not a window. With `--print`, `--value`, `--urls`, or `--paths`,
  it selects one displayed row, not a compacted projection position.
  `first`/`last` mean rendered endpoints; missing payloads fail instead of
  sliding. `-n N` may still limit the result.
- `--count` counts rows in one selected table.

`find`, `implements`, `extensions`, `depends`, `ecosystem`, `vocabulary`,
`timeline`, `package query`, package activity, package `--versions` /
`--versions-with-feed`, and `demo list` use semantic rows. `-n N` selects
complete items; `-n N --lines` instead clips rendered output. Where supported,
`--rows` accepts only `A..B`, `A..`, and `..B`; `-n` and `--rows` compose as
stages in argv order. `--head` and `--tail` modify `-n`, not the range. On
`package query`, `--take N` separately bounds package work before semantic row
selection.
