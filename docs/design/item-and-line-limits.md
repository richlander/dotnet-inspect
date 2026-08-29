# Item and line selection composition

## Status

Composition map for the focused design work replacing the original umbrella
design for
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).

The earlier design combined CLI grammar, semantic row selection, source
pagination, payload projection, line windows, printing, and Markout behavior in
one document. Its independent-window intersection model was based on a mistaken
reading of legacy `-n --rows`: `--rows` changed the unit of `-n` from rendered
lines to table rows; it did not establish two independent semantic selectors.
That umbrella design is superseded and must not guide implementation.

The current CLI remains unchanged until the focused designs land and an
implementation updates the user-facing documentation and shipped skills.

This document owns only the component map and sequencing. It does not settle
the behavior assigned to a focused owner below.

## Components and owners

| Responsibility | Architectural owner | Focused design |
| --- | --- | --- |
| Ordered Head, Tail, Window, and Top stages over complete sequences | Shared `DotnetInspector.RowSelection` leaf component | [Semantic row selection](semantic-row-selection.md) |
| Row predicates, schema-defined ordering, and ranking metadata | L2 `DotnetInspector.Sections` | [Row query and ordering](row-query-order.md) |
| Declared-row-set binding, atomic preflight, and common format handoff | L2 `DotnetInspector.Sections` | Pending focused integration design |
| CLI aliases, argv-order lowering, conflicts, and diagnostics | L3 `dotnet-inspect` | Pending focused design |
| Provider pagination, acquisition extent, completion evidence, merge, and deduplication | L1 query or source-owning Packages/Services component | Pending focused design |
| Payload projection, printing, and rendered-line windows | L3 `dotnet-inspect` | Pending focused design |
| Rendering already-selected rows | Markout | `richlander/markout#217`, selection-ownership design |

The authority named in each focused design decides its behavior. This map
cannot be used to move a decision into a neighboring component.

The shared component evaluates named sequences atomically and returns no
selected collection on failure. L2 owns invoking that pure operation as a
preflight before projection, payload acquisition, rendering, stdout, or
destination mutation.

## Composition

The components compose in this direction:

```text
CLI tokens
-> L3 validates and lowers complete item-selection gestures
-> L2 resolves section schema, predicates, and effective ordering metadata
-> typed selection request:
   row-set identity + resolved predicate/order identity + RowSelection plan
-> source owner may optimize acquisition against that typed request
-> L2 applies residual predicates and establishes effective baseline order
-> shared RowSelection component executes the plan
-> L2 binds the selected sequences back to their declared row sets
-> payload or field projection
-> format-specific presentation and optional line selection
```

This is also the string-to-structure boundary. Raw CLI spellings exist only
before L3/L2 lowering. The source optimizer receives the typed row-set,
predicate, effective-order, and selection identities needed to prove an
equivalent acquisition. The semantic executor receives complete keyed
sequences after residual predicates and effective baseline order, plus the
typed plan and resolved ordering identity. Neither recovers meaning from option
strings, field names, or rendered text.

Every format receives the same selected typed rows. A renderer may add headings,
table headers, graph context, framing, or other non-row presentation, but it
does not choose which logical rows survive.

Source-side pagination is an optimization of the typed request, not an
alternative meaning for it. When a source cannot prove an equivalent result,
the source owner must acquire a broader extent and let L2 finish residual
predicate/order work before the semantic owner runs the selection plan. The
focused source design will define that proof and the associated completion
states.

The pending payload design owns the last step's exact branches. Ordinary report
windowing consumes formatter-produced report text. Per-payload line selection
must instead produce complete payload values before a structured format encodes
them. Neither operation limits item acquisition or can be pushed into Markout
table-row selection.

## Landing order

1. Define the shared row-selection component and reference behavior.
2. Define the L2 declared-row-set integration and common format handoff.
3. Define how L3 argv syntax lowers into that plan and into a separate line
   selection request.
4. Define source capability, pushdown, merge, deduplication, and completion
   evidence against the shared reference semantics.
5. Define payload printing and rendered-line behavior.
6. Implement the focused contracts together where an intermediate
   implementation would expose incoherent user behavior.
7. As the final implementation step, update shipped skills, help, examples,
   completion, and migration or retired-option tests to teach the settled
   model.

The focused documents may land separately as design work. Implementation must
not ship a partial grammar whose meaning depends on a later slice.

## Markout co-development boundary

The ownership decision precedes source co-development:

1. Decide whether behavior is semantic result selection or presentation.
2. Use Markout co-development only for behavior Markout owns.
3. Point dotnet-inspect at exact Markout source and prove the real consumer
   before a Markout behavior PR merges.
4. Restore a released package before raising the dotnet-inspect implementation
   PR.

The current design assigns ordered semantic selection to a dotnet-inspect
library and requires no new Markout composition API. Markout's existing
single-table row window remains a presentation utility; it is not the
implementation substrate for the semantic plan.
