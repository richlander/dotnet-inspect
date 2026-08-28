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
- [Semantic row-selection interaction model](../models/SemanticRowSelection.tla)
  checks bounded stage, failure, publication, and resolver interactions.

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

1. one complete `IReadOnlyList<T>`, or an ordered list of complete
   `IReadOnlyList<T>` sequences identified by unique component-owned
   `RowSequenceKey` tokens;
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

The evaluated Release compile/runtime closure contains only framework
references and this component. The project has no product `PackageReference`,
direct assembly asset, native asset, or `ProjectReference`; repository-wide
build-only analyzers and targets remain allowed only when they contribute no
compile/runtime asset. A static product-closure gate prohibits console,
filesystem, network, process, dedicated-thread, parallel-loop, and native
interop APIs. With deterministic caller callbacks, its public execution
surface is synchronous and deterministic, making it usable by NativeAOT and
single-threaded Browser/Wasm consumers.

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
lowers to `Head`. `Top` carries an opaque ranking-order identity already
resolved by the caller. The component asks the supplied resolver to map that
identity to its comparer and does not parse field names, consult section
schema, or infer ranking intent. Equal comparisons retain current sequence
order, making the current stage position the deterministic final tie-breaker.

The plan permits repeated stages of any kind. It contains no incomplete
modifier waiting for another token and no implicit default count. L3 owns
rejecting or completing such syntax before construction.

Plan construction rejects nonpositive counts, nonpositive range positions, and
a closed range whose end precedes its start. The comparer resolver must return
a non-null comparer for every `Top` order; violating that condition is caller
misuse, not a semantic range failure.

An empty plan preserves every input value and its order without invoking the
comparer resolver.

Callback resolution follows pipeline order. An executor validates at entry
that a non-null resolver is present when the plan contains `Top`, but it does
not invoke the resolver during plan-wide validation. Each `Top` stage asks the
resolver for its comparer exactly once when that stage is first reached during
one executor invocation, and caches that comparer for the same stage across
later named sequences. Repeated `Top` stages resolve independently even when
they carry equal order values.

An earlier strict `Range` failure therefore prevents a later `Top` resolver
from running. `ApplyNamed` considers sequences in input order and stages in
plan order while withholding every result until all sequences succeed. A
failure or callback exception stops that traversal: a callback reached in an
earlier sequence precedes a semantic failure in a later sequence, while a
strict failure in the current sequence precedes every later-stage callback.
Resolver and comparer exceptions propagate unchanged. Comparer call count and
pair order are implementation details; callers must supply a deterministic
comparer.

An unkeyed empty value sequence still executes the plan, so it reaches `Top`
unless an earlier strict stage fails. A named call with no input sequences
reaches no stage and returns an empty successful sequence snapshot; a named
empty value sequence behaves like the unkeyed empty sequence. Resolver
presence is still boundary validation: a plan containing `Top` rejects a null
resolver at entry even when no sequence would reach that stage.

## Public surface and immutability

This is the complete allowed product signature manifest; method bodies are
omitted. No other public type, constructor, property, method, event, or field is
part of the component:

```csharp
namespace DotnetInspector.RowSelection;

public enum RowSelectionStageKind
{
    Head,
    Tail,
    Range,
    Top
}

public sealed class RowSelectionStage<TOrder>
    where TOrder : notnull
{
    public RowSelectionStageKind Kind { get; }
    public int Count { get; }
    public int Start { get; }
    public int? End { get; }
    public TOrder Order { get; }

    public static RowSelectionStage<TOrder> Head(int count);
    public static RowSelectionStage<TOrder> Tail(int count);
    public static RowSelectionStage<TOrder> Range(int start, int? end);
    public static RowSelectionStage<TOrder> Top(int count, TOrder order);
}

public sealed class RowSelectionPlan<TOrder>
    where TOrder : notnull
{
    public static RowSelectionPlan<TOrder> Empty { get; }
    public IReadOnlyList<RowSelectionStage<TOrder>> Stages { get; }

    public static RowSelectionPlan<TOrder> Create(
        IReadOnlyList<RowSelectionStage<TOrder>> stages);
    public RowSelectionPlan<TOrder> Append(RowSelectionStage<TOrder> stage);
}

public sealed class RowSequenceKey : IEquatable<RowSequenceKey>
{
    public int Value { get; }

    public static RowSequenceKey Create(int value);
    public bool Equals(RowSequenceKey? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
}

public sealed class NamedRowSequence<T>
{
    public RowSequenceKey Key { get; }
    public IReadOnlyList<T> Values { get; }

    public static NamedRowSequence<T> Create(
        RowSequenceKey key,
        IReadOnlyList<T> values);
}

public sealed class RowRangeFailure
{
    public int StageNumber { get; }
    public int RequiredPosition { get; }
    public int AvailableCount { get; }
}

public sealed class NamedRowRangeFailure
{
    public RowSequenceKey Key { get; }
    public RowRangeFailure Failure { get; }
}

public sealed class RowSelectionResult<T>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<T> Values { get; }
    public RowRangeFailure? Failure { get; }
}

public sealed class NamedRowSelectionResult<T>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<NamedRowSequence<T>> Sequences { get; }
    public NamedRowRangeFailure? Failure { get; }
}

public static class RowSelectionExecutor
{
    public static RowSelectionResult<T> Apply<T, TOrder>(
        IReadOnlyList<T> values,
        RowSelectionPlan<TOrder> plan,
        Func<TOrder, IComparer<T>?>? comparerResolver = null)
        where TOrder : notnull;

    public static NamedRowSelectionResult<T> ApplyNamed<T, TOrder>(
        IReadOnlyList<NamedRowSequence<T>> sequences,
        RowSelectionPlan<TOrder> plan,
        Func<TOrder, IComparer<T>?>? comparerResolver = null)
        where TOrder : notnull;
}
```

The API manifest includes type kind, visibility, generic arity and constraints,
member name, static/instance shape, parameter name, order, type, nullability,
optionality, default value, return type, and enum values. Inherited `object`
members and compiler-generated metadata that does not add callable surface are
outside the manifest.

`Count` is valid for `Head`, `Tail`, and `Top`; `Start` and `End` are valid for
`Range`; `Order` is valid for `Top`. A wrong-kind accessor throws
`InvalidOperationException`. All required reference arguments reject null with
`ArgumentNullException`; `comparerResolver` may be null only when the plan has
no `Top`. Plan creation and append reject null stage entries, and named
execution rejects null sequence entries. A missing resolver or one returning
null for a `Top` throws `InvalidOperationException` naming its one-based stage.
Resolver and comparer exceptions propagate unchanged.

No public constructor bypasses the validating stage factories, plan creation,
row-sequence-key creation, named-sequence creation, or internal result
factories. `Create` defensively copies the caller's stage collection, `Stages`
exposes no mutable collection, and `Append` returns a new plan without changing
the prior value. Stage values copy their opaque `TOrder`; callers must supply an
immutable order value whose equality and meaning do not change after plan
construction.

`RowSequenceKey.Create` rejects a negative value. Keys compare solely by
`Value`, and `GetHashCode` returns the same value, so separate key instances
with the same value are duplicates under every implementation.
`NamedRowSequence.Create` retains the immutable key and snapshots value
membership and order. Duplicate key values reject before any sequence is
evaluated.
`RowSelectionExecutor` returns component-owned snapshots on every success path,
including empty plans and lenient stages that retain every value. Success
results have a null `Failure`; failure results have an empty immutable value or
sequence collection and a non-null `Failure`. Named success preserves input
sequence order. Exposed `IReadOnlyList` values cannot be cast to a mutable
collection that changes the snapshot.

The component snapshots collections, not row objects. Every selected `T` is the
same caller-owned value or reference supplied at the boundary. Mutating a
mutable `T` remains visible by design; mutating a source collection after
`Create` or `Apply` does not change plan, named-input, or result membership and
order. Keys are component-owned immutable tokens and cannot change after
named-input creation. Callers must not mutate a source collection concurrently
with the synchronous boundary call.

A fixture project outside the component compiles against every signature above
and executes every entry point. The same manifest gate rejects extra public
constructors, mutators, asynchronous protocols, or host-shaped overloads, so an
empty or exclusion-only API cannot satisfy the design.

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
returns every current row. `Head` and `Tail` retain current order, and `Tail`
never reverses the surviving rows. `Top` always resolves and applies its
ranking, including when its count is at least the current count; an oversized
`Top` therefore returns every row in ranked order rather than baseline order.

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

[4, 1, 3, 2].Top(10, ascending).Head(2)
=> [1, 2, 3, 4].Head(2)
=> [1, 2]

[1, 2, 3, 4, 5, 6].Range[2, 5].Top(2, descending)
=> [2, 3, 4, 5].Top(2, descending)
=> [5, 4]
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

Putting `Top` last is an ordinary and useful plan: every preceding stage first
establishes the candidates that the final stage ranks. No eager comparer
resolution may make a later `Top` observable before execution reaches it.

Whether a CLI `--order-by` establishes baseline order or binds to a `Top`
gesture is a CLI/schema-lowering question. The normalized plan never carries an
unresolved field name.

## Multiple named sequences

The named overload applies the same plan independently to every input sequence.
L2 assigns one `RowSequenceKey` per declared row set and retains the immutable
key-to-typed-identity map. The component sees only the key token. Duplicate key
values reject at the boundary so a failure never identifies an ambiguous
input.

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

The named-sequence executor returns `NamedRowRangeFailure`, containing the
component-owned input `Key` and the same `RowRangeFailure`. L2 resolves the key
through its retained map before producing a diagnostic. Failures contain no
message, exception text, row value, or rendered identity.

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
- the same all-or-failure output behavior;
- the same set of reached `Top` stages and one resolver invocation per reached
  stage; and
- the same semantic-failure, resolver-failure, and comparer-failure precedence.

Comparer call count and pair order are not equivalence dimensions for a valid
deterministic comparer. The source-pushdown successor must reject an
optimization it cannot prove against the remaining callback contract.

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
a single-table presentation utility, but dotnet-inspect must not lower any
`RowSelectionStage` or `RowSelectionPlan` into that option, including a
single-stage plan. Doing so would move strictness, reindexing, and source
evidence into a renderer that does not own them.

No Markout behavior or package release is required to implement this
component.

## Interaction model

The small
[TLA+ interaction model](../models/SemanticRowSelection.tla) supplements this
specification. It models one immutable plan applied to ordered named sequences,
with sequence-major and stage-major traversal, stage-local row positions,
strict `Range`, positional `Head`/`Tail`, ranked `Top`, resolver caching,
callback failures, withheld publication, and final atomic success.

The model deliberately abstracts row identity to distinct integers and ranking
to ascending or descending order. It assumes positive normalized coordinates,
a deterministic total-order comparer, complete input sequences, and no source,
CLI, rendering, or concurrency behavior. Those owners remain outside this
component.

[`SemanticRowSelection.cfg`](../models/SemanticRowSelection.cfg) checks all
plans up to two stages over two named sequences containing up to three distinct
values. It checks type safety, atomic publication, completion only after every
sequence, at-most-once resolver invocation and consistent resolver metadata,
sequence/stage failure precedence, strict-range failure evidence anchored to
the failing stage input, each stage's input against the preceding stage's
output, every successful stage's exact semantics through checks independent of
the transition helpers (including strict `Range`, stage-local reindexing, and
ranked `Top` output), resolver coverage for every successful `Top`, and eventual
termination under weak fairness.

The model was checked with the pinned TLA+ Tools v1.8.0 prerelease
`tla2tools.jar` (published SHA-1
`0e4cfdb976f04522d218ec62c6046bbee5098377`), reporting TLC2
`2026.08.21.155922` revision `9787e65`. From `docs/models`:

```bash
java -XX:+UseParallelGC \
  -cp /path/to/tla2tools-1.8.0.jar \
  tlc2.TLC -cleanup -deadlock -workers auto \
  -config SemanticRowSelection.cfg SemanticRowSelection.tla
```

TLC generated and checked 1,935,634 distinct states to depth 7 with no errors
or material counterexamples. Deadlock checking is disabled because success,
strict failure, and callback failure are intentional terminal states; the
model permits terminal stuttering and separately checks eventual termination.

This clean bounded result is evidence about the interaction model, not proof of
the C# implementation; the named Release gates below remain required.

## Required gates

The implementation must add these named Release gates:

| Gate | Contract |
| --- | --- |
| `SelectionStagesComposeInDeclaredOrder` | Reversing `Head`, `Tail`, `Range`, or `Top` stages changes results exactly as the reference examples require; every stage reads positions beginning at 1 from the preceding output. |
| `SelectionCountsAreLenientAndRangesAreStrict` | Oversized `Head` and `Tail` return the complete current input in current order; oversized `Top` returns every current row in ranked order; closed and open ranges fail unless their required endpoint exists at that stage. |
| `RowSelectionPlanRejectsInvalidStages` | Every public construction path rejects nonpositive counts, nonpositive range coordinates, and a closed end before its start rather than creating an empty or unlimited stage. |
| `EmptyRowSelectionPlanIsIdentity` | An empty plan returns an immutable snapshot containing every original value in order and never invokes the comparer resolver. |
| `TopRequiresResolvedComparer` | A reached resolver that returns no comparer identifies the `Top` stage and rejects as caller misuse before any selected result is returned. |
| `SelectionCallbacksFollowStageOrder` | Both executor entry points validate resolver presence without eager invocation, resolve each reached `Top` stage exactly once, cache that stage's comparer across named sequences, and stop before later callbacks after an earlier strict failure or callback exception. Fixtures cover `Range` before and after `Top`, multiple named sequences, repeated equal order values, unkeyed and named empty value sequences, and a named call with no sequences. |
| `SelectionCallbackExceptionsPropagateUnchanged` | Both executor entry points propagate the exact sentinel exception instance thrown by a reached comparer resolver or by an always-throwing comparer over at least two rows; no sorting path wraps, substitutes, or suppresses it. |
| `RowSelectionRejectsNullBoundaryInputs` | Every required reference argument rejects null; a null resolver is accepted only without `Top`; nullable row values remain ordinary selected values. |
| `StageAccessorsRejectWrongKind` | Each kind exposes only its documented values; every wrong-kind `Count`, `Start`, `End`, or `Order` access throws rather than returning a plausible default. |
| `StrictRangesValidateNamedSequencesAtomically` | A strict-range miss in any one of several keyed sequences identifies the key and stage and returns no selected sequence collection. |
| `SelectionFailuresAreDeterministic` | Multiple failing named sequences return the first failure by input sequence order and stage order; duplicate `RowSequenceKey.Value` values reject before execution. |
| `RowSequenceKeyHasStableValueSemantics` | Negative values reject; separately created equal values compare equal and produce equal hash codes; distinct values compare unequal; L2's typed row-set identity never enters the component. |
| `RowRangeFailureShapeIsExact` | Unkeyed failures contain exactly stage number, required position, and available count; named failures add only the opaque key. Closed ranges report their end and open ranges report their start against the post-predecessor count. |
| `TopAlwaysRanksCurrentInput` | Every `Top` over at least two rows, including an oversized one, resolves and applies its comparer; `Top(oversized)` followed by a positional stage observes ranked rather than baseline order. |
| `TopRetainsCurrentOrderForEqualRanks` | Equal comparer results preserve current sequence order, including after an earlier stage changed the current sequence. |
| `SelectionReturnsOriginalValuesInOrder` | The executor preserves each original caller-owned `T` value or reference without cloning, relabeling, or deriving identity from stage positions. |
| `SelectionResultsSnapshotMembership` | Source-list mutation after named-input creation or execution cannot change result membership or order; exposed collections cannot mutate the snapshot. Fixtures cover empty, oversized Head/Tail/Top, Range, mixed stages, and named success/failure paths. |
| `RowSelectionPlanIsImmutableSnapshot` | Mutating a caller-owned stage collection after `Create` cannot change the plan; `Stages` exposes no mutable collection; `Append` leaves the prior plan unchanged; every stage remains immutable. |
| `RowSelectionPublicSurfaceIsExact` | A generated expected set derived from the signature manifest in [Public surface and immutability](#public-surface-and-immutability) rejects any missing or extra type, constructor, member, mutator, host-shaped overload, asynchronous protocol, generic constraint, enum value, parameter name/order/type/nullability/optionality, or default value. |
| `RowSelectionExternalConsumerExercisesSurface` | A non-friend fixture project constructs every stage, plan, and named input through the declared factories; invokes both executor methods with omitted and named optional arguments; and observes every accessor and success/failure branch. Removing any intended public wiring fails the gate. |
| `RowSelectionHasOnlyFrameworkRuntimeDependencies` | Evaluated Release references and resolved compile/runtime/native assets contain only framework references and this component; build-only tooling is allowed only when it contributes no product asset. |
| `RowSelectionForbidsHostApis` | A static product-closure gate rejects console, filesystem, network, process, dedicated-thread, parallel-loop, and native-interop APIs even though those APIs are in the BCL. |
| `RowSelectionRunsOnNativeAotAndBrowser` | The reference stage matrix executes in Release under NativeAOT and single-threaded Browser/Wasm hosts. |

The source-pushdown successor must add an equivalence gate comparing every
optimized plan it supports with this complete-sequence reference executor,
including strict ranges before and after lenient stages, reached-stage resolver
cardinality, and callback/failure precedence.
