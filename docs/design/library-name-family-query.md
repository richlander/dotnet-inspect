# Library name-family query

## Status, owner, and claim

Status: **implementation design** for
[#9027](https://github.com/richlander/dotnet-inspect/issues/9027), the first
production adoption of the
[Library name-family summary](library-name-family-summary.md).

The **Library Name-Family Query** owner defines this claim:

> Given one available Library name-family document and one requested source
> population, select and semantically bound its owner-issued family or Type
> rows without changing the complete population, partition, qualification, or
> receipt evidence.

This owner defines the host-neutral QuerySpace vocabulary and its completed
inspection result. The Research producer retains authority over Type-word
segmentation, family membership, counts, order, provenance qualification, and
receipts. QuerySpace retains authority over typed predicates, baseline order,
semantic row selection, and terminal Count. The CLI retains exact section
selection and Markout presentation.

The first production consumer is one exact-name, explicit-only Library section
named **Name Families**. Browser/Wasm consumes the same managed query in a later
slice. Graph topology, Metrics amplitude, distinctiveness scores, semantic role
labels, and family-level edge aggregation remain outside this design.

## User question

> Which recurring Type-name suffixes describe this Library's vocabulary, how
> widespread is each family, and which exact Types support it?

The first CLI view answers with ranked family rows and compact Type examples.
Complete Content JSON retains every exact Type-word row, every family member
address, every population and partition receipt, and provenance qualification.
The rendered table is a projection of that Content, not a second analysis.

## Basis and boundaries

The producer must enumerate the complete Type-definition inventory and build
complete family partitions before an exact family count exists. QuerySpace
therefore begins at the first meaningful boundary after that irreducible
aggregation:

```text
exact Library image
  -> complete LibraryNameFamilyDocument
  -> requested source population
  -> owner rows
  -> typed predicates
  -> baseline order
  -> semantic row selection
  -> Rows or Count
  -> host projection
```

This is an explicit complete-evidence reference slice. It does not claim that
Head, Window, or Count can avoid Metadata enumeration or family aggregation.
It does require selection to run before a host constructs Markout rows or Type
example cells. Building every presentation row and filtering it in the CLI is
not an implementation of this design.

Before opening the inspection context, a compatibility local-file descriptor
is upgraded once to an immutable artifact-backed snapshot when this query is
requested. The snapshot retains the local path only for adjacent-PDB
discovery. The `PdbContext` opens that exact snapshot, and query execution then
borrows its existing `AssemblyInspectionSession`; it does not reopen the path
or construct a second image. When the command has loaded a matching Portable
PDB, the query supplies the Metadata-owned source-provenance outcome to the
producer. Missing, unavailable, incomplete, failed, or rejected provenance
remains visible and does not make the all-Type population unavailable.

The query exposes two row scopes:

- **family rows** retain one owner-issued suffix family and its complete
  supporting Type addresses in the requested population; and
- **Type rows** retain one owner-issued Type-word row from that population,
  including exact identity, spans, assigned families, residuals, and optional
  source evidence.

The first CLI section renders family rows. Complete JSON carries both owner
row kinds. Browser/Wasm and later focused Type drill-down surfaces may request
the Type row scope without adding another segmentation or family algorithm.

## Population request

The operation request selects exactly one
`LibraryNameFamilyPopulationKind`. `AllTypes` is the default. The four
provenance-qualified populations are admitted only when the producer issued
them from complete, exactly bound source evidence:

- `OrdinaryEvidenceOnly`;
- `GeneratedEvidenceOnly`;
- `MixedEvidence`; and
- `Unknown`.

Requesting a population absent from an otherwise available document produces
a typed unavailable query result naming the requested population and the
document's provenance qualification. It never falls back to `AllTypes` and
never returns an available empty population.

Population selection precedes row-query evaluation. Population is therefore
an operation term, not a row predicate or a presentation column. It chooses
one owner-issued complete population without rebuilding families from Type
rows.

## Family-row vocabulary

The family-row vocabulary declares stable typed facets for:

| Key | Domain | Predicates | Order |
| --- | --- | --- | --- |
| `kind` | one-word or two-word suffix | equal, not equal | yes |
| `word` | exact ordinal suffix word | equal, not equal | no |
| `type-count` | non-negative integer | equal, not equal, at most, at least | yes |
| `public-type-count` | non-negative integer | equal, not equal, at most, at least | yes |
| `namespace-count` | non-negative integer | equal, not equal, at most, at least | yes |

`word` matches any exact word in the one- or two-word family identity. It is
ordinal and does not parse the rendered family spelling.

The default baseline order is the producer's global family order:

1. descending Type count;
2. descending distinct namespace count; and
3. exact ordinal family identity.

The vocabulary names that order `prevalence`. It is a ranking order and is
also the default `Top` ranking. Equal rows preserve the producer's incoming
order.

The family scope admits `Head`, `Tail`, `Window`, and `Top`. Count applies the
same predicates and ordered stages as Rows and is exact because the requested
owner population is complete.

## Type-row vocabulary

The Type-row vocabulary declares stable typed facets for:

| Key | Domain | Predicates | Order |
| --- | --- | --- | --- |
| `simple-name` | exact metadata simple name | equal, not equal | yes |
| `namespace` | exact metadata namespace | equal, not equal | yes |
| `definition-token` | positive TypeDef token | equal, not equal, at most, at least | yes |
| `definition-kind` | Metadata-owned Type kind | equal, not equal | yes |
| `public-surface` | boolean | equal, not equal | yes |
| `family-kind` | one-word or two-word suffix | equal, not equal | no |
| `family-word` | exact ordinal suffix word | equal, not equal | no |

`family-kind` and `family-word` match an assigned family; a Type with no
family of the requested kind does not match. Neither key inspects residual
display text.

The default baseline order is exact Type identity inside the bound module:
ascending TypeDef token. The vocabulary names that sequence order
`definition`. It is not a ranking and cannot be the default for `Top`.
The Type scope admits `Head`, `Tail`, and `Window`.

## Completed result

Execution produces one typed outcome:

- **Available** retains the complete owner document, selected population,
  resolved QuerySpace request, selected family or Type rows for Rows, or exact
  selected count for Count.
- **Unavailable** retains the producer's typed unavailable reason or a missing
  requested provenance population.
- **Rejected** retains the producer's binding or methodology rejection.
- **Failed** retains an unexpected acquisition or execution exception.

An Available row result and Count result identify the same requested
population, row scope, predicates, order, and semantic stages. Hosts do not
re-run predicates or infer Count from rendered lines.

The inspection envelope uses the complete
`LibraryNameFamilyDocument` as Content. Its Share is explicitly
non-projectable until a portable immutable source coordinate and Browser
restore route are adopted. Provenance qualification and all producer receipts
are Content, not diagnostics. Diagnostics are reserved for service-level
conditions and do not duplicate typed producer outcomes.

## CLI and output contract

`library -S "Name Families"` is the only section gesture that executes this
query. The section is:

- exact-name-only and excluded from every automatic verbosity preset and
  category;
- an inventory supporting Rows and Count;
- whole-assembly, bounded by producer limits, and declared unbounded for
  automatic planning because its cost and row population scale with the
  inspected Library; and
- rendered through Markout from already selected host-neutral family rows.

The table shows the exact family spelling, family kind, Type count, public
Type count, namespace spread, and a clearly labeled bounded example list.
The example list is presentation evidence only; it never replaces the
complete supporting addresses in Content.

`--name-family-population` chooses `all`, `ordinary`, `generated`, `mixed`, or
`unknown` and requires exact **Name Families** selection. `-n`, `--head`,
`--tail`, and `--rows` lower to semantic QuerySpace stages for the family row
scope. `--count` executes the QuerySpace Count terminal and emits its exact
result directly.

Exact **Name Families** with `--json` emits complete Content JSON. With
`--envelope`, the same serializer writes the envelope's Content subtree.
These complete transports admit the population request but reject row, field,
column, Count, line, tree, print, and shape projection. Projected table JSON
would erase receipts and supporting evidence and is therefore not a substitute
for this transport.

Typed unavailable, rejected, and failed outcomes write an explicit error and
return nonzero. They never render an empty Name Families table.

## Gates

Release tests must establish:

1. QuerySpace registration exposes both row scopes, their exact facets,
   named orders, stages, Rows, and Count.
2. Family predicates, precedence, order, Head, Tail, Window, Top, and Count
   operate on typed owner rows before host projection.
3. Type predicates and token order preserve exact Type-word evidence.
4. Missing provenance populations remain typed unavailable while `AllTypes`
   remains available.
5. The CLI section is exact-only, omitted from automatic and category
   selection, and borrows the command's one open image.
6. Markdown rows preserve producer order, exact family spelling, counts, and
   bounded examples without host-side row selection.
7. `--count` equals the corresponding QuerySpace Rows cardinality.
8. Content-only JSON and envelope Content are equal and retain every
   population, Type row, family member address, qualification, methodology,
   and receipt.
9. Producer/query failures are visible and nonzero.
10. FluentValidation 12.1.1 demonstrates the `Validator` vocabulary family,
    and the repository fixture demonstrates ordinary/generated population
    separation.

Exact NativeAOT before/after measurements are required for every supported
CLI terminal changed by this adoption. The evidence belongs in the pull
request, not this design.
