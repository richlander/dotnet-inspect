# Semantic row selection

## Status

Focused component design proposal for
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).
It replaces the independent-window intersection semantics from the superseded
umbrella design. The current product does not implement this contract.

All asserted behavior is unverified until the Release gates in
[Required gates](#required-gates) land.

Related designs:

- [Item and line selection composition](item-and-line-limits.md) maps this
  component to the CLI, source-acquisition, payload, and Markout owners.
- [Inspection layers](inspection-layers.md) places the consumer-neutral
  component below L2 so CLI and browser-facing section pipelines can share it.
- [Row query and ordering](row-query-order.md) owns predicate evaluation,
  effective order, ranking metadata, and schema validation.
- [Output shapes](output-shapes.md) owns declared row units and the
  Document-to-Scalar shape ladder.

## Authority and scope

The proposed dependency-free `DotnetInspector.RowSelection` library, through
`RowSelectionPlan` and `RowSelectionExecutor`, is the authority that evaluates
ordered row-selection stages over complete logical sequences.

This design owns:

- the normalized, renderer-independent row-selection stages;
- sequential stage evaluation;
- lenient and strict stage behavior;
- stage-local positions and reindexing;
- preservation of caller-owned values without transformation; and
- all-or-failure evaluation over one or more named sequences.

This design does not own:

- CLI option names, aliases, token arity, argv preprocessing, or diagnostics;
- provider requests, pagination, source capability, merge, deduplication, or
  completion reporting;
- row predicates or the schema grammar for ordering;
- declared row sets, sections, row identity, output shapes, or formats;
- `--count`, exactly-one row addressing, field or payload projection;
- rendered-line selection, print framing, or destination publication; or
- Markout APIs and presentation row windows.

Those excluded concerns consume this contract or provide its inputs; they do
not redefine its semantics.

## Immediate boundary contract

The generic executor receives:

1. one complete `IReadOnlyList<T>`, or an ordered list of uniquely keyed
   complete `IReadOnlyList<T>` sequences;
2. caller-owned values already in their baseline order;
3. one immutable `RowSelectionPlan<TOrder>`; and
4. a caller-supplied comparer resolver for any opaque, already-resolved
   `TOrder` carried by a `Top` stage.

The executor returns either:

- selected sequences containing the original values in their selected order;
  or
- one structured range failure and no selected output.

For keyed input, evaluation is all-or-failure: every sequence succeeds or the
result contains one failure and no selected sequence collection. The failure
identifies the input key, one-based stage number, required position, and
available current count. When several sequences would fail, input sequence
order and then stage order determine the one returned. The failure contains no
presentation text.

The reference executor evaluates a complete logical input sequence. A source
optimizer may avoid acquiring that complete sequence only when it can prove the
same selected values, order, and strict-range outcome. The
source-pushdown design owns how that proof is represented and obtained.

The project references only the BCL. It has no dependency on Sections, Queries,
Packages, Services, or Markout. A static product-closure gate prohibits console,
filesystem, network, process, dedicated-thread, parallel-loop, and native
interop APIs. Its public execution surface is synchronous and deterministic,
making it usable by NativeAOT and single-threaded Browser/Wasm consumers.

## Normalized plan

A plan is an ordered immutable sequence of complete stages:

```text
RowSelectionPlan<TOrder>
  Stages:
    Head(count)
    Tail(count)
    Range(start, end?)
    Top(count, resolved-ranking-order)
```

Counts and range coordinates are positive integers. A closed range is
1-based and inclusive. An open range has a start and no end.

`Take` is not a distinct semantic stage; a caller spelling with that meaning
lowers to `Head`. `Top` carries an opaque ordering value already resolved by
the caller. The component asks the supplied resolver for its comparer and does
not parse field names, consult section schema, or infer ranking intent. Equal
comparisons retain current sequence order, making the current stage position
the deterministic final tie-breaker.

The plan permits repeated stages of any kind. It contains no incomplete
modifier waiting for another token and no implicit default count. L3 owns
rejecting or completing such syntax before construction.

Plan construction rejects nonpositive counts, nonpositive range positions, and
a closed range whose end precedes its start. The comparer resolver must return
a non-null comparer for every `Top` order; violating that condition is caller
misuse, not a semantic range failure.

An empty plan preserves every input value and its order without invoking the
comparer resolver.

## Stage semantics

Each stage consumes the sequence produced by the preceding stage:

| Stage | Result |
| --- | --- |
| `Head(N)` | The first `min(N, count)` rows, in current order. |
| `Tail(N)` | The last `min(N, count)` rows, in current order. |
| `Range(A, B)` | Current positions A through B inclusive; fails unless position B exists. |
| `Range(A, null)` | Current position A through the end; fails unless position A exists. |
| `Top(N, order)` | Rank the current rows by `order`, then keep the first `min(N, count)`. |

`Head`, `Tail`, and `Top` are lenient: a request larger than the current input
returns every current row. `Tail` never reverses the surviving rows.

`Range` is strict. A closed range requires its end, not merely its start, to
exist in the current input. An open range requires its start to exist. A
strict-range failure is not an empty result and must not be reported as source
exhaustion or successful truncation.

## Ordered composition and reindexing

After every stage, its output becomes a new sequence whose selection positions
start at 1. This is Unix-style pipeline composition, not intersection against
the original ordinals.

Conceptual examples make the evaluation order explicit:

```text
[1, 2, 3, 4, 5, 6, 7, 8].Range[3, 4].Tail(2)
=> [3, 4].Tail(2)
=> [3, 4]

[1, 2, 3, 4, 5, 6, 7, 8].Range[3, 6].Tail(2)
=> [3, 4, 5, 6].Tail(2)
=> [5, 6]

[1, 2, 3, 4, 5, 6, 7, 8].Tail(4).Range[2, 3]
=> [5, 6, 7, 8].Range[2, 3]
=> [6, 7]

[1, 2, 3, 4, 5, 6, 7, 8].Head(2).Range[2, 3]
=> [1, 2].Range[2, 3]
=> error: stage 2 requires position 3, but its input has 2 rows
```

Reindexing changes only the temporary positions consumed by the next stage.
It does not rewrite producer-owned package coordinates, metadata identities,
Finding identities, source provenance, or any stable row address carried as
typed data. Selection position is never inferred from rendered text and is
never promoted into identity.

## Filters, baseline order, and ranking

Section selection, producer execution, command-owned filters, row predicates,
and effective baseline order establish each input sequence before this plan
runs:

```text
declared typed rows
-> predicates and command-owned filters
-> effective baseline order
-> ordered semantic selection stages
-> projection
-> presentation
```

A `Top` stage is the one selection stage that changes order itself. It ranks
only its current input, then applies its lenient head count. A later stage sees
that ranked subset with positions restarted at 1. A later `Top` may rank the
surviving subset again by a different resolved order.

Whether a CLI `--order-by` establishes baseline order or binds to a `Top`
gesture is a CLI/schema-lowering question. The normalized plan never carries an
unresolved field name.

## Multiple named sequences

The keyed overload applies the same plan independently to every input sequence.
L2 uses those keys for declared row-set identity, but the component treats them
as opaque values. Duplicate keys reject at the boundary so a failure never
identifies an ambiguous input.

Strict validation is atomic across those sequences. If any applicable sequence
cannot satisfy any `Range` stage:

- evaluation fails;
- the structured failure identifies the input key, one-based stage number,
  required position, and available current-row count;
- no selected sequence collection is returned.

The L2 integration owner must complete this pure preflight before projection,
rendering, per-row payload acquisition, or destination mutation. This avoids a
presentation-dependent partial success in which one table honors a range while
another silently clamps or disappears.

## Failure model

The single-sequence executor returns a `RowRangeFailure` with:

- `StageNumber`: the one-based index of the failing `Range` stage;
- `RequiredPosition`: the closed range end or open range start that had to
  exist; and
- `AvailableCount`: the size of that stage's current input.

The named-sequence executor returns `NamedRowRangeFailure<TKey>`, containing the
opaque input `Key` and the same `RowRangeFailure`. Failures contain no message,
exception text, row value, or rendered identity. The caller owns diagnostic
wording.

Invalid plan construction and a missing resolved comparer are caller misuse,
not `RowRangeFailure` outcomes. They reject before a selected result is
returned.

## Reference semantics and optimized execution

The stage definitions over a complete sequence are the semantic oracle.
Implementations may stream, buffer, sort, or push work into a provider, but
those choices are observationally equivalent only when they preserve:

- the same surviving caller-owned values;
- the same output order;
- the same strict-range success or failure;
- the same named-sequence boundary; and
- the same all-or-failure output behavior.

This distinction matters when a later lenient stage would keep fewer rows than
an earlier strict stage validates:

```text
Rows.Range[100, 200].Head(5)
```

The result contains positions 100 through 104, but successful evaluation still
requires proof that position 200 existed in the input to `Range`.

Conversely:

```text
Rows.Head(5).Range[100, 200]
```

deterministically fails after `Head` produces at most five rows. Acquisition
must not fetch toward position 200 to rescue a range whose current stage input
cannot contain it.

An incomplete provider page is not proof that a strict endpoint is absent.
The source owner must obtain enough evidence, reject the unsupported
optimization, or report an acquisition/completion failure distinct from a
semantic range failure.

## Markout boundary

Markout receives rows after semantic selection. Its table row window can remain
a single-table presentation utility, but dotnet-inspect must not lower a
multi-stage `RowSelectionPlan` into that option. Doing so would move
strictness, reindexing, and source evidence into a renderer that does not own
them.

No Markout behavior or package release is required to implement this
component.

## Required gates

The implementation must add these named Release gates:

| Gate | Contract |
| --- | --- |
| `SelectionStagesComposeInDeclaredOrder` | Reversing `Head`, `Tail`, `Range`, or `Top` stages changes results exactly as the reference examples require; every stage reads positions beginning at 1 from the preceding output. |
| `SelectionCountsAreLenientAndRangesAreStrict` | Oversized `Head`, `Tail`, and `Top` return the complete current input, while closed and open ranges fail unless their required endpoint exists at that stage. |
| `RowSelectionPlanRejectsInvalidStages` | Nonpositive counts, nonpositive range coordinates, and a closed end before its start reject during construction rather than becoming an empty or unlimited stage. |
| `EmptyRowSelectionPlanIsIdentity` | An empty plan returns every original value in order and never invokes the comparer resolver. |
| `TopRequiresResolvedComparer` | A resolver that returns no comparer identifies the `Top` stage and rejects as caller misuse before any selected result is returned. |
| `StrictRangesValidateNamedSequencesAtomically` | A strict-range miss in any one of several keyed sequences identifies the key and stage and returns no selected sequence collection. |
| `SelectionFailuresAreDeterministic` | Multiple failing named sequences return the first failure by input sequence order and stage order; duplicate keys reject before execution. |
| `RowRangeFailureShapeIsExact` | Unkeyed failures contain exactly stage number, required position, and available count; named failures add only the opaque key. Closed ranges report their end and open ranges report their start against the post-predecessor count. |
| `TopRetainsCurrentOrderForEqualRanks` | Equal comparer results preserve current sequence order, including after an earlier stage changed the current sequence. |
| `SelectionReturnsOriginalValuesInOrder` | The executor returns the original caller-owned values without cloning, wrapping, relabeling, or deriving identity from stage positions. |
| `RowSelectionHasOnlyBclDependencies` | The project-reference closure permits only the BCL and rejects Sections, Queries, Packages, Services, Markout, and host projects. |
| `RowSelectionForbidsHostApis` | A static product-closure gate rejects console, filesystem, network, process, dedicated-thread, parallel-loop, and native-interop APIs even though those APIs are in the BCL. |
| `RowSelectionRunsOnNativeAotAndBrowser` | The reference stage matrix executes in Release under NativeAOT and single-threaded Browser/Wasm hosts. |
| `RowSelectionPublicApiIsSynchronous` | A signature-closure gate rejects `Task`/`ValueTask`, `Thread`, `Stream`/`TextReader`/`TextWriter`, `Uri`/`HttpClient`, `FileSystemInfo`, and `Process` from public members. |

The source-pushdown successor must add an equivalence gate comparing every
optimized plan it supports with this complete-sequence reference executor,
including strict ranges before and after lenient stages.
