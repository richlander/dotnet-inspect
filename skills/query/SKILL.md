---
name: dotnet-inspect-query
version: 0.1.0
description: Output formats, curated package/library -D/-S discovery and selection, value projection, @ categories, and output limits shared across commands.
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
Relationship commands render fixed output without `-D` or `-S`. Discover the
shape first where available, then select and project.

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

On `find`, plain `--json` retains the typed result shape. Adding
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

`-D` and `-S` are the uppercase cross-command query namespace. Use `-D` to
discover sections and fields, `-S` to select exact names, categories, compatible
aliases, or wildcards, and `--columns`/`--fields` to project values. Discover
first instead of guessing names.

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

`@Package` groups `Package Info`, `Signals`, `Statistics`, `Target Frameworks`,
`Signature`, `Dependencies`, `Vulnerabilities`, `Manifest`, `Runtime
Dependencies`, and the unbounded `Package files` listing. `@Files` groups the
curated nuspec, README, and skill-file sections. Other commands expose
categories such as member `@Source`; `Switches` is a section. There are no
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
Body Shapes remains the output section; select a Performance section separately
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

- `-n N` and numeric shorthand like `-6` cap output lines, like `head`.
- `--tail` takes the same count from the end, like `tail`.
- `--rows N` takes the first N data rows per table, preserving headings and
  headers; add `--tail` for the last N.
- `--rows 2..10` is an absolute 1-based inclusive range (nine rows), `2+10`
  means ten rows starting at row 2, and `10..` runs from row 10 to the end.
  Ranges reject `--head`/`--tail`; all `--rows` forms reject `-n`.
- `--row` is not a window. With `--print`, `--value`, `--urls`, or `--paths`,
  it selects one displayed row, not a compacted projection position.
  `first`/`last` mean rendered endpoints; missing payloads fail instead of
  sliding. `-n N` may still limit the result.
- `--count` counts rows in one selected table.

Command-specific caps: `-t N` for type/find rows, `-m N` for members, and
`--versions N` for package versions.
