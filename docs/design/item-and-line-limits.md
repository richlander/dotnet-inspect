# Item and line selection composition

## Status

Composition map for the focused design work replacing the original umbrella
design for
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).

The former document combined CLI grammar, semantic row selection, source
execution, payload projection, line windows, printing, and presentation. It is
superseded and must not guide implementation.

Unchanged documents may still cite this path as the owner of the retired
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) target. Those
citations refer to superseded umbrella text and are non-normative: they preserve
neither the deleted decisions nor their former ownership. Each affected passage
must be reconciled by its focused owner before it can guide implementation.

This PR locks the composition pattern with semantic `RowSelection` as its one
bounded first-owner adoption. All other participant adoptions remain separate
focused efforts. The L2 row-query adoption is tracked by
[#5162](https://github.com/richlander/dotnet-inspect/issues/5162), and the
standing layer-boundary adoption is tracked by
[#5163](https://github.com/richlander/dotnet-inspect/issues/5163).

The current CLI grammar is owned by
[Output shapes](output-shapes.md#stable-flag-vocabulary) and may advance in
independently complete slices that visibly reject composition not yet
available. This document defines no product syntax or behavior. Its only
normative section is [Composition](#composition).

## Participant index

This table identifies authority; it does not define a participant's behavior.

| Responsibility | Architectural owner | Focused design |
| --- | --- | --- |
| Ordered Head, Tail, Window, and Top stages | Shared `DotnetInspector.RowSelection` leaf component | [Semantic row selection](semantic-row-selection.md) |
| Row predicates, schema-defined ordering, and ranking metadata | L2 `DotnetInspector.Sections` | [Row query and ordering](row-query-order.md) |
| Declared row units and the Document-to-Scalar shape ladder | L2 `DotnetInspector.Sections` | [Output shapes](output-shapes.md#the-shape-ladder) |
| Declared-row-set binding, field/column shape projection, logical reductions such as count, and common result binding | L2 `DotnetInspector.Sections` | [Section-row shaping](section-row-shaping.md) |
| Source-delegation planning, the delegated result contract, completion-evidence binding, and exact upstream Count acceptance | Cross-cutting L1 source-delegation pattern | [Source delegation](source-delegation.md) |
| CLI aliases, argv lowering, conflicts, and diagnostics | L3 `dotnet-inspect` | Pending focused design |
| Source-specific acquisition, pagination, retries, caching, merge, deduplication, and proof construction | Each adopting L1 query or source owner | Pending focused adoptions |
| Post-selection payload acquisition | L1 query or source-owning component | Pending focused design |
| Payload projection, printing, export, and rendered-line selection | L3 `dotnet-inspect` | Pending focused design |
| Rendering already-selected rows and values | Markout | Existing presentation contracts |

Each focused owner defines its own types, validation, failures, and gates. This
map cannot move a decision into a neighboring component.

## Composition

The owners compose in this direction:

```text
CLI tokens
-> L3 validates and lowers typed operation intent
-> L2 resolves row-set, predicate, effective-order, selection,
   and reduction identities
-> typed execution request
-> source owner may perform semantics-preserving execution
-> typed source execution result and completion evidence return to L2
-> L2 completes its owner-defined residual operations and result binding,
   invoking shared RowSelection for residual semantic stages where required
-> typed L2 result
-> optional L3 payload projection
   -> typed post-selection acquisition request to source owner when required
   -> typed payload result returns to L3
-> presentation and optional rendered-line selection
```

Raw CLI and field spellings stop at L3/L2 lowering. Downstream owners consume
owner-issued identities and typed requests; they do not recover semantics from
option strings, field names, or rendered text.

Source optimization changes physical execution, not logical ownership or
meaning. A source may satisfy part or all of a typed request only when its
focused design can prove an exact equivalent result and report honest
completion. Before acceptance, an unsupported exact-result candidate may be
declined, or the caller may choose a row-handoff candidate that returns
sufficient rows for the owning L2 and semantic components to finish the
residual request. After acceptance, the
[source-delegation effect protocol](source-delegation.md#effect-protocol)
forbids switching result shapes or retrying another strategy.

The later source handoff is distinct from row execution. When projection needs
content for already-selected payload identities, L3 sends a typed
post-selection acquisition request and consumes the source owner's typed
payload result. The focused source and payload designs own capabilities,
fan-out bounds, budgets, and failure behavior.

Count is one example of that separation.
[Section-row shaping](section-row-shaping.md#count-semantics) defines the
logical row sets and stage it observes. A source such as package search may
answer that count upstream when it can prove exact equivalence and completion;
before source acceptance, the caller otherwise retains a strategy in which L2
counts the applicable rows. After exact-Count acceptance, insufficient evidence
returns `NotSatisfied` rather than falling back. This map does not define the
proof or source capability.

Every presentation format receives the same typed result. A renderer may add
headings, table headers, graph context, framing, or other presentation, but it
does not choose which logical rows or values survive.

### Lock and application order

1. Semantic row-selection behavior is locked by
   [Semantic row selection](semantic-row-selection.md).
2. This document locks only owner sequencing and typed handoffs.
3. Lock L2 row-query and section-row-shaping contracts.
4. Lock the
   [source delegation](source-delegation.md) pattern against that
   L2 contract.
5. Define L3 CLI grammar and lowering against the locked typed contracts.
6. Define payload projection, post-selection acquisition, export, and
   rendered-line behavior.
7. Apply the locked design one subsystem at a time, with each owner changing
   its own design, implementation, and gates.
8. Update shipped skills, help, examples, and completion with each independently
   complete user-visible grammar slice; do not advertise pending composition.

Focused designs may land independently. User-visible behavior must not expose a
partial grammar whose meaning depends on a later subsystem.

### Non-claims

This document does not define:

- CLI spellings, aliases, compatibility, or diagnostics;
- row predicates, ordering, field/column projection mechanics, count semantics,
  or result-binding mechanics;
- source capabilities, stopping rules, pagination, proof receipts, or
  post-selection acquisition mechanics;
- payload cardinality, framing, export, or line-selection behavior;
- Markout APIs or semantic row-selection behavior; or
- current command-specific behavior or migration evidence.
