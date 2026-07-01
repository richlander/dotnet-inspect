# Row query and ordering design

## Status

Design proposal. This document describes a future query model; it does not
describe behavior that exists today.

## Problem

Table sections are becoming richer. `Performance Triage` now has columns such as
`Allocation`, `Path`, and `Path Confidence`, and future sections will add more
domain-specific columns. Adding one flag per column, such as `--allocation` or
`--path-confidence`, does not scale:

- the command surface grows without bound;
- field names become less discoverable than table schemas;
- aliases and one-off predicates drift from section column names;
- agents must learn both the section schema and an unrelated option vocabulary.

The existing query model already has `-D`, `--schema`, `--columns`, and
`--fields`. Row filtering and ordering should extend that model instead of
adding more command-specific flags.

## Goals

1. Let users filter rows by section field/column without adding bespoke flags.
2. Make default row ordering discoverable through `--schema`.
3. Keep `--top` meaningful by defining it as a post-filter, post-order semantic
   row cap.
4. Preserve `--columns` and `--fields` as projection, not filtering.
5. Let existing focused flags, such as `--loop`, lower to the same row-predicate
   engine for compatibility.
6. Keep section ownership clear: filterable and sortable fields are declared by
   the selected section schema.

## Non-goals

- Do not add a general expression language in the first version.
- Do not make row predicates span multiple sections.
- Do not change section selection or scanner backpressure.
- Do not make `--top` a renderer cap; `--rows -n N` already owns that role.
- Do not require every section to be sortable or filterable.

## Proposed command model

### Field-scoped predicates

Use `--where:<Field>=<value>` for row filtering:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --where:Allocation="boxed *" \
  --where:Path="loop body" \
  --where:Confidence>=medium
```

`--where` is deliberately separate from `--columns`:

- `--columns` projects visible columns.
- `--where:<Field>` filters rows by a field that may or may not be projected.

Multiple `--where:*` predicates are combined with AND.

### Ordering

Use `--order-by` for explicit row ordering:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --where:Allocation="boxed *" \
  --order-by "RootReach desc,Confidence desc" \
  --top 20
```

If `--order-by` is omitted, the selected section's default order applies. That
default must be discoverable through `--schema`.

### Top

`--top N` means "take the first N rows after filtering and ordering."

Pipeline:

```text
select section -> collect rows -> apply --where -> apply order -> apply --top
-> project --columns/--fields -> render
```

`--rows -n N` remains a renderer/display cap. It is applied after projection and
rendering decisions, and should preserve headings and table headers.

## Predicate grammar

Start with a small field predicate grammar:

| Form | Meaning |
| --- | --- |
| `--where:Field=value` | exact or enum match |
| `--where:Field="glob *"` | glob match for string fields |
| `--where:Field!=value` | negated exact/glob match |
| `--where:Field>=10` | numeric or ranked comparison |
| `--where:Field<=10` | numeric or ranked comparison |

Examples:

```bash
--where:Shape=box-value-type
--where:Allocation="boxed *"
--where:Path="loop body"
--where:PathConfidence=dominates-return
--where:RootReach>=10
--where:Confidence>=medium
```

Ranked comparisons are section/schema-defined. For `Performance Triage`,
`Confidence>=medium` means `high` or `medium`.

Regular expressions can be considered later. If added, they should use an
explicit operator such as `~=` so glob and regex behavior do not blur.

## Ordering grammar

`--order-by` accepts a comma-separated list of fields plus optional direction:

```bash
--order-by "RootReach desc"
--order-by "Confidence desc,RootReach desc,Member asc"
```

Rules:

- Unknown fields produce a diagnostic with suggestions.
- Direction defaults to `asc` unless the section declares a field-specific
  default.
- Named composite orders are allowed when declared by the section, for example
  `Triage desc`.
- `--top` uses the effective order, whether explicit or default.

## Schema discoverability

`--schema` must expose row query metadata for each table section:

```text
Section: Performance Triage

Default order:
  Triage desc
    1. InLoop desc
    2. Confidence desc (high > medium > low)
    3. RootReach desc
    4. Member asc
    5. IL asc
    6. Shape asc

Filterable fields:
  Member, RootReach, Shape, Evidence, Fix, Confidence, Loop, Allocation, Path,
  PathConfidence, IL

Sortable fields:
  Triage, RootReach, Confidence, Member, Shape, IL, Allocation, Path,
  PathConfidence
```

This makes the default sort order visible exactly where users and agents already
learn columns.

`-D <section>` may include a compact form of the same metadata, but `--schema`
is the authoritative static contract.

## Performance Triage example

Default query:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" --top 20
```

Equivalent conceptual query:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --order-by "Triage desc" \
  --top 20
```

Filtered query:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --where:Allocation="boxed *" \
  --where:Path="loop body" \
  --order-by "RootReach desc" \
  --top 10 \
  --columns "Member,Shape,Allocation,Path,PathConfidence,RootReach,IL"
```

Compact note in human output when `--top` is used:

```text
Showing top 10 by RootReach desc after 2 row filters.
```

For default ordering:

```text
Showing top 20 by Performance Triage default order: InLoop, Confidence, RootReach.
```

Suppress these notes for `--tsv`, `--jsonl`, `--json`, and quiet output.

## Compatibility and lowering

Existing focused flags remain for compatibility, but should lower to row
predicates internally:

| Existing option | Row-query equivalent |
| --- | --- |
| `--loop` | `--where:Loop=loop` or section-specific loop predicate |
| `--min-confidence medium` | `--where:Confidence>=medium` |
| `--triage-shape box-value-type` | `--where:Shape=box-value-type` |
| `--top 20` | unchanged; semantic cap after order |

This lets command-specific UX remain stable while new columns avoid bespoke
flags.

## Section descriptor contract

Table sections should declare query metadata alongside their row schema:

```csharp
DefaultOrder = "Triage desc";
OrderDescription =
[
    "InLoop desc",
    "Confidence desc (high > medium > low)",
    "RootReach desc",
    "Member asc",
    "IL asc",
    "Shape asc",
];
FilterableFields = [...];
SortableFields = [...];
CompositeOrders = ["Triage"];
```

The same metadata should drive:

- `--schema` output;
- validation and suggestions for `--where:*` and `--order-by`;
- help text for section-scoped options;
- agent skills and generated examples;
- compatibility lowering from legacy focused flags.

## Error behavior

Unknown fields should fail with suggestions:

```text
Error: Field 'Allocaton' is not filterable in section 'Performance Triage'.

Did you mean:
  Allocation
```

Invalid operators should name valid forms:

```text
Error: Field 'RootReach' supports numeric comparisons: =, !=, >=, <=.
```

Unsortable sections should reject `--order-by` clearly:

```text
Error: Section 'Facts' does not declare sortable fields. Use --rows -n N to cap
rendered rows.
```

## Open questions

1. Should `--where:Field=value` treat unquoted `*` as a glob or require quotes?
2. Should field names normalize aliases such as `PathConfidence` and
   `Path Confidence`?
3. Should `--order-by Confidence` default to descending for ranked fields?
4. Should a selected section with no default order allow `--top`, or should
   `--top` require a default or explicit order?
5. Should `--schema` include examples generated from the section metadata?
