# Inspect-web managed operation bridge

## Status

This document defines the target managed boundary for long-running inspect-web
operations. It is the normative owner for issue
[#5094](https://github.com/richlander/dotnet-inspect/issues/5094).

The dynamic operation lifecycle through quiescent release is implemented by
`BrowserManagedOperationBridge` and gated by
`BrowserManagedOperationBridgeTests` in the Release browser-engine suite. Its
abstract lifecycle is checked by the companion
[managed operation bridge model](models/inspect-web-managed-operation-bridge/README.md).
The generated `[JSExport]` Browser/Wasm canary also gates authenticated
nonterminal callback shape, ordered progress and durable event delivery,
terminal-after-events settlement, callback-failure rejection, and callback
closure after release. Shared-producer attachment, epoch-work leases, and the
remaining complete browser-host gate cases named below remain required before
an implementation may claim those corresponding behaviors.

## Decision

Inspect-web uses a dynamic `ActiveOperationTable` at the managed browser-host
boundary. Each worker-invoked long-running export registers one operation ID,
one `CancellationTokenSource`, one immutable first cancellation reason, and
one scoped nonterminal event callback before the exported method can reach its
first incomplete wait.

The table is not a static catalog of operations. Its container may be held by
static `[JSExport]` entry points, but an entry exists only from synchronous
admission until that invocation closes its callback and removes its resources.

The bridge:

- receives an owner-issued opaque operation ID and a feature-specific
  synchronous nonterminal event callback from worker TypeScript;
- provides the feature operation with a cancellation token and a scoped
  event sink;
- accepts cancellation by operation ID and preserves the first normalized
  reason separately from `CancellationToken`;
- fulfills ordinary operation calls with a closed succeeded, failed, or
  canceled envelope;
- releases operation-scoped callbacks and table state before the returned
  `Task` settles; and
- lets a logical waiter detach from shared physical work without assigning that
  producer to the waiter's cancellation token or callback.

Physical work that outlives every operation wrapper uses a separate
epoch-scoped work lease. This owner allocates the lease's non-reused monotonic
work sequence and brackets the managed producer. The worker runtime owner in
[#5093](https://github.com/richlander/dotnet-inspect/issues/5093) consumes those
notifications and owns epoch liveness, validation response, restart, and hard
termination.

## User scenarios

### Responsive long-running inspection

A package query, source acquisition, call graph, or other expensive inspection
runs in the long-lived .NET WebAssembly worker. Moving the runtime keeps the
DOM event loop free even while managed CPU work is synchronous. The bridge
keeps the operation addressable while managed work is admitted or running and
returns a typed physical result when the wrapper settles.

The worker placement, not `Task` or `Promise`, provides DOM responsiveness.
This bridge makes no claim that a managed await yields to the DOM thread or
that CPU-only work processes worker messages promptly.

### Nonterminal event reporting

A feature export accepts a bounded synchronous `Action` callback supported by
the authenticated `ts-jsexport` facade. Managed code reports the
feature-owned nonterminal union through one scoped event sink. That union can
contain advisory progress plus durable Item and ItemFailure variants; the
semantic distinction remains owned by the
[engine-to-browser async event stream](engine-browser-async-event-stream.md).
The callback runs worker JavaScript, validates its primitive fields, and
immediately calls `postMessage`; it never performs DOM work.

The main thread applies its own current-operation authority before rendering.
The managed bridge guarantees only operation-scoped callback lifetime and
release plus producer-order callback invocation. It does not coalesce progress,
discard durable events, batch messages, or grant DOM publication authority.

### User cancellation and supersession

The main-thread operation owner first completes the logical operation with a
normalized reason such as `user` or `superseded`. Worker TypeScript forwards
that reason to the managed cancellation export when the operation has already
entered managed code.

The bridge locates the exact active entry, records the first reason before
calling `CancellationTokenSource.Cancel()`, and signals the token once.
`OperationCanceledException` text is never used to reconstruct the reason.

Cancellation remains cooperative. An I/O wait can observe the token at an
incomplete await. CPU work observes it only at feature-owned checkpoints, and
the worker cannot receive the cancellation message while managed code
monopolizes its event loop. No prompt-cancellation claim exists without the
browser latency gate required by the relevant feature owner.

### Shared physical work

Two operations can await one package acquisition or another feature-owned
producer. Canceling one operation stops that waiter, removes its event
subscription, and lets its wrapper return `Canceled` without canceling the
producer while another waiter remains.

If the producer continues after its final operation wrapper can quiesce, the
broker either proves it remains compatible with the worker's idle heartbeat or
acquires an epoch-work lease before acknowledging the final detach. The lease
ends from the producer's `finally`.

### Longer browser work

The worker can retain package and inspection caches across operations. A
single operation can logically finish or cancel independently of its physical
producer and independently of other operations in the worker. The dynamic
table and lease handoffs prevent that longer lifetime from becoming a
page-wide singleton cancellation slot.

## Ownership

This document owns:

- synchronous managed admission keyed by worker-issued operation ID;
- dynamic active-entry identity and duplicate-active rejection;
- `CancellationTokenSource` lifetime;
- first-reason storage and cancellation-result classification;
- operation-scoped nonterminal event sink and callback lifetime;
- managed result-envelope classification;
- settlement, callback close, table removal, and managed quiescence ordering;
- shared-producer waiter attachment and detachment at the managed bridge;
- epoch-work sequence allocation and managed producer lease bracketing; and
- the typed handoffs those responsibilities expose to worker TypeScript.

It consumes:

- page-wide opaque operation IDs and normalized reasons from
  [inspect-web operation authority](inspect-web-operation-authority.md);
- authenticated synchronous callback and result types from
  [`ts-jsexport`](ts-jsexport.md);
- progress, durable Item and ItemFailure meaning, and terminal ordering from
  [engine-to-browser async event streams](engine-browser-async-event-stream.md);
- worker placement and the epoch-work allowance type from #5093;
- concrete work, error payloads, progress phases, cancellation checkpoints,
  producer sharing keys, and last-waiter policy from feature owners; and
- package acquisition and cache semantics from
  [browser package sources](browser-package-sources.md).

It does not own:

- DOM state, feature-view authority, stale-result suppression, or rendering;
- operation-ID allocation, reuse policy, or page-sequence semantics;
- queued starts before managed invocation;
- worker creation, epochs, readiness, message inventory, watchdogs, replay
  response, restart, realm destruction, or hard cancellation;
- generic `[JSExport]` discovery, facade generation, runtime bootstrap, or
  TypeScript compilation;
- feature-specific event unions, progress coalescing, batching, or payload
  budgets;
- feature cancellation checkpoints, timeout duration, retry, cache, or
  producer-sharing policy; or
- package-source identity, transport, reservation, or publication semantics.

The async composition document in #5095 will connect these owners without
restating their internal lifecycles.

## Boundary shapes

The shapes below describe the owned semantics. Each `[JSExport]` wrapper uses
concrete source-generated DTOs and feature-specific callback parameters rather
than exporting these generic sketches directly.

```csharp
public enum ManagedOperationCancelReason
{
    User,
    Superseded,
    Disposed,
    FeatureObserverFailed,
    Timeout,
    WorkerRestarted,
}

public enum ManagedOperationFailureKind
{
    Expected,
    Unexpected,
}

public interface ManagedOperationEvents<in TEvent>
{
    bool IsClosed { get; }

    void Report(TEvent operationEvent);
}

public abstract record ManagedOperationResult<TValue, TError, TDiagnostic>
{
    public sealed record Succeeded(TValue Value)
        : ManagedOperationResult<TValue, TError, TDiagnostic>;

    public sealed record Failed(
        ManagedOperationFailureKind FailureKind,
        TError Error,
        TDiagnostic Diagnostic)
        : ManagedOperationResult<TValue, TError, TDiagnostic>;

    public sealed record Canceled(ManagedOperationCancelReason Reason)
        : ManagedOperationResult<TValue, TError, TDiagnostic>;
}

public abstract record ManagedCancellationRequestResult
{
    public sealed record Requested(ManagedOperationCancelReason Reason)
        : ManagedCancellationRequestResult;

    public sealed record AlreadyRequested(ManagedOperationCancelReason Reason)
        : ManagedCancellationRequestResult;

    public sealed record NotActive : ManagedCancellationRequestResult;
}
```

`IsClosed` means no later report can reach the JavaScript callback. It is also
true when an active operation was admitted without a callback or after a
callback failure; it does not mean the feature body or operation has settled
and must not control whether feature work is performed.

The wire envelope is closed and versioned. `Succeeded` always carries its
value, `Failed` always carries its feature-owned safe error and diagnostic
payloads, and `Canceled` always carries the exact stored reason. A discriminator
without the required payload is malformed.

Expected feature failure, unexpected producer failure, and cancellation fulfill
the managed `Task`, and therefore the JavaScript `Promise`, with one of these
variants. An expected failure reaches the operation authority's terminal path.
An unexpected failure also requires the worker adapter to report its diagnostic
through `reportUnexpectedFailure`, so logical cancellation or stale terminal
suppression cannot hide it.

Promise rejection is reserved for runtime, interop, serialization, or
bridge-contract failure, including an invalid boundary value, duplicate active
ID, or throwing nonterminal event callback. The worker runtime owner narrows
an unknown rejection into its boundary-failure path; it never infers expected
cancellation from error text.

`ManagedCancellationRequestResult.Requested` means the reason was installed
and token signaling was attempted. `AlreadyRequested` returns the immutable
first reason and does not signal again. `NotActive` means no cancellable entry
was active at the request's linearization point. It can represent completion,
settlement, absence, or an ordinary cancellation race; it does not prove which
side won before the worker observes the result.

Worker TypeScript may map both active results to its `running`
acknowledgment. The exact worker message inventory remains owned by #5093.

## Dynamic active-operation lifecycle

### Entry contents

One admitted entry contains at least:

```text
operation ID
entry instance identity
active / settling state
CancellationTokenSource
first normalized cancellation reason, initially absent
token-callback failure, initially absent
operation-scoped event gate and callback reference
in-flight bridge-callout count
zero or one shared-producer subscription
terminal classification state
```

Entry instance identity prevents an old wrapper's `finally` from removing a
new entry even if a boundary defect presents the same opaque ID later. The
table retains no completed-ID tombstones. The page owner prevents legitimate
ID reuse, and #5093 owns protocol replay checks.

### Admission

Admission is a synchronous, non-awaiting transition:

1. validate the required boundary values without parsing meaning from the
   opaque operation ID;
2. construct the cancellation source and closed event gate;
3. atomically add the complete entry if the ID is not already active;
4. expose the entry's token and scoped event sink to the feature adapter; and
5. invoke the feature body.

The add completes before the feature body can reach an incomplete wait or make
work externally cancellable. Validation that requires asynchronous work occurs
inside the admitted operation rather than leaving an unaddressable interval.

Failure before insertion owns and releases its temporary resources and invokes
no feature body. A duplicate concurrently active ID installs no second entry
and is a bridge-contract failure. A later legitimate operation does not rely
on the bridge retaining an unbounded set of completed IDs.

This ordering is checked abstractly by
`RegistrationPrecedesManagedWork` and `OneActiveEntryPerId` in the companion
model. `BrowserManagedOperationBridgeTests` and the compiled Browser/Wasm
canary gate the concrete admission ordering for the implemented lifecycle.

### Cancellation

Cancellation linearizes under the entry's state guard:

1. look up the operation ID;
2. require the entry to remain active rather than settling;
3. if no reason exists, store the normalized reason and mark token signaling
   claimed;
4. acquire one entry-scoped callout lease;
5. leave the table guard;
6. call `CancellationTokenSource.Cancel()` once;
7. record any token-callback failure on the entry; and
8. release the callout lease.

Reason storage precedes the callout because cancellation-token callbacks run
synchronously and may observe or reenter the bridge. The table is not held
across that callout. Failure recording precedes callout-lease release, so
settlement cannot classify the result before observing the failure.

A later request while the entry remains active returns `AlreadyRequested` with
the original reason. It cannot overwrite the reason or call `Cancel()` again.
A request after settlement begins returns `NotActive` even if `finally` has not
yet removed the entry.

If a managed cancellation callback throws, the reason and token-signal attempt
remain committed. The bridge records that producer-side failure on the entry,
returns the applied cancellation status, and classifies the operation as
failed when its wrapper settles. It does not roll back the reason, retry token
signaling, or let callback failure make the cancellation export appear not
applied.

The companion model checks `FirstCancellationReasonWins`,
`CancellationSignalsAtMostOnce`, and
`SettlingOperationRejectsCancellation`. It also requires settlement to drain
the cancellation callout before classification and release. Concrete reentrancy
and throwing-token callback behavior remain unverified until the required
Release gate exists.

### Nonterminal events

The bridge gives feature code a scoped managed event sink rather than the raw
JavaScript delegate. The sink owns the only path to that delegate. Its
`TEvent` is the feature-owned complete nonterminal union; the bridge does not
reinterpret a durable Item or ItemFailure as progress.

Each report:

1. validates the feature-owned bounded payload before crossing interop;
2. enters the operation's event gate and acquires one entry-scoped callout
   lease only while the entry is active and the callback lease is open;
3. invokes the synchronous callback without holding the active-table guard;
4. records callback failure as a bridge-contract failure;
5. leaves the event gate; and
6. releases the callout lease.

Settlement seals the gate against new callouts, then asynchronously waits for
every existing cancellation and event callout lease to drain. It never
blocks a synchronous reentrant callback stack. Closing the gate then drops the
callback reference. Reports racing with or following the seal do not invoke
JavaScript. Feature code must not report through a retained sink beyond the
operation or call another managed export from the callback.

The callback observes every event admitted while the gate is open
synchronously in producer order. Among admitted events, the bridge neither
coalesces progress nor drops or batches durable events. Reports racing with or
following the seal are not admitted and cannot invoke JavaScript; an adapter
must finish its legitimate nonterminal handoff before returning the terminal
body result. The adapter retains semantic `Completed` and returns it through
the terminal result envelope; it never reports `Completed` through this
callback.

The callback performs bounded validation and `postMessage` in worker
JavaScript. It returns `undefined`, never a Promise, and never accesses the DOM.
The main-thread owner independently rejects events from operations that no
longer hold publication authority.

A callback exception closes further events, requests cooperative operation
cancellation, and is rethrown by the outer bridge only after its `finally`
releases the entry. It is a Promise-rejecting boundary failure, not a
feature-owned `Failed` envelope.

That cooperative request uses the same reason-and-signal claim as keyed
cancellation. When no earlier reason exists, it stores
`FeatureObserverFailed` before signaling the token. A concurrent or reentrant
keyed request therefore observes `AlreadyRequested(FeatureObserverFailed)`
rather than signaling the token again. An earlier accepted reason remains
unchanged and its signal is not repeated.

The bridge still classifies the feature body's observation once after callout
drain so failure precedence and diagnostics remain deterministic. That
classification is inert bookkeeping when an event boundary failure exists: it
does not replace the rejection, and the worker must not publish it as the
managed terminal result. #5093 maps the rejection to its boundary-failure path.

`NoCallbackAfterClose` in the companion model checks the abstract release
invariant. Authenticated callback shape is already gated by `ts-jsexport`;
the compiled Browser/Wasm canary gates same-operation ordered nonterminal
routing, callback rejection, and no callback after return. In-flight close
remains gated by `BrowserManagedOperationBridgeTests`.

### Settlement and terminal classification

Settlement begins with one atomic transition from active to settling. That
transition closes cancellation and event admission. The wrapper then awaits
the entry's callout drain. Every callout records its failure before releasing
its lease, so only the post-drain state supplies the first reason and failures
used for classification.

The wrapper classifies its physical result by the first matching row:

| Observation at settlement | Recorded bridge failure | Stored reason | Bridge result |
| --- | --- | --- | --- |
| Nonterminal event callback failure | present | either | Promise rejection after release |
| Token-callback failure | present | either | `Failed(Unexpected, error, diagnostic)` |
| Unexpected feature failure | absent | either | `Failed(Unexpected, error, diagnostic)` |
| Expected feature failure | absent | either | `Failed(Expected, error, diagnostic)` |
| Value returned | absent | present | `Canceled(reason)` |
| `OperationCanceledException` after the operation token was signaled | absent | present | `Canceled(reason)` |
| `OperationCanceledException` | absent | absent | `Failed(Unexpected, error, diagnostic)` |
| Value returned | absent | absent | `Succeeded(value)` |

An accepted cancellation therefore wins over an ordinary late value and
preserves its exact reason. An unexpected failure is never hidden as expected
cancellation merely because cancellation was also requested. Feature adapters
return expected failures explicitly; an escaping exception is unexpected.
A cancellation exception is matched by the accepted reason and the operation
token's signaled state, not by comparing
`OperationCanceledException.CancellationToken`: linked feature tokens may
surface a different token identity after the bridge's operation token is
signaled. Without an accepted reason and a signaled operation token, a
`TaskCanceledException` or `OperationCanceledException` remains unexpected.
A recorded token-callback failure outranks an otherwise expected feature
failure and forces `Failed(Unexpected, ...)`. Feature adapters own the safe
error and diagnostic projection; the bridge owns the failure class, closed
variant, and ordered precedence above.

The first settlement transition owns the only feature-body classification.
Duplicate classification, a second result envelope, or cancellation accepted
after that transition is a bridge-contract failure. A recorded boundary failure
can suppress that classification from the return channel as defined above; it
does not cause a second classification.

For `Failed(Unexpected, ...)`, the worker adapter reports the diagnostic before
or with the physical terminal report. For `Failed(Expected, ...)`, it reports
only the physical terminal result. #5093 owns the worker call ordering; this
owner supplies the discriminator that prevents stale unexpected failures from
becoming silent.

The companion model checks `OneTerminalClassification` and
`CancellationReasonIsFaithful`. Concrete exception and envelope projection
remain unverified until the required Release gate exists.

### Release and quiescence

Every admitted wrapper executes one release sequence in `finally`:

1. seal the entry against new cancellation and event callouts;
2. asynchronously drain every existing callout lease;
3. close the event gate and drop the JavaScript callback reference;
4. detach any shared-producer subscription through the final-detach handoff;
5. remove the exact entry instance from `ActiveOperationTable`;
6. dispose the operation `CancellationTokenSource`; and
7. allow the exported `Task` to settle with its result or boundary rejection.

Locally owned cleanup is failure-complete. Each step runs under nested
`try`/`finally` or an equivalent explicit failure accumulator, so a detach,
exact-removal, or disposal failure cannot skip later release. An existing
boundary failure remains primary. Otherwise the first cleanup failure becomes
the primary boundary rejection. Additional cleanup failures are retained in
one diagnostic rather than swallowed or replacing the primary failure.

The worker can report managed operation quiescence only after observing that
settlement. The returned `Task` is therefore the managed release barrier:
after it settles, no bridge callout is in flight, the operation ID is not
active, the operation-scoped callback cannot run, and every locally owned
operation resource has received its release attempt.

For an ordinary owned producer, feature work has also settled before this
barrier. For shared physical work, the operation's subscription and callback
have settled, while the producer may remain represented by an epoch-work
lease or a broker-owned epoch-fault record. Operation quiescence never claims
that unrelated or shared physical work has stopped.

`CalloutsDrainBeforeClassification`, `CallbackClosesBeforeRemoval`, and
`QuiescenceRequiresRelease` in the companion model check the abstract ordering.
Concrete resource disposal, cleanup-failure precedence, and Task-to-Promise
ordering remain unverified until the Release gate exists.

## Shared producer attachment

Feature owners decide whether requests share one producer, how its key is
formed, what it caches, and whether last-waiter cancellation is legal. The
bridge consumes a feature-owned broker through a narrow attachment contract:

```text
Attach(operation, scoped event sink) -> waiter subscription
Await(subscription, operation token) -> feature result
Detach(subscription) -> producer disposition
```

The disposition is one of:

- another waiter remains;
- the producer is terminal;
- the producer continues under a #5093-issued idle-compatible classification;
- the producer continues under an already-active epoch-work lease; or
- the producer was transferred to an epoch-fault record because required lease
  acquisition failed.

The operation token cancels only `Await` and the waiter subscription. It is not
the shared producer token. A producer may be canceled after the last waiter
leaves only when its feature-owned policy permits that transition. Existing
`BrowserPackageWorkspace.WaitForSharedAcquisitionAsync` behavior is migration
evidence: `Task.WaitAsync(cancellationToken)` releases a canceled waiter while
the shared acquisition task continues.

Detachment closes operation events before the broker can acknowledge release
of the subscription. A broker never retains the raw JavaScript callback. It
may retain only the scoped sink, which stops all JavaScript invocation when
the bridge closes it.

Final detachment is a two-phase transition under the broker's producer guard:

1. classify the producer using a #5093-issued liveness requirement;
2. if a lease is required, ask the bridge to allocate and start it;
3. install the resulting lease handle into that exact producer's terminal path;
4. only then remove the final waiter; and
5. return the committed disposition to the operation wrapper.

A start-callback failure does not leave an unleased producer disguised as
ordinary detached work. The broker atomically transfers it to a broker-owned
epoch-fault record containing no operation callback, token, or identity,
requests producer stop when feature policy permits, removes the waiter, and
returns the boundary failure. #5093 must fail and drain the epoch after
observing that rejection. Producer finalization releases the fault record.

The operation wrapper cannot cross its local quiescence barrier until final
waiter removal or fault-record transfer commits. The exact producer that
receives the installed lease owns its disposal from `finally`.

The companion model checks `OneWaiterDoesNotStopSharedProducer` and
`OutlivingProducerHasEpochWorkLease`. Concrete broker keys, network behavior,
cache publication, and last-waiter policy remain gated by their feature
owners.

## Epoch-work lease handoff

Worker initialization registers one synchronous epoch reporter with the
managed host after the generated facade is ready. Registration supplies
`started(workSequence, allowance)` and `finished(workSequence)` callbacks.
The allowance and idle-compatible classification are opaque typed values owned
and validated by #5093.

This bridge owns the sender-side work identity:

- sequences begin at one for the reporter registration;
- every successful allocation is greater than every prior allocation;
- a sequence is never reused, including after work finishes;
- allocation stops visibly before exceeding JavaScript's maximum safe integer;
- exhaustion never wraps, resets, or silently omits a required lease; and
- one lease handle can report `finished` exactly once.

Lease acquisition calls `started` before returning a handle to the broker. The
broker installs that handle into the outliving producer before its final
related operation can quiesce. The producer's `finally` disposes the handle,
which calls `finished`. A start callback failure prevents the lease from
becoming active and enters the final-detach fault path above. A finish callback
failure remains a visible epoch boundary failure; the managed handle still
becomes terminal and cannot retry with the same or a new identity.

Normal reporter unregister is legal only after admission has stopped and every
active work lease has finished. Hard worker termination destroys the entire
realm and is the separate release boundary owned by #5093.

Monotonic identity permits bounded receiver validation: the worker needs one
highest-started sequence plus its currently active lease set. It can reject a
non-increasing start and an unmatched or duplicate finish without retaining
all completed IDs. #5093 owns those checks and the resulting epoch transition;
this owner guarantees the sender facts they consume.

The companion model checks `WorkSequenceNeverReused`,
`WorkSequenceExhaustionIsVisible`, `WorkLeaseFinishesAtMostOnce`, and
`OutlivingProducerHasEpochWorkLease`. The #5093 protocol gate must check
bounded receiver replay detection and duplicate or unmatched notification
handling.

## Target boundary sequence

This mockup begins only after #5093 has validated and admitted a worker `Start`.
It demonstrates the managed handoff rather than defining the operation-authority
adapter or worker message protocol. The generated facade has concrete
feature-specific parameter and result types. The surrounding #5093 adapter,
including its required rejection path, is intentionally omitted.

```ts
const result = await facade.inspectPackage(
  start.operationId,
  start.packageId,
  start.version,
  (kind, payload) => {
    forwardManagedEvent(start.operationId, { kind, payload });
    return undefined;
  },
);

if (result.kind === "failed" && result.failureKind === "unexpected") {
  reportManagedDiagnostic(start.operationId, result.diagnostic);
}

reportManagedTerminal(start.operationId, result);
```

After managed invocation begins, a separate admitted cancellation path calls:

```ts
const status = await facade.requestOperationCancellation(
  cancellation.operationId,
  cancellation.reason,
);
```

Issue #5093 owns cancellation queued before invocation, worker message
ordering, boundary rejection, and the point at which managed Task settlement
becomes its terminal and quiescence reports.

The cross-owner observable sequence for a canceled shared waiter is:

```text
main thread            worker TypeScript         managed bridge
-----------            -----------------         --------------
start op-41        ->  invoke facade         ->  register op-41
                                             ->  attach waiter A
                    <- event callback        <-  progress/item/item failure
event op-41        <-
cancel(user)       ->  cancel op-41          ->  store "user", signal token
                                             ->  detach waiter A
                                             ->  producer continues for B
                    <- Canceled("user")       <-  close callback, remove op-41
terminal/quiesced  <-
```

The producer remains feature-owned. If waiter B later detaches while the
producer continues with a liveness allowance, `EpochWorkStarted` precedes B's
quiescence and `EpochWorkFinished` comes from producer finalization.

## Migration

Current inspect-web source and package-query coordinators serialize one current
operation behind a static slot and expose parameterless `CancelCurrent()`.
Those coordinators are evidence for token propagation and cooperative
cancellation, not the target identity contract.

Implementation proceeds in independently coherent slices:

1. introduce the dynamic lifecycle core and
   `BrowserManagedOperationBridgeTests` Release sub-gate without changing
   feature event semantics;
2. add the generated `[JSExport]` Browser/Wasm canary for the complete
   nonterminal union and callback lifetime;
3. adapt one current export to pass an operation ID, concrete result envelope,
   and authenticated synchronous nonterminal event callback;
4. replace parameterless cancellation with keyed cancellation for that export;
5. migrate shared acquisition waits through broker subscriptions and
   epoch-work leases where needed; and
6. remove singleton coordinators only after every caller uses keyed operation
   identity.

The migration must not thread browser operation IDs into host-neutral
inspection models. IDs terminate at the browser host adapter.

### Source-host adoption and retirement

The first production consumer is the existing Type Source view, tracked by
[#5419](https://github.com/richlander/dotnet-inspect/issues/5419). Its
operation-authority adapter passes the page-owned ID and normalized cancellation
reason through the generated source facade. The managed wrapper participates
in the existing aggregate source-acquisition budget; keyed admission does not
authorize concurrent source acquisition. Its scope and budget leases are
released before the managed terminal result settles.

Type Source has no nonterminal payload in this slice. It uses the bridge's
existing admission-without-callback contract rather than manufacturing progress
events. Its concrete result and cancellation-status DTOs use supported
source-generated JSON contracts; native C# JSON union projection is not a
prerequisite. Source rendering, provenance, and acquisition policy remain
feature-owned.

`BrowserTypeSourceOperationTests` gates keyed cancellation, shared-budget
accounting, result classification, and lease release in the Release engine
suite. `test/type-source-managed-operation.test.ts` gates browser-adapter
publication, late-failure diagnostics, cancellation, and quiescence;
`scripts/verify-engine-facade-runtime.ts` gates the generated DTO projection
and argument forwarding.

Source singleton retirement has **two ordered adoption slices**:

1. migrate the Type Source export and its production caller together to keyed
   admission, cancellation, and terminal results;
2. migrate the remaining member-source and graph-source callers and their
   exports to the same operation-keyed boundary, then remove parameterless
   source cancellation and the singleton cancellation slot in that slice,
   retaining the feature's aggregate acquisition-budget enforcement.

Only the first slice is included here. The legacy source coordinator remains
necessary for the other callers. Shared-producer subscriptions and epoch-work
leases remain separate bridge work, not a replacement for this source budget.
The end-to-end Worker adoption scenario is
[#5420](https://github.com/richlander/dotnet-inspect/issues/5420), composed through
[#5095](https://github.com/richlander/dotnet-inspect/issues/5095). This direct
facade adoption does not move work off the DOM thread or establish a
responsiveness or prompt physical-cancellation claim.

## Checked abstract model

The companion TLA+ model covers two managed operations and one shared producer.
It checks:

- registration before managed work and one active entry per ID;
- immutable first cancellation reason and at-most-once token signaling;
- cancellation exclusion after settlement begins;
- callout failure recording and drain before terminal classification;
- one terminal classification with exact canceled-reason fidelity;
- callback close before table removal and no callback after close;
- release before quiescence;
- independent shared-waiter detachment;
- an epoch-work lease before a producer outlives its final wrapper;
- monotonic non-reused work identity, visible exhaustion, and exactly one
  finish; and
- eventual release under the model's stated fairness assumptions.

Targeted mutation configurations must produce counterexamples for duplicate
active admission, reason overwrite, classification before callout drain,
callback after close, early table removal, early quiescence, first-waiter
producer cancellation, missing epoch-work handoff, duplicate work finish, and
work-sequence reuse or invisible exhaustion.

The model proves only the finite abstract transition system. It does not prove
C#, JavaScript, interop, browser scheduling, worker protocol, or feature
implementation behavior.

The model's abstract progress action represents any nonterminal callback
callout. Event-category semantics and producer order are enforced by the
concrete Release gates rather than added as another bridge state machine.

## Required implementation gate

`BrowserManagedOperationBridgeTests` is the Release managed-core sub-gate. It
covers synchronous admission, duplicate rejection, keyed first-reason
cancellation, counted non-blocking callout drain, ordered advisory and durable
nonterminal events, event callback closure, terminal precedence, exact-entry
removal, failure-complete local cleanup, and quiescent task settlement. It does
not stand in for browser interop evidence.

`inspect-web-managed-operation-bridge` is the complete Release browser-host
gate. Its compiled Browser/Wasm canary now covers authenticated callback and
result shapes, ordered Progress, Item, and ItemFailure delivery before the
terminal result, callback-failure rejection, and no invocation through a
retained sink after release. The canary runs through
`eng/test-inspect-web-multi-facade-canary.sh`. The complete gate remains open
and must combine the managed-core sub-gate and canary with:

- two concurrent feature operations with distinct IDs and keyed cancellation
  reaching only the selected token;
- duplicate active-ID rejection with no second body, token, or callback;
- synchronous registration before a body reaches its first incomplete wait;
- exact `user`, `superseded`, `disposed`, `feature-observer-failed`, `timeout`,
  and `worker-restarted` reason fidelity across cancel/settle races;
- repeated same- and different-reason cancellation preserving the first reason
  and signaling the token once;
- cancellation reentrancy and a throwing token callback after reason commit,
  including settlement racing the in-flight cancellation callout and an
  otherwise expected feature failure;
- cancellation losing to an already-started settlement and returning
  `NotActive`;
- late ordinary success after accepted cancellation becoming canceled;
- unexpected failure after accepted cancellation remaining failed;
- `OperationCanceledException` without an accepted operation reason remaining
  failed;
- concrete succeeded, failed, and canceled envelope serialization, including
  malformed required-payload negatives;
- expected terminal variants fulfilling the Promise and bridge/runtime,
  malformed-contract, serialization, and event-callback failures rejecting
  it;
- synchronous nonterminal events before cancellation and suppression during
  close, after close, after table removal, and after Task settlement;
- callback close racing an in-flight report without deadlock or a later
  JavaScript invocation, with callback failure recorded before settlement
  classification;
- no callback reference retained after settlement, including failed and
  canceled bodies;
- exact-entry removal preventing an old `finally` from removing another entry;
- one terminal classification and one managed quiescence barrier per admitted
  operation;
- injected failure at every locally owned cleanup stage, proving later cleanup
  still runs and primary/secondary failures remain visible;
- two waiters sharing one controlled producer, with either waiter canceling and
  quiescing independently while the other continues;
- last-waiter policy remaining feature-owned and no waiter token becoming the
  producer token;
- epoch-work start before final waiter quiescence when a controlled producer
  outlives its wrappers;
- atomic lease installation into the exact producer before final waiter
  removal, plus start failure transferring to a callback-free epoch-fault
  record;
- a later waiter attaching to an already leased producer and detaching without
  allocating, exhausting, or replacing that lease;
- producer-finally work finish, at-most-once finish, start/finish callback
  failure, and normal reporter unregister only after all leases finish;
- monotonic work sequences at the JavaScript safe-integer boundary, visible
  exhaustion, and no wrap or reuse;
- a neighboring operation without progress or shared work, proving the bridge
  does not require those optional capabilities.

The #5093 Release protocol gate separately proves high-water replay validation,
active work-lease matching, malformed allowance handling, worker quiescence
messages, epoch closure, and hard realm release.

## Non-claims

This owner does not claim:

- that a `Task` or Promise yields a browser paint;
- prompt physical cancellation or any maximum checkpoint latency;
- DOM access from a managed callback;
- worker message processing while synchronous managed code monopolizes the
  worker event loop;
- worker crash recovery, watchdog soundness, or hard termination;
- arbitrary-cardinality performance of the active table or broker;
- feature-specific progress usefulness, frequency, or rendering;
- package acquisition, cache, reservation, or publication correctness; or
- that an epoch-work allowance is bounded.

Those claims require their adjacent owner and named gate. Until the remaining
browser, shared-producer, and epoch-work gates exist, those implementation
behaviors described by this target design are unverified.
