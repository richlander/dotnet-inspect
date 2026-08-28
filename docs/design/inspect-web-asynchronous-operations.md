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
replaceable view. Starting work mints a session-local opaque operation ID. IDs
are never reconstructed from request text, package identity, or display state.

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

`OperationId` is a branded, session-issued value. A caller cannot manufacture
one from a string or number. The wire representation includes the worker epoch
plus a monotonic session counter so a message from a terminated worker realm
cannot acquire authority in its replacement.

### Start and authority

Starting an operation:

1. synchronously installs its loading state and ID;
2. logically cancels and revokes any active predecessor;
3. publishes the new ID as the sole authority for that feature session; and
4. posts a start message to the worker.

The feature's producer cannot mutate UI state directly. Success, failure,
progress, and cleanup reach the feature only through authority-checking
session observers. This removes the repeated, fallible requirement that every
`catch` and `finally` block reproduce the same generation checks.

### Logical completion and producer quiescence

Each handle has exactly one logical outcome. User cancellation, supersession,
disposal, or worker restart may complete that outcome before physical work
settles. The separate `quiesced` promise resolves after the worker reports
producer settlement and operation-scoped resource release, or after the owning
epoch closes and its realm is destroyed, whichever occurs first.

A late producer success or ordinary cancellation after logical cancellation
is consumed without publication. A late unexpected failure cannot mutate the
stale feature view, but it is still sent to the worker diagnostic sink; stale
suppression must not become silent error suppression.

### Cancellation

`cancel()` is idempotent. The first call records the typed reason, completes
the logical outcome, revokes publication authority, aborts main-thread work,
and sends one worker cancellation request. Later calls change nothing.

The worker acknowledges whether the operation was queued, running, or already
terminal. An acknowledgment is not proof that managed work stopped. C# owns
the terminal physical result and reports quiescence in `finally`.

If cancellation is observed before managed invocation begins, the worker does
not call the export. Once managed code is running, responsiveness and
cancellation diverge:

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

Messages are closed, versioned records. Every operation message carries
`protocolVersion`, `workerEpoch`, and `operationId`.

```text
Main to worker:
  Start(kind, operationId, payload)
  Cancel(operationId, reason)
  Dispose(operationId)

Worker to main:
  Accepted(operationId)
  Progress(operationId, payload)
  Terminal(operationId, success | failure | canceled)
  Quiesced(operationId)
  WorkerFailure(workerEpoch, diagnostic)
```

Unknown versions, message kinds, operation IDs, duplicate starts, invalid
payloads, and repeated terminal messages are explicit protocol failures. They
must not be ignored or converted into empty results.

The worker validates and narrows `MessageEvent.data` from `unknown` before
dispatch. Payloads use structured-clone-compatible immutable data. Existing
JSON result envelopes may remain strings while their authenticated wire owner
continues to require that representation; the worker protocol does not infer
JSON meaning.

### Runtime lifetime

The worker initializes one generated facade and one .NET runtime. Concurrent
operations reuse that runtime and its package/workspace caches. Feature owners
or the operation session do not dispose the shared runtime.

A worker startup failure is terminal for that worker epoch. The main thread
fails every operation assigned to it, revokes the epoch, and may create a new
worker under explicit retry policy. A worker crash or hard termination loses
runtime-owned caches and in-flight physical work; messages from the old epoch
remain stale even if an operation counter repeats.

Closing an epoch also resolves every outstanding `quiesced` promise for that
epoch. Realm destruction is the physical release boundary for its managed
registry and JavaScript callbacks, even when no `Quiesced` message can arrive.
A startup failure first terminates its partial worker realm, then closes the
epoch. A worker that merely stops responding is not presumed quiescent; timeout
policy must terminate it before disposal can finish.

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

### Operation identity and cancellation

There is no general `AbortSignal`/`CancellationToken` interop mapping.
Long-running cancellable exports take the operation's wire ID. A worker-owned
managed operation registry creates and removes the corresponding
`CancellationTokenSource`; one cancellation export requests cancellation by
ID. The existing singleton `CancelSourceQuery` becomes an incremental migration
source, not the general contract.

Registration occurs synchronously before the exported method reaches its first
incomplete await. Removal occurs in `finally`. Duplicate active IDs fail, and
canceling an absent or terminal ID returns a typed acknowledgment rather than
pretending that work was canceled.

### C#-to-JavaScript progress

The .NET interop generator supports synchronous `Action` and `Func` delegates
marshalled as JavaScript functions. A long-running export may accept an
operation-scoped synchronous progress delegate. The worker creates that
function; its only work is to validate primitive progress fields and call
`postMessage`.

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
its own `finally`. Canceling one waiter removes that subscription but does not
cancel shared work needed by another; the broker may cancel physical work only
after its last waiter leaves and the producer contract permits cancellation.

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
   Adapt one latest-request-wins coordinator without changing execution
   placement.
2. Build a dedicated-worker canary from the official .NET 11 template. Exercise
   one cached CPU-heavy query and one network acquisition through typed
   messages.
3. Move generated facade initialization and engine invocation into one
   long-lived worker. Keep package/workspace caches worker-local.
4. Replace feature generation and request-ID checks with the shared authority
   owner, preserving feature rendering, errors, retry, and queueing.
5. Replace singleton source cancellation with operation IDs and the managed
   registry.
6. Add coarse source progress as the first delegate-backed progress canary.
7. Add progress or cancellation checkpoints to other operations only with
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
  duplicate/unknown IDs, crash, restart, and stale-message tests;
- `inspect-web-async-interop`: compiled and browser-executed Task/Promise,
  synchronous delegate, delegate-release, cancellation-registry, and
  `Func<Task>` negative characterizations;
- `inspect-web-async-browser`: a real browser heartbeat and paint canary while
  a pinned managed CPU operation runs, plus progress, cancellation,
  supersession, and worker-restart scenarios; and
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
