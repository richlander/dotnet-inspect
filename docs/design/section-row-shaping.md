# L2 section-row shaping

## Status

Focused L2 design proposal for
[#5187](https://github.com/richlander/dotnet-inspect/issues/5187), adopting the
row-selection composition pattern locked by
[Item and line selection composition](item-and-line-limits.md). The current
product does not implement this contract.

All asserted behavior is unverified until the Release gates in
[Required gates](#required-gates) land.

Related designs:

- [Output shapes](output-shapes.md) owns declared row units and the
  Document-to-Scalar shape ladder.
- [Row query and ordering](row-query-order.md) owns typed predicates, effective
  baseline order, ranking metadata, and executable-plan resolution.
- [Semantic row selection](semantic-row-selection.md) owns ordered `Head`,
  `Tail`, `Window`, and `Top` behavior over named sequences.
- [Inspection layers](inspection-layers.md) assigns executable request and
  result binding to L2.
- [Item and line selection composition](item-and-line-limits.md) sequences L2
  with source execution and presentation without redefining either owner.

## Authority and scope

L2 `DotnetInspector.Sections` is the authority that binds resolved row-shaping
intent to owner-declared logical row sets and returns typed row or reduction
outcomes.

This design owns:

- stable declared-row-set identities and their ordered binding;
- the mapping between those identities and semantic `RowSequenceKey` tokens;
- the distinction between membership projection and cell projection;
- the L2 reference order across projection, row query, semantic selection, and
  terminal reduction;
- Count's logical meaning and observed stage;
- Count outcomes across one or more declared row sets; and
- typed rebinding of selected rows, exact counts, and failures to row-set
  identities.

This design does not own:

- which logical item a section declares as one row;
- predicate, baseline-order, ranking-order, or semantic-stage behavior;
- CLI option names, grammar, aliases, conflicts, diagnostics, or
  compatibility;
- source requests, capabilities, pagination, acquisition, pushdown proofs, or
  completion-evidence construction;
- payload acquisition, printing, export, rendering, formats, or line
  selection;
- Markout behavior; or
- concrete implementation APIs.

Those adjacent owners provide typed inputs or consume typed results without
redefining L2 row shaping.

## Declared row sets

A declared row set is one ordered logical sequence to which row-query and
semantic-selection operations apply independently. Its owner supplies:

- a stable typed row-set identity;
- its row-schema identity;
- its ordered caller-owned row values; and
- the shape capabilities valid for those rows.

The row-set identity is not a section display name, subject label, heading, or
rendered path. Those values may accompany an outer presentation envelope, but
they do not participate in binding and never enter `SectionRowResult`.

The producer decides whether related subjects contribute one aggregate row set
or several independent row sets before L2 shaping begins. L2 preserves that
decision. It does not merge independent sets because their labels match, split
an aggregate set by provenance, or infer scope from rendered output.

Requested empty row sets remain declared. They may produce an exact zero count
or an empty row result. A selected set carrying an owner-issued `Absent` source
disposition is not complete empty; it enters the source-failure branch. An
unrequested or undeclared set produces no result entry. These three states are
distinct.

## Sequence-key binding

L2 assigns one unique `RowSequenceKey` to each declared row-set identity and
retains an immutable two-way binding for one shaping request. Semantic
`RowSelection` receives only the keys and ordered values.

On return, L2 resolves every selected sequence or
`NamedRowWindowFailure` through that retained binding. Component-owned numeric
keys never escape as product identities, and L2 never reconstructs identity
from sequence position or display text.

Duplicate row-set identities, duplicate key values, an unknown returned key, or
a schema mismatch reject binding rather than selecting an arbitrary set.

## Shaping cohorts

One semantic named-sequence invocation can contain only row sets that share:

- one row-schema identity;
- one resolved predicate and baseline-order plan;
- one semantic `RowSelectionPlan`; and
- one resolved-order catalog.

L2 partitions selected row sets into **shaping cohorts** by that complete
resolved contract. Cohorts follow the position of their first declared row set,
and row sets retain declaration order within a cohort. Equal schema identities
alone do not combine requests whose resolved plans differ.

Each cohort receives one semantic named-sequence invocation. Resolver caching
and named-sequence failure precedence therefore follow the semantic contract
within that cohort. Across cohorts, the first semantic failure in cohort order
wins. L2 completes each cohort invocation before starting the next one. A
failure skips every later cohort; callbacks reached in earlier cohorts and
before the failure in the failing cohort remain observable, and L2 neither
replays nor rolls them back.

`RowSequenceKey` values remain unique across the complete shaping request even
though semantic execution occurs per cohort. L2 reassembles successful cohort
results into original declared row-set order.

## Projection kinds

L2 resolves typed projection intent against each selected row schema before it
constructs an executable request. Projection has two cardinality roles.

### Membership projection

Membership projection narrows which declared items are rows. It applies only
when the shape's named items are themselves the declared row unit. A field-set
section is the canonical case: selecting two fields selects two field-entry
rows.

Membership projection runs before row predicates, baseline ordering, and
semantic selection. Its survivors become the owner-defined rows consumed by
the row-query contract.

### Cell projection

Cell projection narrows the fields or columns carried by each surviving row. It
does not create, remove, duplicate, merge, or reorder rows.

Cell projection runs only on the row-result branch after semantic selection.
Predicate and order bindings may use schema fields that cell projection does
not retain. Count validates cell-projection intent but does not apply it,
because a terminal cardinality has no row cells.

An unsupported, unresolved, or inapplicable projection fails request resolution
rather than silently preserving the unprojected result.

## Resolution order

Resolution is fail-fast in this total order:

1. Validate request-wide structure, including the selected-row-set list, the
   terminal Rows-or-Count branch, and request-wide operation combinations owned
   by L2.
2. Visit selected row sets in declaration order. For each set:
   1. bind its declared identity, sequence key, schema, and shape capability,
      diagnosing a duplicate at the later declaration;
   2. resolve membership projection;
   3. resolve row-query intent under the row-query owner's internal failure
      order;
   4. construct and bind the semantic selection plan and order catalog;
   5. resolve cell projection; and
   6. validate that the terminal branch is supported for that set.
3. Validate cross-set cohort and result-binding invariants in first-declared
   row-set order.

The first failure wins and no later category is evaluated. Its failure variant
names request-wide scope or the declared row-set identity reached by this
order. Atomicity prevents a partial executable request; it does not permit an
implementation to choose among simultaneous invalid bindings.

## Reference composition

For each shaping cohort, L2's logical reference sequence is:

```text
declared rows
-> membership projection
-> typed row predicates
-> effective baseline order
-> one named semantic RowSelection invocation
-> one terminal branch:
   -> selected rows -> cell projection -> typed row result
   -> Count -> typed exact-count outcome
```

The row-query owner defines predicate and baseline-order behavior. The semantic
owner defines every selection stage, including stage-local ranking and strict
failure. This design defines where their completed result enters L2 projection
or reduction.

Count is terminal. No later operation that requires rows, fields, row order, or
row identity can consume its reduction outcome. A later presentation owner may
render the typed count result but cannot resume row shaping from it.

## Count semantics

For one row set whose cohort's preceding stages successfully produce logical
sequence `R`, Count returns the exact non-negative cardinality `|R|`.

Count therefore observes every preceding membership projection, predicate,
baseline-order binding, and semantic stage. Ordering alone does not change
cardinality, but it remains part of the logical request because a preceding
`Top` can expose resolver or comparer behavior and because later semantic
stages consume its ordered result.

Examples:

| Preceding result | Count outcome |
| --- | --- |
| Empty complete sequence | `0` |
| Three rows, then `Head(5)` | `3` |
| Twelve rows, then `Head(5)` | `5` |
| Twelve rows, then `Window(3, 5)` | `3` |
| Three rows, then strict `Window(3, 5)` | window failure; no count |
| Twelve rows, then `Top(5, ScoreDescending)` | `5`, after preserving `Top`'s semantic observations |

For `Head(N) -> Count`, finding N ordered matches is sufficient to prove the
exact result N without proving corpus exhaustion. Returning fewer than N is
exact only when the implementation can prove that no additional applicable
row exists. Source completion is therefore relative to the resolved logical
request, not necessarily to the complete underlying corpus.

A work, page, time, memory, or acquisition budget is not semantic `Head`.
Reaching such a budget cannot turn the rows observed so far into a successful
Count.

Strict semantic stages retain their failure behavior. Count cannot replace an
unsatisfied `Window` with the number of rows that happened to fall inside its
partial bounds.

## Multiple row sets

Count preserves declared row-set identity and order. A successful Count result
contains one exact cardinality, including exact zero, for every selected set.

One exact count entry occupies the Scalar rung of the shape ladder. Multiple
entries occupy an ordered row-set/count Table. L2 does not also invent a total
across those entries.

An aggregate count exists only when the producer declared one aggregate row set
before shaping. Conversely, independently declared sets remain independent even
when they share a section definition or display label.

A set whose source disposition is failed, `Absent`, or insufficient for its
resolved logical request never contributes zero and never disappears from a
successful-looking aggregate. Exact zero means the available evidence proves
that the logical request has no surviving rows.

The reference failure precedence is:

1. Resolve the complete L2 request. Resolution failure returns no executable
   request. Validation follows declared row-set order, then the row-query
   owner's order within each set.
2. Establish source evidence sufficient for every selected row set's resolved
   logical request. If any owner-issued disposition is failed, `Absent`, or
   reports insufficient completion for that request, return an L2 failure
   result binding every selected row-set identity to its source outcome,
   invoke no residual semantic selection, and produce no Count result. A
   proven semantic prefix may be sufficient without corpus exhaustion; a work,
   page, time, memory, or acquisition cutoff is not.
3. Apply one named semantic-selection invocation per cohort in cohort order.
   The first semantic failure returns its bound row-set failure and no Count
   result.
4. Only semantic success produces exact count entries for every selected set.

This order prevents a source-failed set from disappearing merely because it
could not enter the named semantic invocation. It also preserves the semantic
component's request-wide all-or-failure and cross-sequence resolver behavior.
An accepted source optimization may replace the reference path only under the
full equivalence rule below.

## Result binding and failure

The conceptual L2 result algebra is:

```text
SectionRowResult =
    Rows(ordered row-set row results)
  | Count(ordered row-set exact counts)
  | Failure(
        Resolution(Request | RowSet(declared row-set identity),
                   structured resolution failure)
      | Source(ordered row-set source dispositions)
      | Semantic(declared row-set identity, semantic failure))
```

L2 therefore returns exactly one typed result branch:

- **Rows** returns each selected caller-owned value under its declared
  row-set identity, with only the validated cell projection attached.
- **Count** returns ordered row-set identities and exact cardinalities.
- **Failure** binds a request-wide or row-set-scoped resolution failure,
  owner-issued source outcomes, or one semantic failure to its explicit scope.

Each source-disposition entry contains the declared row-set identity plus the
opaque owner-issued disposition and completion evidence. The L2 failure branch
does not redefine their construction, reason taxonomy, or lifetime. When source
binding fails, it preserves one ordered entry per selected set so successful,
failed, incomplete, and absent source outcomes remain distinguishable.
Complete companion sets remain visible as complete source dispositions, not as
successful L2 row results. The `Failure` variant structurally contains neither
a `Rows` nor a `Count` payload.

Selection position and `RowSequenceKey` are not promoted into row identity.
Presentation labels, diagnostic sentences, rendered values, and exception text
do not enter any result branch.

Resolution of row-set, projection, row-query, selection, and reduction intent
is atomic. A resolution failure returns no partial executable request. A
semantic named-sequence failure returns no selected row collection and no count
entries, preserving the semantic component's all-or-failure contract.

Typed source failures and completion evidence remain visible when L2 binds a
source result. A selected set lacking evidence sufficient for its resolved
logical request prevents the entire Count result rather than producing a
partial exact-count payload. No unproven cardinality is exposed as exact.

## Physical execution freedom

L2 owns Count's logical meaning, not where or how it executes. Equivalent
physical strategies may include ordinary L2 enumeration, provider count APIs,
source aggregation, exact feed or index metadata, cached exact cardinality, or
early termination.

The source-execution design owns capability negotiation, proof shape, stopping
rules, completion evidence, and the non-vacuous optimized-execution gate. L2
accepts an optimized result only when it is observationally equivalent to the
complete reference contracts in
[Row query and ordering](row-query-order.md#logical-composition) and
[Semantic row selection](semantic-row-selection.md#reference-semantics-and-optimized-execution).
That includes their predicate, baseline-order, callback, exception-identity,
resolver-cardinality, caching, and failure-precedence observations. Comparer
call count and pair order remain excluded exactly where the semantic owner
excludes them.

The optimized result must also preserve:

- the same row-set identities and exact cardinalities;
- the same request-wide versus per-set failure boundary; and
- honest evidence that every exact count is complete for the logical request.

If an optimization cannot establish those properties, sufficient rows return
to L2 for residual shaping. A fast incomplete number is not a Count result.

This optimization property remains unverified until the focused source design
adds the conditional Release gate
`OptimizedCountMatchesSectionRowReference`. That gate is required only when an
optimized-result acceptance path exists. It must prove that the optimized path
was exercised, compare it with the complete reference contract over positive
and sentinel-failure cases, and reject insufficient completion evidence. An
implementation with no optimized-result acceptance path makes no optimization
claim and does not satisfy the conditional gate vacuously.

## Required gates

The implementation must add these named Release gates:

| Gate | Contract |
| --- | --- |
| `DeclaredRowSetIdentityIsTyped` | Equal owner-issued identities bind equal row sets; labels, headings, paths, and sequence positions do not affect identity or equality. |
| `RowSequenceKeysRoundTripDeclaredIdentity` | Every declared row set receives one unique semantic key, every returned key resolves to the original identity, and duplicate or unknown identities/keys reject. |
| `MembershipProjectionPrecedesRowQuery` | Field-entry membership selection changes the rows seen by predicates, baseline order, and semantic stages; a close negative table-column projection changes no membership. |
| `CellProjectionFollowsSelectionAndPreservesCardinality` | Cell projection applies only to selected row results, may omit predicate/order fields, and never changes membership, order, or Count. |
| `CountObservesPrecedingSemanticStages` | Empty, undersized, exact, and oversized `Head`, `Tail`, and `Top` plans reduce to the cardinality of their selected result, including `Head(N) -> Count = min(N, input count)`. |
| `CountConsumesSuccessfulWindows` | Successful closed, prefix, suffix, and boundless `Window` stages feed their selected cardinality to Count, including a Window after an earlier stage has reindexed its input. |
| `CountPreservesTopStageObservations` | A Count terminal still reaches every reached `Top` stage, resolves its comparer once through the supplied resolver, and propagates sentinel resolver or comparer exceptions unchanged instead of returning a cardinality. |
| `StrictWindowFailurePreemptsCount` | Closed, prefix, and suffix windows return their semantic failure rather than a partial cardinality when the required position is absent. |
| `HeadCountAcceptsProvenPrefixCompletion` | N ordered applicable rows with proof sufficient for `Head(N) -> Count` return exact N without corpus exhaustion; fewer than N without proof that no later applicable row exists returns source failure rather than a count. |
| `CountPreservesDeclaredRowSetScope` | One request-complete selected set produces one exact entry; multiple request-complete sets retain declaration order and identity; no total is invented unless the producer declared one aggregate set. |
| `HeterogeneousSchemasFormOrderedCohorts` | Sets sharing one complete schema-and-plan contract enter one named semantic invocation; different contracts form cohorts ordered by first declaration; two schemas with different `Top` bindings use their own resolvers; a sentinel failure skips every later cohort without replaying or rolling back earlier callbacks; successful results reassemble in declared order. |
| `CountFailurePrecedenceIsDeterministic` | Resolution failure prevents source execution; any selected source-failed, `Absent`, or request-insufficient set prevents every residual cohort and Count; source evidence sufficient for the request executes cohorts in order, and the first semantic cohort failure prevents every count entry. |
| `EmptyFailedAndAbsentSetsStayDistinct` | A request-complete empty selected set produces exact zero after semantic success; a selected request-insufficient, failed, or owner-issued `Absent` disposition prevents Count and remains a typed source failure; an unrequested or undeclared set produces no entry. |
| `SectionRowFailureBindingPreservesSourceOutcomes` | A source failure result retains one ordered selected-set entry and each owner-issued success, failure, incompleteness, or absence disposition; its variant structurally exposes no Rows or Count payload and invokes no semantic selection. |
| `SectionRowResolutionIsAtomic` | Any invalid row-set, schema, projection, row-query, selection, or reduction binding returns one structured failure with explicit request-wide or declared-row-set scope and no partial executable request. |
| `SectionRowResolutionFailureOrderIsDeterministic` | Simultaneous invalid bindings return the first failure in the request-wide, declared-row-set, and per-set category order above; the row-query substep preserves its owner's internal order. |
| `SectionRowResultsArePresentationFree` | Row, Count, and failure results contain typed row-set identities, values, exact cardinalities, or owner-issued outcome evidence without headings, formatted cells, diagnostic sentences, or renderer state. |

## Non-claims

This design does not select CLI combinations, define source optimization
protocols, publish concrete implementation types, decide presentation wording,
or change current product behavior. Those decisions remain with their focused
owners.
