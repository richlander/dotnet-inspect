# Inspect-web asynchronous composition

## Status

This document is the normative owner for the cross-owner sequencing and typed
handoffs tracked by
[#5095](https://github.com/richlander/dotnet-inspect/issues/5095).

It composes:

- main-thread logical authority;
- dedicated-worker placement and lifetime;
- generated TypeScript-to-.NET facades;
- managed operation admission and release; and
- feature-owned physical work.

It does not redefine those components. If a sequence or type reference here
disagrees with an owning document, the owning document is authoritative.

The generated inspect-web facade and authenticated synchronous delegate support
have landed through
[#5003](https://github.com/richlander/dotnet-inspect/issues/5003) and
[#5005](https://github.com/richlander/dotnet-inspect/issues/5005).
The operation-authority product component and its first Type Source adoption
are implemented. The worker-runtime and managed-operation-bridge product
components have not landed, so their composed behavior remains **unverified**.

## Composition responsibility

This document owns only:

- the order in which owner-issued values cross component boundaries;
- the distinction between logical outcome, physical execution, publication,
  progress, quiescence, and realm release;
- the scenario-level relationship among browser, TypeScript, .NET, and feature
  semantics;
- the portability vocabulary used to compare a future Rust engine; and
- the dependency order for focused implementation and adoption slices.

The participating owners are:

- [operation authority](inspect-web-operation-authority.md), which owns logical
  operation identity, current-view authority, one logical outcome,
  cancellation and supersession, stale-event suppression, and operation
  quiescence;
- [worker runtime](inspect-web-worker-runtime.md), which owns worker placement,
  epoch identity, readiness, message validation, dispatch, liveness, draining,
  hard termination, and realm release;
- [managed operation bridge](inspect-web-managed-operation-bridge.md), which
  owns managed admission, keyed cancellation, progress-callback lifetime,
  result classification, managed quiescence, shared-waiter detachment, and
  epoch-work leases;
- [`ts-jsexport`](ts-jsexport.md), which owns generated facade construction,
  authenticated callback and result types, `Task<T>` to `Promise<T>`
  projection, and generated runtime helpers;
- the
  [inspect-web facade partition](inspect-web-jsexport-partitioning.md), which
  owns the proposed production module set and one-runtime composition if that
  partition is adopted;
- the [inspect-web consumer](../../prototypes/inspect-web/README.md), which owns
  the implemented browser build, startup sequencing, hosting, and deployment;
  and
- each feature, which owns input and result meaning, physical work,
  cancellation checkpoints, progress phases, structural yielding, retry,
  sharing, cache, and rendering policy.

This composition owns no participant's construction, validation, identity,
lifetime, failure semantics, message inventory, state machine, or gate
internals.

## User scenarios

### Responsive initial inspection

The page can start the work needed for its initial result as soon as user intent
is known. Operation authority issues the logical identity and its `started`
event lets the feature render loading state. A worker adapter carries the
physical work to the dedicated single-threaded .NET realm. The generated
facade projects managed completion back to a JavaScript Promise.

Worker placement, not the Promise or managed `Task`, keeps synchronous managed
CPU work off the DOM event loop. The result may update the page only while the
original logical operation still owns the feature view.

### Cancellation and supersession

When the user cancels or starts a replacement, operation authority completes
the old logical outcome immediately and removes its publication authority.
The producer adapter then forwards the owner-issued reason through the worker
runtime and, after managed admission, through the managed bridge.

Physical work may continue until feature code or a cancellable wait observes
the request, until natural completion, or until worker-realm destruction. Late
progress, success, failure, and cleanup from the old producer cannot overwrite
the replacement view. Its separate quiescence signal resolves only after the
responsible owner releases the operation resources or the worker runtime
destroys their realm.

### Nonterminal events without publication confusion

The feature defines the progress payload and semantic phases. The managed
bridge scopes the synchronous callback to one admitted operation. The worker
runtime validates and transports current-epoch progress. Operation authority
admits progress only while the operation remains current and pending. The
feature observer decides how to render the admitted value.

Progress is neither a terminal result nor proof that the DOM received a paint.
It is also not worker task-loop liveness evidence unless the worker-runtime
owner explicitly classifies the corresponding event that way.

For async-stream adopters, the feature also defines a durable nonterminal
payload whose item and item-failure variants remain part of the outcome.
Operation authority publishes each current durable payload once in producer
order and consumes stale payloads without publication. The managed bridge and
worker runtime do not yet carry that payload; #5419 and #5418 own those
remaining handoffs. Durable events are not progress and do not acquire
publication authority that survives cancellation or replacement.

### Shared and speculative physical work

A feature may share acquisition or analysis work among logical operations. It
may also start anticipated follow-up work after the initial result is
available. The feature owns whether that work should start, be shared, be
cached, or be retried.

When physical work must outlive its last operation wrapper, it must either hold
a worker-issued idle-compatible classification or move to an epoch-work lease
before the wrapper quiesces. The managed bridge owns that disposition and lease
handoff; the worker runtime owns the classification plus epoch liveness and
release accounting. The initiating operation contributes no progress sink,
cancellation token, or publication authority after detachment.

The producer may finish and retain feature-owned epoch-local cache state before
the expected request arrives. A later request still needs an ordinary logical
operation, worker admission, managed result, and current-view publication
decision. If the worker realm is replaced, the cache and in-flight physical
work are lost.

### Bounded and unbounded worker work

A feature that claims prompt worker message service must enforce a structural
bound on how long its synchronous work can avoid returning to the worker event
loop. The worker-runtime owner consumes that declaration and applies its
liveness policy. Browser measurements can validate margin around an enforced
bound; they cannot create one.

Work without such an enforcing structure is unbounded. Moving it to a worker
still protects the DOM event loop, but it makes no prompt cancellation,
heartbeat, probe, or follow-up-request claim for the worker's own event loop.

## Semantic dimensions

| Dimension | Normative owner | Composition rule |
| --- | --- | --- |
| Logical identity and ordering | [Operation authority](inspect-web-operation-authority.md#value-contracts) | The opaque ID accompanies every adapter that addresses the logical operation. A placement owner such as the worker runtime may consume the authority-issued sequence for ordering and replay; the managed bridge receives only the ID. No downstream owner reallocates the identity or derives feature meaning from it. |
| Logical outcome | [Operation authority](inspect-web-operation-authority.md#logical-completion) | Cancellation can complete the user-visible operation before physical work settles. |
| Physical execution | Worker or browser-native producer owner | A producer's placement and execution policy do not grant publication authority. |
| DOM publication | [Operation authority](inspect-web-operation-authority.md#publication-authority) and feature owner | Only an admitted current-operation event reaches feature rendering. |
| Worker realm | [Worker runtime](inspect-web-worker-runtime.md#worker-epoch-identity-problem) | Worker-object identity and epoch correlation contain physical work and messages to one realm. They do not identify the logical operation. |
| Managed admission | [Managed bridge](inspect-web-managed-operation-bridge.md#admission) | The owner-issued operation identity becomes addressable by keyed cancellation before managed work can reach its first incomplete wait. |
| Cancellation reason | [Operation authority](inspect-web-operation-authority.md#cancellation-and-supersession) and [managed bridge](inspect-web-managed-operation-bridge.md#cancellation) | The main-thread reason is preserved as application data. Runtime exception text is not used to reconstruct it. |
| Progress | Feature, [managed bridge](inspect-web-managed-operation-bridge.md#progress), [worker runtime](inspect-web-worker-runtime.md), and operation authority | Each owner controls one hop: meaning, callback lifetime, transport validity, and current-view admission. |
| Durable nonterminal events | [Engine-to-browser async event stream](engine-browser-async-event-stream.md), feature, and operation authority; managed bridge and worker runtime after their residuals | The stream and feature own meaning and retention; operation authority owns current-view admission and producer-order publication; later transport owners must preserve the same typed payload without reclassifying it as progress. |
| Terminal classification | Feature and [managed bridge](inspect-web-managed-operation-bridge.md#settlement-and-terminal-classification) | Expected feature outcomes remain typed results. Runtime, interop, or protocol failures follow their boundary owner instead of becoming success-shaped feature data. |
| Quiescence | Operation authority, managed bridge, and worker runtime | The operation handle's quiescence follows adapter-reported release; hard realm destruction is the final release barrier for unresolved epoch resources. |
| Sharing and cache | Feature and [managed bridge](inspect-web-managed-operation-bridge.md#shared-producer-attachment) | Logical waiter lifetime does not silently become physical producer lifetime. |
| Liveness | Feature and [worker runtime](inspect-web-worker-runtime.md#post-readiness-liveness) | The feature supplies the structural event-loop-return claim; the runtime owns accounting and recovery. |
| Facade shape | [`ts-jsexport`](ts-jsexport.md#generated-module) | Generated functions and DTOs describe the managed boundary but do not define operation or worker policy. |
| Browser bootstrap | Inspect-web consumer and, if adopted, the [facade partition](inspect-web-jsexport-partitioning.md#browser-composition) | One consumer-owned readiness barrier completes before the worker runtime declares the managed adapter ready. |

## Owner-issued handoffs

This table identifies the handoff edges. The linked owner defines each value's
complete shape and validity rules.

| From | To | Owner-issued handoff |
| --- | --- | --- |
| Feature | Operation authority | Feature session, typed input, producer adapter, feature-event observer, and diagnostic observer |
| Operation authority | Producer adapter | `OperationIdentity`, normalized cancellation reason, and producer event sink |
| `ts-jsexport` and inspect-web consumer | Worker bootstrap | `initializeRuntime()`, `runEntryPoint()`, generated operation functions, DTOs, and authenticated synchronous callbacks |
| Feature adapter | Worker runtime | Operation kind, payload validator, result/progress mappings, and structural liveness declaration |
| Worker runtime | Managed bridge | Validated opaque operation ID, feature input, synchronous progress callback, cancellation request, epoch reporter, and worker-issued idle-compatible capability |
| Managed bridge | Worker runtime | Typed managed outcome, cancellation status, and epoch-work lease notifications |
| Worker runtime adapter | Operation authority | Typed progress, typed durable nonterminal event or batch, physical terminal result, unexpected diagnostic, and physical quiescence |
| Operation authority | Feature | Current-operation start, replacement, admitted progress and durable events, terminal or canceled outcome, and disposal events |

The worker protocol names and orders its own wire messages. This document uses
the semantic handoffs above so adding or renaming a protocol envelope does not
silently move ownership into the composition layer.

## Scenario composition

### Initial result followed by speculative preparation

This trace names owner boundaries, not component-internal transitions:

```text
main-thread feature
  -> operation authority: start initial inspection
  <- operation authority: handle plus logical identity

operation producer adapter
  -> worker runtime: activate owner-issued identity and feature payload
  -> managed bridge: invoke generated facade with keyed callback
  -> feature work: compute the initial result

feature-owned anticipated work, before the wrapper settles
  -> feature broker and managed bridge: transfer the outliving producer
     to an epoch-work lease
  -> worker runtime: account for the lease without inventing an operation

operation producer adapter
  <- managed bridge: typed physical outcome after callback release
  -> operation authority: terminal report; publish only if still current
  -> operation authority: quiescence report after operation-resource release

feature-owned anticipated work
  <- feature cache: retain the completed epoch-local result

later main-thread request
  -> operation authority: allocate a new logical operation
  -> ordinary worker and managed admission
  <- feature adapter: satisfy from cache if feature policy permits
  <- operation authority: publish only under the new operation's authority
```

The initial result does not need to wait for anticipated follow-up work. The
anticipated work does not borrow the first operation's publication authority,
and the later cache hit does not bypass ordinary operation admission.

### Supersession while managed work is still running

```text
operation A is current and physically running in the worker

main thread starts B
  -> operation authority: A becomes logically canceled("superseded")
  -> operation authority: B becomes current
  -> adapter: forward A's cancellation reason once

worker event loop is temporarily held by synchronous managed CPU work
  -> A cannot yet observe the queued cancellation request
  -> that managed CPU work does not occupy the DOM event loop

A later reports progress, success, failure, or cleanup
  -> operation authority: stale feature publication is suppressed
  -> diagnostics remain visible through their owner-issued path
  -> A.quiesced resolves only after physical release

B settles
  -> operation authority: B alone may publish into the current view
```

Hard recovery is different: planned restart or worker failure releases the
entire epoch and loses all its operations and cache. It is not an
operation-scoped interrupt.

### Neighboring browser-native producer

A browser `fetch` adapter uses the same operation-authority contract without a
worker or managed bridge:

```text
feature -> operation authority -> fetch adapter
fetch terminal/release -> operation authority -> current feature view
```

This neighboring case keeps logical authority independent of execution
placement. The worker path adds CPU isolation, protocol, realm lifetime, and
managed interop; it does not replace the shared logical contract.

## Browser, .NET, and Rust semantics

The portable architecture is the explicit application protocol:

```text
identity -> start -> optional progress -> cancellation request
         -> one logical outcome
physical settlement -> resource release -> quiescence acknowledgment
```

Promise, `Task`, and `Future` remain runtime-specific completion mechanisms.
They are not the cross-runtime protocol.

| Concern | Browser and TypeScript | .NET WebAssembly | Rust comparison | Composition consequence |
| --- | --- | --- | --- | --- |
| Invocation onset | Calling a JavaScript `async` function begins its body in the current execution context; an `await` continuation runs as a Promise job in the browser microtask queue. | Calling a C# task-returning async method evaluates its body until it suspends at an incomplete `await` or terminates. | Calling an `async fn` returns an inert `Future`; its body begins when an executor polls it. | Start timing cannot be inferred from the return type. Each adapter has an explicit start/admission boundary. |
| Completion carrier | Promise fulfillment or rejection | `Task<T>` is projected to `Promise<T>` by .NET JavaScript interop and the generated facade | `Future::Output` becomes ready through polling | Completion typing does not grant publication authority or prove physical release. |
| UI isolation | Promise and microtask scheduling remain on the current browser agent. A dedicated Worker has its own execution context and cannot directly manipulate the DOM. | `Task` does not move work to another browser agent. Hosting the single-threaded runtime in a dedicated Worker supplies DOM-event-loop isolation. | The language-level `Future` abstraction does not choose a thread or browser worker; the executor and host do. | Placement is explicit and owned separately from completion projection. |
| Cancellation | `AbortSignal` records one abort reason and runs registered abort steps; each operation decides how to react. | `CancellationToken` cancellation is cooperative; the application separately preserves the normalized reason. | Dropping a future stops that future's polling, but not independently spawned work; reason-bearing or cooperative cancellation requires an application or runtime convention. | Cancellation is a typed request plus an owner-defined physical response, not a universal interrupt ABI. |
| Progress | Promise has no intrinsic progress channel. | `Task` has no intrinsic progress channel; inspect-web uses an authenticated synchronous callback. | `Future::poll` reports pending or ready, not feature progress. | Progress payload, transport, admission, and rendering remain explicit owner handoffs. |
| Quiescence | Promise settlement does not prove event listeners, callbacks, or producer resources are released. | Task settlement does not by itself prove bridge callbacks, shared producers, or worker resources are released. | Future readiness or drop does not define an inspect-web resource-release acknowledgment. | Every operation exposes a separate application-level quiescence signal; worker destruction is the epoch release barrier. |

The JavaScript execution model and browser event-loop processing rules are
defined by
[ECMAScript async functions](https://tc39.es/ecma262/#sec-async-functions-abstract-operations-async-function-start),
[`Await`](https://tc39.es/ecma262/#await), and the
[HTML event-loop processing model](https://html.spec.whatwg.org/multipage/webappapis.html#event-loop-processing-model).
The HTML worker specification defines the
[worker event loop](https://html.spec.whatwg.org/multipage/workers.html#worker-event-loop)
and
[`DedicatedWorkerGlobalScope`](https://html.spec.whatwg.org/multipage/workers.html#dedicated-workers-and-the-dedicatedworkerglobalscope-interface).
The DOM specification defines
[`AbortSignal`](https://dom.spec.whatwg.org/#abortsignal) and its
[abort algorithm](https://dom.spec.whatwg.org/#abortsignal-signal-abort).

The C# specification describes
[task-returning async evaluation](https://github.com/dotnet/csharpstandard/blob/draft-v9/standard/classes.md#15143-evaluation-of-a-task-returning-async-function),
Microsoft documents
[cooperative managed cancellation](https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads),
and the .NET WebAssembly interop documentation records
[`Task` and Promise marshalling](https://learn.microsoft.com/aspnet/core/client-side/dotnet-interop/).

Rust documents that
[`Future` values are inert until polled](https://doc.rust-lang.org/std/future/trait.Future.html#runtime-characteristics)
and describes
[drop and cooperative cancellation](https://rust-lang.github.io/async-book/part-reference/cancellation.html).
These sources justify the semantic comparison; they do not transfer ownership
of inspect-web's application protocol to any language runtime.

## Evidence and gate ownership

The composition has no aggregate runtime gate of its own. A scenario claim is
established only by the gates belonging to every participating owner. One
owner's evidence cannot stand in for another owner's behavior.

| Claimed behavior | Owner and gate | Current status |
| --- | --- | --- |
| One logical outcome, current-view publication, ordered durable publication, cancellation, stale-event suppression, and quiescence | [Operation-authority model and `inspect-web-operation-authority` Release TypeScript gate](inspect-web-operation-authority.md#required-implementation-gate) | Abstract model checked; product component, durable-publication gate, and first Type Source adoption implemented |
| Worker message validity, epoch containment, readiness, liveness, draining, and realm release | [Worker-runtime models and `inspect-web-worker-protocol` plus `inspect-web-worker-lifecycle` Release gates](inspect-web-worker-runtime.md#required-implementation-gates) | Abstract models checked; product components and gates **unverified** |
| DOM responsiveness while representative managed CPU work runs | Worker-runtime owner and [`inspect-web-worker-responsiveness` real-browser gate](inspect-web-worker-runtime.md#required-implementation-gates) | **Unverified** |
| Keyed managed cancellation, exact reason, callback release, managed quiescence, shared-waiter detachment, and epoch-work handoff | [Managed-bridge model and `inspect-web-managed-operation-bridge` Release browser-host gate](inspect-web-managed-operation-bridge.md#required-implementation-gate) | Abstract model checked; product component and gate **unverified** |
| Generated Task/Promise functions, authenticated callback types, initialization, and managed dispatch | [`ts-jsexport` acceptance gates](ts-jsexport.md#acceptance) and the implemented inspect-web consumer gates from #5003 and #5005 | Implemented for the current production facade |
| Complete multi-facade module set over one runtime | [Facade-partition acceptance gates](inspect-web-jsexport-partitioning.md#acceptance) | Proposed under [#4497](https://github.com/richlander/dotnet-inspect/issues/4497); not a prerequisite for the current monolithic facade |
| Feature result, progress meaning, cancellation checkpoints, structural yield bound, sharing, cache, retry, and rendering | The adopting feature's focused Release and browser gates | Unverified until each feature adopts the composed boundary |

The official .NET worker template and runtime work provide implementation
direction, not proofs of this contract. The worker-runtime owner records that
[runtime evidence](inspect-web-worker-runtime.md#runtime-evidence) and owns the
browser gates needed to turn it into a product claim.

## Dependency and migration map

Migration remains focused by owner:

1. **Generated boundary prerequisites -- complete.**
   [#5003](https://github.com/richlander/dotnet-inspect/issues/5003) adopted
   the generated production facade, and
   [#5005](https://github.com/richlander/dotnet-inspect/issues/5005) added
   authenticated synchronous delegate support.
2. **Logical authority.**
   [#5092](https://github.com/richlander/dotnet-inspect/issues/5092)
   implements operation authority and adopts it in one existing source view
   without changing execution placement or feature behavior.
   [#5570](https://github.com/richlander/dotnet-inspect/issues/5570) extends
   that owner with typed durable nonterminal publication for stream adopters.
3. **Worker runtime.**
   [#5418](https://github.com/richlander/dotnet-inspect/issues/5418)
   realizes the [worker owner](inspect-web-worker-runtime.md#migration) and
   its focused protocol, lifecycle, and responsiveness gates.
4. **Managed bridge.**
   [#5419](https://github.com/richlander/dotnet-inspect/issues/5419)
   realizes the
   [managed owner](inspect-web-managed-operation-bridge.md#migration) and its
   focused browser-host gate. It may proceed independently of #5418 against
   the locked typed handoff; neither issue absorbs the other's internals.
5. **One composed feature.**
   [#5420](https://github.com/richlander/dotnet-inspect/issues/5420)
   connects the settled owners for one long-running source operation. It
   proves the user-visible responsive, progress, cancellation, supersession,
   and release scenario without moving feature semantics into shared
   infrastructure.
6. **Shared or speculative work.**
   Later feature-owned adoption issues add sharing, epoch-work leases, and
   anticipated cache preparation only where a concrete scenario justifies
   them.

The worker-runtime, managed-bridge, and composed-feature implementation issues
must be tracked separately. This document does not prescribe their internal
implementation plans or combine them into one broad change.

## Mock demo

The docs-only demo is one initial package inspection followed by a likely
source request:

```text
1. The page paints its shell and starts package inspection immediately.
2. The .NET work runs in the dedicated worker; input and animation remain live.
3. Package progress appears only while that request owns the package view.
4. The package result publishes.
5. Feature-owned source preparation continues under an epoch-work lease and
   stores its result in the worker-local cache.
6. The user opens Source; a new logical operation is created and receives the
   cached result through the ordinary worker and managed boundary.
7. The user immediately selects another member; the old Source operation is
   logically canceled, cannot overwrite the new view, and quiesces separately.
```

What to notice:

- the initial result is not delayed by speculative follow-up work;
- Promise and `Task` still carry completion;
- worker placement keeps managed CPU work off the DOM event loop;
- publication authority remains on the main thread;
- cancellation does not pretend to be a synchronous physical interrupt; and
- cached work accelerates a later request without inventing that request.

The neighboring browser-native `fetch` demo uses the same operation authority
for package search. It proves the logical contract is reusable without the
worker, while only the worker demo proves managed CPU isolation.

## Non-claims

This composition does not claim:

- that Promise, `Task`, `Future`, `await`, a microtask, or progress delivery
  yields a DOM paint;
- that asynchronous completion moves CPU work off the current event loop;
- prompt cooperative cancellation or a maximum checkpoint latency;
- operation-scoped hard interruption of managed work;
- DOM authority in a worker or managed callback;
- that progress is liveness evidence or publication authority;
- that terminal completion proves physical quiescence;
- survival of worker-local cache or physical work across realm replacement;
- one shared retry, queue, cache, timeout, progress, or rendering policy;
- a universal Promise/Task/Future foreign-function ABI;
- the proposed production facade partition;
- implementation behavior not enforced by its owning Release or browser gate;
  or
- ownership of any participant's internal lifecycle.
