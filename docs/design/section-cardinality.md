# Section cardinality

## Status

Focused Output Shapes contract for semantic section cardinality. This document
defines the cross-cutting scalar-or-inventory pattern that section owners adopt
one at a time. It does not claim that every existing command has adopted the
pattern.

The motivating production cases are deliberately different:

- merged #8221 proves that an installed-Platform Type inventory can answer
  exact Count without retaining its Rows; and
- `library --platform System.Text.Json -S "Library Info"` describes one
  assembly value whose properties are not a row population.

Issue #8198 supplies the Browser demand: obtain exact inventory Count for
planning, then obtain Rows from the same immutable comparison generation,
using continuation when the population is large.
[Tracker #8229](https://github.com/richlander/dotnet-inspect/issues/8229)
owns the four-slice production-adoption sequence.

## Authority and scope

This document owns:

- classification of a resolved section as scalar or inventory;
- the invariant that an inventory exposes Rows and Count as peer semantic
  terminals, while a scalar exposes neither;
- correspondence between an inventory's Rows and Count population;
- the requirement that Count followed by Rows preserve one owner-issued
  population binding; and
- the declaration obligations an adopting section exposes to hosts and
  discovery.

This document does not own:

- which domain item a particular section declares as one row;
- row-query, ordering, semantic selection, Count execution, completion
  evidence, or failure precedence;
- construction or validation of snapshot, generation, or continuation
  identities;
- CLI syntax, compatibility, diagnostics, or option admission;
- Browser callback, credit, worker, or operation-lifetime mechanics;
- rendering, Markout lowering, JSON shape, fields, columns, or lines; or
- any producer's optimized Count implementation.

[Output shapes](output-shapes.md) consumes this classification before applying
its Document-to-Scalar rendering ladder.
[L2 section-row shaping](section-row-shaping.md) receives only inventory row
sets and owns Rows and Count execution over them.
[Query-space composition](query-space-composition.md) owns terminal resolution,
source completion, generation, and continuation composition.

## Conventional basis

The model starts from established value-versus-sequence distinctions:

| Precedent | Adopted idea | Deliberate difference |
| --- | --- | --- |
| JSON objects and arrays | Object properties describe one value; array elements form a sequence. | Rendered JSON is not authority. The product owner declares semantic shape before serialization. |
| LINQ sequences | `Count` describes sequence elements and can use a specialized cardinality path without changing sequence meaning. | Exact Count retains completion and population-binding evidence instead of reporting whatever was enumerated. |
| Relational tables | Rows have owner-defined identity and `COUNT(*)` reduces the same selected relation. | One section may declare multiple independent row sets whose Counts remain separate. |

The deliberate restriction is stronger than permissive output tooling: a
scalar's visible fields never become an implicit sequence merely because a
renderer can enumerate them. This keeps Count useful for planning and prevents
presentation changes from changing semantic cardinality.

## Semantic shapes

Every resolved section instance has exactly one semantic shape:

| Shape | Meaning | Rows | Count |
| --- | --- | --- | --- |
| **Scalar** | Zero or one typed value describing its resolved subject | No | No |
| **Inventory** | Zero or more typed domain items in one or more declared row sets | Yes | Yes |

There is no Count-only or Rows-only section shape.

A scalar may be absent or inapplicable, but absence does not become an empty
inventory or Count zero. An inventory may be complete and empty; its exact
Count is zero and its Rows result is empty under the row-shaping owner's
ordinary completion contract.

The section's display name and renderer do not determine its semantic shape.
The owner classifies the resolved section after operation and subject scope are
known. The same display name may consequently participate in different
resolved operations:

- `Library Info` for one assembly is scalar: assembly name, version,
  architecture, and related properties describe one value.
- An all-libraries survey may declare one library as its row unit and is then
  an inventory, even if its renderer reuses a `Library Info` heading.

Field count, JSON property count, table-cell count, Markout row count, and
rendered-line count are presentation facts. None establishes semantic
cardinality. Selecting one property from a scalar value may project another
scalar; it does not create an inventory.

Conversely, a tree, graph, list, field set, or table may be an inventory when
its owner declares a domain row unit. Its Rows and Count observe that unit
regardless of presentation. A graph may declare directed relationships as
rows; a metadata table may declare metadata records as rows. The renderer does
not add nodes, labels, fields, or properties to the population.

## Inventory terminal contract

Rows and Count are peer terminals over the same owner-declared population.
They share:

- resolved operation and subject scope;
- participating row-set identities;
- predicates, membership projection, order, and semantic selection;
- source completion requirements; and
- one owner-issued snapshot or generation when the population can change
  between executions.

One execution chooses one terminal. A consumer that needs Count before Rows
performs two explicit executions:

```text
Count(structural request, row intent)
    -> exact cardinality + population binding

Rows(structural request, same row intent, population binding)
    -> row values + completion/continuation
```

The second request must name or otherwise prove the same population binding.
An expired or incompatible binding fails explicitly; the consumer restarts
rather than combining Count from one generation with Rows from another.

Count does not imply random access, source seekability, eager row production,
or a specific physical scan. Rows may arrive through one complete result,
streamed transport batches, or source continuation. Every continuation remains
bound to the same structural request, row intent, and population generation.
Different source batch sizes cannot change Count or row meaning.

Browser callback credit is transport backpressure, not source continuation.
Counting callback rows reports observed delivery only and never substitutes
for exact Count. Transport manifests may separately count the typed pieces
needed to reconstruct one delivered value; those counts do not describe an
inventory population.

## Count execution

Every inventory has exact Count semantics. A specialized Count kernel is an
optional execution optimization.

An adopter may initially obtain Count by executing the complete Rows
population and reducing its exact cardinality. It may later add a lower-layer
fold, owner-issued cardinality, or accepted upstream Count without changing
the section contract. The optimized path must preserve the same admission,
selection, completion, and population binding as Rows.

When required evidence is unavailable, Count returns the owner-composed failure
or decline outcome. It never reports the number of rows produced so far, a
source page size, a retained prefix, or zero as success.

Merged #8221 is the first lower-layer reference. Metadata counts the same
compact public Type inventory used by Rows, binds success to module MVID, and
declines when image-local evidence cannot prove the complete population. That
implementation proves one adopter; it does not define every inventory's fold
or establish a general cursor API.

## Declaration and discovery

An adopting owner declares semantic shape as resource-free capability metadata
beside the section's stable identity. Hosts consume that declaration; they do
not infer shape from section names, rendered schemas, CLR collection types, or
sample output.

For a scalar declaration:

- structural discovery advertises neither Rows nor Count;
- row-query and semantic-selection controls do not target the section; and
- property/value projection remains governed by the scalar's output owner.

For an inventory declaration:

- structural discovery advertises both Rows and Count;
- the owner names every declared row-set identity and logical row unit;
- Rows and Count lower through the same row intent and population binding; and
- an optimized Count capability, when present, is execution metadata rather
  than a different semantic shape.

Hosts may omit a control from a particular interface, but the resource-free
descriptor remains complete. A CLI may expose `--count` while a website shows
a summary chip; both consume the same inventory declaration and terminal
meaning.

## Adoption sequence

The pattern stages through focused owners rather than sweeping every section:

1. Lock this scalar-or-inventory contract, using merged #8221 as the positive
   inventory reference and single-assembly `Library Info` as the scalar
   counterexample.
2. Under #8227, publish resource-free scalar/inventory and terminal
   capabilities through the host-neutral section declaration and structural
   discovery path.
3. Under #8228, have the Library owner adopt scalar declaration for
   single-assembly `Library Info`, reject Count and row selection there, and
   preserve aggregate all-libraries output as its independently declared
   library-row inventory.
4. Under #8198, have the Compare inventory owner execute Count followed by
   Rows over one immutable generation in Inspect Web, preserving continuation
   for large Type/member inventories.

The current plan therefore has four slices, two of which reach production
hosts directly: CLI scalar capability adoption and Browser inventory
Count/Rows adoption. Each owner records its own implementation and retirement
decisions without reopening this pattern contract.

After those slices lock, apply the inventory pattern to one Analysis-owned
population. The generation-bound assembly-group call census from merged #8214
is the current candidate; its focused owner chooses the row unit and production
consumer before implementation.

## Required evidence

| Gate | Property | Status |
| --- | --- | --- |
| `Type_ListingKindCount_MatchesMetadataInventory` and the compact Type Count gates named in [Assembly inspection query](assembly-inspection-query.md#compact-type-inventory-cardinality) | One inventory's optimized Count and Rows retain the same population and MVID binding. | Verified by merged #8221. |
| `SectionCapabilitiesPairRowsAndCount` | Resource-free declarations expose Rows and Count together or neither, including direct .NET and structural-discovery projections. | Unverified until the declaration substrate lands. |
| `SingleLibraryInfoHasNoRowsOrCount` | `System.Text.Json` single-assembly `Library Info` retains its typed fields/value projections while Count and row selection fail before acquisition or rendering. | Unverified until the CLI adoption slice lands. |
| `AggregateLibraryInfoCountsLibraries` | An all-libraries survey counts library rows rather than properties and preserves the owner-declared aggregate scope. | Unverified until the CLI adoption slice lands. |
| `BrowserCountThenRowsPreservesGeneration` | Inspect Web obtains Count and continued Rows from one immutable comparison generation and rejects stale or incompatible population bindings. | Unverified until the Browser adoption slice lands. |
| Analysis adopter gate | A second owner preserves its named row population across Rows, Count, failure, and generation evidence without copying Metadata-specific machinery. | Named by the focused Analysis adoption issue before implementation. |

No gate should count rendered fields, JSON properties, transport rows, or
partial delivery as a substitute for semantic inventory cardinality.

## Non-claims

This contract does not require every scalar to render as one token, every
inventory to stream, every Count to be cheaper than Rows, or every source to
support random access. It does not introduce a universal `MoveNext` API,
combined Count-plus-Rows terminal, hidden pagination, or one global section
registry migration.

Existing unclassified sections retain their current behavior until their
owner adopts the pattern. That behavior is compatibility evidence, not
authority for new Count semantics.
