# Row query and ordering design

## Status

Design proposal. This document describes a future query model; it does not
describe behavior that exists today.

[Item and line limits](item-and-line-limits.md) settles the adjacent
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) vocabulary:
`-n`/bare `-N` is the plain first/last item count, `--rows` carries only
absolute ranges, and `--top` is a validated ranked count composed with
`--order-by`.

[The package query CLI](package-query-cli.md) proposes reusing this model's
`--where` grammar, unchanged, as the nuspec/promoted facet vocabulary for
`find --package-prefix` package rows.

## Problem

Table sections are becoming richer. `Performance Triage` now has columns such as
`Allocation`, `Path`, `Path Confidence`, and `Post Dominance`, and future
sections will add more domain-specific columns. Adding one flag per column, such
as `--allocation` or `--path-confidence`, does not scale:

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
3. Keep `--top` meaningful by requiring a ranking order and defining it as a
   post-filter, post-order semantic row cap.
4. Preserve `--columns` and `--fields` as projection, not filtering.
5. Let existing focused flags, such as `--loop`, lower to the same row-predicate
   engine for compatibility.
6. Keep section ownership clear: filterable and sortable fields are declared by
   the selected section schema.

## Non-goals

- Do not add a general expression language in the first version.
- Do not make row predicates span multiple sections.
- Do not change section selection or scanner backpressure.
- Do not make `--top` a plain result count; `-n N` owns that role.
- Do not make `--top` an absolute row range; `--rows N..M` owns that role.
- Do not require every section to be sortable or filterable.

## Proposed command model

### Field-scoped predicates

Use `--where "<Field><operator><value>"` for row filtering:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --where "Allocation=boxed *" \
  --where "Path=loop body" \
  --where "Confidence>=medium"
```

`--where` is deliberately separate from `--columns`:

- `--columns` projects visible columns.
- `--where` filters rows by a field that may or may not be projected.

Multiple `--where` predicates are combined with AND.

The predicate expression is one quoted command-line argument. This avoids shell
redirection bugs with operators such as `>=` and `<=`.

### Ordering

Use `--order-by` for explicit row ordering:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --where "Allocation=boxed *" \
  --top 20 \
  --order-by "RootReach desc,Confidence desc"
```

If `--order-by` is omitted, the selected section's default order applies. That
default and whether it is a ranking or stable sequence must be discoverable
through `--schema`.

### Top

`--top N` means "rank the filtered rows by the effective order, then take the
first N." It requires an explicit `--order-by` unless the section declares that
its default order is a ranking order. Alphabetical, insertion, and upstream
listing order are stable sequences, not ranking defaults.

`--top` is mutually exclusive with `-n`, `--tail`, and `--count`. An absolute
`--rows` range may page within the ranked result.

Pipeline:

```text
select section -> collect rows -> apply --where -> apply effective ranking order
-> apply --top -> intersect --rows range -> project --columns/--fields -> render
```

Plain `-n N` follows the same pipeline but makes no ranking claim. `--count`
branches after filtering and rejects item/range windows. The full cross-shape
pipeline lives in [Item and line limits](item-and-line-limits.md).

## Predicate grammar

Start with a small field predicate grammar:

| Form | Meaning |
| --- | --- |
| `--where "Field=value"` | exact or enum match |
| `--where "Field=glob *"` | glob match for string fields |
| `--where "Field!=value"` | negated exact/glob match |
| `--where "Field>=10"` | numeric or ranked comparison |
| `--where "Field<=10"` | numeric or ranked comparison |

Examples:

```bash
--where "Candidate=pt~0123456789abcdef"
--where "Finding=analysis.call-site"
--where "Provenance=exact"
--where "Shape=box-value-type"
--where "Operation=box"
--where "Allocation=boxed *"
--where "Path=loop body"
--where "PathConfidence=dominates-return"
--where "PostDominance=return-post-dominates"
--where "RootReach>=10"
--where "Confidence>=medium"
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
- An explicit `--order-by` satisfies `--top`'s ranking requirement.
- A default order satisfies bare `--top` only when the section declares
  `DefaultOrderKind = Ranking`.

## Schema discoverability

`--schema` must expose row query metadata for each table section:

```text
Section: Performance Triage

Default order:
  Triage desc
Default order kind: Ranking
Order expansion:
  1. Loop desc
  2. Confidence desc (high > medium > low)
  3. RootReach desc
  4. Member asc
  5. IL asc
  6. Shape asc

Filterable fields:
  Member, Candidate, Finding, Provenance, RootReach, Shape, Operation, Token,
  Evidence, Fix, Confidence, Loop, Allocation, Path, PathConfidence,
  PostDominance, IL, Weight, DirectSites, OncePaths, ConditionalPaths,
  RepeatedPaths, UnknownPaths, CachedSites, OpaquePaths, Saturated

Sortable fields:
  Triage, RootReach, Confidence, Loop, Member, Candidate, Finding, Provenance,
  Shape, Operation, Token, IL, Allocation, Path, PathConfidence, PostDominance,
  Weight, DirectSites, OncePaths, ConditionalPaths, RepeatedPaths, UnknownPaths,
  CachedSites, OpaquePaths
```

This makes the default sort order and its meaning visible exactly where users
and agents already learn columns. Candidate fingerprint ordering, for example,
is a `Sequence`: it is useful for stable pagination within one build, not as a
semantic priority and cannot support bare `--top`. Exact token predicates
normalize hexadecimal metadata tokens, so `0x2000001` matches rendered
`0x02000001`.

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
  --top 20 \
  --order-by "Triage desc"
```

Filtered query:

```bash
dotnet-inspect library MyApp.dll -S "Performance Triage" \
  --where "Allocation=boxed *" \
  --where "Path=loop body" \
  --top 10 \
  --order-by "RootReach desc" \
  --columns "Member,Shape,Allocation,Path,PathConfidence,PostDominance,RootReach,IL"
```

Compact human output names the explicit ranking:

```text
Showing top 10 by RootReach desc after 2 row filters.
```

For a schema-declared ranking default:

```text
Showing top 20 by Performance Triage default order: Loop, Confidence, RootReach.
```

Suppress these notes for `--tsv`, `--jsonl`, `--json`, and quiet output.
Plain `-n` uses "first N" or "last N" wording even when an explicit order is
present; it never upgrades itself to a ranking claim.

## Compatibility and lowering

Existing focused flags remain for compatibility, but should lower to row
predicates internally:

| Existing option | Row-query equivalent |
| --- | --- |
| `--loop` | `--where "Loop=loop"` or section-specific loop predicate |
| `--min-confidence medium` | `--where "Confidence>=medium"` |
| `--triage-shape box-value-type` | `--where "Shape=box-value-type"` |
| `--top 20` | ranked semantic cap using the declared Performance Triage default order |

This lets command-specific UX remain stable while new columns avoid bespoke
flags.

## Section descriptor contract

Table sections should declare query metadata alongside their row schema:

```csharp
DefaultOrder = "Triage desc";
DefaultOrderKind = OrderKind.Ranking;
OrderDescription =
[
    "Loop desc",
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
- validation and suggestions for `--where` and `--order-by`;
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
Error: Section 'Facts' does not declare sortable fields. Use -n N to limit its
declared rows.
```

A stable but non-ranking default should reject bare `--top`:

```text
Error: Section 'Files' has a sequence default, not a ranking default.
Use --top N with --order-by, or use -n N for a positional limit.
```

## Open questions

1. Should `--where "Field=glob *"` use glob matching automatically when `*` or
   `?` appears, or require an explicit glob operator?
2. Should field names normalize aliases such as `PathConfidence` and
   `Path Confidence`, or `PostDominance` and `Post Dominance`?
3. Should `--order-by Confidence` default to descending for ranked fields?
4. Should `--schema` include examples generated from the section metadata?
