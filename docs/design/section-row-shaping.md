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
- [Source delegation](source-delegation.md) owns delegation planning, the
  delegated result contract, completion-evidence binding, and the equivalence
  gates for accepted upstream Count and row handoff.
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

The shaping request separately supplies zero or more ordered row-intent
associations. Each explicit association contains one immutable typed
row-query/selection intent instance and a non-empty ordered list of selected
row-set identities to which that instance applies. L2 owns validating and
binding those associations; the declared-row-set owner does not infer them
from schema, position, or presentation.

The row-set identity is not a section display name, subject label, heading, or
rendered path. Those values may accompany an outer presentation envelope, but
they do not participate in binding and never enter `SectionRowResult`.

The producer decides whether related subjects contribute one aggregate row set
or several independent row sets before L2 shaping begins. L2 preserves that
decision. It does not merge independent sets because their labels match, split
an aggregate set by provenance, or infer scope from rendered output.

Requested empty row sets remain declared. They may produce an exact zero count
or an empty row result. A selected set carrying an owner-issued `Absent` source
disposition is not complete empty; Count treats it as source failure, while a
Rows request preserves it as a typed row-set outcome. An unrequested,
undeclared, or projection-inapplicable set produces no shaping entry. These
states are distinct.

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

During request resolution, L2 assigns one immutable, request-local
`RowIntentBindingIdentity` to each typed row-query/selection intent instance
reached through the ordered associations. Reuse of the same instance reuses
the identity. Separately declared instances receive different identities even
when their fields are structurally equal. Equality is owner-issued token
equality only.

When the request carries no row-intent associations, L2 mints one default
request-local `RowIntentBindingIdentity` and associates it with every
participating row set in declaration order. Per schema, that binding resolves
to no predicates, the effective baseline order defined by the row-query owner,
and an empty semantic selection plan. Plain Rows and Count requests therefore
use the same cohort path rather than bypassing row-query or semantic
composition.

An explicit association may name a selected set that projection later makes
nonparticipating, but every participating set must be named by exactly one
association. An unknown row-set identity, a repeated association for one
selected set, or a participating set with no association rejects resolution.

L2 then mints one immutable, request-local `ShapingCohortIdentity` for each
exact pair of owner-issued row-schema identity and
`RowIntentBindingIdentity`. L2 resolves that pair once and associates the
resulting predicate plan, baseline order, semantic plan, and order catalog with
the cohort identity.

One semantic named-sequence invocation can contain only participating row sets
bound to the same `ShapingCohortIdentity`, and therefore sharing:

- one row-schema identity;
- one resolved predicate and baseline-order plan;
- one semantic `RowSelectionPlan`; and
- one resolved-order catalog.

L2 does not infer binding or cohort equality by structurally comparing intent
fields, resolved plans, catalogs, delegates, or display values. Equal-looking
contracts resolved under different identities remain separate. Cohorts follow
the position of their first participating declared row set, and row sets retain
declaration order within a cohort.

During execution, a cohort is **entered** only when the current terminal admits
at least one of its row-bearing members to residual shaping. Each entered cohort
receives one semantic named-sequence invocation containing only those admitted
members. A cohort with no admitted member receives no invocation, and a
Count-blocking source outcome prevents every cohort from being entered.

Resolver caching and named-sequence failure precedence therefore follow the
semantic contract within each entered cohort. Across entered cohorts, the first
semantic failure in cohort order wins. L2 completes each invocation before
starting the next one. A failure skips every later cohort; callbacks reached in
earlier cohorts and before the failure in the failing cohort remain observable,
and L2 neither replays nor rolls them back.

`RowSequenceKey` values remain unique across the complete shaping request even
though semantic execution occurs per cohort. L2 reassembles successful cohort
results into original participating declared-row-set order.

## Projection kinds

When a request carries no projection intent, every selected declared row set
participates with its full cells.

For a non-empty projection request, L2 resolves typed intent across all selected
row schemas before it constructs an executable request. Projection is a
request-wide allow list: it succeeds when at least one requested name resolves
in at least one selected schema and fails with request-wide scope only when no
requested name resolves anywhere.

For each selected row set under that non-empty request, resolution records one
of three outcomes:

- membership projection;
- cell projection; or
- no applicable requested name, so the set contributes no shaping entry.

The third outcome is neither a complete empty set nor source absence.
Projection has two cardinality roles for participating sets.

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

A malformed or unsupported projection operation fails request resolution.
An unmatched allow list fails only when it resolves in no selected schema;
an individual nonmatching set contributes nothing rather than failing its
healthy companions or silently preserving an unprojected result.

## Resolution order

Resolution is fail-fast in this total order:

1. Require a present, non-empty selected-row-set list.
2. Require exactly one recognized terminal branch: Row outcomes or Count.
3. Validate L2-owned request-wide operation combinations in operation
   declaration order.
4. Visit selected row sets in declaration order. For each set:
   1. validate its declared row-set identity;
   2. reject a duplicate identity at the later declaration;
   3. assign its `RowSequenceKey` and reject a duplicate key at the later
      declaration;
   4. resolve its owner-issued row-schema identity; and
   5. validate its declared shape capabilities.
5. Resolve projection:
   1. with no projection intent, mark every selected set as participating with
      full cells;
   2. otherwise validate the projection-operation kind;
   3. visit requested names in declaration order and selected schemas in
      declaration order, recording every match;
   4. if no match exists, return request-wide failure; and
   5. classify every selected set in declaration order as membership, cell, or
      no-contribution.
6. Resolve row-intent associations:
   1. when the request carries none, mint one default
      `RowIntentBindingIdentity` and associate every participating row set with
      it in declaration order;
   2. otherwise visit associations in declaration order, require one typed
      intent instance and a non-empty associated-row-set list, and assign or
      reuse that instance's `RowIntentBindingIdentity`;
   3. within each association, visit row-set identities in declaration order,
      reject an identity outside the selected-row-set list at the reached
      reference, and reject a repeated association at the later reference; and
   4. visit participating row sets in declaration order and reject the first
      set with no association.
7. Visit participating row sets in declaration order. For each set:
   1. combine its associated `RowIntentBindingIdentity` with the set's schema
      identity;
   2. at that exact pair's first occurrence, mint its cohort identity, resolve
      row-query intent under the row-query owner's internal failure order, and
      construct its semantic plan and order catalog;
   3. at later occurrences, reuse that exact cohort identity and resolved
      contract; and
   4. validate the terminal branch against the set's shape capabilities.
8. Visit cohorts in first-participating-declaration order and require:
   1. every member to carry the cohort's schema and intent-binding identities;
      and
   2. every participating row set and sequence key to occur exactly once in
      the immutable forward and reverse result-binding maps.

An empty cohort is unrepresentable by construction: step 7 mints an identity
only at the first participating row set for its schema/intent-binding pair.
Non-emptiness is therefore an invariant of construction, not a resolution
failure.

The first failure wins and no later category is evaluated. Its failure scope
is:

- request-wide for steps 1-3, a malformed row-set identity in step 4.1, a
  malformed or unsupported projection operation in step 5.2, an unmatched
  projection request in step 5.4, a malformed or empty association in step
  6.2, or an unknown row-set reference in step 6.3;
- the reached validated declared row-set identity for steps 4.2-4.5, a
  duplicate association or missing participating-set association in step 6,
  or per-set validation in step 7; and
- the first participating row-set identity for an invalid cohort in step 8.

A request-wide invalid-identity failure retains the selected-row-set
declaration ordinal and supplied token in the structured reason. A request-wide
projection-operation failure retains its typed operation position and kind.
A request-wide association failure retains the typed association ordinal and,
for an unknown reference, its reference ordinal and supplied row-set token.
None manufactures a declared-row-set scope for an entry that never bound
successfully.

Atomicity prevents a partial executable request; it does not permit an
implementation to choose among simultaneous invalid bindings. This list is
complete; there is no unordered "remaining invariant" bucket.

## Reference composition

For each shaping cohort, L2's logical reference sequence is:

```text
participating declared rows
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
contains one exact cardinality, including exact zero, for every participating
selected set.

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

Without redefining the source owner's disposition or evidence taxonomy, L2
consumes two independent typed facts for each participating set:

- whether supplied row values are usable for the resolved **Rows** request and
  may enter residual shaping; and
- whether the evidence is sufficient for an exact **Count** result.

A source result may be Rows-usable while carrying evidence that the underlying
candidate set is incomplete and therefore Count-insufficient. Bare
`package search` is the canonical case: its capped rows remain visible with
their owner-issued incompleteness evidence, but the cap does not become
semantic `Head` or prove an exact count. A failed, `Absent`, or otherwise
Rows-unavailable result carries no row values into residual shaping.

The execution and failure precedence after successful resolution is:

1. Bind one owner-issued source result and its completion evidence to every
   participating row set in declaration order.
2. For **Count**, any failed, `Absent`, or Count-insufficient result returns one
   source-failure result containing every participating set's disposition and
   completion evidence but no row values. It invokes no residual row-query or
   semantic execution and produces no Count. A proven semantic prefix may be
   Count-sufficient without corpus exhaustion; a work, page, time, memory, or
   acquisition cutoff is not.
3. For **Row outcomes**, retain every source disposition and completion
   evidence. A Rows-usable result enters residual execution with its supplied
   row values; a failed, `Absent`, or otherwise Rows-unavailable result remains
   a disposition-and-evidence-only outcome with no row values. One set's
   outcome does not suppress healthy or incomplete-but-usable companion rows.
4. Visit entered cohorts in cohort order:
   1. prepare admitted row-bearing sets in declaration order through membership
      projection, predicates, and effective baseline ordering;
   2. propagate an accessor, predicate, or baseline-order exception unchanged
      and skip every later row set and cohort; then
   3. invoke one named semantic-selection operation for the prepared cohort.
      A semantic failure returns its one bound failure and skips every later
      cohort; resolver and comparer exceptions propagate unchanged.
5. Only after every entered cohort succeeds does L2 publish a result:
   1. Count returns exact entries for every participating set; or
   2. Row outcomes reassemble selected rows with their source dispositions and
      completion evidence, plus disposition-and-evidence-only unavailable
      outcomes, in original participating row-set order.

No partially assembled top-level result is published before step 5. Callbacks
already reached remain observable, but successful rows from an earlier cohort
do not escape when a later row-query exception or semantic failure preempts the
request. Within each cohort, the semantic component retains its named-sequence
all-or-failure and cross-sequence resolver behavior. An accepted source
optimization may replace the reference path only under the full equivalence
rule below.

## Result binding and failure

The conceptual L2 result algebra is:

```text
RowSetOutcome =
    SelectedRows(declared row-set identity, rows, cell projection,
                 owner-issued disposition and completion evidence)
  | SourceDisposition(declared row-set identity,
                      owner-issued disposition and completion evidence)

SectionRowResult =
    RowOutcomes(ordered RowSetOutcome values)
  | Count(ordered row-set exact counts)
  | Failure(
        Resolution(Request | RowSet(declared row-set identity),
                   structured resolution failure)
      | SourceForCount(
          ordered (declared row-set identity,
                   owner-issued disposition and completion evidence) entries)
      | Semantic(declared row-set identity, semantic failure))
```

On non-exceptional completion, L2 returns exactly one typed result branch:

- **Row outcomes** returns selected caller-owned values with validated cell
  projection plus opaque source disposition and completion evidence for
  Rows-usable sets, and disposition-and-evidence-only outcomes with no row
  values for unavailable sets, all under their declared row-set identities.
- **Count** returns ordered row-set identities and exact cardinalities.
- **Failure** binds a request-wide or row-set-scoped resolution failure,
  Count-blocking source outcomes, or one semantic failure to its explicit
  scope.

Each row-set outcome contains the declared row-set identity plus the opaque
owner-issued disposition and completion evidence. L2 does not redefine their
construction, reason taxonomy, or lifetime. A `SelectedRows` outcome may
therefore carry incompleteness evidence beside the rows it discloses, while a
failed, `Absent`, or Rows-unavailable outcome carries its source disposition
and completion evidence but no row values. A `SourceForCount` failure preserves
one ordered disposition-and-completion-evidence entry per participating set
and structurally contains neither row values, Row outcomes, nor a Count
payload.

Selection position and `RowSequenceKey` are not promoted into row identity.
Presentation labels, diagnostic sentences, rendered values, and exception text
do not enter any result branch.

Resolution of row-set, projection, row-query, selection, and reduction intent
is atomic. A resolution failure returns no partial executable request. A
row-query execution exception propagates unchanged. A semantic named-sequence
failure returns no Row-outcomes or Count result, preserving the semantic
component's all-or-failure contract and L2's cross-cohort publication boundary.

Typed source failures and completion evidence remain visible when L2 binds a
source result. A selected set lacking evidence sufficient for its resolved
logical request prevents the entire Count result rather than producing a
partial exact-count payload. No unproven cardinality is exposed as exact.

## Physical execution freedom

L2 owns Count's logical meaning, not where or how it executes. Equivalent
physical strategies may include ordinary L2 enumeration, provider count APIs,
source aggregation, exact feed or index metadata, cached exact cardinality, or
early termination.

The
[source delegation design](source-delegation.md) owns delegation
planning, the delegated result contract, completion-evidence binding, and the
non-vacuous optimized-execution gates. L2 accepts an optimized result only
when it is observationally equivalent to the complete reference contracts in
[Row query and ordering](row-query-order.md#logical-composition) and
[Semantic row selection](semantic-row-selection.md#reference-evaluator-and-alternative-interpretation).
That includes their predicate, baseline-order, callback, exception-identity,
resolver-cardinality, caching, and failure-precedence observations. Comparer
call count and pair order remain excluded exactly where the semantic owner
excludes them.

The optimized result must also preserve:

- the same row-set identities and exact cardinalities;
- the same request-wide versus per-set failure boundary; and
- honest evidence that every exact count is complete for the logical request.

Before acceptance, an unsupported exact-Count candidate may be declined, or
the caller may select a row-handoff candidate whose sufficient rows return to
L2 for residual shaping. After exact-Count acceptance, insufficient evidence
returns terminal `NotSatisfied` with no rows, residual execution, or retry. A
fast incomplete number is not a Count result. An operation its owner has not
declared source-closed remains on the reference or row-handoff residual path.

This optimization property remains unverified until an adoption implements the
applicable conditional Release gates from the focused source design.
`OptimizedCountMatchesSectionRowReference` covers exact Count, and
`OptimizedRowHandoffMatchesSectionRowReference` covers row handoff after its
named residual. Each gate is required only when its optimized acceptance path
exists. It must prove that path was exercised, compare it with the complete
reference contract over positive and sentinel cases, and reject insufficient
completion evidence. An implementation with no optimized-result acceptance
path makes no optimization claim and does not satisfy a conditional gate
vacuously.

## Required gates

The implementation must add these named Release gates:

| Gate | Contract |
| --- | --- |
| `DeclaredRowSetIdentityIsTyped` | Equal owner-issued identities bind equal row sets; labels, headings, paths, and sequence positions do not affect identity or equality. An invalid supplied identity returns request-wide failure with its declaration ordinal and token rather than manufacturing row-set scope. |
| `SelectedRowSetListIsNonEmpty` | A missing or empty selected-row-set list returns request-wide resolution failure before terminal, projection, schema, or row-query validation; Count never produces an undefined zero-entry shape. |
| `RowSequenceKeysRoundTripDeclaredIdentity` | Every declared row set receives one unique semantic key, every returned key resolves to the original identity, and duplicate or unknown identities/keys reject. |
| `ProjectionApplicabilityIsRequestWide` | No projection intent preserves every selected set with full cells; a malformed or unsupported operation returns request-wide failure with its typed position and kind; a non-empty request resolving in any selected schema succeeds; each nonmatching set contributes no shaping entry; a request whose names resolve nowhere returns one request-wide failure. |
| `MembershipProjectionPrecedesRowQuery` | Applicable field-entry membership selection changes the rows seen by predicates, baseline order, and semantic stages; a close negative table-column projection changes no membership. |
| `MembershipProjectionChangesCount` | Count over an unprojected field set returns its complete membership cardinality, while projecting a strict field-entry subset returns that subset's cardinality; projecting table columns is the negative control and leaves the same table-row Count unchanged. |
| `CellProjectionFollowsSelectionAndPreservesCardinality` | Cell projection applies only to selected row results, may omit predicate/order fields, and never changes membership, order, or Count. |
| `CountObservesPrecedingSemanticStages` | Empty, undersized, exact, and oversized `Head`, `Tail`, and `Top` plans reduce to the cardinality of their selected result, including `Head(N) -> Count = min(N, input count)`. |
| `CountConsumesSuccessfulWindows` | Successful closed, prefix, suffix, and boundless `Window` stages feed their selected cardinality to Count, including a Window after an earlier stage has reindexed its input. |
| `CountPreservesTopStageObservations` | A Count terminal still reaches every reached `Top` stage, resolves its comparer once through the supplied resolver, and propagates sentinel resolver or comparer exceptions unchanged instead of returning a cardinality. |
| `CountPreservesRowQueryObservations` | Count reflects predicate survivors and propagates the exact sentinel exception instance from a reached row accessor, predicate, or effective-baseline-order comparer over enough rows to exercise it; no Count-specific path bypasses row-query execution or turns its failure into a cardinality. |
| `StrictWindowFailurePreemptsCount` | Closed, prefix, and suffix windows return their semantic failure rather than a partial cardinality when the required position is absent. |
| `HeadCountAcceptsProvenPrefixCompletion` | N ordered applicable rows with proof sufficient for `Head(N) -> Count` return exact N without corpus exhaustion; fewer than N without proof that no later applicable row exists returns source failure rather than a count. |
| `CountPreservesDeclaredRowSetScope` | One participating Count-sufficient set produces one exact entry; multiple participating Count-sufficient sets retain declaration order and identity; no total is invented unless the producer declared one aggregate set. |
| `RowIntentBindingIdentityIsOwnerIssued` | Repeated use of one typed intent instance shares one request-local identity and structurally equal separately declared instances remain distinct. A malformed association, empty associated-row-set list, or unknown reference returns request-wide failure with its typed association/reference position; a duplicate or missing participating-set association returns row-set-scoped failure, all in the exact resolution order. |
| `NoRowIntentUsesDefaultBinding` | A request with no row-intent associations assigns one default request-local identity to every participating set; each schema resolves empty predicates, its effective baseline order, and an empty semantic plan, and plain Rows and Count requests complete through the ordinary cohort path. |
| `ShapingCohortIdentityIsOwnerIssued` | Exactly one non-empty cohort identity is minted at the first participating set for each schema-identity/intent-binding-identity pair; sets sharing it reuse one resolved contract, and each entered cohort invokes semantic selection once over only its admitted row-bearing members; no structural intent, plan, or catalog comparison participates. |
| `HeterogeneousSchemasFormOrderedCohorts` | Different cohort identities follow first participating declaration; cohorts with no admitted member invoke nothing; two entered schemas with different `Top` bindings use their own resolvers; a sentinel failure skips every later entered cohort without replaying or rolling back earlier callbacks; successful results reassemble in declared order. |
| `CountFailurePrecedenceIsDeterministic` | Resolution failure prevents source execution; any participating source-failed, `Absent`, or Count-insufficient set prevents every residual cohort and Count; all and only entered residual cohorts execute in order, a Count satisfied upstream may enter none, and the first semantic failure among entered cohorts prevents every count entry. |
| `EmptyFailedAndAbsentSetsStayDistinct` | A participating Count-sufficient empty set produces exact zero after semantic success; a Count-insufficient, failed, or owner-issued `Absent` disposition prevents Count and remains a typed source failure; an unrequested, undeclared, or projection-inapplicable set produces no entry. |
| `RowsPreserveIndependentSourceOutcomes` | A Rows request shapes each Rows-usable set and binds its rows together with the exact owner-issued disposition and completion evidence; failed, `Absent`, or Rows-unavailable sets retain the exact disposition and completion evidence in disposition-and-evidence-only outcomes with no row values and do not suppress usable companions. |
| `IncompleteRowsRemainVisibleWithoutBecomingCount` | A capped Rows-usable source result produces its shaped rows plus incompleteness evidence, while the same Count-insufficient evidence under Count returns `SourceForCount`, enters no cohort, and never reports the cap as exact cardinality. |
| `CountSourceFailureBindingPreservesOutcomes` | A Count-blocking source failure retains one ordered participating-set entry with the exact owner-issued disposition and completion evidence for every success, failure, incompleteness, or absence outcome; it exposes no row values, Row-outcomes, or Count payload and invokes no residual shaping. |
| `CohortExecutionFailureOrderIsDeterministic` | Entered cohorts execute in order; each prepares only its admitted row-bearing sets in declaration order before its one semantic invocation; competing row-query exceptions and semantic failures select the first reached cursor outcome and skip later work. |
| `CrossCohortRowsAreAtomicOnExecutionFailure` | A successful earlier cohort followed by a later row-query exception or strict Window failure publishes no earlier Row-outcomes payload; exceptions propagate unchanged and semantic failure returns only its bound failure. |
| `SectionRowResolutionIsAtomic` | Any invalid row-set, schema, projection, row-query, selection, or reduction binding returns one structured failure with explicit request-wide or declared-row-set scope and no partial executable request. |
| `SectionRowResolutionFailureOrderIsDeterministic` | Simultaneous failures within and across request, row-set, projection, intent-binding, cohort, and result-map checks return the first exact step and request-wide or row-set scope above; the row-query substep preserves its owner's internal order. |
| `SectionRowResultsArePresentationFree` | Row, Count, and failure results contain typed row-set identities, values, exact cardinalities, or owner-issued outcome evidence without headings, formatted cells, diagnostic sentences, or renderer state. |

## Non-claims

This design does not select CLI combinations, define source optimization
protocols, publish concrete implementation types, decide presentation wording,
or change current product behavior. Those decisions remain with their focused
owners.
