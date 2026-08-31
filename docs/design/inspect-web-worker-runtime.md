# Inspect-web worker runtime

## Status

This document defines the target worker-runtime host and protocol for
[issue #5093](https://github.com/richlander/dotnet-inspect/issues/5093).
The design is not yet implemented. Its finite state models establish only the
abstract properties recorded with those models; the TypeScript, browser, and
managed gates named below remain required.

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

## Boundary shapes

The exact product types may use narrower generic parameters. These sketches
state the owned distinctions.

```ts
declare const workerEpochBrand: unique symbol;
declare const workerIdleCompatibleBrand: unique symbol;

type WorkerEpoch = string & {
  readonly [workerEpochBrand]: "WorkerEpoch";
};

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

`WorkerEpoch` is opaque and bound to the exact `Worker` object created for it.
It is never parsed for ordering. The host allocates it from page-lifetime
non-reused identity state and refuses another realm visibly if that identity
source is exhausted.

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

The first main-to-worker envelope supplies the protocol version, epoch,
structured-clone-safe bootstrap input, and the expected idle-heartbeat policy.
The worker validates that envelope before beginning the consumer-owned
bootstrap operation. A duplicate initialization envelope is an epoch protocol
failure.

The bootstrap operation owns its concrete steps. This runtime owner requires
only one result:

- fulfillment means every consumer prerequisite for managed dispatch is ready;
- rejection is startup failure; and
- the bootstrap operation retains no main-thread DOM object.

The worker registers the managed epoch-work reporter only after the generated
facade is ready and before reporting readiness. It sends `Ready` only after the
whole bootstrap operation fulfills. `Ready` echoes the exact protocol version,
epoch, and configured idle-heartbeat interval. A mismatch is startup failure,
not a partially compatible realm.

Worker creation starts one non-renewable active-time startup budget. Only a
matching `Ready` succeeds. Heartbeats or probe acknowledgments can demonstrate a responsive JavaScript
realm but cannot renew, reset, or satisfy that budget. Lifecycle suspension and a detected
main-loop discontinuity pause active elapsed time; they preserve the remaining
budget rather than grant a fresh one.

Startup rejection or budget exhaustion closes admission, terminates the
partial realm, reports unexpected startup failure for every activated held
producer, quiesces those producers after termination, and closes the epoch.

## Operation adapter

The worker producer adapter implements operation authority's two-phase
preparation contract.

Preparation synchronously:

1. rejects if no starting or ready epoch accepts assignments;
2. validates the operation reference, operation kind, feature adapter, and
   structured-clone-safe input without retaining the producer sink on failure;
3. creates one prepared binding with an already-usable cancellation endpoint;
   and
4. retains no worker or sink state until activation.

Abandoning a prepared binding is synchronous and resource-free. Activating it
installs one epoch-assigned record before any worker callout can be observed.
Activation while the epoch is starting places the record in the held queue.
Activation while ready posts `Start`. If the epoch closed between preparation
and activation, activation reports that committed closure verbatim through the
already-installed sink: planned restart reports
`Canceled("worker-restarted")`, while unexpected closure reports its boundary
failure. Both paths report quiescence and do not throw.

Held records are ordered by operation sequence. A held cancellation removes
the record without sending `Start` or `Cancel`, reports the supplied canceled
outcome, and reports quiescence. Matching readiness posts every remaining held
start in sequence order before the ready epoch accepts a new warm activation.
A held record never becomes an accepted worker record merely because the realm
became ready; it remains awaiting the worker's explicit `Accepted` response.

Admission stops synchronously when an epoch enters draining. Preparation then
rejects new work, while already prepared bindings either abandon or activate
into the committed epoch closure.

## Closed worker protocol

Every envelope carries:

- the exact protocol version;
- the exact worker epoch; and
- one closed message discriminator with all required fields.

Operation envelopes also carry the complete operation reference. The receiver
narrows `MessageEvent.data` from `unknown`, validates own data properties
without invoking accessors, rejects unsafe integers, and validates bounded
payload sizes before constructing a typed message. JSON-looking strings remain
strings unless their feature wire owner authenticates and parses them.

The main-to-worker inventory is:

```text
Initialize(bootstrap, idleHeartbeatInterval)
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

1. reporting an unexpected diagnostic when its failure kind requires one;
2. reporting the terminal result; and
3. reporting quiescence.

Those remain separate operation-authority signals, but no wire state can lose
one after receiving the other. `Rejected` is the exclusive never-accepted
alternative and proves that no operation-scoped worker or managed resource was
admitted; the adapter reports its failure and quiescence together.

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
consumed before kind or payload validation. A later validation failure returns
`Rejected`; retry uses a new operation identity.

An operation ID already present in the active map is ambiguous and fails the
epoch. Historical operation-ID uniqueness remains an operation-authority
precondition; the runtime does not retain every completed opaque ID to
re-prove it.

A valid start installs its protocol record and sends `Accepted` before invoking
managed code. `Accepted` means the operation passed worker admission. The same
serialized Start command then synchronously enters the managed bridge before a
later command can run. The worker checks that the operation kind's registered
allowance exactly matches the advertised allowance. The main host performs the
same comparison against its feature adapter. A mismatch fails the epoch and
uses the registered allowance while draining; it never silently narrows the
liveness set.

`Progress` and `Settled` are legal only after `Accepted`. `Rejected` is legal
only before acceptance. Duplicate acceptance, rejection after acceptance,
progress before acceptance, duplicate settlement, and any current-epoch
operation message for an absent record fail the epoch.

An old epoch's event is stale and cannot affect the current epoch, operation
sink, liveness clock, or diagnostics attributed to the current realm. An
invalid message from the current worker source proves only that the source ran;
it still begins protocol-failure draining.

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

A covered response that arrives before the probe acknowledgment retires its
obligation normally. The acknowledgment still retires the probe and does not
fail the epoch merely because its original snapshot is now empty. Requests
posted after the probe are not covered by it.

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
  unresolved deferred obligation is covered by the next probe.

An acknowledgment first validates every covered response obligation. A missing
required response fails the epoch even though the acknowledgment also proves
that the worker task loop ran. Otherwise it clears watchdog suspicion, retires
the register, and permits any deferred control probe to be sent. This
arbitration preserves one sequence space and at most one in-flight probe
without letting an older watchdog probe prove completion of a later command.
Other task-loop evidence clears suspicion and renews the liveness origin but
does not retire the shared probe register, invalidate its sequence, or discard
its response-obligation snapshot.

An unbounded operation that prevents the worker from processing both the
request and probe has not supplied that proof. It remains eligible only for an
explicit hard-termination choice, not an elapsed-time inference.

Probe sequences begin at one per epoch, strictly increase, never wrap, and stop
visibly at JavaScript's maximum safe integer. An acknowledgment must match the
one outstanding probe. Duplicate, future, or stale current-epoch
acknowledgments fail the epoch.

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
  protocol-command lane processed an inbound task; and
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
serialized response is task-loop evidence. A bounded-to-bounded allowance-set
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
assignments until draining commits. A newly committed serialized response is
task-loop evidence and clears the suspicion in the ordinary way.

This watchdog detects loss of the worker event loop only where the active
allowance set is bounded. It is not an operation timeout, managed deadlock
proof, or prompt-cancellation guarantee.

## Failure, draining, and realm release

Epoch closure has one cause:

- planned restart; or
- unexpected startup, worker crash, worker-declared, message, protocol,
  control-response, or watchdog failure.

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

Entering draining atomically refuses new assignments and fixes that cause.
Every still-pending assigned producer receives one physical closure:

- planned restart reports `Canceled("worker-restarted")`; or
- unexpected failure reports one boundary failure and diagnostic.

Ordinary success, failure, progress, or cancellation messages arriving after
that commit cannot replace the fixed closure. They may still prove physical
release while the realm drains.

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

No worker message or managed callback can be delivered through this host after
revocation. Realm release claims that worker code and operation-scoped
callbacks can no longer run. It does not claim immediate browser-process
memory reclamation.

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
                                  Promise fulfills    <-   close callback,
                                                            remove op-42
                          <-      Settled(op-42, Succeeded)
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

## Model evidence

The companion model directory contains four finite models:

- `InspectWebWorkerValidation.tla` covers registered versus advertised
  operation allowances, epoch-work identity validation, and mismatch-driven
  failed draining.
- `InspectWebWorkerProtocol.tla` covers held starts, admission ordering,
  cancellation acknowledgment order and closure, atomic settlement, replay
  high-water marks, missing-response proof, epoch-work identity, and stale
  epochs.
- `InspectWebWorkerLifecycle.tla` covers startup active time, matching
  readiness, bounded and unbounded silence, probes, lifecycle and main-loop
  discontinuities, planned versus unexpected closure, draining, termination,
  and quiescence.
- `InspectWebWorkerProbe.tla` composes control-response and watchdog probe
  triggers over the one physical probe register, including adoption, deferred
  coverage, exact acknowledgment, and missing-response failure.

The models separate allowance validation, protocol bookkeeping, clock and
worker lifetime, and the cross-cutting probe arbitration seam. Their README
records assumptions, bounds, checked properties, counterexample mutations, and
exact TLC results. They prove no TypeScript, browser, worker, managed, or
feature implementation behavior.

## Required implementation gates

`inspect-web-worker-protocol` is a Release TypeScript gate and must include:

- own-property narrowing of every envelope from `unknown`, with malformed,
  inherited, accessor-backed, oversized, unsafe-integer, wrong-version, and
  wrong-epoch negatives;
- non-reused epoch identity bound to the exact worker source;
- preparation, abandonment, activation, held starts, sequence-order readiness
  flush without warm-start overtaking, held cancellation,
  `StartupFailed`-driven startup closure, and activation after a committed
  close preserving planned-restart cancellation versus unexpected boundary
  failure without posting `Start`;
- strictly increasing operation sequences with legal gaps, high-water replay
  rejection after record release, active duplicate IDs, and visible sequence
  exhaustion;
- `Accepted` before progress or settlement, `Rejected` as the exclusive
  never-accepted closure, and exact registered allowance comparison;
- atomic `Settled` mapping to diagnostic, terminal, and quiescence call order;
- managed Promise rejection entering epoch failure rather than becoming a
  feature result;
- running cancellation, `not-active` race validation, one acknowledgment,
  closure-before-record-release, and compact post-settlement acknowledgment
  retention;
- unanswered start and cancellation requests where matching probe
  acknowledgment from the serialized command lane proves a missing covered
  response and begins bounded draining, plus asynchronous cancellation that
  cannot be overtaken by a later probe;
- probe-sequence monotonicity, matching, exhaustion, duplicate, future, and
  stale acknowledgment cases;
- worker and main-side epoch-work high-water and active-set validation,
  unmatched or duplicate finish, allowance mismatch, and release on epoch
  close;
- `EpochFailed` mapping to `worker-declared` unexpected draining, with no
  continued admission and no feature-result reinterpretation;
- registered idle-compatible producer classes receiving opaque capabilities,
  with unregistered or over-budget classes requiring epoch-work leases;
- current-epoch invalid ordering as protocol failure and old-epoch messages as
  stale no-ops;
- failure-complete sink notification and record release when adapter callbacks
  throw; and
- a neighboring browser-native producer proving operation authority does not
  depend on the worker adapter.

`inspect-web-worker-lifecycle` is a Release browser gate and must include:

- cold and warm bootstrap through the consumer-owned barrier;
- responsive JavaScript with permanently stalled .NET initialization;
- a non-renewable startup active-time budget that only matching `Ready`
  satisfies;
- startup suspension and main-loop discontinuity preserving, not resetting,
  the remaining budget;
- idle heartbeat, probe acknowledgment, and serialized command-response
  renewal, with progress and managed reporter callbacks explicitly excluded;
- every bounded operation and epoch-work class naming its product-structural
  event-loop-return gate, with browser measurements validating margin;
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
- heartbeat and serialized command-response evidence clearing suspicion
  without retiring the shared register or its obligation snapshot;
- lifecycle and main-loop recovery preserving an outstanding probe sequence
  and control-response snapshot;
- first expiry obtaining a probe without termination, second expiry permitting
  failure only under continuous main-loop scheduling;
- hide, freeze, back-forward-cache, resume, overdue watchdog-task, and long
  main-thread-task scenarios;
- valid liveness, matching probe acknowledgment, malformed message, worker
  `error`, worker `messageerror`, bootstrap rejection sending
  `StartupFailed` and immediately releasing the partial realm, protocol
  failure, worker-declared failure, and watchdog loss;
- planned restart cancellation versus unexpected boundary failure;
- preparation followed by epoch closure before activation, preserving planned
  versus unexpected classification;
- bounded failed draining with early natural release and deadline hard
  termination;
- source revocation and no message, progress callback, managed callback, or
  sink delivery after realm release;
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

1. introduce the worker host, protocol parser, and fake-worker Release gates;
2. adapt the current generated facade bootstrap behind the consumer-owned
   bootstrap operation;
3. move one long-running source or package inspection through a typed worker
   operation adapter;
4. connect keyed cancellation, progress, managed settlement, and epoch-work
   reporting through their existing owners;
5. prove real-browser responsiveness and hard realm release; and
6. migrate additional feature adapters only after each declares its own
   payload and liveness policy.

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
