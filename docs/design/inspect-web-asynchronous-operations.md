# Inspect-web asynchronous operations

## Status

This document defines the target architecture for
[issue #4937](https://github.com/richlander/dotnet-inspect/issues/4937).
It governs asynchronous operation ownership in inspect-web, including browser
responsiveness, TypeScript lifecycle state, worker transport, .NET interop,
progress, cancellation, and stale-publication safety.

The worker migration and implementation gates described here have not landed.
Product responsiveness, cancellation latency, progress delivery, callback
quiescence, and worker-restart behavior therefore remain **unverified**. The
checked model establishes only the bounded abstract properties recorded in
[its README](models/inspect-web-async-operations/README.md).

## Decision

Inspect-web will host its .NET WebAssembly engine in one long-lived dedicated
Web Worker. Main-thread TypeScript owns the DOM, user intent, operation
identity, logical terminal outcomes, and authority to publish into UI state.
Worker TypeScript owns .NET runtime startup, generated facade invocation, and
the versioned message transport. Managed feature owners retain their work,
resource limits, cancellation checkpoints, and domain results.

```text
Main browser thread
  DOM, input, rendering, current-view authority
       |
       | versioned start/cancel/progress/terminal messages
       v
Long-lived dedicated worker
  operation dispatch and .NET runtime
       |
       | generated ts-jsexport facade
       v
C# browser engine
  acquisition, workspaces, inspection, analysis, decompilation
```

The worker is not an optimization that can be added after the async contract.
It is the execution boundary that lets the UI remain interactive while
managed CPU work continues. Operation identity and stale-result suppression
remain useful without it, but visible progress and usable controls during
uninterrupted managed work depend on separating that work from the DOM event
loop.

This decision does not enable WebAssembly multithreading. The worker hosts one
single-threaded .NET runtime. Thread-pool execution, `Task.Run`, shared Wasm
memory, cross-origin isolation, and JS interop redirected to the UI thread are
outside this design.

## User scenarios

### Responsive inspection

A user opens a package, type, or member. Inspect-web publishes the loading
state and returns control to the browser. Package decompression, metadata
scans, analysis, and decompilation can continue without blocking navigation,
painting, scrolling, or the Cancel control.

### Cancellation behavior

A user cancels source resolution or a package-wide analysis. The main-thread
operation owner immediately records a typed canceled outcome and revokes the
operation's authority to publish progress, success, failure, or cleanup into
that view. It also sends a cancellation request to the worker.

Cancellation of underlying work is cooperative. An operation states whether
it can honor cancellation during I/O, at CPU checkpoints, or only when it next
returns to the worker event loop. The UI must not claim that physical work has
stopped until the producer becomes quiescent.

### Supersession

A user changes selection while an earlier request is active. The new
operation becomes the only publisher for that view. The previous operation
receives a cancellation request and a logical canceled outcome with reason
`superseded`; any late progress, success, failure, or cleanup is prevented from
mutating the newer view.

### Progress behavior

Source resolution can report coarse phases such as package acquisition, PDB
discovery, verified SourceLink fetch, and decompilation fallback. Package-wide
analysis can report bounded assembly or method counts. Progress remains
observation, not control, and never carries feature-owned mutable objects.

### Longer browser work

The worker permits product scenarios that take materially longer than one UI
frame without freezing the page. It does not remove existing network,
expanded-byte, metadata, graph, result-size, or time budgets. Expensive and
exhaustive behavior remains explicit under the repository's progressive
disclosure rules.

## Four independent properties

The word "async" must not collapse these properties:

| Property | Meaning | Supplied by |
| --- | --- | --- |
| Asynchronous result | A caller receives a `Promise<T>` rather than an immediate value | JavaScript/TypeScript and Task/Promise interop |
| UI responsiveness | Input and rendering can proceed while work continues | A separate worker or deliberately bounded main-thread tasks |
| Cancellation | A caller requests that physical work stop | `AbortSignal`, worker messages, and `CancellationToken` adapters |
| Publication safety | Only the currently authorized operation mutates its view | The main-thread operation owner |

Progress, physical execution placement, and producer quiescence are additional
concerns. None follows merely from an `async` keyword.

## TypeScript and browser semantics

TypeScript owns static Promise types and JavaScript emission. Its official
[design goals](https://github.com/microsoft/TypeScript/wiki/TypeScript-Design-Goals)
explicitly avoid adding runtime behavior or libraries. TypeScript therefore
does not define the browser event loop, painting, worker execution, or
cancellation. `Promise<T>`, `Awaited<T>`, and declarations for `AbortSignal`
describe runtime contracts owned elsewhere.

Promise reactions and `await` continuations use microtasks. Awaiting an
already-settled Promise does not create a rendering opportunity; the browser
drains microtasks before it may render. `await Promise.resolve()` is therefore
not a paint or responsiveness primitive.

The HTML worker specification defines workers for background scripts
independent of UI scripts and specifically identifies long-running computation
without UI interruption as their purpose. Workers are comparatively
heavyweight and intended to be long-lived, which matches one runtime-owning
worker rather than one worker per operation.

The DOM
[abort contract](https://dom.spec.whatwg.org/#aborting-ongoing-activities)
treats cancellation as a request. `AbortSignal.reason` is untyped at the
TypeScript boundary, so inspect-web must normalize it into a repository-owned
reason rather than propagating `any`.

Expected operation outcomes use discriminated values. Rejection remains
available for an unexpected infrastructure or programming failure, but every
`catch` receives `unknown` and must normalize it visibly. This follows the
TypeScript team's
[async guidance](https://www.typescriptlang.org/play/javascript/modern-javascript/async-await.ts.html)
to return expected result information and reserve throwing for exceptional
conditions.

## Ownership

| Owner | Owns | Does not own |
| --- | --- | --- |
| Main-thread operation owner | IDs, current-operation authority, logical outcomes, stale suppression, normalized cancellation reasons, feature observer admission | Managed execution, inspection meaning, rendering content |
| Feature coordinator | Inputs, feature state, rendering, retry policy, error wording, progress presentation | Worker lifecycle, raw interop, another feature's operation |
| Worker host | Runtime startup, protocol validation, dispatch, operation registry, worker epoch, worker diagnostics | DOM, current-view selection, feature rendering |
| [`ts-jsexport`](ts-jsexport.md) | Generated facade shape, authenticated dispatch, Task/Promise projection, supported interop types | Operation policy, worker messages, cancellation meaning |
| C# browser feature | Physical work, domain result, budgets, legitimate token checks and progress points | DOM state, TypeScript currentness |
| Browser platform | Event loops, rendering opportunities, workers, structured clone, network APIs | Product operation semantics |

`package-acquisition.ts` retains its shared-work and serialization policy.
`source-inspection.ts`, `metadata-inspection.ts`,
`member-detail-inspection.ts`, and `spotlight-package-search.ts` retain their
feature data and rendering. Adoption replaces duplicated authority mechanics;
it does not force their distinct retry, caching, or queueing semantics into one
generic workflow.

## Logical operation contract

One feature owner creates one operation session for each independently
replaceable view. One main-thread operation owner holds a page-lifetime
allocator; every feature session requests its next opaque operation ID from
that owner.
IDs are never allocated independently by feature sessions or reconstructed
from request text, package identity, or display state.

The target TypeScript shapes are:

```ts
type OperationCancelReason =
  | "user"
  | "superseded"
  | "disposed"
  | "timeout"
  | "worker-restarted";

type OperationOutcome<TValue, TError> =
  | { readonly kind: "succeeded"; readonly value: TValue }
  | { readonly kind: "failed"; readonly error: TError }
  | {
      readonly kind: "canceled";
      readonly reason: OperationCancelReason;
    };

interface OperationProgress<TProgress> {
  readonly operationId: OperationId;
  readonly value: TProgress;
}

interface OperationHandle<TValue, TError> {
  readonly id: OperationId;
  readonly outcome: Promise<OperationOutcome<TValue, TError>>;
  readonly quiesced: Promise<void>;
  cancel(reason?: OperationCancelReason): void;
}
```

`OperationId` is a branded, operation-owner-issued value. A feature caller
cannot manufacture one from a string or number. Its wire representation is
opaque text. The owner atomically allocates it with an `operationSequence`, a
page-wide monotonic safe integer that never resets or wraps during that page
lifetime. Sequence exhaustion fails visibly and prevents another start.
Concurrent feature sessions therefore cannot collide, while worker replay
checks do not parse or compare the opaque ID.

Logical operation identity is independent of worker identity. A worker-backed
dispatch separately carries the current `workerEpoch`; main-thread work such as
browser `fetch` has an operation ID but no worker assignment. Worker restart
fails and quiesces only operations assigned to that epoch. The epoch check,
rather than an operation-ID encoding convention, prevents a message from a
terminated worker realm from acquiring authority in its replacement.

Allocation and worker dispatch are one synchronous owner action. Worker-backed
starts therefore reach one worker port in increasing page-sequence order,
although main-thread-native operations can leave gaps. The owner assigns an
operation ID to at most one worker epoch and posts at most one `Start`; retry
creates a new operation. The worker keeps one highest-seen sequence per epoch
and rejects a non-increasing `Start`, so completed-ID replay does not require
per-operation tombstones.

### Start and authority

Starting an operation:

1. synchronously installs its loading state and ID;
2. logically cancels and revokes any active predecessor;
3. publishes the new ID as the sole authority for that feature session; and
4. starts the owned producer, posting a start message when it is worker-backed.

The feature's producer cannot mutate UI state directly. Success, failure,
progress, and cleanup reach the feature only through authority-checking
session observers. This removes the repeated, fallible requirement that every
`catch` and `finally` block reproduce the same generation checks.

### Logical completion and producer quiescence

Each handle has exactly one logical outcome. User cancellation, supersession,
disposal, or restart of an assigned worker may complete that outcome before
physical work settles. The separate `quiesced` promise resolves after the
producer adapter reports settlement and operation-scoped resource release. For
worker-backed work, closing the assigned epoch and destroying its realm also
resolves quiescence when no `Quiesced` message can arrive.

A late producer success or ordinary cancellation after logical cancellation
is consumed without publication. A late unexpected failure cannot mutate the
stale feature view, but it is still sent to the worker diagnostic sink; stale
suppression must not become silent error suppression.

### Cancellation

`cancel()` is idempotent. The first call records the typed reason, completes
the logical outcome, revokes publication authority, aborts owned main-thread
work, and sends one cancellation request when the operation is worker-backed.
An omitted reason is normalized to `"user"` before any state or transport
transition. Later calls change nothing.

The worker acknowledges whether it observed the operation as queued, running,
or not active. An acknowledgment is not proof that managed work stopped. C#
classifies the terminal physical result for an invoked export, and its
`finally` releases managed operation resources before the worker reports
quiescence.

If cancellation is observed before managed invocation begins, the worker does
not call the export. It acknowledges `queued`, releases queued-operation
resources, and emits canceled terminal and quiescence messages itself. Once
managed code is running, responsiveness and cancellation diverge:

- the UI remains responsive because C# is on another event loop;
- I/O-bound C# can observe its token at incomplete awaits;
- CPU-bound C# observes cancellation only at explicit checkpoints that also
  permit the worker to process its cancel message; and
- terminating the worker is hard cancellation of the whole runtime, not an
  ordinary per-operation mechanism.

No operation advertises prompt cancellation without a browser gate measuring
its maximum checkpoint latency. Until that gate exists, prompt physical
cancellation is unverified.

### Progress

Progress is optional and capability-specific. It uses a closed
feature-defined union whose events carry semantic phases or monotonic aggregate
counts. Per-instruction, per-row, or unconstrained per-method notifications are
not allowed.

The operation session admits progress only while the operation has authority
and no logical outcome. Delivery after cancellation, supersession, terminal
completion, callback release, or owner disposal is suppressed. Main-thread
rendering may coalesce progress to a frame or a bounded interval without
changing the producer outcome.

Initial candidates are:

- source resolution: acquisition, symbol discovery, SourceLink verification,
  and decompilation fallback;
- package performance, opportunities, and integrations: assembly or method
  totals;
- workspace call graphs: package, assembly, or member-frontier totals; and
- runtime-pack loading: pack and assembly acquisition phases.

Single metadata windows, member documentation, and other normally short
queries do not gain progress merely because they return a Promise.

### Disposal

Disposing a feature session logically cancels its current operation, revokes
all publication authority, removes main-thread observers, and prevents new
starts. Disposal may await all `quiesced` promises when the owner itself must
guarantee callback release. Cleanup from an older operation never changes a
newer operation's loading, error, result, or progress state.

## Worker protocol

Messages are closed, versioned records.

```text
Main to worker:
  Start(kind, operationId, operationSequence, payload)
  Cancel(operationId, reason)

Worker to main:
  Ready(workerEpoch, idleHeartbeatInterval)
  EpochWorkStarted(workId, bounded(maxSilentInterval) | unbounded)
  EpochWorkFinished(workId)
  Accepted(operationId, bounded(maxSilentInterval) | unbounded)
  Rejected(operationId, error, diagnostic)
  Heartbeat(workerEpoch)
  CancelAcknowledged(operationId, queued | running | not-active)
  Progress(operationId, payload)
  Terminal(operationId,
    Succeeded(result)
    | Failed(error, diagnostic)
    | Canceled(reason))
  Quiesced(operationId)
  WorkerFailure(workerEpoch, diagnostic)
```

Every message carries `protocolVersion` and `workerEpoch`; operation messages
also carry `operationId`. The main thread sends no operation messages until it
has received a matching `Ready`, which the worker emits only after runtime and
facade initialization completes. A version or epoch mismatch is not safely
operation-scoped. The main thread treats a mismatched `Ready` as startup
failure; a worker that can parse an incompatible inbound envelope reports
`WorkerFailure`. Both paths terminate the incompatible realm and close the
epoch.

After readiness, invalid operation IDs, unsafe or non-increasing
`operationSequence` values, active duplicate IDs, unknown operation kinds, and
invalid payloads produce `Rejected` without invoking managed code. A valid
increasing sequence is consumed even when a later kind or payload check rejects
the start, so retry requires a new operation. `Rejected` is the operation's
pre-admission terminal path: it supplies a failed outcome and proves that no
producer or operation-scoped worker resource exists, so `quiesced` resolves.
The main-thread record remains only when it must still consume an
acknowledgment for a `Cancel` sent before rejection arrived.

For an accepted `Start`, the worker validates the operation ID, numeric
sequence, kind, and payload; installs the queued record; advances its sequence
high-water mark; and posts `Accepted` before invoking managed code. `Accepted`
means queue admission, not producer start or completion. Progress, `Terminal`,
or `Quiesced` before `Accepted` is a protocol failure; `Rejected` is the
explicit alternative to acceptance. The main-thread owner records acceptance
and the advertised maximum silent interval for worker-level liveness
accounting, and rejects an interval that does not match the registered policy
for that operation kind.

Unknown message kinds and repeated terminal messages are explicit protocol
failures. The operation owner gates the complementary cross-epoch property:
one operation ID and sequence pair is assigned and dispatched at most once. A
`Cancel` race is the sole unknown-operation exception: `not-active` says that
no queued or running producer received the request. The main thread accepts
that acknowledgment only when it has observed, or subsequently observes,
either `Rejected` or the operation's terminal and quiesced messages. It is
never interpreted as successful physical cancellation.

The main-thread owner records whether it sent `Cancel` and retains the protocol
record until it has received either `Rejected` or both `Terminal` and
`Quiesced`, plus the one expected cancellation acknowledgment when it sent
`Cancel`. An early `not-active` remains pending until rejection or terminal and
quiescence validate the race. A per-operation settlement deadline can report a
missing acceptance or rejection, acknowledgment, terminal result, or
quiescence message as a visible protocol failure, but it does not infer
physical completion, resolve `quiesced`, or terminate the shared worker. The
record remains until the expected messages arrive or a separately justified
epoch closure destroys the realm.

The worker retains active records only while an operation is queued or running.
For queued cancellation, the worker posts `CancelAcknowledged(queued)`, releases
the queued payload and callback references, posts `Terminal(Canceled(reason))`
and `Quiesced`, and then removes the record without invoking C#. For an invoked
export, it posts the payload-bearing terminal result, posts `Quiesced` only
after managed `finally` released operation resources, and then removes the
record. No worker-side terminal tombstone is retained. Main-thread feature
disposal uses the ordinary idempotent `Cancel` path and detaches feature
observers, while the operation owner continues consuming protocol messages
until its stricter removal condition is satisfied. There is no separate
worker-side dispose message.

The worker validates and narrows `MessageEvent.data` from `unknown` before
dispatch. Payloads use structured-clone-compatible immutable data. Existing
authenticated JSON result and error envelopes may remain strings while their
wire owner requires that representation; the worker protocol does not infer
JSON meaning. `Succeeded`, `Failed`, and `Canceled` are distinct closed
variants, not a discriminator detached from its required payload.

### Runtime lifetime

The worker initializes one generated facade and one .NET runtime. Concurrent
operations reuse that runtime and its package/workspace caches. Feature owners
or the operation session do not dispose the shared runtime.

Worker creation starts a measured startup allowance. `Ready` ends startup,
establishes the validated idle heartbeat interval, and permits the first
`Start`. The worker posts `Heartbeat` from its event loop while it can process
tasks, including when no operation is accepted. Before managed invocation,
each operation kind declares through `Accepted` either a measured maximum
interval during which it may legitimately prevent the worker from emitting any
liveness message, or `unbounded`. The idle allowance is the validated heartbeat
interval plus scheduling tolerance.

Physical work that can outlive all accepted operation wrappers acquires an
epoch-owned lease through `EpochWorkStarted` before the last related operation
quiesces and releases it through `EpochWorkFinished`. This includes a shared
broker producer that can monopolize the event loop after its last waiter
leaves. An async producer that continues to permit the idle heartbeat does not
need a wider lease.

Progress and other valid worker messages also prove liveness. After readiness,
the epoch watchdog permits the largest of the idle allowance, current accepted
operation allowances, and epoch-work allowances, plus scheduling tolerance. A
pending unaccepted start, canceled waiter, or shorter concurrent operation
never shrinks that allowance. Each valid liveness message renews that epoch
deadline from its receipt. Silence cannot justify automatic termination while
any accepted operation or epoch-work lease is `unbounded`.

An epoch watchdog may terminate the worker only after the current epoch
allowance expires without a valid liveness message. This is a worker-level
lease violation, not an operation timeout. A bounded advertisement requires a
real-browser gate measuring that operation kind's maximum silent worker
occupancy in `inspect-web-async-browser`. Otherwise the operation must advertise
`unbounded`, and hard termination remains an explicit whole-runtime recovery
choice that reports the loss of in-flight work and worker-local caches.

Watchdog time does not accrue while the document is hidden, frozen, in the
back-forward cache, or otherwise lifecycle-suspended. The lifecycle handler
suspends evaluation before background timer throttling can be mistaken for
worker silence. On resume, it rebases the full current startup, idle, accepted,
and epoch-work allowance from the resume time. Automatic termination is not
eligible until that complete post-resume allowance elapses without a fresh
valid liveness message.

A worker startup failure is terminal for that worker epoch. The main thread
fails every operation assigned to it, revokes the epoch, and may create a new
worker under explicit retry policy. Main-thread-native operations remain owned
by their producers and are not failed merely because the worker changed. A
worker crash or hard termination loses runtime-owned caches and in-flight
physical work; messages from the old epoch remain stale.

Closing an epoch also resolves every outstanding `quiesced` promise assigned to
that epoch. Realm destruction is the physical release boundary for its managed
registry and JavaScript callbacks, even when no `Quiesced` message can arrive.
A startup failure first terminates its partial worker realm, then closes the
epoch. A worker that merely stops responding is not presumed quiescent.
Disposal that requires physical release must either receive quiescence, observe
a justified epoch-watchdog violation, or explicitly choose whole-runtime
termination; a per-operation deadline cannot silently make that choice.

The implementation should start from the official .NET 11 Web Worker template
and client protocol introduced by
[dotnet/aspnetcore#65037](https://github.com/dotnet/aspnetcore/pull/65037),
then replace its stringly method invocation with inspect-web's generated facade
and closed messages.

## JavaScript and .NET interop

### Task and Promise

`.NET JavaScript interop`
[maps](https://learn.microsoft.com/aspnet/core/client-side/dotnet-interop)
a `[JSExport]` `Task<T>` result to a JavaScript `Promise<T>`. Calling either an
async JavaScript function or an async C# method executes its synchronous prefix
before the first incomplete wait. The Promise type therefore does not prove
that control returned to either event loop.

The generated `ts-jsexport` facade runs inside the worker. Main-thread code does
not hold its runtime, managed exports, JS proxies, or callbacks.

Long-running managed exports return a typed wire result that classifies
`Succeeded(result)`, `Failed(error, diagnostic)`, or `Canceled(reason)` before
Task-to-Promise projection. Expected domain failures and cancellation therefore
fulfill the Promise with distinct variants. The envelope is closed, versioned,
and authenticated like other `ts-jsexport` JSON wire results. A Promise
rejection represents an interop, runtime, or malformed-contract failure; the
worker normalizes the unknown rejection into a boundary-error variant and a
diagnostic, reports a failed terminal result, and never infers cancellation
from JavaScript error text. The worker maps a fulfilled managed result to the
corresponding payload-bearing `Terminal` message.

### Operation identity and cancellation

There is no general `AbortSignal`/`CancellationToken` interop mapping.
Long-running cancellable exports take the operation's wire ID. The worker owns
queued-operation state. On managed invocation, a managed operation registry
creates and removes the corresponding `CancellationTokenSource`; one
cancellation export requests cancellation by ID. The existing singleton
`CancelSourceQuery` becomes an incremental migration source, not the general
contract.

Registration occurs synchronously before the exported method reaches its first
incomplete await. Removal occurs in `finally`, before the worker posts
`Quiesced`. Duplicate active IDs fail. The worker owns pre-invocation
cancellation and its synthetic terminal/quiescence path. For invoked work, the
managed cancellation export reports whether it requested cancellation from the
registry entry. The worker maps that result to `CancelAcknowledged(running)` or,
when completion already removed the entry, `CancelAcknowledged(not-active)`.
None of these acknowledgments claims that a running producer has stopped.

### C#-to-JavaScript progress

The .NET interop generator supports synchronous `Action` and `Func` delegates
marshalled as JavaScript functions. A long-running export may accept an
operation-scoped synchronous progress delegate. The worker creates that
function; its only work is to validate primitive progress fields and call
`postMessage`.

This runtime capability is not yet accepted by the authenticated generated
facade: `ILInspector.JsExportSurface` deliberately rejects delegate parameters
that its compatibility table cannot prove, and `ts-jsexport` cannot publish
them on weaker evidence. Delegate-backed progress therefore depends on a
separately reviewed prerequisite that authenticates synchronous delegate
parameters, represents their TypeScript function type, and proves the generated
facade against a compiled browser canary. Until that prerequisite lands,
progress is not transported through the generated facade.

The delegate:

- is never a DOM callback;
- is never invoked after the managed operation's `finally` completes;
- is not retained beyond the operation;
- does not return a Promise;
- does not re-enter another managed export; and
- reports only bounded feature-owned progress records.

The current .NET 11 toolchain rejects
`Func<..., Task<T>>` mapped to `Function<..., Promise<T>>` with `SYSLIB1072`.
[dotnet/runtime#101913](https://github.com/dotnet/runtime/issues/101913)
tracks that unsupported shape and its JS thread-affinity problem. This design
does not depend on it.

Shared physical work needs a broker rather than one caller owning its progress
delegate. Package acquisition, for example, can fan one producer's progress
out to every still-authorized operation waiting on that package coordinate. A
shared producer owns a broker-scoped progress sink and cancellation lifetime,
not any caller's delegate or `CancellationTokenSource`. Each exported
operation attaches its delegate as one broker subscription and removes it in
its own `finally`. Its await uses the operation token to stop waiting and enter
that `finally` without canceling the shared producer task. The wrapper can then
return `Canceled` and quiesce independently while another authorized waiter
continues. Before the last waiter quiesces, an outliving producer that can
prevent idle heartbeats transfers its liveness allowance to an epoch-work
lease. The broker may cancel physical work only after its last waiter leaves
and the producer contract permits cancellation.

## Effects on C# API design

The browser boundary does not make every product API asynchronous.

- Use `Task` for genuine asynchronous I/O, waits, or orchestration.
- Keep bounded CPU-only product queries synchronous and execute them in the
  worker.
- Add `CancellationToken` only where the implementation can state and test
  useful checkpoints.
- Add progress only at semantic phase boundaries or bounded aggregate
  checkpoints.
- Do not use `Task.Run`, blocking waits, or assumed browser threads.
- Do not thread browser operation IDs into host-neutral inspection models;
  adapt them at the browser host boundary.

An exported orchestration method may be async while invoking synchronous
product queries after acquisition. That shape accurately represents the wait;
it does not claim that the CPU segment yields.

A future cooperative CPU scheduler requires its own host service and browser
evidence. `Task.Yield`, an already-completed awaitable, or a Promise microtask
is not accepted as proof that the worker processed cancellation messages.

## Rust comparison

Rust is a portability argument for the operation contract, not an inspect-web
implementation requirement or an empirical gate.

| Concern | JavaScript and C# | Native Rust and C# |
| --- | --- | --- |
| Async value | Eager Promise/Task with a synchronous prefix | Lazy `Future`, progressed by an executor |
| Standard bridge | `[JSExport]` and `[JSImport]` map Task/Promise | Ordinary C ABI has no async bridge |
| Start | Invocation may perform work immediately | Calling `async fn` creates an inert Future |
| Cancellation | Abort/token request plus adapter | Drop or runtime token; FFI still needs a protocol |
| Completion | Promise settlement | Polling or callback across FFI |
| Ownership | JS/.NET proxies and task lifetime | Explicit handle, callback context, and release |

A native Rust engine would expose `start`, `request_cancel`, completion or
polling, and `release`; it would not export a Rust `Future` or accept a .NET
`Task`. Rust compiled to Wasm through `wasm-bindgen` is a third,
JavaScript-mediated path: Rust Future to Promise to .NET Task. It still does
not supply automatic cross-runtime cancellation.

The portable concepts are:

- explicit operation identity and start;
- progress with bounded payloads;
- idempotent cancellation request;
- exactly one logical terminal outcome;
- a defined completion/cancellation race;
- versioned payload and error encoding;
- callback quiescence; and
- explicit release.

`Promise`, `Task`, `Future`, `Waker`, `AbortSignal`, `CancellationToken`,
exceptions, microtasks, and worker affinity remain transport-specific.

The protocol must tolerate immediate and deferred completion because C# may
execute a synchronous prefix while a Rust async body does not begin until its
Future is polled.

## Existing protocol migration

Adoption is incremental:

1. Land the main-thread operation-session owner and its model-backed unit gate.
   Its page-lifetime allocator must not depend on a worker. Adapt one
   latest-request-wins coordinator without changing execution placement.
2. Build a dedicated-worker canary from the official .NET 11 template. Exercise
   one cached CPU-heavy query and one network acquisition through typed
   messages, including the managed terminal-result envelope. This temporary
   canary does not become the production facade.
3. Land the base `ts-jsexport` TypeScript module and its separately owned
   inspect-web consumer migration from
   [#4792](https://github.com/richlander/dotnet-inspect/issues/4792). Before
   production worker adoption, that facade must avoid `window` and DOM state,
   accept host configuration explicitly, keep entry-point execution explicit,
   and publish neither the raw runtime nor raw exports.
4. Move the worker-safe generated facade initialization and engine invocation
   into one long-lived worker. Keep package/workspace caches worker-local.
5. Replace feature generation and request-ID checks with the shared authority
   owner, preserving feature rendering, errors, retry, and queueing.
6. Replace singleton source cancellation with operation IDs, the managed
   registry, and explicit queued and invoked cancellation settlement.
7. Land separately owned `ILInspector.JsExportSurface` and `ts-jsexport`
   support for authenticated synchronous delegate parameters, then add coarse
   source progress as its first real-consumer canary.
8. Add progress or cancellation checkpoints to other operations only with
   measured user value and focused gates.

`spotlight-package-search.ts` may continue to use main-thread browser `fetch`;
it can consume the same logical operation owner without routing browser-native
work through .NET. `package-acquisition.ts` retains its shared-work queue and
adds an adapter for operation authority rather than becoming a
latest-request-wins session.

## Evidence and gates

### Checked abstract model

[`InspectWebAsyncOperations.tla`](models/inspect-web-async-operations/InspectWebAsyncOperations.tla)
models two operations, queue admission, user cancellation, supersession,
success, failure, progress, disposal, physical settlement, and release. Its
positive configuration checks:

- one logical completion;
- at-most-once cancellation forwarding;
- publication only with current authority;
- cleanup preserving the newer visible owner;
- no callback after release;
- no producer start after observed pre-start cancellation;
- no operation start after owner disposal; and
- eventual logical completion, physical settlement, and release under stated
  fairness assumptions.

Mutation configurations require counterexamples for stale progress, late
success, late failure, duplicate completion, cleanup of a superseded operation
mutating the newer view, callback after release, start after disposal, and
producer start after pre-start cancellation.

The model abstracts browser task ordering, managed implementation, actual
callback proxies, transport parsing, and arbitrary cardinality. It does not
prove implementation conformance.

### Required implementation gates

These gates are design requirements and do not yet exist:

- `inspect-web-async-operation-state`: mutation-backed TypeScript tests for
  authority, outcomes, cancellation, supersession, disposal, diagnostics, and
  quiescence;
- `inspect-web-async-worker-protocol`: closed-message parsing, epoch isolation,
  concurrent feature-session ID uniqueness, worker-independent logical IDs,
  at-most-once assignment, explicit safe-integer sequence ordering and
  exhaustion, replay after record removal, duplicate/unknown IDs, readiness,
  accepted-versus-rejected start closure, payload-bearing terminal variants,
  queued cancellation settlement, bare-cancel reason normalization,
  outstanding-acknowledgment races, settlement deadlines that cannot terminate
  an epoch, startup, idle, accepted-operation, and epoch-work leases, bounded
  and unbounded liveness, busy-versus-wedged discrimination, shared-waiter
  quiescence, record release, crash, restart, main-thread-operation isolation,
  and stale-message tests;
- `inspect-web-async-interop`: compiled and browser-executed typed managed
  result classification before Task/Promise projection, Promise-rejection
  failure handling, authenticated synchronous delegates, delegate release,
  cancellation-registry behavior, and `Func<Task>` negative
  characterizations;
- `inspect-web-async-browser`: a real browser heartbeat and paint canary while
  a pinned managed CPU operation runs, measured maximum silent occupancy for
  every bounded operation kind, a shorter sibling that cannot terminate a
  healthy busy worker, cold and warm readiness, idle and broker-owned work,
  hide, freeze, resume, and back-forward-cache transitions, plus progress,
  cancellation, supersession, and worker-restart scenarios; and
- `inspect-web-async-performance`: pinned interpreter and AOT measurements for
  worker startup, first operation, cached operation, message payload transfer,
  and peak memory.

The browser gate must include a neighboring operation not used to tune the
implementation. Synthetic delays may isolate races but cannot be the only
evidence for responsiveness or managed CPU behavior.

## Runtime evidence

The direction follows active .NET runtime work:

- [dotnet/runtime#95452](https://github.com/dotnet/runtime/issues/95452)
  completed official worker documentation, samples, and template work for
  .NET 11;
- [dotnet/aspnetcore#65037](https://github.com/dotnet/aspnetcore/pull/65037)
  added the official Web Worker template and client;
- [dotnet/runtime#114918](https://github.com/dotnet/runtime/issues/114918)
  tracks a single-threaded runtime in a worker with application-owned
  messaging;
- [dotnet/runtime#121879](https://github.com/dotnet/runtime/pull/121879)
  made a single-threaded worker build consistently use sidecar mode;
- [dotnet/runtime#65559](https://github.com/dotnet/runtime/issues/65559)
  records the UI-locking motivation; and
- [dotnet/aspnetcore#65823](https://github.com/dotnet/aspnetcore/issues/65823)
  tracks incomplete debugging of C# running inside the worker.

Worker debugging limitations affect development evidence. Browser integration
tests, structured operation traces, worker diagnostics, and reproducible
inputs are required even when an interactive managed breakpoint is unavailable.
