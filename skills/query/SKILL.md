---
name: dotnet-inspect-query
version: 0.1.0
description: Output formats, -D/-S section discovery and selection, value projection, @ categories, and output limits shared across all commands.
---

# dotnet-inspect: query and output system

The query system is like Go templates, without a DSL: inspection commands emit
structured sections, with the broadest shared query surface on `type`, `member`,
`package`, and `library`. `project` supports `-D` and `-S` but not general
field/column projection. `find` supports `-D` discovery and field/column
projection but not `-S` selection. `diff` supports `-D` and `-S` but not
field/column projection. `timeline` supports section selection and projection
but not `-D` discovery. Relationship commands render fixed output without `-D`
or `-S`. Discover the shape first where available, then select and project.

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
- `--print` — print one document behind a selected printable row; use `--row N` when multiple printable rows exist.
- `--mermaid` — graph-shaped output.

## Discover and select sections

Use `-D` to discover the sections and columns a command can emit, `-S Section` to
select sections by name or wildcard, and `--columns`/`--fields` to project
values. Discover first instead of guessing names.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
```

`@` names a category of sections. Examples include `@Library`, `@Surface`,
`@Audit`, `@Context`, `@Source`, `@SourceLink`, `@Integrations`,
`@Performance`, and `@Metadata`. `Switches` is a plain section, not a category.
Some large families expose only their category door in the top-level catalog;
drill in with `-D @Performance` or `-D @Metadata`. Row formats
(`--tsv`/`--jsonl`/`--table`) require one concrete section or a supported
homogeneous family; `Performance:*` flattens with a leading `Kind` column.

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
- `--rows N` caps table data rows; `2..10` is inclusive, `2+10` is start plus count, and `10..` is open-ended.
- `--tail --rows N` takes table rows from the end; otherwise row windows start at the beginning.
- With `--print`, `--value`, `--urls`, or `--paths`, `--row N|first|last` chooses the projected row; `-n N` still limits output lines.
- `--count` counts rows in one selected table.

Command-specific caps: `-t N` for type/find rows, `-m N` for members, and
`--versions N` for package versions.
