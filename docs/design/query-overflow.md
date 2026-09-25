# QueryOverflow: resumable QuerySpace execution

## Status

Focused design for
[#8591](https://github.com/richlander/dotnet-inspect/issues/8591).
This document establishes **QueryOverflow** as one architectural owner. The
contract is not implemented; every implementation and production claim in
this document is **unverified** until its named Release gate or adoption lands.

[Query Space Composition](query-space-composition.md) remains authoritative
for structural query composition, semantic selection, work bounds, Rows,
exact Count, and preservation of an adjacent source continuation.
[QuerySpace Library Boundary](query-space-library.md) remains authoritative
for the synchronous, dependency-free `QuerySpace` library and its complete
sequence reference evaluators. QueryOverflow consumes those owner-issued
contracts without changing their meaning.

The first planned production adopter is the decoded-document work tracked by
[#8319](https://github.com/richlander/dotnet-inspect/issues/8319), followed by
Source composition under
[#8281](https://github.com/richlander/dotnet-inspect/issues/8281).
Package Type inventory across all selected assemblies is the second target row
domain. Closed-world results such as call graphs remain valid completion-only
consumers and do not need progressive publication.

## Owner and exact claim

**QueryOverflow** owns this exact claim:

> One resolved QuerySpace request can advance synchronously across an ordered
> succession of independently owned row batches while retaining only detached
> execution state. Every accepted partition of the same complete logical
> population has the same Rows, exact Count, semantic failures, callback
> observations, and terminal meaning as the complete-sequence QuerySpace
> reference execution.

QueryOverflow defines:

- one synchronous execution spanning zero or more row batches;
- the checkpoint carried between batch steps;
- the request for the next bounded candidate-row batch;
- the distinction between candidate-row demand, final-row delivery credit,
  semantic selection, and source-native physical reads;
- ownership transfer at batch admission and release;
- progressive publication of rows that are final under the resolved plan;
- publication of exact Count only after an accepted terminal-sufficiency
  witness;
- finalization from owner-issued population completion or another
  QuerySpace-owned terminal witness;
- preservation of complete-sequence failure and callback semantics; and
- equivalence requirements for alternative batch kernels.

QueryOverflow does not define:

- portable query syntax, predicates, order, semantic selection, Rows, or Count
  meaning;
- source population identity, authority, acquisition, retry, caching, paging,
  or physical cursor semantics;
- asynchronous I/O, network fetch, file reads, process output, decoding, or
  line framing;
- host delivery credit, Browser virtualization, worker transport, CLI
  rendering, or destination writes;
- owner-specific row identity, evidence, provenance, diagnostics, or failure
  taxonomy;
- source-delegation capability or completion-evidence construction;
- a universal stream, file, document, package, or graph abstraction; or
- progressive publication for plans whose output is not final before source
  completion.

## Product goal

QuerySpace works naturally when one complete stable population is already
available. Package acquisition may be asynchronous, but an acquired assembly
is a stable resource and System.Reflection.Metadata inspection is synchronous.
The complete rows can therefore enter one QuerySpace reference execution.

Large stable resources and live sources need the same query semantics without
requiring the complete row population to be resident at once:

```text
source or stable resource
  -> one owned row batch
     -> synchronous QueryOverflow step
        -> release the input batch
        -> publish final Rows or retain terminal state
  -> next owned row batch
```

This enables:

- Source and package-document lines to reach CLI or Inspect Web incrementally;
- package Type inventory to process and release one assembly at a time;
- exact Count over a large file while retaining only a scalar accumulator;
  and
- a line-native live operation to satisfy `Head(100)` without waiting for an
  otherwise open-ended source to terminate.

QueryOverflow does not make every query progressive. A call graph, global
sort, strict failure condition, or other closed-world computation may require
the complete logical population before any result row is final.

## Conventional basis and deliberate differences

QueryOverflow combines established patterns without copying one complete
system:

| Precedent | Adopted idea | Deliberate difference |
| --- | --- | --- |
| NLinq at commit `229e2435fc10f8a50e0fecb5e5a57bb18832f415` | Pull enumeration, source-specific synchronous folds, fused filtering and projection, and early-exit terminals | NLinq's current sources are finite and synchronous; its `ref struct` pipeline state cannot cross `await`, a Worker call, or a retained operation boundary. QueryOverflow advances owned batches through an ordinary detached checkpoint. |
| Vectorized database execution | Run a synchronous operator pipeline over bounded batches while carrying operator state between batches | QueryOverflow preserves QuerySpace's typed plan and complete-sequence oracle rather than defining a relational algebra or storage engine. |
| `Stream.Read` and `PipeReader` | The puller supplies bounded capacity, a short read does not itself establish EOF, and unconsumed input remains owned until explicitly advanced | QueryOverflow requests candidate rows rather than bytes and performs no I/O. Source adapters translate row demand into their own byte, page, assembly, or response bounds. |
| Existing package-prefix pages | Useful source batches can be processed before later acquisition begins, and physical request size remains source policy | QueryOverflow is synchronous and source-neutral; it neither exposes `IAsyncEnumerable<T>` nor owns NuGet paging. |
| Engine-to-Browser async event streams | Durable rows can publish before terminal completion, under independently supplied delivery credit | QueryOverflow defines final-row availability and terminal state, not callback, worker, credit-replenishment, or rendering policy. |

The novel part is the exact join: a source-owned position and a
QueryOverflow-owned checkpoint advance as one outer operation without making
QuerySpace or its per-row kernel asynchronous.

## Physical boundary

The target physical boundary is a `QueryOverflow` library and root namespace
with a one-way dependency on `QuerySpace`. `QuerySpace` does not reference
QueryOverflow. Source, House, Sections, CLI, and Browser types do not enter the
reusable library.

The library contains the synchronous execution, checkpoint, demand, step, and
terminal contracts. Source adapters, asynchronous loops, retained-operation
registries, and host-visible receipts remain in their adopting owners.

## Three-layer execution

The runtime composition has three distinct owners:

```text
House or source operation
  owns source cursor, stable-resource lease, async or sync acquisition,
  cancellation, source failure, and host-visible continuation receipt

QueryOverflow execution
  owns the QuerySpace checkpoint, candidate-row batch demand,
  terminal accumulation, and final-row publication eligibility

QuerySpace batch kernel
  owns synchronous interpretation of the resolved plan over admitted rows
```

The House or source operation may obtain a batch synchronously or
asynchronously. QueryOverflow receives only an already-produced batch and
advances synchronously. The batch kernel performs no I/O and returns no
`Task`, `ValueTask`, lazy enumerable, or retained source handle.

A complete population is the one-batch case:

```text
Start
  -> Advance(complete rows, source complete)
     -> final Rows or exact Count
```

A large or delayed population uses the same logical execution:

```text
Start
  -> Advance(batch 1, source not complete)
  -> Advance(batch 2, source not complete)
  -> ...
  -> Advance(final batch, source complete)
     -> final terminal
```

An adopter may use the existing complete-sequence evaluator directly when it
already has the complete population. QueryOverflow equivalence means that this
choice changes execution only, never result meaning.

## Vocabulary

### Logical population

The **logical population** is the source-owner-issued ordered row sequence to
which one resolved QuerySpace request applies. Its identity, consistency,
completion, and authority remain source-owned.

### Candidate row

A **candidate row** is one row from the logical population admitted to the
QueryOverflow execution before residual QuerySpace predicates and semantic
selection. A QueryOverflow input batch contains only complete candidate rows.

Physical byte blocks, archive pages, network responses, partial decoded lines,
and long-line fragments are not candidate rows.

### Batch

A **batch** is a finite, ordered, non-overlapping prefix of the logical
population remaining at the source cursor. It is owned by the caller during
one synchronous step.

Batch boundaries have no semantic meaning. Changing them must not change row
identity, order, selection, Count, completion, callback observations, or
failure precedence.

### Checkpoint

A **checkpoint** is the complete detached QueryOverflow state after an exact
prefix of candidate rows has been consumed. Depending on the resolved plan it
may retain:

- source and selected-row ordinals;
- remaining Head or Window positions;
- predicate and stage progress;
- a running Count;
- owned Tail or Top candidates;
- delayed rows whose finality is not yet established; and
- terminal and failure state.

A checkpoint retains no borrowed row, span, pooled buffer, enumerator, stream,
source lease, cancellation source, or host callback.

### Step

A **step** synchronously applies one owned candidate-row batch to one
checkpoint. It returns:

- the next checkpoint;
- the number of candidate rows consumed;
- zero or more detached rows eligible for publication;
- whether more input is required;
- whether the semantic terminal is satisfied; and
- any QuerySpace-owned semantic failure.

### Source completion

**Source completion** is owner-issued evidence that no candidate row follows
the batch under the same logical-population binding. A short or empty physical
read, source page, row batch, delivery grant, or work bound is not source
completion.

### Terminal sufficiency

**Terminal sufficiency** means that the resolved QuerySpace terminal has an
exact answer. Population completion is one sufficient witness, but it is not
the only one. QuerySpace's existing Count contract also admits an
owner-accepted exact source Count and permits `Head(N) -> Count` to complete
after witnessing `N` applicable ordered rows. QueryOverflow preserves every
such owner-issued witness without deriving one from batch size, observed
cardinality, or continuation state.

## State and transition contract

One execution is linear and single-consumer:

```text
Created
  -> NeedsInput
  -> RowsAvailable -> NeedsInput
  -> ...
  -> Completed

Created or NeedsInput
  -> Failed

Created, NeedsInput, or RowsAvailable
  -> Cancelled
```

`Completed`, `Failed`, and `Cancelled` are terminal. No later batch is admitted
and no later row is published.

One batch step consumes candidate rows in order. A step may stop before the end
of its supplied batch when:

- the semantic result is satisfied;
- current final-row delivery credit is exhausted;
- the execution reaches an owner-issued work boundary; or
- a semantic failure occurs.

The returned consumed count determines the next source position. An
unconsumed suffix remains source-owned or outer-operation-owned and must be
supplied before any later source row. It cannot be silently discarded when
the logical execution will resume.

When semantic completion intentionally ends the logical query, such as
`Head(100)`, the outer operation may abandon the unconsumed source suffix and
release the source.

## Requesting the next batch

Batch size is an execution-control agreement between QueryOverflow and the
row-source adapter. It is not portable query meaning.

Before each pull, the outer operation asks QueryOverflow whether more
candidate rows are needed. When they are, QueryOverflow returns a
**candidate-row request** containing one positive hard maximum:

```text
MaximumCandidateRows
```

The outer operation supplies an owner-selected positive ceiling when asking
for demand. QueryOverflow may lower that ceiling when the resolved plan,
remaining terminal requirement, or current delivery credit proves that fewer
candidate rows can be useful. It never raises the supplied ceiling.

The outer operation then asks the row-source adapter for at most that many
complete candidate rows:

```text
QueryOverflow demand
  MaximumCandidateRows = min(
    owner batch ceiling,
    proven useful candidate-row ceiling)

row source response
  0..MaximumCandidateRows complete candidate rows
  + explicit source completion, suspension, or failure
```

The request direction is deliberately puller-to-source: QueryOverflow states
the maximum candidate rows it can admit next, and the outer operation requests
that capacity from the source. The source never pushes an unbounded batch or
turns its preferred size into a requirement.

The contract can be expressed without making the source asynchronous:

```text
demand = execution.RequestInput(ownerBatchCeiling, finalRowCredit)
batch = source.ReadAtMost(demand.MaximumCandidateRows)
step = execution.Advance(batch.Rows, batch.Completion)
source.CommitConsumedPrefix(step.ConsumedRows)
```

`ReadAtMost` may be synchronous or awaited by the outer operation. Neither
choice changes the QueryOverflow request or step.

The request is a maximum, not a requirement to fill the batch. A source may
return fewer rows because it reached a natural boundary, exhausted currently
available live input, or completed the population. A short batch never implies
completion. A source with no row currently available waits or reports its
owner-defined suspension; it does not return a successful nonterminal empty
batch that would cause a busy loop.

The source must not exceed `MaximumCandidateRows`. If it naturally reads ahead,
it retains the unread suffix under its own bounded cursor or returns that
suffix as unconsumed input; it does not advance the committed logical source
position past rows QueryOverflow has not consumed.

### Source-selected batch policy

Each source owner chooses its ordinary batch ceiling from its own cost and
resource evidence. It may expose a positive preferred size to its outer
operation. The outer operation may request that preferred size or a smaller
size, but must enforce its own hard maximum.

A source preference is advisory. It cannot:

- change the resolved QuerySpace request;
- establish semantic Head, Window, Count, or completion;
- override host delivery credit;
- authorize more source work;
- change failure behavior; or
- require QueryOverflow to retain borrowed values.

An implementation may adapt future request sizes from observed source density
or cost, as package-prefix acquisition does today, provided every request
remains under the owner ceiling and batch-size independence is gated.

### Physical read size

QueryOverflow requests candidate rows, not bytes. A source adapter translates
the row request into source-native work:

```text
MaximumCandidateRows
  -> source-owned byte/page/assembly policy
     -> bounded physical reads
        -> complete candidate rows
```

A decoded-line adapter may use a byte-block ceiling, UTF-16 text ceiling, and
serialized-output ceiling in addition to the candidate-row maximum. Those
limits remain owned by the decoded-text or Source adopter. A physical read may
cross several line boundaries or stop within one line; decoder and fragment
state remain below QueryOverflow until a complete candidate row exists.

For an assembly inventory, the adapter may open one stable assembly and yield
at most the requested number of Type rows before retaining or releasing its
metadata cursor. Assembly boundaries are source boundaries, not QuerySpace
batch requirements.

### Delivery credit

Final-row delivery credit is supplied independently by a host or adapter. It
limits how many already-final Rows QueryOverflow may publish before yielding
control. It does not limit Count work and does not become a semantic Head or
Window.

The candidate-row request accounts for available delivery credit when doing so
can avoid useless read-ahead. Residual predicates may require multiple
candidate batches to fill one final-row grant. QueryOverflow may stop within a
candidate batch when credit is exhausted and reports the exact consumed count
so the suffix remains available.

An implementation may permit bounded pull-ahead only when the owning outer
operation retains the unconsumed suffix and its resource cost remains within
an explicit owner limit. Pull-ahead is not part of the initial QueryOverflow
claim.

## Terminal behavior

### Rows

Rows are published in detached batches as soon as they are final under the
resolved plan. A row is final when no future candidate can invalidate,
replace, deduplicate, or reorder it and no unresolved QuerySpace failure can
require the complete result to be withheld.

An empty row plan in source order and Head over that plan can admit progressive
publication. A residual predicate or callback does not automatically qualify:
the complete-sequence QuerySpace evaluator may invoke it on a later candidate
and fail atomically. Progressive publication requires an accepted
interpretation proving that no future QuerySpace-owned observation can
invalidate already returned rows.

Tail, global ordering, ranking Top, strict Window, and any unresolved
QuerySpace callback or failure requirement generally require terminal
sufficiency or owned retention before publication.

QueryOverflow may decline a plan for which it has no equivalent bounded
interpretation. It must not reinterpret that plan as a batch-local query.
Applying Head, Window, Tail, Top, predicates, or ordering independently to each
batch is invalid.

Rows published before a later source or acquisition failure remain durable
partial source outcomes. The terminal failure is visible and does not claim
successful completion or exact Count.

A later QuerySpace-owned semantic failure is different: the existing
complete-sequence contract publishes no earlier residual Rows. QueryOverflow
therefore withholds rows for any plan with an unresolved semantic-failure or
callback path, or declines that plan. It cannot publish rows and later
reinterpret the semantic failure as a source-style partial outcome.

An adopter requiring atomic Rows for an otherwise publication-safe plan may
retain all output until completion. That is a publication policy, not a
different query result.

### Count

Count retains an internal accumulator and publishes no count value until every
preceding QuerySpace stage and an accepted terminal-sufficiency witness
establish the exact terminal result.

A source suspension, continuation, work bound, delivery grant, observed count,
provider total, or short batch never establishes exact Count. Failure or
cancellation before terminal sufficiency publishes no Count value.

Population completion, an owner-issued exact source Count, and a
QuerySpace-owned semantic witness such as `Head(N)` after `N` applicable
ordered rows may satisfy Count under the existing QuerySpace and
source-delegation evidence contracts. When `Head(N)` observes fewer than `N`,
population completion or another accepted witness is still required.
QueryOverflow does not promote an operational source value into semantic proof.

### Completion-only rows

A producer or resolved plan may require a complete view before any row is
final. Call graphs, global ordering, strict all-or-failure selection, and
cross-row analyses are completion-only examples.

Such work may still consume and release input batches internally. QueryOverflow
withholds host-observable rows until the producer and plan establish finality,
or the owner may use ordinary complete QuerySpace execution after constructing
its stable result.

## Complete-sequence equivalence

For one resolved plan `P`, logical population `R`, and any ordered partition
`B1..Bn` whose concatenation is exactly `R`:

```text
CompleteQuerySpace(P, R)
==
Finalize(
  Advance(
    ...
      Advance(Start(P), B1),
    ...),
  Bn with source completion))
```

Equality covers:

- selected row values and order;
- exact Count;
- semantic success or failure;
- strict-stage failure identity and precedence;
- resolver and comparer invocation order where observable;
- callback exceptions;
- semantic completion;
- source-population completion interpretation; and
- absence of duplicated or skipped candidate rows.

Different positive batch ceilings, short nonterminal batches, and source-native
physical read boundaries must preserve this equality.

QueryOverflow may support only a subset of resolved plan topologies initially.
Construction or plan admission must decline unsupported shapes before
candidate-row effects begin. A decline permits the caller to use the complete
reference evaluator; a failure after accepted execution does not silently
restart through another strategy.

## Ownership and re-entry

QueryOverflow checkpoints and source positions are issued by different owners
but form one correctness join. The outer operation commits them together:

```text
source population binding
+ next source position
+ QueryOverflow checkpoint generation
+ unchanged resolved plan identity
= resumable operation state
```

The host-visible receipt identifies that joined state under the outer
operation's lifetime contract. QueryOverflow neither constructs nor validates
network, package, file, process, Workspace, Worker, or authorization state.

Re-entry with a stale checkpoint, different plan, different population, or
incompatible source position fails visibly at the owner that can validate that
association. It never restarts at the first row while presenting the operation
as a continuation.

Only one step may advance one checkpoint generation. Concurrent advancement is
not part of this contract. An outer operation that publishes rows and then
commits a checkpoint must define retry or one-shot receipt behavior so a retry
cannot duplicate published rows.

## Batch ownership

The caller owns an admitted batch for the duration of one synchronous step.
QueryOverflow may borrow its rows only during that call.

After the step:

- the caller may release every consumed input value;
- the checkpoint contains no borrowed batch value;
- every published row is detached;
- every retained Tail, Top, delayed, or pending row is owned by the checkpoint;
  and
- an unconsumed suffix remains explicitly owned outside the checkpoint or is
  transferred into owned checkpoint storage.

For byte-shaped rows, a detached output may own one bounded byte slab plus row
ranges rather than allocate one array per row. For string-shaped rows, the
source may transfer existing immutable strings or QueryOverflow may copy the
values required by its plan. Representation changes do not change row
identity, order, selection, or Count.

## Failure and cancellation

QueryOverflow preserves three failure locations:

1. **Before admission:** invalid or unsupported plans decline before source
   work.
2. **During a batch step:** QuerySpace predicate, resolver, comparer, strict
   selection, or kernel failures terminate the execution under the existing
   semantic precedence.
3. **Outside QueryOverflow:** acquisition, parsing, source, cancellation, and
   resource failures terminate the outer operation under their owning
   contracts.

A source or outer failure after progressive Rows publication preserves those
durable rows and reports terminal failure. It cannot publish exact Count or a
successful complete Rows outcome. A QuerySpace-owned semantic failure retains
the existing atomic Rows behavior: any plan that can still reach such a failure
withholds output, so no earlier residual Rows have been published.
Completion-only publication emits no rows on failure.

Cancellation requests no later source batch. QueryOverflow performs no hidden
cleanup because it owns no source resource; the outer operation releases its
current batch, cursor, lease, and retained operation state.

## Examples

### Exact Count over a large CSV

An outer operation reads and parses a stable 1 GB CSV under source-owned byte
and row limits. QueryOverflow applies an even-value predicate and retains only
the running count:

```text
batch 1: 64 matches -> checkpoint Count = 64
batch 2: 25 matches -> checkpoint Count = 89
...
EOF: publish exact Count
```

No intermediate observation is a Count result. A parse or read failure before
EOF publishes no exact Count.

### Source lines

A verified immutable Source document supplies complete line rows through a
decoded-text adapter:

- `Head(100)` stops after the hundredth selected line without requiring source
  exhaustion;
- `Window(4,64)` preserves global line positions across every physical batch;
- Count drains to verified document EOF; and
- the same QueryOverflow semantics apply whether a row owns encoded bytes or a
  decoded string.

The Source owner's measured 256-row, UTF-16, and Browser JSON limits remain
Source or decoded-text policy. QueryOverflow consumes only the resulting
candidate-row maximum.

### Package Types across all assemblies

The package owner establishes one stable assembly order and each metadata owner
establishes Type row order. QueryOverflow retains one global selection and
terminal state while the outer operation opens, inspects, and releases
assemblies in order.

Rows may publish after each batch when their order and identity are final.
Count publishes only after the last selected assembly and every required
failure/completion outcome are known.

### Call graph

Call-graph construction may read assemblies or bodies incrementally, but a
later input can change graph identity, reachability, ordering, or derived
findings. Its rows are therefore completion-only. QueryOverflow does not make
them progressive merely because construction used batches.

### Open-ended line source

Suppose a Windows host starts `ping -t host`, frames each complete output line
as one semantic row, and lowers `-n 100` to `Head(100)`. With an owner batch
ceiling of 32 rows, execution may proceed as follows:

```text
request 1: at most 32 candidates -> consume and publish rows 1..32
request 2: at most 32 candidates -> consume and publish rows 33..64
request 3: at most 32 candidates -> consume and publish rows 65..96
request 4: at most 4 candidates  -> consume and publish rows 97..100
QueryOverflow: Completed; request no later batch
process owner: cancel ping, await process shutdown, release streams and buffers
```

QueryOverflow lowers the fourth request because only four more candidate rows
can contribute to `Head(100)`. If natural process read-ahead has already
produced later lines, the source owner may discard that unread suffix because
the logical query will not resume.

Stopping the process is resource cleanup after successful semantic completion;
it does not change the QueryOverflow result to `Cancelled` and does not report
a source failure. Output produced while process cancellation takes effect is
not admitted to the completed execution.

Exact Count over the unrestricted process remains unavailable without an
owner-defined finite horizon or eventual source completion. A retained live
process may support an operation-stable outer receipt while the query still
needs input, but QueryOverflow owns no process or async enumeration.

## Production adoption

The planned sequence is:

1. Implement QueryOverflow's synchronous checkpoint, batch-demand, step, and
   equivalence-test substrate with an independent application-owned row type.
2. Adopt decoded document lines under #8319, keeping physical byte reads,
   decoding, fragments, and document binding with their owners.
3. Compose Source Rows and Count under #8281.
4. Adopt Source in CLI, where line-native `-n`, `--rows`, and `--count` can use
   the same resolved semantic plan when their host lowering is equivalent.
5. Adopt Source in Inspect Web through owner-issued row delivery credit and
   outer-operation receipts.
6. Evaluate package Type inventory across all selected assemblies as the
   second row-domain adopter.

Exact-byte CLI output remains under
[#8303](https://github.com/richlander/dotnet-inspect/issues/8303) and bypasses
QueryOverflow when it asks no row question.

## Required evidence

| Gate | Required property |
| --- | --- |
| `EveryBatchPartitionMatchesCompleteExecution` | For every tested partition of the same ordered rows, synchronous QueryOverflow execution matches the complete QuerySpace reference result and failure. |
| `BatchSizeDoesNotChangeQueryMeaning` | Positive batch ceilings, short nonterminal batches, and natural source boundaries preserve rows, order, Count, completion, and callback observations. |
| `BatchDemandIsNotSemanticSelection` | Candidate-row maximum, final-row credit, Head or Window, source completion, and physical byte/page limits remain independently observable and are never substituted for one another. |
| `ConsumedPrefixPreservesUnconsumedSuffix` | A step that stops within a batch reports its exact consumed prefix; resumption neither skips nor duplicates the suffix. |
| `HeadStopsOpenEndedSourcePull` | `Head(N)` over an instrumented live source admits exactly `N` final applicable rows, requests no later batch, and returns `Completed` so the outer owner releases the source without changing successful semantic completion into cancellation. |
| `CheckpointOwnsNoReleasedBatchValue` | After a step releases its input owner, Count and position state remain valid, and every retained or published row has detached ownership. |
| `ProgressiveRowsAreFinal` | Publication-safe plans publish each row exactly once in final order; plans with unresolved semantic failures or callbacks, completion-only plans, and strict all-or-failure plans withhold rows until their requirements are proven. |
| `ExactCountRequiresTerminalSufficiency` | Count publishes once after population completion or another accepted QuerySpace terminal witness; `Head(N) -> Count` completes after `N` applicable ordered rows, while fewer rows still require completion, and no operational bound or observation substitutes for proof. |
| `SourceFailureDoesNotBecomeCompletion` | A late source failure preserves already published progressive Rows, publishes no exact Count, and never produces a successful complete outcome. |
| `SemanticFailurePreservesAtomicRows` | A plan with an unresolved QuerySpace-owned failure or callback path publishes no residual Rows before that path is discharged; a later semantic failure therefore retains the complete-sequence all-or-failure result. |
| `OneBatchMatchesDirectQuerySpace` | One final batch and the direct complete-sequence path have identical observable results. |
| `SourceAndCheckpointAdvanceTogether` | A bounded state model checks that accepted re-entry cannot skip or duplicate rows and that stale or concurrent checkpoint advancement cannot produce a second accepted transition. |
| `IndependentConsumerRunsQueryOverflow` | A non-dotnet-inspect consumer uses application-owned batch and row types to execute Rows and Count without async, House, source, CLI, Browser, or reflection dependencies. |

The state model and implementation gates are required before the first
production adoption. Until then, the contract remains unverified.

## Non-claims

This design does not claim:

- that every QuerySpace plan can publish progressive rows;
- that every source can resume after releasing its live resources;
- that batch size predicts selected-row count, latency, bytes, or memory;
- that exact Count implies random access;
- that a continuation implies source exhaustion or semantic completion;
- that one source batch corresponds to one network response, archive page,
  assembly, byte block, or Browser message;
- that QueryOverflow replaces source delegation or async event streams;
- that a source can publish unverified or incompletely contained content;
- that QueryOverflow makes a live or infinite source finite; or
- that a host may lower rendered-line `-n` to semantic Head without proving a
  one-row-to-one-rendered-line correspondence.
