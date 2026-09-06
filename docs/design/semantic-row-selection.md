# Semantic row selection

## Status

Focused component design proposal for
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677).
It defines the intended replacement for the semantic-selection portion of the
existing umbrella design. The
[composition map](item-and-line-limits.md#composition) adopts this component
and retires the umbrella assignment. The product implementation lives in
`src/DotnetInspector.RowSelection`.

The executable Release gates in
`tests/DotnetInspector.RowSelection.Tests` and the non-friend consumer in
`tests/DotnetInspector.RowSelection.Consumer` verify the implemented contract.

Related designs:

- [Item and line selection composition](item-and-line-limits.md) maps this
  component's typed boundary to adjacent owners without redefining its
  behavior.
- [Inspection layers](inspection-layers.md) places the consumer-neutral
  component below L2 so CLI and browser-facing section pipelines can share it;
  focused adoption of the composition handoff is tracked by
  [#5163](https://github.com/richlander/dotnet-inspect/issues/5163).
- [Row query and ordering](row-query-order.md) is the existing proposal for
  predicate evaluation, effective order, ranking metadata, and schema
  validation; its focused adoption is tracked by
  [#5162](https://github.com/richlander/dotnet-inspect/issues/5162).
- [Output shapes](output-shapes.md) owns declared row units and the
  Document-to-Scalar shape ladder.
- [Source delegation](source-delegation.md) owns one specialized optional
  protocol for source-executed prefixes and completion-evidence binding. It is
  not required to consume the typed language or its reference evaluator.
- [Semantic row-selection interaction model](../models/semantic-row-selection/SemanticRowSelection.tla)
  checks bounded stage, failure, publication, and resolver interactions.

## Authority and scope

The `DotnetInspector.RowSelection` library is the authority for two distinct
capabilities:

- the typed declaration language expressed by `RowSelectionStage` and
  `RowSelectionPlan`; and
- the generic complete-sequence reference evaluator expressed by
  `RowSelectionExecutor`.

This design owns:

- construction, validation, and runtime inspection of the normalized,
  renderer-independent row-selection language;
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

## Three-part architecture and adoption

The broader row-selection system has three separately adoptable parts:

| Part | Contract | Dependency |
| --- | --- | --- |
| Typed selection language | An immutable, runtime-inspectable declaration of ordered `Head`, `Tail`, `Window`, and `Top` stages with validated operands and an opaque resolved-order identity. | No row values, evaluator, source protocol, Sections, CLI, or presentation concepts are required to construct or inspect it. |
| Generic reference evaluator | The canonical implementation of that language over complete finite `IReadOnlyList<T>` sequences, including named inputs, ordering callbacks, snapshots, and structured strict-window failures. | Depends on the typed language but not on source execution, Sections, CLI, or presentation. |
| Optional delegated interpretation | A separately owned component may accept the typed declaration and perform an equivalent interpretation, or decline so its caller can use another strategy such as the reference evaluator. | Depends on the language's meaning. It need not invoke the reference evaluator in production, but its supported interpretations require equivalence evidence against that oracle. |

These are capability boundaries, not three required assemblies. The initial
implementation places the language and reference evaluator in the same
library. A language-only consumer can construct, retain, inspect, and transport
a plan without supplying row values or invoking
`RowSelectionExecutor`; this design does not claim that the language ships as a
separate package or assembly.

The declaration carries meaning, not an execution strategy. A component that
receives it may:

1. provide a complete logical sequence to the reference evaluator; or
2. use a separately designed interpreter that preserves the reference
   evaluator's observable semantics for every stage it accepts.

This design owns the equivalence target but not a universal delegation API.
Each delegation protocol owns its own capability negotiation, acceptance,
decline, commitment, result transport, and failure boundary. The
[source-delegation](source-delegation.md) design is one narrower protocol for
source-owned execution and completion evidence; it must not become a
dependency of applications that only need the language or evaluator, and it
must not be mistaken for permission to reinterpret unsupported stages.

## Purpose and review boundary

This component gives dotnet-inspect and other applications one reusable,
presentation-independent language and reference implementation of ordered row
selection. Its supported caller is cooperating in-process code. A
language-only caller supplies stage operands and resolved `Top` order
identities. An evaluator caller additionally supplies complete logical
sequences and deterministic comparers for those resolved orders. The variable
inputs are the sequence values, stage order and operands, named-sequence keys,
and comparer results. The observable evaluator contract is the selected values
in order or one structured strict-window failure.

The caller, its row objects, its resolved order identities, and its comparer
implementation are trusted. This is not a security boundary and does not defend
against reflection or private access, deliberate internal-state corruption,
concurrent mutation during a synchronous call, or malicious cooperating code.
Ordinary invalid arguments that the public API can represent still fail as
documented so they cannot produce plausible selection results.

Repository-wide platform policy applies to this code as it does to other simple
product libraries; this design defines no component-specific platform behavior
or evidence.

## Typed declaration boundary

`RowSelectionPlan<TOrder>` is an inert ordered declaration. It contains stage
kinds, validated operands, and opaque caller-resolved `TOrder` values; it
contains no row values, comparer, callback, source, result, or execution state.
A consumer can inspect every stage and choose an execution component at
runtime without parsing CLI tokens, rendered text, or field names.

Constructing or inspecting a plan does not invoke selection behavior. The plan
does not choose the reference evaluator, advertise a source capability, or
assert that an alternative interpreter supports its stages. Those decisions
belong to the adopting component and any separately owned delegation protocol.

## Reference evaluator boundary

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
- one structured window failure and no selected output.

For keyed input, evaluation is all-or-failure: every sequence succeeds or the
result contains one failure and no selected sequence collection. The failure
identifies the input key, one-based stage number, required position, and
available current count. When several sequences would fail, input sequence
order and then stage order determine the one returned. The failure contains no
presentation text.

The reference executor evaluates a complete logical input sequence. An
alternative interpreter may avoid using that complete sequence only under its
own protocol and only when its accepted interpretation preserves the
observable equivalence contract in
[Reference evaluator and alternative interpretation](#reference-evaluator-and-alternative-interpretation).

With deterministic caller callbacks, the public execution surface is
synchronous and deterministic.

## Normalized plan

A plan is an ordered immutable sequence of complete stages:

```text
RowSelectionPlan<TOrder>
  Stages:
    Head(count)
    Tail(count)
    Window(start?, end?)
    Top(count, resolved-ranking-order)
```

Counts and present window coordinates are positive integers. Window
coordinates are 1-based data-row positions, and both bounds are inclusive.
Either or both bounds may be omitted. A window with neither bound is an
identity stage.

`Take` is not a distinct semantic stage; a caller spelling with that meaning
lowers to `Head`. `Top` carries an opaque ranking-order identity already
resolved by the caller. The component asks the supplied resolver to map that
identity to its comparer and does not parse field names, consult section
schema, or infer ranking intent. Equal comparisons retain current sequence
order, making the current stage position the deterministic final tie-breaker.

The plan permits repeated stages of any kind. It contains no incomplete
modifier waiting for another token and no implicit default count. L3 owns
rejecting or completing such syntax before construction.

Plan construction rejects nonpositive counts, nonpositive present window
positions, and a closed window whose end precedes its start. The comparer
resolver must return a non-null comparer for every `Top` order; violating that
condition is caller misuse, not a semantic window failure.

An empty plan preserves every input value and its order without invoking the
comparer resolver.

Callback resolution follows pipeline order. An executor validates at entry
that a non-null resolver is present when the plan contains `Top`, but it does
not invoke the resolver during plan-wide validation. When the resolver is null,
entry validation reports the first `Top` in plan order before input traversal,
including when named input contains no sequences. Each `Top` stage asks the
resolver for its comparer exactly once when that stage is first reached during
one executor invocation, and caches that comparer for the same stage across
later named sequences. Repeated `Top` stages resolve independently even when
they carry equal order values.

An earlier strict `Window` failure therefore prevents a later `Top` resolver
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

The supported typed-language surface is:

```csharp
namespace DotnetInspector.RowSelection;

public enum RowSelectionStageKind
{
    Head,
    Tail,
    Window,
    Top
}

public sealed class RowSelectionStage<TOrder>
    where TOrder : notnull
{
    public RowSelectionStageKind Kind { get; }
    public int Count { get; }
    public int? Start { get; }
    public int? End { get; }
    public TOrder Order { get; }

    public static RowSelectionStage<TOrder> Head(int count);
    public static RowSelectionStage<TOrder> Tail(int count);
    public static RowSelectionStage<TOrder> Window(int? start, int? end);
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
```

The supported reference-evaluator surface is:

```csharp
namespace DotnetInspector.RowSelection;

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

public sealed class RowWindowFailure
{
    public int StageNumber { get; }
    public int RequiredPosition { get; }
    public int AvailableCount { get; }
}

public sealed class NamedRowWindowFailure
{
    public RowSequenceKey Key { get; }
    public RowWindowFailure Failure { get; }
}

public sealed class RowSelectionResult<T>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<T> Values { get; }
    public RowWindowFailure? Failure { get; }
}

public sealed class NamedRowSelectionResult<T>
{
    public bool IsSuccess { get; }
    public IReadOnlyList<NamedRowSequence<T>> Sequences { get; }
    public NamedRowWindowFailure? Failure { get; }
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

`Count` is valid for `Head`, `Tail`, and `Top`; `Start` and `End` are valid for
`Window`; `Order` is valid for `Top`. A wrong-kind accessor throws
`InvalidOperationException`. All required reference arguments reject null with
`ArgumentNullException`; `comparerResolver` may be null only when the plan has
no `Top`. Plan creation and append reject null stage entries, and named
execution rejects null sequence entries. A missing resolver or one returning
null for a `Top` throws `InvalidOperationException`. A missing resolver names
the first `Top` in plan order; a resolver returning null names the reached
`Top`. Both use the one-based stage number. Resolver and comparer exceptions
propagate unchanged.

No public constructor bypasses the validating stage factories, plan creation,
row-sequence-key creation, named-sequence creation, or internal result
factories. `Create` defensively copies the caller's stage collection, `Stages`
exposes no mutable collection, and `Append` returns a new plan without changing
the prior value. Stage values copy their opaque `TOrder`; callers must supply an
immutable order value whose equality and meaning do not change after plan
construction.

`RowSequenceKey.Create` accepts any `int`. Keys compare solely by `Value`, and
`GetHashCode` returns the same value, so separate key instances with the same
value are duplicates under every implementation.
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

A fixture project outside the component compiles against the supported surface
above. One language-only scenario constructs and inspects every declaration
entry point without row values or executor use. A second scenario executes
every reference-evaluator entry point. Together they prove that an ordinary
non-friend consumer can use either capability without importing Sections,
source execution, CLI, or presentation concepts.

## Mock component demo

The first implementation demonstrates both logical surfaces of the public leaf
directly. A component can construct and inspect the declaration before it has
row values or chooses an execution strategy:

```csharp
var plan = RowSelectionPlan<string>.Create(
[
    RowSelectionStage<string>.Window(3, 6),
    RowSelectionStage<string>.Tail(2)
]);

// plan.Stages reports [Window, Tail]
```

The same component may then choose the generic reference evaluator:

```csharp
var result = RowSelectionExecutor.Apply(
    new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
    plan);

// result.Values is [5, 6]
```

What to notice: the second stage consumes and reindexes the first stage's
output. An alternative interpreter receives the same typed plan rather than a
different source-specific spelling and must produce the same observation for
every stage it accepts. The neighboring pathological plan `Head(2)` then
`Window(2,3)` returns a structured stage-2 failure requiring position 3 from an
input of 2; it does not intersect both stages against the original sequence and
return row 2.

## Stage semantics

Each stage consumes the sequence produced by the preceding stage:

| Stage | Result |
| --- | --- |
| `Head(N)` | The first `min(N, count)` rows, in current order. |
| `Tail(N)` | The last `min(N, count)` rows, in current order. |
| `Window(A, B)` | Current positions A through B inclusive; fails unless position B exists. |
| `Window(null, B)` | The first B current positions; fails unless position B exists. |
| `Window(A, null)` | Current position A through the end; fails unless position A exists. |
| `Window(null, null)` | Every current position, unchanged. |
| `Top(N, order)` | Rank the current rows by `order`, then keep the first `min(N, count)`. |

`Head`, `Tail`, and `Top` are lenient: a request larger than the current input
returns every current row. `Head` and `Tail` retain current order, and `Tail`
never reverses the surviving rows. `Top` always resolves and applies its
ranking, including when its count is at least the current count; an oversized
`Top` therefore returns every row in ranked order rather than baseline order.

`Window` is strict. A closed or prefix window requires its end to exist in the
current input. A suffix window requires its start to exist. A strict-window
failure is not an empty result and must not be reported as source exhaustion or
successful truncation. A boundless identity window has no required endpoint and
cannot fail.

### Convention and deliberate divergence

Selection positions count only declared data rows. They are the positions a
plain-text pipeline would see after removing a rendered table header:

- `Window(null, B)` has the positional shape of Unix `head -n B`;
- `Window(A, null)` has the positional shape of Unix `tail -n +A`; and
- `Window(A, B)` has the positional shape of
  `tail -n +A | head -n (B - A + 1)`.

The resemblance is about 1-based direction and composition. GNU `head` and
`tail` are lenient when the requested position exceeds the input, while
`Window` is deliberately strict so an unavailable requested row cannot look
like successful truncation. The component's `Head` and `Tail` count stages
retain the lenient Unix behavior.

The grammar also has established CLI precedents. `sed` uses 1-based inclusive
line-address ranges such as `10,20`; GNU `cut` accepts 1-based closed, prefix,
and suffix lists such as `1-3`, `-3`, and `3-`; and `bat --line-range` accepts
inclusive `30:40`, `:40`, and `30:` forms. Those tools are lenient when a bound
exceeds available input, unlike this semantic Window.

`Window` is not C# `System.Range`. C# ranges use zero-based indices, exclude the
end, permit a boundless `..`, and support from-end `^N` operands. A C# range
such as `1..3` selects indices 1 and 2 and permits the empty `3..3`; this
component's `Window(1, 3)` selects data rows 1, 2, and 3. Both reject
out-of-bounds slicing, but that shared strictness does not make their coordinate
systems interchangeable.

Kusto's `between (A .. B)` is inclusive at both ends, like a closed `Window`,
but it filters scalar values rather than selecting row positions. Kusto
`row_number()` supplies 1-based positions only over a serialized row set;
`Window` instead consumes the caller's already-ordered current sequence and
reindexes after every stage. Kusto `take` is lenient like `Head` but does not
guarantee which records survive unless its input is sorted.

References:

- [GNU `head`](https://www.gnu.org/software/coreutils/manual/html_node/head-invocation.html)
  and
  [GNU `tail`](https://www.gnu.org/software/coreutils/manual/html_node/tail-invocation.html)
- [GNU `sed` addresses](https://www.gnu.org/software/sed/manual/html_node/Addresses.html)
  and
  [GNU `cut`](https://www.gnu.org/software/coreutils/manual/html_node/cut-invocation.html)
- [`bat --line-range`](https://github.com/sharkdp/bat/blob/master/doc/long-help.txt)
- [C# range operator](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/member-access-operators#range-operator-)
- [Kusto `between`](https://learn.microsoft.com/kusto/query/between-operator),
  [`row_number()`](https://learn.microsoft.com/kusto/query/row-number-function),
  and [`take`](https://learn.microsoft.com/kusto/query/take-operator)

## Ordered composition and reindexing

After every stage, its output becomes a new sequence whose selection positions
start at 1. This is Unix-style pipeline composition, not intersection against
the original ordinals.

Conceptual examples make the evaluation order explicit:

```text
[1, 2, 3, 4, 5, 6, 7, 8].Window[3..4].Tail(2)
=> [3, 4].Tail(2)
=> [3, 4]

[1, 2, 3, 4, 5, 6, 7, 8].Window[3..6].Tail(2)
=> [3, 4, 5, 6].Tail(2)
=> [5, 6]

[1, 2, 3, 4, 5, 6, 7, 8].Tail(4).Window[2..3]
=> [5, 6, 7, 8].Window[2..3]
=> [6, 7]

[1, 2, 3, 4, 5, 6, 7, 8].Head(2).Window[2..3]
=> [1, 2].Window[2..3]
=> error: stage 2 requires position 3, but its input has 2 rows

[1, 2, 3, 4, 5, 6, 7, 8].Window[..3].Tail(2)
=> [1, 2, 3].Tail(2)
=> [2, 3]

[4, 1, 3, 2].Top(10, ascending).Head(2)
=> [1, 2, 3, 4].Head(2)
=> [1, 2]

[1, 2, 3, 4, 5, 6].Window[2..5].Top(2, descending)
=> [2, 3, 4, 5].Top(2, descending)
=> [5, 4]
```

The pathological case is `Head(2)` followed by `Window(2,3)`. Ordered
stage-local evaluation fails because row 3 does not exist after `Head`;
intersecting both requests against original ordinals would incorrectly return
row 2 and hide the unsatisfied window.

Reindexing changes only the temporary positions consumed by the next stage.
It does not rewrite producer-owned package coordinates, metadata identities,
Finding identities, source provenance, or any stable row address carried as
typed data. Selection position is never inferred from rendered text and is
never promoted into identity.

## Caller-supplied order and ranking

The executor receives each complete input sequence in the caller-selected
baseline order. This component does not decide how predicates, filters,
reductions, field or payload projection, acquisition, or presentation compose
around that invocation.

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
cannot satisfy any `Window` stage:

- evaluation fails;
- the structured failure identifies the input key, one-based stage number,
  required position, and available current-row count;
- no selected sequence collection is returned.

The executor must complete this pure preflight before returning any selected
sequence collection. The caller receives either all selected sequences or the
structured failure; surrounding projection, acquisition, presentation, and
destination effects remain outside this component.

## Failure model

The single-sequence executor returns a `RowWindowFailure` with:

- `StageNumber`: the one-based index of the failing `Window` stage;
- `RequiredPosition`: the end of a closed or prefix window, or the start of a
  suffix window, that had to exist; and
- `AvailableCount`: the size of that stage's current input.

The named-sequence executor returns `NamedRowWindowFailure`, containing the
component-owned input `Key` and the same `RowWindowFailure`. L2 resolves the
key through its retained map before producing a diagnostic. Failures contain
no message, exception text, row value, or rendered identity.

Invalid plan construction and a missing resolved comparer are caller misuse,
not `RowWindowFailure` outcomes. They reject before a selected result is
returned.

## Reference evaluator and alternative interpretation

The reference evaluator's stage definitions over a complete sequence are the
semantic oracle. An alternative interpreter may stream, buffer, sort, or push
work into a provider, but it is conforming only for the stages it accepts and
only when it preserves:

- the same surviving caller-owned values;
- the same output order;
- the same strict-window success or failure;
- the same named-sequence boundary; and
- the same all-or-failure output behavior;
- the same set of reached `Top` stages and one resolver invocation per reached
  stage; and
- the same semantic-failure, resolver-failure, and comparer-failure precedence.

Comparer call count and pair order are not equivalence dimensions for a valid
deterministic comparer. A delegation protocol may decline a plan or supported
subset according to its own contract; decline is not a semantic result and
does not change this language. The
[source delegation](source-delegation.md) protocol applies its
[source-closed boundary](source-delegation.md#source-closed-operations);
operations this design does not declare source-closed remain in the reference
or row-handoff residual path. A later observation-transport extension must
still preserve this complete callback contract.

This distinction matters when a later lenient stage would keep fewer rows than
an earlier strict stage validates:

```text
Rows.Window[100..200].Head(5)
```

The result contains positions 100 through 104, but successful evaluation still
requires proof that position 200 existed in the input to `Window`.

Conversely:

```text
Rows.Head(5).Window[100..200]
```

deterministically fails after `Head` produces at most five rows. Acquisition
must not fetch toward position 200 to rescue a window whose current stage input
cannot contain it.

An incomplete provider page is not proof that a strict endpoint is absent.
The source owner must obtain enough evidence, reject the unsupported
optimization, or report an acquisition/completion failure distinct from a
semantic window failure.

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
[TLA+ interaction model](../models/semantic-row-selection/SemanticRowSelection.tla)
supplements this
specification. It models one immutable plan applied to ordered named sequences,
with sequence-major and stage-major traversal, stage-local row positions,
strict `Window`, positional `Head`/`Tail`, ranked `Top`, resolver caching,
callback failures, withheld publication, and final atomic success.

The model deliberately abstracts row identity to distinct integers and ranking
to ascending or descending order. It assumes positive normalized coordinates,
a deterministic total-order comparer, complete input sequences, and no source,
CLI, rendering, or concurrency behavior. Those owners remain outside this
component.

First-time `Top` resolution is a distinct model transition. Ranking and
comparison are enabled only after that transition succeeds; a cached resolver
lets later named sequences apply the same stage directly.

[`SemanticRowSelection.cfg`](../models/semantic-row-selection/SemanticRowSelection.cfg)
checks all
plans up to two stages over two named sequences containing up to three distinct
values. It checks non-vacuous closed, prefix, suffix, and boundless window
forms, type safety, atomic publication, completion only after every sequence,
at-most-once resolver invocation and consistent resolver metadata,
sequence/stage failure precedence through the exact successful-history prefix
at every cursor, terminal failures against their current strict-window or
callback cause, resolver visibility no earlier than its traversal cursor,
resolver completion before ranking or comparer failure, each stage's input
against the preceding stage's output, live rows against completed history,
every successful stage's exact semantics through checks independent of the
transition helpers (including strict `Window`, stage-local reindexing, and ranked
`Top` output), callback admissibility and resolver coverage for every successful
`Top`, complete history and final rows for every published named result, and
eventual termination under weak fairness.

The model was checked with the pinned TLA+ Tools v1.8.0 prerelease
`tla2tools.jar` (published SHA-1
`0e4cfdb976f04522d218ec62c6046bbee5098377`), reporting TLC2
`2026.08.21.155922` revision `9787e65`. From
`docs/models/semantic-row-selection`:

```bash
java -XX:+UseParallelGC \
  -cp /path/to/tla2tools-1.8.0.jar \
  tlc2.TLC -cleanup -deadlock -workers auto \
  -config SemanticRowSelection.cfg SemanticRowSelection.tla
```

TLC generated and checked 2,715,108 distinct states to depth 9 with no errors
or material counterexamples. Deadlock checking is disabled because success,
strict failure, and callback failure are intentional terminal states; the
model permits terminal stuttering and separately checks eventual termination.
The named `WindowFormsAreModeled` assumption keeps closed, prefix, suffix, and
boundless forms non-vacuous; restoring the former positive-start-only generator
makes TLC reject that assumption before state exploration.

This clean bounded result is evidence about the interaction model, not proof of
the C# implementation; the named Release gates below remain required.

## Required gates

The implementation provides these proportional outcome-level Release gates:

| Gate | Contract |
| --- | --- |
| `SelectionStagesComposeInDeclaredOrder` | The reference examples and pathological `Head(2)` then `Window(2,3)` case prove stage order, stage-local reindexing, original-value preservation, empty-plan identity, stable equal-rank ordering, and ranked oversized `Top`. |
| `SelectionCountsAreLenientAndWindowsAreStrict` | Head, Tail, and Top retain their lenient behavior; closed, prefix, and suffix windows require their current-stage endpoint; boundless Window is identity; failures report the documented stage, required position, and current count. |
| `RowSelectionConstructionRejectsInvalidInputs` | Factories and executors reject representable invalid stage operands, null required arguments, null stage or sequence entries, wrong-kind accessors, missing reached comparers, and closed-window reversal without inventing successful output. Nullable row values and every `int` key remain ordinary inputs. |
| `SelectionCallbacksFollowStageOrder` | Resolver presence is validated at entry without eager invocation; each reached Top resolves once per stage and is cached across named sequences; earlier strict failures stop later callbacks; resolver and comparer exceptions propagate unchanged. |
| `NamedSelectionIsAtomicAndDeterministic` | Named success preserves input order; a strict miss returns no selected sequence collection; the first failure follows sequence then stage order; equal key values reject before execution and keys use stable value equality. |
| `RowSelectionSnapshotsAreImmutable` | Plans, named inputs, and returned collections snapshot membership and order; Append leaves the prior plan unchanged; exposed collections cannot mutate snapshots; selected row objects remain the caller's original values. |
| `RowSelectionLanguageConsumerExercisesDeclaration` | A non-friend fixture constructs and inspects every stage and plan entry point without row values or executor invocation, using only the public declaration API. |
| `RowSelectionReferenceEvaluatorExercisesSurface` | A non-friend fixture consumes the typed plan, constructs named input through the supported factories, invokes both executor methods with omitted and named optional arguments, and observes accessor, success, and failure behavior using only the public evaluator API. |

Every optional interpreter must name an equivalence gate for the stages it
accepts, using this complete-sequence reference evaluator as the oracle. The
[source delegation](source-delegation.md) contract owns that gate for its
specialized source protocol. Its delegation follows that owner's
[source-closed boundary](source-delegation.md#source-closed-operations):
operations this design does not declare source-closed remain in the reference
or row-handoff residual path. Any later extension that transports those
failures must also compare strict windows before and after lenient stages,
reached-stage resolver cardinality, and callback/failure precedence.

If this owner declares an operation source-closed, it proves that declaration
with `SourceClosedDeclarationsMatchOwnerContracts` against this design's typed
failure, resolver, comparer, and callback contract.
