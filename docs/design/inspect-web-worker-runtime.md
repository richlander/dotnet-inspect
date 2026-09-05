# Inspect-web worker runtime

## Status

This document defines the target worker-runtime host and protocol for
[issue #5093](https://github.com/richlander/dotnet-inspect/issues/5093).
The user approved its inspect-web-only host scope on 2026-09-02. Implementation
under [issue #5418](https://github.com/richlander/dotnet-inspect/issues/5418)
is dependency-ordered and partial. The descriptor-safe two-stage wire codec,
shared runtime core, host authority, and complete base TypeScript protocol
gate are implemented under `inspect-web-worker-envelope-validation` and
`inspect-web-worker-protocol`. The browser binding additionally connects a real
module `Worker`, the current generated facade set, and native lifecycle inputs
under `inspect-web-worker-browser-binding`. Its explicit diagnostic consumer
uses the existing managed async-lowering canary. It does not route current UI
features through the Worker. Feature adoption, managed epoch-work reporter
registration, full lifecycle coverage, responsiveness evidence, and the
remaining browser gates named below are still required.

Its finite state models establish only the abstract properties recorded with
those models. The engine-to-browser event-stream contract is now defined by
[the async event-stream owner](engine-browser-async-event-stream.md). Durable
worker event batches remain a later residual blocked on
[#5570](https://github.com/richlander/dotnet-inspect/issues/5570) and the
relevant [#5419](https://github.com/richlander/dotnet-inspect/issues/5419)
managed handoff.

## Decision

Inspect-web runs one long-lived, single-threaded .NET WebAssembly runtime in a
dedicated Web Worker. One main-thread runtime host owns each worker epoch from
creation through readiness, operation dispatch, liveness, draining, hard
termination, and realm release.

The host:

- creates a non-reused epoch identity and a worker bound to that identity;
- holds activated operation starts until one consumer-owned bootstrap barrier
  fulfills and the worker reports matching readiness;
- exchanges only closed, versioned, validated messages;
- retains bounded replay evidence through sequence high-water marks and active
  records rather than completed-operation tombstones;
- accounts for task-loop evidence, accepted operations, and managed epoch-work
  leases when judging worker silence;
- uses a non-renewable startup budget and a two-stage post-readiness watchdog;
- turns detectable missing protocol responses into bounded epoch draining;
- distinguishes planned restart from unexpected worker loss; and
- treats realm destruction as the release barrier for every unresolved
  operation and callback assigned to that epoch.

Moving the runtime, rather than relying on a `Task` or Promise alone, keeps
managed CPU work off the DOM event loop. `Task` and Promise still carry
asynchronous completion; worker placement supplies event-loop isolation. The
worker can still monopolize its own event loop. Cancellation commands,
heartbeats, probes, and unrelated worker tasks cannot run while synchronous
managed work holds that event loop. A synchronous managed progress callback
can post a message from that same call stack, but it does not yield to queued
worker tasks.

## User scenarios

### Responsive inspection

A package query or analysis can run synchronously for seconds in managed code
without blocking document input, layout, or paint. The main thread remains the
only DOM authority. Worker progress is an optional typed message, not the
mechanism that creates responsiveness.

### Cold startup with immediate user intent

The page may start an operation before .NET is ready. The runtime adapter
retains that activated start in operation-sequence order without posting it to
the partial worker realm. A cancellation or supersession before readiness
settles and quiesces that held producer locally. Matching `Ready` dispatches
the remaining held starts; startup failure fails and quiesces them.

### Cooperative cancellation and hard recovery

Once a start has been posted, cancellation is a protocol request addressed by
the operation identity. It signals the managed active-operation table after
the serialized Start command has entered the managed bridge. It cannot
interrupt synchronous managed CPU work before the worker event loop processes
the request.

An explicit planned restart or a justified epoch failure can destroy the
entire worker. That is hard cancellation: all in-flight managed work and
worker-local caches are lost. Planned restart produces
`canceled("worker-restarted")`; an unexpected startup, crash, worker-declared,
protocol, or watchdog loss produces a boundary failure.

### Shared physical work

Managed work that outlives its final operation wrapper can retain the epoch
through an epoch-work lease. The worker validates the managed bridge's
monotonic work sequence and reports the lease to the main host. The lease can
widen or disable automatic silence judgment without pretending that an
operation record still owns the producer.

### Speculative worker-local preparation

After producing the result needed for the initial user experience, an initial
operation may start a feature-owned shared producer for an expected follow-up
request. Before that operation wrapper quiesces, the managed bridge transfers
the outliving producer to an epoch-work lease unless it has a registered
idle-compatible classification. This runtime consumes that lease transition;
it does not redefine the bridge's transfer or the feature's decision to start
the producer.

After transfer, the speculative producer has no operation sink, progress
stream, cancellation token, or publication authority from the initial
operation. The initial operation can settle and quiesce while the producer
continues physically.

Completing the physical work releases its lease while permitting the
feature-owned result to remain in an epoch-local cache. A later main-thread
request still follows ordinary `Start`, admission, cancellation, and
settlement; its feature adapter may satisfy the operation from that cache. If
the request never arrives, no logical operation record is invented. Planned
restart or unexpected realm loss discards the cache, and retry or recomputation
remains feature policy.

Because the worker is single-threaded, speculative work creates no scheduling
priority or preemption. To remain responsive to the expected request, it must
return to the worker event loop under a feature-owned structural bound. An
unbounded declaration keeps watchdog accounting honest but does not guarantee
prompt request handling.

## Ownership

This document owns:

- worker creation and one current worker realm per runtime host;
- page-lifetime worker-epoch identity and non-reuse;
- the closed main-to-worker and worker-to-main protocol;
- validation and ordering of worker messages;
- the worker-side operation dispatch catalog, liveness declarations, and
  idle-compatible producer-class capabilities;
- held starts before readiness;
- operation protocol records from dispatch through wire settlement;
- worker-side operation-sequence and epoch-work-sequence replay validation;
- startup, idle, accepted-operation, and epoch-work liveness accounting;
- lifecycle-suspended active-time accounting and main-loop discontinuity
  handling;
- planned restart, unexpected failed draining, crash closure, and hard worker
  termination; and
- realm release plus adapter notification for unresolved epoch resources.

It consumes:

- opaque operation IDs, strictly increasing page operation sequences,
  cancellation reasons, producer sinks, and logical authority from
  [inspect-web operation authority](inspect-web-operation-authority.md);
- `initializeRuntime()`, `runEntryPoint()`, generated managed functions, and
  authenticated callback/result types from
  [`ts-jsexport`](ts-jsexport.md);
- managed result classification, keyed cancellation, operation callback
  release, and sender-side epoch-work lease identity from
  [inspect-web managed operation bridge](inspect-web-managed-operation-bridge.md);
- one consumer-owned bootstrap operation whose fulfillment means that runtime
  initialization, host configuration, required entry-point execution, and
  worker-local adapter registration have completed;
- operation kinds, payload validators, result mappings, progress mappings, and
  structurally justified liveness declarations from feature adapters; and
- page lifecycle and a monotonic clock from the browser host.

It does not own:

- operation-ID allocation, logical cancellation, stale-publication
  suppression, feature outcomes, DOM state, or feature disposal;
- generated-facade construction, export authentication, runtime configuration
  steps, or the meaning of entry-point completion;
- managed cancellation-token, progress-reporter, shared-producer, or result
  classification semantics;
- feature input, result, error, diagnostic, or progress payload meaning;
- feature checkpoint placement, retry, cache, timeout, or sharing policy; or
- cross-owner sequencing beyond its immediate adapter contracts.

The thin composition map in #5095 connects these owners without restating
their internal state machines.

## Worker epoch identity problem

Replacing a worker creates an overlap hazard. Messages queued by the old
worker can arrive after the host has created its replacement, and message text
alone cannot prove which `Worker` object emitted it. Operation IDs and
operation sequences identify logical work, not the physical realm that
processed it. Retaining every completed worker identity would also turn stale
message rejection into page-lifetime unbounded state.

The runtime therefore needs two related values:

- a host-only epoch binding that pairs one exact `Worker` object with its
  lifecycle authority; and
- a bounded, structured-clone-safe token that correlates envelopes with that
  binding.

The token is not a capability or authentication secret. A message is current
only when both its token equals the current binding's token and its handler is
still bound to that exact `Worker` object. Copying the current token onto
another worker source grants no authority.

The page owns one monotonically increasing safe-integer token source. Creating
an epoch reserves the next positive token before installing any handler,
constructs the worker, and commits the host binding before accepting an event.
A reserved token is never reused during that page lifetime, including after
startup failure or realm release. Allocation never wraps; exhaustion refuses
worker creation visibly. Token ordering has no protocol meaning despite the
monotonic allocator, so receivers compare tokens only for exact equality.

## Boundary shapes

The exact product types may use narrower generic parameters. These sketches
state the owned distinctions.

```ts
declare const workerEpochBrand: unique symbol;
declare const workerEpochTokenBrand: unique symbol;
declare const workerIdleCompatibleBrand: unique symbol;

type WorkerEpochToken = number & {
  readonly [workerEpochTokenBrand]: "WorkerEpochToken";
};

interface WorkerEpoch {
  readonly [workerEpochBrand]: "WorkerEpoch";
  readonly token: WorkerEpochToken;
  readonly worker: Worker;
}

interface WorkerOperationReference {
  readonly operationId: OperationId;
  readonly operationSequence: number;
}

type WorkerLivenessAllowance =
  | {
      readonly kind: "bounded";
      readonly maxSilentActiveMilliseconds: number;
    }
  | { readonly kind: "unbounded" };

type WorkerIdleCompatible = {
  readonly [workerIdleCompatibleBrand]: "WorkerIdleCompatible";
};

type WorkerRuntimeFailureKind =
  | "startup"
  | "worker-crash"
  | "protocol"
  | "watchdog"
  | "control-response"
  | "probe-exhaustion"
  | "worker-declared"
  | "worker-message";

interface WorkerRuntimeFailure {
  readonly kind: WorkerRuntimeFailureKind;
  readonly diagnostic: unknown;
}

type WorkerEpochClosure =
  | {
      readonly kind: "planned-restart";
      readonly reason: "worker-restarted";
    }
  | {
      readonly kind: "unexpected-failure";
      readonly failure: WorkerRuntimeFailure;
    };
```

`WorkerEpoch` is a host-only identity binding and is never structured-cloned.
`WorkerEpochToken` is a positive safe integer carried on the wire. The host
obtains a token only from the page-lifetime allocator and creates a binding
only from that freshly reserved token and its newly created `Worker`. It never
derives either identity from received message data.

Operation sequences remain owned by operation authority. This host consumes
them as safe integers and requires every start assigned to one epoch to be
strictly greater than that epoch's previously assigned high-water mark. Gaps
are legal because page-native producers may consume intervening sequences.

`maxSilentActiveMilliseconds` is a positive safe integer. It is product policy,
not a measurement result. A bounded declaration is legal only when the
operation or lease implementation structurally returns control to the worker
event loop within that active-time bound. Browser measurements validate
margin. They do not invent the bound. An operation without such an enforcing
structure declares `unbounded`.

`WorkerIdleCompatible` is an opaque producer-class capability issued by this
runtime owner only when that class's structural event-loop-return bound fits
within the configured idle allowance. A managed producer holding that
classification may continue after its final operation waiter leaves without
opening an epoch-work lease. Every other outliving producer must acquire an
explicit bounded or unbounded epoch-work lease. The managed bridge owns when a
producer outlives its waiters; this owner validates and issues only the
liveness classification.

## Runtime construction and readiness

One runtime host has at most one live epoch. Creation commits the epoch and
`Worker` object before any worker event can be accepted. Event handlers retain
both the source object and epoch; matching message text from another source is
stale.

The host reserves one start transition before invoking the bootstrap encoder
or any other consumer-supplied creation callout. A nested `start()` therefore
rejects as `epoch-active` rather than constructing a competing realm. The host
rechecks terminal disposal after each pre-commit callout. If disposal occurred,
the outer start rejects as `host-disposed`; a transport already created but not
installed is terminated before return. Disposal subscription cleanup remains
reserved through that pre-commit transition; if the unowned transport cannot be
terminated, the host reports the failure without claiming cleanup completion.
Once the epoch is installed, reentrant
disposal or failure uses ordinary hard termination, and the outer start cannot
return a success-shaped epoch after any closure has committed, including a
bounded draining closure that retains physical work.

Installing the epoch before transport binding gives bind-time failures an
authoritative source and token, but does not authorize Worker protocol input
before initialization dispatch begins. A current-source message delivered
synchronously by `bind()` fails the partial epoch as protocol-invalid. The host
marks initialization dispatch immediately before calling `send(Initialize)`,
so a synchronous response from inside that send follows the ordinary startup
protocol.

The first main-to-worker envelope supplies the protocol version, epoch token,
structured-clone-safe bootstrap input, the expected idle-heartbeat interval,
and the total idle allowance after host scheduling tolerance. The worker
validates that its producer-class registry uses that exact total allowance
before beginning the consumer-owned bootstrap operation. A mismatch is visible
startup failure. A duplicate initialization envelope is an epoch protocol
failure.

The bootstrap operation owns its concrete steps. This runtime owner requires
only one result:

- fulfillment means every consumer prerequisite for managed dispatch is ready;
- rejection is startup failure; and
- the bootstrap operation retains no main-thread DOM object.

The worker registers the managed epoch-work reporter only after the generated
facade is ready and before reporting readiness. It sends `Ready` only after the
whole bootstrap operation fulfills. `Ready` echoes the exact protocol version,
epoch token, and configured idle-heartbeat interval. A mismatch is startup
failure, not a partially compatible realm.

Worker creation starts one non-renewable active-time startup budget. Only a
matching `Ready` received before the budget is exhausted succeeds. The handler
compares the active-time deadline before opening the epoch; matching readiness
at or after exhaustion closes the partial realm as startup failure. Matching
readiness ends the startup budget before the host flushes held starts; time
spent in synchronous start-send callbacks cannot retroactively expire
successful readiness. Heartbeats or probe acknowledgments received before
matching `Ready` are protocol-invalid and immediately close the partial realm
as described below; they cannot renew, reset, or satisfy that budget.
Lifecycle suspension and a detected main-loop discontinuity pause active
elapsed time; they preserve the remaining budget rather than grant a fresh one.

Startup rejection or budget exhaustion closes admission, terminates the
partial realm, reports unexpected startup failure for every activated held
producer, quiesces those producers after termination, and closes the epoch.

### Browser binding

The native binding uses a module Worker and retains one exact source binding
for its message, error, and messageerror listeners. Detachment removes those
listeners before native termination. A browser `error` event does not prove
physical destruction: it reports a worker-message failure, and ordinary
termination remains the destruction barrier.

Both the page and Worker use the same seven generated facade modules and
consumer-owned bootstrap coordinator. A Worker does not inherit the page's
import map. The consumer's published runtime loader therefore resolves the
SDK-selected fingerprinted runtime module directly; it must neither depend on
document import-map state nor publish an unhashed framework alias. This is
consumer artifact addressing, not a second facade-generation mechanism or
runtime initialization policy.

Native active time pauses while any visibility, page-hide, or freeze
suspension remains in effect. Resume clears only its corresponding suspension.
A clock read or timer notification that detects a scheduling gap reports
recovery before the host judges deadlines. That gap preserves the remaining
startup, command-response, and drain budgets and gives the existing
post-readiness watchdog its recovery interval.

The initial binding's diagnostic operation invokes the existing generated
`asyncLoweringCanary` export and declares unbounded managed execution. Its
registry admits no shared managed producers or epoch-work classes.
Registering the managed epoch-work reporter and adopting feature operations
remain later handoffs; the diagnostic operation does not depend on them.

## Operation adapter

The worker producer adapter implements operation authority's two-phase
preparation contract.

The runtime host is generic only in bootstrap and runtime diagnostic data.
Each operation registration is independently generic in its input, value,
error, operation diagnostic, progress, and preparation-error types, and owns
one closed, total boundary-error table keyed by every
`WorkerRuntimeFailureKind`. Runtime diagnostic detail remains on the one epoch
failure callback rather than requiring a fallible per-operation conversion
during closure. The host erases feature types only behind a private record of
closed callbacks and data after registration; one operation kind cannot widen
another kind's returned adapter or select its payload codecs.

Preparation synchronously:

1. rejects if no starting or ready epoch accepts assignments;
2. validates the operation reference, operation kind, and feature adapter
   without retaining the producer sink on failure;
3. reserves the operation sequence before invoking the input encoder, so a
   nested preparation cannot activate a later assignment ahead of it;
4. records one epoch-visible prepared lifetime before invoking the input
   encoder, so reentrant epoch closure cannot overtake a successful or rejected
   preparation;
5. validates and encodes the structured-clone-safe input;
6. creates one prepared binding with an already-usable cancellation endpoint;
   and
7. retains the sink and encoded payload only inside that binding, without
   creating an assigned operation record or calling the Worker until
   activation.

Prepared reservations form one sequence-ordered lane across operation
sessions. Activation waits behind every earlier unresolved preparation.
Activation of the earlier binding assigns it before later activated bindings;
rejection or abandonment releases its reservation as a legal sequence gap and
unblocks later assignments. Cancellation of an activated binding waiting in
that lane retains the first reason and applies it immediately after assignment.
Reservation consumes the identity sequence even when encoding rejects or
throws, or the binding is abandoned; operation authority never reuses those
page-issued identities.

Operation authority resolves every prepared binding through `activate()` or
`abandon()` before the corresponding `start()` call returns. An intentionally
unresolved prepared binding therefore blocks later assignments and realm
release by contract rather than being force-abandoned by epoch termination.

Abandoning a prepared binding synchronously releases its retained state and
prepared lifetime without assigning Worker work. Once an activated binding
reaches the head of the prepared lane, it installs one epoch-assigned record
before any worker callout can be observed, delivers any already committed
closure and quiescence, and only then releases the prepared lifetime.
At the head of that lane, activation while the epoch is starting places the
record in the held queue, while activation when ready posts `Start`. If the
epoch closed before the binding reaches the head, activation reports that
committed closure verbatim through the already-installed sink: planned restart
reports
`Canceled("worker-restarted")`, while unexpected closure reports its boundary
failure. Both paths report quiescence and do not throw.

Held records are ordered by operation sequence. A held cancellation removes
the record without sending `Start` or `Cancel`, reports the supplied canceled
outcome, and reports quiescence. Matching readiness posts every remaining held
start in sequence order before the ready epoch accepts a new warm activation.
A response delivered synchronously by `send(Start)` during that internal flush
uses the ordinary post-readiness operation and control protocol. The host keeps
the flush barrier closed to warm activation until every held start has been
posted; it does not misclassify a response to an already-posted start as
pre-readiness traffic.
If posting one held start synchronously commits epoch closure, every later
record still in the held queue is known never to have reached the Worker. The
host fixes all assigned logical outcomes first, publishes the one runtime
failure, and only then records those never-posted records as physically closed,
reports quiescence, and releases their retained state. They do not consume the
drain budget while the already-posted record supplies the remaining physical
release evidence.
A held record never becomes an accepted worker record merely because the realm
became ready; it remains awaiting the worker's explicit `Accepted` response.

Admission stops synchronously when an epoch enters draining. Preparation then
rejects new work, while already prepared bindings either abandon or activate
into the committed epoch closure. Hard termination may destroy the Worker
before that choice, but realm release waits until every such binding has
abandoned or completed activation callbacks.

## Closed worker protocol

Every envelope carries:

- the exact protocol version;
- the exact worker epoch token; and
- one closed message discriminator with all required fields.

Operation envelopes also carry the complete operation reference. The receiver
narrows `MessageEvent.data` from `unknown`, validates own data properties
without invoking accessors, rejects unsafe integers, and constructs the exact
outer and nested wire variant while retaining feature-owned payload fields as
`unknown`. The runtime core then validates protocol state and resolves the
bootstrap consumer or active operation record before invoking that owner's
bounded codec to construct the typed payload variant. JSON-looking strings
remain strings unless their feature wire owner authenticates and parses them.

The unbound startup entry accepts only `Initialize`, validates its wire
structure, version, epoch token, and scalar fields, and then applies the
consumer bootstrap codec. After binding, every main-to-worker structural
decode requires the exact expected epoch token, including a duplicate
`Initialize` retained for state-machine rejection. Worker-to-main structural
decode likewise performs no global cross-operation payload selection: the
operation reference selects the active record and its result, error,
diagnostic, or progress codecs.

The main-to-worker inventory is:

```text
Initialize(bootstrap, idleHeartbeatInterval, idleAllowanceMilliseconds)
Start(operation, kind, payload)
Cancel(operation, reason)
Probe(probeSequence)
```

The worker-to-main inventory is:

```text
Ready(idleHeartbeatInterval)
StartupFailed(diagnostic)
Accepted(operation, allowance)
Rejected(operation, error, diagnostic)
CancelAcknowledged(operation, running | not-active)
Progress(operation, payload)
Settled(operation,
  Succeeded(result)
  | Failed(expected | unexpected, error, diagnostic)
  | Canceled(reason))
Heartbeat()
ProbeAcknowledged(probeSequence)
EpochWorkStarted(workSequence, allowance)
EpochWorkFinished(workSequence)
EpochFailed(diagnostic)
```

`Settled` is the accepted operation's one physical closure record. The worker
can construct it only after the generated managed Promise fulfills and the
managed bridge has therefore crossed its operation-resource release barrier.
The main adapter processes one valid `Settled` by:

1. atomically reporting the terminal failure and its unexpected diagnostic
   through `reportUnexpectedTerminal` when that failure kind applies, or
   reporting the ordinary terminal result otherwise; and
2. reporting quiescence.

The atomic operation-authority call commits terminal authority before its
synchronous diagnostic observer can reenter operation APIs. `Rejected` is the
exclusive never-accepted alternative and proves that no operation-scoped
worker or managed resource was admitted; the adapter reports its failure and
quiescence together.

Promise rejection from the managed facade is not a `Failed` managed result. It
is a worker boundary failure and begins unexpected epoch draining because the
worker can no longer prove that the operation boundary remains usable.

## Admission, ordering, and replay

The worker retains:

- one highest received operation sequence;
- one active map keyed by operation ID and sequence; and
- no completed-operation tombstones.

A `Start` whose sequence is not strictly greater than the received high-water
mark is replay or reordering and fails the epoch. A valid greater sequence is
consumed before operation-ID, kind, or payload validation. A fresh operation ID
with that newer sequence proceeds to ordinary validation; an already-active ID
fails the epoch. The serialized handler cannot silently stop between sequence
consumption and that admission-or-failure decision. A later kind or payload
validation failure returns `Rejected`; retry uses a new operation identity.

An operation ID already present in the active map is ambiguous and fails the
epoch. Historical operation-ID uniqueness remains an operation-authority
precondition; the runtime does not retain every completed opaque ID to
re-prove it.

A valid start installs its protocol record and sends `Accepted` before invoking
managed code. `Accepted` means the operation passed worker admission. The same
serialized Start command then synchronously enters the managed bridge before a
later command can run. The worker checks that the operation kind's registered
allowance exactly matches the advertised allowance. The main host performs the
same comparison against its feature adapter. A matching operation identity and
sequence records physical acceptance before that comparison. A mismatch then
fails the epoch and uses the host registration's allowance while draining, so
a later valid settlement can still release the realm naturally; it never
silently narrows the liveness set or erases evidence that managed execution was
admitted.

`Progress` and `Settled` are legal only after `Accepted`. `Rejected` is legal
only before acceptance. Duplicate acceptance, rejection after acceptance,
progress before acceptance, duplicate settlement, and any current-epoch
operation message for an absent record fail the epoch. These are explicit
receive outcomes, not absent transitions that an implementation may treat as
ignored input.

An old epoch's event is stale and cannot affect the current epoch, operation
sink, liveness clock, or diagnostics attributed to the current realm. An
invalid message from the current worker source proves only that the source ran.
Before matching readiness it immediately revokes and closes the partial realm;
after matching readiness it begins bounded protocol-failure draining. The
`StartupFailed` and a mismatched `Ready` echo retain `startup`; every other
pre-readiness fault retains its specific `protocol` or `worker-message`
failure kind. A delivered envelope that is malformed or illegal in startup
state, including `Heartbeat` or unsolicited `ProbeAcknowledged`, is
`protocol`. A browser worker `error` or `messageerror` event is
`worker-message`.

## Cancellation and record release

The main adapter sends at most one `Cancel` for an assigned record. It sends
none for a held start settled before readiness. Main-to-worker message ordering
ensures a posted `Start` precedes its posted `Cancel`.

The worker processes `Start`, `Cancel`, and `Probe` through one serialized
protocol-command lane. A command cannot let the next command begin until it
has committed its required immediate response:

- `Start` commits `Accepted` or `Rejected`, invokes an accepted facade function
  without awaiting its returned Promise in the lane, and attaches settlement
  handling outside the lane;
- `Cancel` commits `CancelAcknowledged` after its keyed managed cancellation
  call completes; and
- `Probe` commits `ProbeAcknowledged`.

The lane can await a command's boundary call, but it cannot run a later probe
handler concurrently with that unfinished command. Calling an accepted facade
can execute a synchronous managed prefix before it returns the Promise; that
prefix can still monopolize the worker event loop. Once the Promise is
returned, the lane does not await physical operation completion; otherwise one
operation would prevent cancellation and starts for every other operation.

The worker responds:

- `running` after the managed keyed-cancellation export returns an active
  result; or
- `not-active` when the managed bridge reports no cancellable active entry at
  the cancellation linearization point, including an entry whose settlement
  has already begun.

`not-active` is legal only for a sequence no greater than the worker's received
high-water mark. It never proves cancellation or physical closure. It may
arrive while the wire record is still accepted when managed settlement has
sealed cancellation but has not yet crossed the release barrier. The main host
retains the record until its `Rejected` or `Settled` closure also arrives.

No cancellation acknowledgment can commit while the operation is still
awaiting its `Accepted` or `Rejected` response.

The main protocol record is released when:

- `Rejected` or `Settled` has arrived; and
- the one cancellation acknowledgment has arrived when `Cancel` was sent.

After physical closure, the sink and payload are released even when a compact
control-response record must remain for a pending acknowledgment. No feature
observer or managed callback is retained by that record.

Missing responses are not inferred from elapsed operation duration. A bounded
control-response grace can cause the host to post a `Probe` after an
unanswered `Start` or `Cancel`. That probe snapshots the exact earlier response
obligations it covers. The serialized command lane delays its acknowledgment
until every earlier command has completed its response-commit point. If a
covered required response is still absent when the matching
`ProbeAcknowledged` arrives, the handler completed without its contractually
required response; the epoch enters bounded unexpected draining.

Completing the readiness flush evaluates response obligations already due at
that transition, including grace that elapsed while a held `Start` was posted
synchronously. A response dispatched during the flush retires its obligation
normally. This evaluation preserves the fresh post-readiness watchdog origin;
startup and flush time do not age that watchdog.

A covered response that arrives before the probe acknowledgment retires its
obligation normally. The acknowledgment still retires the probe and does not
fail the epoch merely because its original snapshot is now empty. Requests
posted after the probe are not covered by it.

While a probe is outstanding, the host marks every later `Start` and `Cancel`
command record against that exact probe sequence. That mark is immutable:
posting a later command never re-marks an earlier record. Main-to-worker
delivery preserves posting order, the serialized worker lane preserves
processing order, and the worker posts `ProbeAcknowledged` and later immediate
responses through the same worker-to-main channel whose delivery preserves
posting order. An immediate response for one of those commands while that same
probe remains outstanding therefore proves that the lane passed the probe
without committing `ProbeAcknowledged`. The host records `control-response`
failure and begins bounded draining before treating that later response as
liveness evidence. The valid response still commits its physical meaning:
acceptance records admission, rejection records never-admitted closure, and a
cancellation acknowledgment records release progress. Those transitions
permit later settlement or acknowledgment to drain naturally but do not renew
task-loop evidence. The host dispatches every Worker source event through one
epoch-local FIFO, including decoded envelopes, structural decode failures, and
browser `error` and `messageerror` events. An event received reentrantly during
response, terminal, diagnostic, progress, or quiescence callbacks joins the
same queue behind every earlier arrival and cannot start a nested drain.
Data-derived decode facts are captured when the event arrives; phase-dependent
startup versus post-readiness classification is selected when that FIFO item
dispatches. Physical browser crash is the sole destruction override and does
not wait behind the FIFO. This serialization preserves cross-operation
multiplexing, prevents a same-record settlement or
cancellation acknowledgment from being rejected against the record's
pre-response phase, and prevents a later probe acknowledgment from overtaking
an earlier command response. A reentrant restart or disposal request with
earlier queued source events appends one cutoff marker: those earlier events
dispatch first, then the cutoff revokes later queued source authority and hard
terminates the epoch. A matching
acknowledgment or other register retirement
discharges every mark for that probe; it cannot accuse a response that arrives
after the register has moved to a later probe. This proof uses local posting
order and the response's existing operation correlation; it does not add a
wire command sequence.

Control-response proof and the silence watchdog share one physical probe
register. The register holds the probe sequence, its immutable
response-obligation snapshot, whether the watchdog has adopted it, and the
watchdog stage-one timestamp when applicable:

- a first watchdog expiry with no outstanding probe sends one watchdog probe;
- a first watchdog expiry with an outstanding control-response probe adopts
  that probe instead of sending a second one, and starts the second watchdog
  interval at the adoption time;
- a control-response grace that expires while any outstanding probe lacks that
  command in its immutable snapshot cannot add it after send, so it records a
  deferred control-probe need; and
- after the matching acknowledgment retires the outstanding probe, any still
  unresolved deferred obligation keeps the next control-probe dispatch enabled
  until the response arrives, that probe is sent, or the epoch enters draining.

An acknowledgment first validates every covered response obligation. A missing
required response fails the epoch even though the acknowledgment also proves
that the worker task loop ran. Otherwise it clears watchdog suspicion, retires
the register, and schedules any unresolved deferred control probe before that
obligation can be forgotten. While the response remains unresolved, the host
must eventually send that probe or enter draining. This arbitration preserves
one sequence space and at most one in-flight probe without letting an older
watchdog probe prove completion of a later command.
Heartbeat evidence clears suspicion and renews the liveness origin but does
not retire the shared probe register, invalidate its sequence, discard its
response-obligation snapshot, or prove that the serialized lane passed the
probe. A serialized response for a command posted after the probe takes the
failure path above instead of ordinary renewal.

An unbounded operation that prevents the worker from processing both the
request and probe has not supplied that proof. It remains eligible only for an
explicit hard-termination choice, not an elapsed-time inference. The same is
true when neither the probe acknowledgment nor a causally later serialized
response arrives: heartbeats alone do not manufacture a missing-response
proof.

Probe sequences begin at one per epoch, strictly increase, and never wrap.
Retiring a valid probe whose sequence is JavaScript's maximum safe integer
first validates its immutable covered-response obligations. A covered omitted
response commits `control-response` failure. Otherwise retirement commits
unexpected `probe-exhaustion` failure, closes admission, and begins bounded
draining; the epoch never remains live without an allocatable next sequence.
An acknowledgment must match the one outstanding probe. Duplicate, future, or
stale current-epoch acknowledgments fail the epoch.

## Epoch-work leases

The worker-side managed reporter validates the sender contract owned by the
managed bridge. It retains one highest started work sequence and the active
lease set.

`EpochWorkStarted` requires a safe sequence strictly greater than the
high-water mark and an allowance registered for that producer class. The
sequence is consumed before the lease becomes active. A duplicate,
non-increasing, malformed, or non-conforming start fails the epoch.

`EpochWorkFinished` requires the exact active sequence and removes it once.
An unmatched or duplicate finish fails the epoch. Completed work IDs need no
tombstones because a finish not in the active set is invalid and any later
start must exceed the high-water mark.

The main host mirrors the active set from validated worker messages. A lease
continues to influence liveness after all related operation records quiesce.
Epoch close releases the whole set; it does not synthesize successful finishes.

## Post-readiness liveness

After readiness, the worker posts a heartbeat whenever its event loop can
process the heartbeat task. Event-loop liveness evidence is deliberately
narrow:

- `Heartbeat` and `ProbeAcknowledged` prove a worker task ran;
- `Accepted`, `Rejected`, and `CancelAcknowledged` prove the serialized
  protocol-command lane processed an inbound task, except that a response for
  a command posted after an unacknowledged probe first proves
  `control-response` failure as described above; and
- matching readiness ends startup but is not a post-readiness renewal.

Matching readiness establishes the first complete post-readiness idle
allowance. It is not a later renewal source.

`Progress`, `Settled`, `EpochWorkStarted`, and `EpochWorkFinished` can be posted
from a managed continuation or synchronous managed callback without processing
another worker task. They prove realm activity, not task-loop responsiveness,
and do not by themselves clear watchdog suspicion.

The current silence allowance is the largest of:

- the configured idle heartbeat interval plus scheduling tolerance;
- every accepted, not-yet-settled operation allowance; and
- every active epoch-work allowance.

An awaiting-admission start contributes no allowance. Existing accepted work
must cover the interval until the worker processes that start. Removing a
shorter operation never shrinks a longer concurrent allowance. When any active
allowance is `unbounded`, silence alone cannot trigger automatic termination.

The host retains the active-time origin of the last task-loop evidence.
`Accepted` installs its allowance and then renews that origin because its
serialized response is task-loop evidence, unless its command was marked as
posted after the still-outstanding probe and therefore takes the
`control-response` failure path first. A bounded-to-bounded allowance-set
change recomputes the deadline from the retained origin, not from the topology
message's receipt. Starting or finishing bounded epoch work and settling a
bounded operation therefore cannot renew the watchdog through callback churn.
If the newly computed deadline has already elapsed, the corresponding
watchdog stage is immediately eligible.

An unbounded allowance disables silence judgment without manufacturing
task-loop evidence. When the final unbounded allowance closes, the host starts
one complete bounded interval at that receipt because no bounded deadline was
enforceable during the unbounded period. This is the only allowance-topology
transition that grants a fresh origin. Numeric maximum selection among
concurrent bounded allowances is an implementation responsibility: changing
the maximum changes the deadline length but preserves its existing origin.

Allowance time is active time. It does not accrue while the document is hidden,
frozen, in the back-forward cache, or otherwise lifecycle-suspended. The host
also detects a main-loop scheduling gap beyond tolerance. A lifecycle resume
or detected-gap recovery clears watchdog suspicion and rebases one complete
current allowance. It preserves the shared probe register, its sequence, and
its immutable response-obligation snapshot. It neither invalidates an
in-flight acknowledgment nor sends a replacement probe. If the next first
expiry occurs before acknowledgment, the watchdog adopts the still-outstanding
register; it sends a new probe only when no probe is outstanding.

The post-readiness watchdog is two-stage:

1. the first complete bounded silent interval moves the epoch to suspect,
   obtains the shared probe register as described above, and starts one new
   complete current allowance from the probe send or adoption time; and
2. only a second complete silent interval, with no task-loop evidence and no
   lifecycle or main-loop discontinuity, begins watchdog-failure draining.

Task-loop evidence clears suspicion and rebases the current allowance. An
allowance-set change never clears suspicion or retires the shared probe.
Bounded-to-bounded changes recompute the suspect deadline from the existing
stage-one origin. A final-unbounded close grants one complete bounded interval
from that close while retaining the same outstanding probe and suspicion.
Other valid messages remain subject to protocol validation and operation
routing but do not renew the watchdog. A matching `ProbeAcknowledged` proves
worker task-loop liveness but does not prove managed progress or cancellation
responsiveness.

`Suspect` is an admitting sub-state of ready: existing protocol validation,
sequence consumption, and the serialized command lane continue to accept new
assignments until draining commits. A newly committed serialized response for
a command not marked against the outstanding probe is task-loop evidence and
clears the suspicion in the ordinary way. A response marked against that probe
takes the `control-response` failure path instead.

This watchdog detects loss of the worker event loop only where the active
allowance set is bounded. It is not an operation timeout, managed deadlock
proof, or prompt-cancellation guarantee.

## Failure, draining, and realm release

Epoch closure has one cause:

- planned restart; or
- unexpected startup, worker crash, worker-declared, message, protocol,
  control-response, probe-exhaustion, or watchdog failure.

A valid current-epoch `EpochFailed(diagnostic)` is the worker's cooperative
declaration that its managed bridge or epoch-work reporter reached an
unrecoverable boundary failure. Receipt commits unexpected closure with
failure kind `worker-declared` and begins bounded draining. It is neither a
feature outcome nor task-loop liveness evidence, and later operation or work
messages cannot replace the committed closure.

A valid current-epoch `StartupFailed(diagnostic)` is legal only before
matching readiness. It commits unexpected closure with failure kind `startup`,
revokes the partial realm, terminates it immediately, and closes every
activated held producer with that boundary failure. It is distinct from
post-readiness `EpochFailed`, which permits bounded draining of admitted
managed work and epoch-work leases.

Other than `StartupFailed` and a mismatched `Ready` echo, any current-source
message or protocol fault while still waiting for matching readiness uses the
same immediate partial-realm closure mechanics while retaining its specific
`worker-message` or `protocol` failure kind. Matching readiness ends that
classification before held-start flushing begins. Faults committed during the
flush or later use bounded unexpected draining.

Entering draining atomically refuses new assignments and fixes the exact
closure kind and diagnostic identity.
The host first seals that logical closure on every still-pending assigned
record without invoking a producer sink. It then uses operation authority's
two-phase terminal publication contract: call `commitTerminal` for every
sealed record, retain every returned publication capability, and only then
exercise those capabilities. Observer failure or diagnostic reentrancy from
one publication therefore sees every sibling outcome as final. Every
still-pending assigned producer receives one physical closure:

- planned restart reports `Canceled("worker-restarted")`; or
- unexpected failure reports one boundary failure.

The unexpected epoch diagnostic is reported once through the runtime failure
observer after all publication capabilities have been exercised. Per-operation
unexpected-terminal diagnostics remain reserved for an operation's own
unexpected `Settled` result; multiplying one realm failure across every
operation diagnostic observer would reintroduce cross-operation authority and
duplicate the same boundary evidence.

A synchronous physical `Settled` or `Rejected` received while those committed
publications are being exercised records physical closure but cannot report
quiescence, release the retained sink, or retire the record until every
operation in the closure snapshot has published. Physical response reentrancy
therefore cannot erase a sibling publication capability or let realm release
overtake a committed terminal event. A synchronous `CancelAcknowledged`
following that physical response records the acknowledgment but remains under
the same deferred-retirement barrier.

Ordinary success, failure, progress, or cancellation messages arriving after
that commit cannot replace the fixed closure. They may still prove physical
release while the realm drains.

A later protocol, message, or worker-declared fault during draining cannot
replace the first committed cause, diagnostic, or producer outcomes. A worker
crash during draining proves that the realm is already gone, so the host closes
and releases immediately while preserving that first cause and those outcomes
rather than waiting for the remaining drain budget.

A worker that has declared epoch failure refuses new starts, cancellation
commands, probes, and epoch-work leases. Settlement callbacks and
epoch-work-finish calls for work admitted before that declaration still emit
their physical release evidence. They cannot replace the committed failure,
but they permit the main host to release the failed realm naturally.

A live failed realm receives one bounded active-time drain budget. It may
release accepted operations and epoch-work leases naturally. It is terminated
when all assigned resources release or when the budget expires. Missing
protocol responses therefore cannot retain records indefinitely after the
host has proof of the defect. Lifecycle suspension pauses the remaining drain
budget without resetting it.

A worker crash has already lost the realm and closes without a drain wait.
Startup failure terminates its partial realm. Planned restart may terminate
immediately after fixing operation closures or may use the same bounded drain
path for cooperative release; no success outcome can escape the restart.

Hard termination:

1. revokes the epoch's message and callback authority;
2. detaches worker event handlers;
3. calls `Worker.terminate()` when a live worker object remains;
4. releases held, active, control-response, probe, and epoch-work records; and
5. reports quiescence for every assigned producer not already quiescent.

Clock unsubscription, lifecycle unsubscription, and transport detachment are
fallible external callbacks. Each failure reports one callback diagnostic but
cannot interrupt the remaining mandatory cleanup steps. In particular, a
throwing detach callback cannot prevent the host from attempting
`Worker.terminate()` or permit `realmReleased` before that attempt. Detach and
termination errors are reported only after both mandatory callbacks have been
attempted, and replacement startup remains reserved through that physical
shutdown barrier.

Binding is also an epoch-visible lifetime. If a synchronous bind-time event
closes the epoch before `bind()` returns its detach capability, logical
authority is revoked immediately, but detach, termination, finalization, and
realm release wait for that return. The host then attempts detach before
termination under the ordinary shutdown barrier.

Successful return from `Worker.terminate()` is the host's ordinary evidence
that physical destruction completed. A browser worker crash event
independently establishes that the realm is already gone, whether it arrives
before or after a failed termination attempt. The host still attempts detach
and `Worker.terminate()` cleanup when the crash arrives first, but a throwing
termination call cannot revoke that destruction evidence or block finalization,
realm release, and replacement startup. Without crash-established physical
loss, a termination throw leaves the epoch unreleased, refuses replacement
startup, and, for disposal, retains its clock and lifecycle subscriptions
rather than claiming that teardown completed. Every termination throw remains
a callback diagnostic. A detach failure does not block release when either
destruction proof exists.

If hard termination is requested reentrantly from a producer-sink callout,
steps 1-3 remain immediate unless an ordinary current-source event has already
arrived behind the event being dispatched. In that case restart or disposal
appends the cutoff behind the earlier arrival. Operation quiescence, record
release, and realm release wait until the outermost epoch producer callout
returns and the enclosing closure-publication transition completes. For
unexpected closure, that transition includes publishing every committed
operation outcome and the one runtime failure, so the old epoch's
`realmReleased` callback cannot precede its runtime failure callback. The host
counts producer callback lifetime around every sink invocation, including
terminal, diagnostic, progress, cancellation, and quiescence publication.

No worker message or managed callback can be delivered through this host after
revocation. Realm release claims that worker code and operation-scoped
callbacks can no longer run. It does not claim immediate browser-process
memory reclamation.

Prepared bindings are operation-authority-owned and are not force-abandoned by
hard termination. The Worker may already be destroyed, but `realmReleased`
remains deferred until every epoch-visible prepared lifetime either abandons or
activates into the committed closure and finishes its terminal and quiescence
callbacks. A replacement epoch may start while that old-epoch notification is
deferred; the later notification retains the old epoch token.

Disposing the runtime host is terminal. It closes any current epoch, revokes
its clock and lifecycle subscriptions, and rejects every later epoch start
rather than creating work whose deadlines can no longer be evaluated.
Failure from one subscription's unsubscribe callback does not prevent revoking
the other subscription. Disposal closes operation admission and physically
terminates the current epoch before invoking either unsubscribe callback, so a
cleanup diagnostic cannot activate work in the disposing realm.

Creating a replacement worker is explicit retry policy. It allocates a new
epoch and new operation assignments. Messages and identities from the old
epoch remain stale and are never replayed into the replacement.

## Mock interaction

This docs-only demo shows cold start, supersession, managed work, and hard
release without giving the worker DOM authority:

```text
main runtime host                 worker realm             managed bridge
-----------------                 ------------             --------------
create epoch-7
hold Start(op-41, seq 41)
hold Start(op-42, seq 42)
cancel op-41 locally
  terminal canceled(user)
  quiesced

                                  bootstrap facade
                          <-      Ready(epoch-7)
post Start(op-42)          ->
                          <-      Accepted(op-42, unbounded)
                                  invoke export       ->   register op-42
                          <-      Progress(op-42)
publish only if op-42 still owns its view
                                  begin speculative index
                          <-      EpochWorkStarted(work-9, bounded)
                                  Promise fulfills    <-   close callback,
                                                            remove op-42
                          <-      Settled(op-42, Succeeded)
terminal succeeded
quiesced

                                  compute in yielding slices
                          <-      EpochWorkFinished(work-9)
                                  retain epoch-local cache

later Start(op-43)         ->
                          <-      Accepted(op-43, bounded)
                                  satisfy from worker cache
                          <-      Settled(op-43, Succeeded)
terminal succeeded
quiesced

planned restart epoch-7
revoke source, terminate worker
create epoch-8
old epoch-7 messages are stale
```

A neighboring browser-native producer uses operation authority without this
adapter. That case proves the worker runtime is one producer placement rather
than the owner of all asynchronous feature behavior.

The speculative index demonstrates that physical preparation can precede its
logical operation without manufacturing publication authority. Its completed
lease no longer affects liveness, while its cache remains disposable
epoch-local feature state.

## Model evidence

The companion model directory contains seven finite models:

- `InspectWebWorkerValidation.tla` covers registered versus advertised
  operation allowances, epoch-work identity validation, and mismatch-driven
  failed draining.
- `InspectWebWorkerProtocol.tla` covers held starts, admission ordering,
  cancellation acknowledgment order and closure, atomic settlement, replay
  high-water marks, missing-response proof, epoch-work identity, and exact
  worker-source plus epoch-token replacement binding.
- `InspectWebWorkerLifecycle.tla` covers startup active time, matching
  readiness, bounded and unbounded silence, probes, lifecycle and main-loop
  discontinuities, planned versus unexpected closure, draining, termination,
  and quiescence.
- `InspectWebWorkerProbe.tla` composes control-response and watchdog probe
  triggers over the one physical probe register, including adoption, deferred
  coverage, exact acknowledgment, exhaustion, missing-response failure, and
  exact-probe marking for a causally later serialized response.
- `InspectWebWorkerProbeMarks.tla` expands the seam to two command records and
  two probe generations. Its exact-command failure property rejects false
  attribution to a replacement probe; a separate global-mark mutation detector
  exposes overwriting an earlier record's authoritative mark.
- `InspectWebWorkerClosureIdentity.tla` expands the lifecycle closure into its
  exact failure kind, diagnostic identity, and producer outcomes. It checks
  that a later different fault or worker crash cannot replace the first
  committed closure.
- `InspectWebWorkerOperationValidation.tla` receives invalid operation
  envelopes explicitly and separates operation ID from sequence. It checks
  that every invalid ordering named by the protocol enters protocol-failure
  draining, including a newer sequence for an active ID, while the same newer
  sequence remains valid for a fresh ID.

The models separate allowance validation, protocol bookkeeping, clock and
worker lifetime, cross-cutting probe arbitration, per-command mark ownership,
exact closure identity, and exogenous operation-message validation. Their
README records assumptions, bounds, checked properties, counterexample
mutations, and exact TLC results. They prove no TypeScript, browser, worker,
managed, or feature implementation behavior.

## Required implementation gates

`inspect-web-worker-envelope-validation` is the focused Node TypeScript
sub-gate for the first dependency-ordered implementation slice. It covers
direction-specific raw construction for the current closed envelope inventory,
own-data-property and exact-field validation from `unknown`, wire-scalar
validity, exact version and expected-epoch checks, exact managed-settlement
structure, unbound initialization, and owner-selected bounded payload decoding
as a separate second stage with exact failure paths. It does not select a
feature adapter, allocate host identity, construct closure or idle-compatible
authority, implement runtime state, or satisfy `inspect-web-worker-protocol`.

`inspect-web-worker-protocol` is the complete base Release TypeScript gate. It
uses injected worker-like transport, active-time and lifecycle signals, and
deterministic scheduling rather than a real browser worker. It includes:

- own-property narrowing of every envelope from `unknown`, with malformed,
  inherited, accessor-backed, oversized, unsafe-integer, wrong-version, and
  wrong-epoch negatives;
- positive safe-integer epoch-token allocation, exact token equality, no
  page-lifetime reuse or wrap, visible exhaustion, and authority requiring both
  the current token and exact bound worker source, including same-token
  different-worker and same-worker wrong-token negatives, with exact-source
  invalid traffic failing rather than producing stale diagnostics;
- synchronous `Initialize` send failure rejecting the epoch start after
  preserving failure reporting, realm release, and token non-reuse;
- bind-time `Ready` failing before initialization dispatch, synchronous
  responses from inside `send(Initialize)` remaining legal, and any closure
  committed by that send preventing a success-shaped start result even while
  physical epoch work keeps the failed realm draining;
- bootstrap encoding reserving start ownership before callout, with reentrant
  start rejecting as `epoch-active` and reentrant disposal rejecting the outer
  start as `host-disposed` without creating a post-disposal epoch;
- terminal host disposal rejecting later starts after closing the current
  realm and lifecycle subscriptions;
- heterogeneous main and fake-worker operation catalogs whose independently
  typed registrations retain narrow producer adapters, per-operation boundary
  mappings and diagnostic codecs, and fail closed on an absent record while
  another differently typed kind remains live;
- preparation, abandonment, activation, held starts, sequence-order readiness
  flush without warm-start overtaking, synchronous `Accepted` delivery from an
  emitted held start during that flush, repeated response-driven yields
  resuming every remaining held start in sequence, startup-budget completion at
  matching readiness before synchronous flush work, overdue response grace
  evaluated at flush completion without aging the watchdog or probing a
  not-yet-due or already-retired response, held cancellation,
  `StartupFailed`-driven startup closure, and activation after a committed
  close preserving planned-restart cancellation versus unexpected boundary
  failure without posting `Start`, including cross-session preparation
  reentrancy that preserves assignment order and queued cancellation plus
  same-session replacement and encoder rejection that release sequence gaps;
- current-source malformed or protocol-invalid messages before `Ready`
  immediately closing the partial realm with their specific failure kind,
  while the corresponding post-readiness faults use bounded draining,
  closure sealing followed by commit-all and publish-all operation authority,
  including a first terminal feature observer that throws and whose diagnostic
  observer attempts to cancel a committed sibling and requests termination:
  sibling cancellation is a no-op, both selected boundary outcomes remain
  final, exactly one runtime failure publishes, and old-epoch `realmReleased`
  follows that runtime failure, plus synchronous sibling `Settled` and
  `Rejected` responses that cannot erase the sibling's committed publication
  or quiescence, including a following `CancelAcknowledged` that cannot retire
  the physically closed sibling before publication completes;
- worker-declared failure refusing new work while later settlement and
  epoch-work-finish callbacks for already-admitted work continue to emit
  physical release evidence for natural failed-realm draining;
- strictly increasing operation sequences with legal gaps, high-water replay
  rejection after record release, a valid newer sequence for a fresh ID,
  active duplicate IDs consuming that sequence before failure, no silent
  return between consumption and admission-or-failure, and visible sequence
  exhaustion;
- `Accepted` before progress or settlement, `Rejected` as the exclusive
  never-accepted closure, exact registered allowance comparison, and explicit
  fail-closed receipt tests for duplicate acceptance, rejection after
  acceptance, progress before acceptance, duplicate settlement, absent-record
  messages, including an absent ID while another record remains live, plus
  allowance-mismatched physical acceptance followed by natural settlement and
  realm release during failed draining for both warm admission and synchronous
  held-start flushing;
- atomic `Settled` mapping to diagnostic, terminal, and quiescence call order;
- managed Promise rejection entering epoch failure rather than becoming a
  feature result;
- running cancellation, `not-active` race validation, one acknowledgment,
  closure-before-record-release, and compact post-settlement acknowledgment
  retention;
- unanswered start and cancellation requests where matching probe
  acknowledgment from the serialized command lane proves a missing covered
  response and begins bounded draining, a later serialized response proving a
  missing probe acknowledgment, heartbeats alone preserving that outstanding
  register without manufacturing proof, exact immutable command-record marks
  that a later command cannot overwrite, deferred probe dispatch that cannot
  stall after the older register retires, main-loop recovery preserving every
  unresolved command's remaining active-time grace, plus asynchronous
  cancellation that cannot be overtaken by a later probe, and a physically
  valid later response applying admission, rejection, or cancellation-release
  state after proving the missing acknowledgment so draining can complete,
  same-record physical evidence deferred until its triggering response commits,
  nested cross-operation response replay preserving FIFO order, a probe
  acknowledgment unable to overtake an earlier acceptance, and malformed data
  plus browser `error` and `messageerror` events unable to overtake an earlier
  reentrant progress callback or be erased by a later reentrant restart or
  disposal cutoff;
- probe-sequence monotonicity, matching, exhaustion, duplicate, future, and
  stale acknowledgment cases, including retirement of the maximum safe
  sequence entering `probe-exhaustion` draining rather than leaving a degraded
  epoch;
- worker and main-side epoch-work high-water and active-set validation,
  unmatched or duplicate finish, allowance mismatch, and release on epoch
  close, including delayed physical admission and work-start messages during
  draining remaining eligible only to prove later settlement or finish;
- an initial operation transferring an anticipated shared producer to an
  epoch-work lease before quiescence, lease release followed by a feature-owned
  fixture retaining epoch-local cache state, a later ordinary operation
  consuming that cache, and restart discarding it;
- `EpochFailed` mapping to `worker-declared` unexpected draining, with no
  continued admission and no feature-result reinterpretation;
- the first committed closure retaining its exact failure kind, diagnostic
  identity, and producer outcomes when a different protocol, worker-message,
  worker-declared, or worker-crash fault arrives during draining, with
  post-readiness worker `error` and `messageerror` permitting natural
  operation and epoch-work release before the bounded fallback, including
  synchronous faults during the post-`Ready` held-start flush and immediate
  quiescence for later never-posted held starts after closure publication;
- registered idle-compatible producer classes receiving opaque capabilities,
  with separately constructed equivalent main and worker registries accepting
  legitimate leases, initialization rejecting a worker registry configured for
  a different total idle allowance, and unregistered classes, unknown
  allowances, or over-budget classes failing or requiring epoch-work leases as
  appropriate;
- current-epoch invalid ordering as protocol failure and old-epoch messages as
  stale no-ops;
- failure-complete sink notification and record release when sink callbacks
  throw, compiler-complete boundary-error tables that require no closure-time
  mapper callout, clock and lifecycle unsubscribe plus transport-detach
  failures that cannot admit disposed work or a replacement before physical
  termination, direct reentrant starts from detach or terminate callbacks
  rejecting until that barrier completes, disposal reentered from teardown
  and disposal during pre-commit transport creation deferring subscription
  cleanup through successful termination, cleanup diagnostics observing the
  completed barrier, bind-time closure retaining the returned detach capability
  before teardown and release, termination failure without independent crash
  evidence withholding realm release, replacement, and disposal subscription
  cleanup, crash-established physical loss surviving a throwing cleanup call,
  disposal during a throwing creation call retaining `host-disposed` as the
  returned classification, and preserved realm-release ordering, plus
  synchronous fake-worker admission aborting before invocation when its
  response reentrantly terminates the realm; and
- a neighboring browser-native producer proving operation authority does not
  depend on the worker adapter.

`inspect-web-worker-browser-binding` is the first Release browser sub-gate. It
uses the product-published client, module Worker, seven generated facades, and
actual .NET runtime. It covers cold and warm managed calls, idle heartbeats,
explicit replacement with a new epoch, native termination, failed bootstrap,
and a stalled Wasm initialization while page input remains available. Its
focused TypeScript cases cover overlapping lifecycle suspensions, initial
hidden state, scheduling-gap recovery before deadline reads, and subscription
cleanup. It is not the complete lifecycle or managed CPU responsiveness gate.

`inspect-web-worker-lifecycle` is the complete Release browser gate and must include:

- cold and warm bootstrap through the consumer-owned barrier;
- responsive JavaScript with permanently stalled .NET initialization;
- a non-renewable startup active-time budget that only matching `Ready`
  satisfies;
- matching `Ready` immediately before the startup deadline succeeding and at
  or after the deadline closing the partial realm;
- startup suspension and main-loop discontinuity preserving, not resetting,
  the remaining budget;
- idle heartbeat, probe acknowledgment, and serialized command-response
  renewal, with progress and managed reporter callbacks explicitly excluded;
- every bounded operation and epoch-work class naming its product-structural
  event-loop-return gate, with browser measurements validating margin;
- bounded speculative preparation continuing to process heartbeats and an
  expected later request, plus an unbounded case that makes no prompt-service
  claim;
- unbounded operation and epoch-work silence preventing automatic watchdog
  termination;
- the largest concurrent allowance winning until its record closes;
- `Accepted` and task-loop evidence renewing the active-time origin;
- bounded settlement and epoch-work churn preserving that origin while
  recomputing the current maximum allowance;
- final-unbounded close, lifecycle resume, and main-loop resume granting one
  complete current allowance under their distinct rules;
- one shared probe register, watchdog adoption of an outstanding control
  probe, deferred control coverage behind an older probe, and no
  duplicate in-flight probe;
- heartbeat evidence clearing suspicion without retiring the shared register
  or its obligation snapshot, while a response for a command posted after the
  probe proves its missing acknowledgment and begins draining;
- lifecycle and main-loop recovery preserving an outstanding probe sequence
  and control-response snapshot;
- first expiry obtaining a probe without termination, second expiry permitting
  failure only under continuous main-loop scheduling;
- hide, freeze, back-forward-cache, resume, overdue watchdog-task, and long
  main-thread-task scenarios;
- valid liveness, matching probe acknowledgment, malformed message, worker
  `error`, worker `messageerror`, bootstrap rejection sending
  `StartupFailed` and immediately releasing the partial realm, protocol
  failure, worker-declared failure, and watchdog loss, with an illegal
  pre-`Ready` heartbeat or probe acknowledgment immediately releasing the
  partial realm as `protocol`, while worker `error` and `messageerror` retain
  `worker-message`;
- planned restart cancellation versus unexpected boundary failure;
- multi-record closure sealing before any producer or runtime callback, with a
  callback for one record unable to cancel or replace a sibling's fixed
  outcome;
- unexpected `Settled` publication using the atomic operation-authority sink,
  including diagnostic-reentrant cancellation;
- a later fault during draining preserving the first committed cause and
  outcomes, plus a crash during draining closing immediately without waiting
  for the drain deadline;
- preparation followed by epoch closure before activation, preserving planned
  versus unexpected classification, with activation or abandonment completing
  before `realmReleased`;
- terminal-observer reentrant restart revoking the Worker immediately while
  deferring quiescence, record release, and `realmReleased` until the active
  producer callout returns;
- bounded failed draining with early natural release and deadline hard
  termination;
- source revocation and no message, progress callback, managed callback, or
  sink delivery after realm release, with the same callback path first shown
  reachable while source authority is live;
- unresolved operation quiescence only after its resource settles or the realm
  is destroyed; and
- explicit replacement-worker creation with stale old-epoch events.

`inspect-web-worker-responsiveness` is a Release real-browser gate. It runs
pinned managed CPU work in the worker while asserting document paint and input
on the main thread. It includes one neighboring operation not used to tune any
bound, progress, cooperative cancellation, supersession, planned restart,
unexpected worker loss, and worker-local cache loss across epochs.

## Migration

Implementation proceeds without moving operation authority or feature meaning
into the runtime host:

1. introduce the descriptor-safe wire codec under
   `inspect-web-worker-envelope-validation` (**implemented**);
2. add the shared runtime core, host authority, and complete
   `inspect-web-worker-protocol` gate (**implemented**);
3. adapt the current generated facade bootstrap behind the consumer-owned
   bootstrap operation (**implemented** with the browser-binding sub-gate);
4. add durable event batches after #5570 and the relevant #5419 handoff supply
   their prerequisite contracts, before moving the existing Package Query
   stream; #5566 and #5570 are merged;
5. consume the [single-runtime client cutover](inspect-web-jsexport-partitioning.md#page-facing-engine-client)
   and move the existing source operation through a typed worker adapter;
6. connect keyed cancellation, progress, managed settlement, and epoch-work
   reporting through their existing owners;
7. prove real-browser responsiveness and hard realm release; and
8. migrate additional feature lifecycle adapters only after each declares its
   own payload and liveness policy.

These eight production-host adoption steps are tracked by #5418 under #4937
and #4571, with composition handoffs mapped by #5095. The first feature
consumer is the existing source operation in
issue #5420, not a duplicate feature. The client owner retires direct
main-thread managed dispatch in one production cutover; step 8 is subsequent
feature lifecycle adoption, not permission to retain a second runtime. The approved
inspect-web-only scope does not imply a CLI runtime migration. Typed feature
results continue to reach their existing rendering owners; this binding adds
no rendering or format-lowering domain.

The implementation starts from the official .NET 11 Web Worker hosting pattern
but replaces stringly method invocation with the generated inspect-web facade
and this closed protocol.

## Runtime evidence

The direction is informed by:

- [dotnet/runtime#95452](https://github.com/dotnet/runtime/issues/95452),
  which completed official worker documentation, samples, and template work;
- [dotnet/aspnetcore#65037](https://github.com/dotnet/aspnetcore/pull/65037),
  which added the official Web Worker template and client;
- [dotnet/runtime#114918](https://github.com/dotnet/runtime/issues/114918),
  which tracks a single-threaded runtime in a worker with application-owned
  messaging;
- [dotnet/runtime#121879](https://github.com/dotnet/runtime/pull/121879),
  which made a single-threaded worker build consistently use sidecar mode;
- [dotnet/runtime#65559](https://github.com/dotnet/runtime/issues/65559),
  which records the UI-locking motivation; and
- [dotnet/aspnetcore#65823](https://github.com/dotnet/aspnetcore/issues/65823),
  which tracks incomplete debugging for C# running inside a worker.

Debugging limitations make structured traces, reproducible inputs, and browser
integration tests required evidence even when interactive managed breakpoints
are unavailable.

## Non-claims

This owner does not claim:

- that `Task`, Promise, microtask, or progress delivery yields a DOM paint;
- prompt cooperative cancellation or any maximum feature checkpoint latency;
- automatic watchdog recovery while any active allowance is `unbounded`;
- recovery of worker-local cache or in-flight physical work after termination;
- DOM access from the worker or a managed callback;
- WebAssembly multithreading, `Task.Run`, or parallel managed execution;
- browser-process memory reclamation immediately after `terminate()`; or
- correctness of operation authority, the generated facade, the managed
  bridge, feature payloads, or the later composition map.
