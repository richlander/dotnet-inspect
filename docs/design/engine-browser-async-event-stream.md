# Engine-to-browser async event streams

## Status

This document defines the engine-to-browser async event-stream contract tracked
by [#5565](https://github.com/richlander/dotnet-inspect/issues/5565).
Package Query is the first named adopter through
[#5549](https://github.com/richlander/dotnet-inspect/issues/5549).

The contract is not yet implemented as a shared abstraction. Package Query
already has an `IAsyncEnumerable<PackageQueryEvent>` and a Browser callback
adapter, but its event vocabulary and terminal handoff do not yet satisfy this
document. The gates named below remain required.

## Decision

A long-running engine operation that produces useful partial outcomes exposes
one host-neutral `IAsyncEnumerable<TEvent>`. One host adapter consumes that
stream with `await foreach` and publishes its events across the active host
boundary. Browser UI code does not pull the engine enumerator.

The event union has four semantic categories:

- **Progress** reports a bounded phase or aggregate checkpoint. It is advisory,
  replaceable, and never evidence that an item matched.
- **Item** reports durable partial data that remains part of the outcome.
- **Item failure** reports a durable, scoped failure without discarding prior
  items or claiming that the whole operation failed.
- **Completed** is the one semantic terminal event and carries final accounting
  plus the feature-owned completion kind.

The stream is request-scoped, ordered, and single-consumer. It is not a global
event bus, a multi-subscriber observable, or a protocol by which the UI asks
the engine for more work.

## User scenarios

### Perceptible sparse query

A query can inspect many candidates that do not match. The engine reports
bounded aggregate progress while preserving silence about rejected candidates'
contents. The result pane can therefore say that 14 of 20 candidates have been
evaluated before any match exists, instead of appearing idle and then changing
directly to a bounded empty result.

### Durable partial results

Matches and item failures reach the host as soon as they are established. A
later progress checkpoint, item failure, cancellation, or bounded completion
does not replace rows already admitted into the outcome.

### Browser worker migration

The same engine stream works before and after .NET moves to a Web Worker.
Today an adapter may invoke a bounded synchronous JavaScript callback. The
worker adapter will map nonterminal events to validated `postMessage` payloads
and return the terminal result through the managed-operation result envelope.
Neither transport changes the engine event meanings.

### CLI consumption

A CLI host can consume the same stream, render durable items and failures, and
choose whether its output mode displays progress. Browser adoption does not put
DOM, JavaScript, worker, or callback types into the engine contract.

## Ownership

This document owns:

- the four semantic event categories;
- one ordered, request-scoped, single-consumer async sequence;
- the distinction between advisory progress and durable outcome events;
- exactly one semantic completion, after every nonterminal event;
- adapter-side pull, batching, progress coalescing, and bounded buffering;
- cancellation observation at async-enumerator and feature checkpoints; and
- the host-neutral obligations an adopting feature must define and gate.

It consumes:

- feature-owned inputs, item types, failure types, progress phases, completion
  kinds, bounds, and cancellation checkpoints;
- current-view identity and publication authority from
  [inspect-web operation authority](inspect-web-operation-authority.md);
- callback lifetime and managed terminal envelopes from
  [the managed operation bridge](inspect-web-managed-operation-bridge.md);
- worker placement, message validation, ordering, and realm lifetime from
  [the worker runtime](inspect-web-worker-runtime.md); and
- generated callback and result types from [`ts-jsexport`](ts-jsexport.md).

It does not own:

- feature-specific query, analysis, acquisition, matching, or rendering policy;
- operation identity, supersession, retry, sharing, cache, or timeout policy;
- DOM state, rendering cadence, focus, or browser-history behavior;
- callback retention, worker epochs, message inventories, replay validation,
  liveness recovery, or hard cancellation;
- source transport paging or whether a source can produce its first result
  incrementally; or
- a claim that reporting progress causes a browser paint or yields a worker
  event loop.

## Event contract

The following sketch states the semantic shape. Adopters use concrete closed
records so NativeAOT and generated interop can preserve every discriminator and
payload.

```csharp
public abstract record EngineStreamEvent<
    TItem,
    TItemFailure,
    TProgress,
    TCompletion>
{
    public sealed record Progress(TProgress Value)
        : EngineStreamEvent<TItem, TItemFailure, TProgress, TCompletion>;

    public sealed record Item(TItem Value)
        : EngineStreamEvent<TItem, TItemFailure, TProgress, TCompletion>;

    public sealed record ItemFailure(TItemFailure Value)
        : EngineStreamEvent<TItem, TItemFailure, TProgress, TCompletion>;

    public sealed record Completed(TCompletion Value)
        : EngineStreamEvent<TItem, TItemFailure, TProgress, TCompletion>;
}
```

The generic sketch is descriptive, not a requirement to add this exact generic
base type. A feature-specific closed union is preferable when it provides
clearer source-generated serialization or domain names.

An admitted stream satisfies:

1. zero or more nonterminal events occur in producer order;
2. durable item and item-failure events are never coalesced or dropped;
3. progress can be coalesced only within the same feature phase, and the
   retained event represents at least as much completed work as the replaced
   event;
4. one `Completed` event follows all nonterminal events;
5. no event follows `Completed`; and
6. normal enumeration ends immediately after `Completed`.

An unexpected exception or cancellation is not a second semantic completion.
The host adapter maps it through its operation failure or cancellation owner.
An expected feature-wide failure may be a feature-owned completion kind when
that owner can provide honest final accounting.

## Progress contract

Each adopter defines a closed progress union whose phases correspond to
user-meaningful work, not implementation call stacks. A progress value:

- names one feature-owned phase;
- carries only bounded primitive or owner-issued inert values;
- uses monotonic completed work within that phase;
- states a total only when the feature knows an honest bound;
- does not contain a matched item or item failure in disguise; and
- remains safe to omit from a non-interactive host.

Progress frequency must be structurally bounded. Report phase changes and
aggregate checkpoints, not every decoded instruction, archive entry, or byte.
The first useful checkpoint should precede a stage that can otherwise complete
with no item or failure events.

Progress does not prove responsiveness. Before worker migration, synchronous
managed work can still occupy the DOM event loop. After migration, synchronous
managed work can occupy the worker event loop. Worker placement protects DOM
input and paint; feature-owned awaits and structural yielding determine when
the worker can process cancellation or another message.

## Adapter contract

The engine adapter is the sole enumerator:

```text
feature IAsyncEnumerable<TEvent>
  -> host adapter await foreach
     -> callback today
     -> validated worker message after worker migration
        -> current-operation publication authority
           -> feature reducer and renderer
```

`MoveNextAsync` supplies natural backpressure inside managed code: the adapter
does not request the next event until it has handled the current one. A
synchronous callback must return promptly and perform no DOM work. Across a
worker boundary, `postMessage` does not carry pull-based backpressure, so the
worker adapter owns bounded batching:

- durable items and item failures may be grouped but not discarded;
- progress may be coalesced under the progress rules above;
- a batch preserves event order; and
- `Completed` cannot overtake an earlier batch.

The callback channel carries only nonterminal stream events. The adapter
retains `Completed` and returns its value once through the operation's terminal
result envelope. This avoids publishing the same semantic completion through
both a callback and a fulfilled `Task`.

The current-operation owner decides whether a transported event may update the
view. A stale event can be consumed for protocol and release purposes without
regaining publication authority.

## Cancellation and failure

The enumerator receives the operation cancellation token through
`[EnumeratorCancellation]`. Each awaited dependency receives that token, and
CPU work observes it at feature-owned bounded checkpoints. Cancellation stops
further semantic events; the operation owner supplies the visible cancellation
reason.

Item failures are data only when the feature can continue and retain honest
accounting. A source or boundary failure that prevents an honest completion
uses the feature's failed completion or the enclosing operation failure path.
Adapters do not convert malformed events, callback failures, worker failures,
or unexpected exceptions into empty successful streams.

## Consumer adoption

Each adopting owner specifies:

- the concrete event union and which variants are durable;
- progress phases, counters, totals, and coalescing keys;
- the terminal accounting and completion kinds;
- the first checkpoint for a no-item path;
- cancellation checkpoints and awaited calls;
- maximum durable events and adapter batch size;
- mapping to its CLI and Browser hosts; and
- neighboring no-progress and failure cases.

Package Query is the first adopter. Its progress must distinguish bounded
source discovery, manifest evaluation, and explicit package-content evaluation.
Its item evidence continues to identify why a package matched. This document
does not decide Package Query's facet combination or empty-state wording.

## Analogous designs

- C# async streams establish `IAsyncEnumerable<T>` and `await foreach` as the
  conventional model for asynchronously generated sequences, cancellation, and
  incremental consumption.
- WHATWG readable streams distinguish producer mechanics from consumer reads
  and centralize queuing and backpressure at the stream boundary.
- Reactive Streams uses an ordered `onNext*` followed by one `onError` or
  `onComplete` protocol and treats bounded demand as the protection against
  unbounded asynchronous buffering.
- Web Worker `postMessage` is a message transport, not an engine enumerator or
  feature state model.

These are comparative evidence. This contract deliberately keeps .NET
enumerator backpressure inside the engine adapter and uses bounded
batching/coalescing, rather than implementing a cross-worker Reactive Streams
request protocol. Package and analysis operations are already bounded, and the
additional bidirectional demand machinery would not improve their user-visible
outcomes.

## Validation

The shared contract is enforced through each adopter's Release gates. A first
adopter must prove:

1. progress precedes terminal completion on a no-item path;
2. progress is monotonic within a phase and never exceeds a declared total;
3. items and item failures retain producer order around progress events;
4. exactly one completion is observed and no callback carries it;
5. cancellation and unexpected failure produce no later semantic event;
6. callback or worker batching cannot drop durable events or reorder completion;
7. stale-operation events cannot update replacement feature state;
8. a CLI consumer can enumerate the same host-neutral stream without Browser
   types; and
9. a neighboring operation with no useful partial outcome remains a simple
   task rather than being forced into an event stream.

The operation-authority and worker-runtime models already own cross-operation
publication and worker protocol state. This linear, single-consumer feature
sequence adds no independent concurrent state machine, so it requires
outcome-level Release tests rather than another TLA+ model.

## References

- [Generate and consume async streams — Microsoft Learn](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream)
- [Streams Standard](https://streams.spec.whatwg.org/)
- [Reactive Streams](https://www.reactive-streams.org/)
- [Worker `postMessage()` — MDN](https://developer.mozilla.org/docs/Web/API/Worker/postMessage)
