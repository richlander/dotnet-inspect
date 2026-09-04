# Inspect-web operation authority

## Status

This document defines the main-thread operation-authority component for
[issue #5092](https://github.com/richlander/dotnet-inspect/issues/5092).
The component is implemented in
`prototypes/inspect-web/src/operation-authority.ts` and first adopted by Type
Source in `prototypes/inspect-web/src/source-inspection.ts`. Operation-ID
uniqueness, operation cancellation, stale-publication safety, and quiescence
are enforced by `prototypes/inspect-web/test/operation-authority.test.ts` and
the Type Source adoption cases in
`prototypes/inspect-web/test/source-inspection.test.ts`.

Issue #5672 adds atomic unexpected-terminal publication for the Worker runtime
consumer in #5636. It remains an operation-authority contract: producer
placement and Worker settlement decoding stay outside this owner.

Issue #5735 adds two-phase ordinary-terminal publication for the same consumer.
It lets a producer commit multiple operation outcomes before any observer
callout while leaving Worker closure selection and batching outside this owner.

The checked
[operation-authority model](models/inspect-web-operation-authority/README.md)
establishes only the bounded abstract properties recorded with that model. It
does not prove TypeScript, browser, producer, worker, or managed behavior.

## Approved host scope

This component is intentionally inspect-web browser-host policy. Replaceable
DOM-view operation lifetime and current-view publication authority are its
named host concerns; one existing source view is the first implementation
consumer. The CLI has no corresponding named consumer, so this design does not
create or claim a shared CLI/browser substrate.

The user approved this single-host scope on 2026-09-02 before implementation.
The same decision is recorded in implementation issue #5092 and end-to-end
tracker #4937.

## Responsibility

The inspect-web operation-authority component owns:

- page-wide operation identity allocation;
- one current operation for each independently replaceable feature view;
- one typed logical outcome per operation;
- cancellation, supersession, and disposal transitions;
- authority to publish progress and terminal state into the current view; and
- a separate quiescence signal after the producer releases
  operation-scoped resources.

This is one new shared responsibility. Feature components retain their input
meaning, data, rendering, loading presentation, retry, caching, queueing, and
error wording. Producer components retain physical execution, cancellation
checkpoints, terminal-result construction, resource release, and any worker or
managed lifecycle.

## Immediate boundary

A feature creates one operation session for each view whose result can be
replaced independently. Starting work gives the authority component:

- the feature session;
- a producer adapter;
- an operation-specific input owned by the feature; and
- one typed feature-event observer and one diagnostic observer supplied when
  the session is created.

The authority component synchronously returns an `OperationStartResult` and
gives a preparing producer adapter an owner-issued identity plus an event sink.
The adapter may represent browser `fetch`, a worker transport, or another
asynchronous producer. This component does not inspect or distinguish those
placements.

The producer adapter accepts one cancellation request and reports:

- optional typed progress;
- exactly one physical terminal result;
- an unexpected terminal failure through one atomic diagnostic-and-terminal
  report when that classification applies;
- physical quiescence after its operation-scoped resources are released; and
- unexpected late failures to a diagnostic observer.

The adapter owns how those reports are obtained. In particular, worker message
validation belongs to
[#5093](https://github.com/richlander/dotnet-inspect/issues/5093), managed
registry and callback semantics belong to
[#5094](https://github.com/richlander/dotnet-inspect/issues/5094), and
generated-facade bootstrap belongs to
[#5003](https://github.com/richlander/dotnet-inspect/issues/5003).

## Value contracts

```ts
declare const operationIdBrand: unique symbol;

type OperationId = string & {
  readonly [operationIdBrand]: "OperationId";
};

interface OperationIdentity {
  readonly id: OperationId;
  readonly sequence: number;
}

type OperationCancelReason =
  | "user"
  | "superseded"
  | "disposed"
  | "feature-observer-failed"
  | "timeout"
  | "worker-restarted";

type OperationTerminalOutcome<TValue, TError> =
  | { readonly kind: "succeeded"; readonly value: TValue }
  | { readonly kind: "failed"; readonly error: TError };

type OperationOutcome<TValue, TError> =
  | OperationTerminalOutcome<TValue, TError>
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
  cancel(reason?: OperationCancelReason): OperationControlResult;
}

type OperationStartError<TPrepareError> =
  | { readonly kind: "session-disposed" }
  | { readonly kind: "session-changed" }
  | { readonly kind: "identity-exhausted" }
  | { readonly kind: "feature-observer-active" }
  | {
      readonly kind: "producer-rejected";
      readonly error: TPrepareError;
    };

type OperationStartResult<TValue, TError, TPrepareError> =
  | {
      readonly kind: "started";
      readonly handle: OperationHandle<TValue, TError>;
    }
  | {
      readonly kind: "rejected";
      readonly reason: OperationStartError<TPrepareError>;
    };

type OperationControlResult =
  | { readonly kind: "applied" }
  | { readonly kind: "no-op" }
  | {
      readonly kind: "rejected";
      readonly reason: "feature-observer-active";
    };

type OperationFeatureEvent<TValue, TError, TProgress> =
  | {
      readonly kind: "started";
      readonly operation: OperationIdentity;
    }
  | {
      readonly kind: "replaced";
      readonly previousOperationId: OperationId;
      readonly operation: OperationIdentity;
      readonly reason: "superseded";
    }
  | {
      readonly kind: "progress";
      readonly progress: OperationProgress<TProgress>;
    }
  | {
      readonly kind: "terminal";
      readonly operationId: OperationId;
      readonly outcome: OperationTerminalOutcome<TValue, TError>;
    }
  | {
      readonly kind: "canceled";
      readonly operationId: OperationId;
      readonly reason: OperationCancelReason;
    }
  | {
      readonly kind: "disposed";
      readonly operationId: OperationId | null;
    };

interface OperationFeatureObserver<TValue, TError, TProgress> {
  readonly publish: (
    event: OperationFeatureEvent<TValue, TError, TProgress>,
  ) => undefined;
}

interface OperationDiagnostic {
  readonly kind:
    | "producer-contract"
    | "producer-callout"
    | "feature-observer";
  readonly operationId: OperationId | null;
  readonly error: unknown;
}

interface OperationDiagnosticObserver {
  readonly report: (diagnostic: OperationDiagnostic) => undefined;
}

interface OperationTerminalPublication {
  readonly publish: () => undefined;
}

interface OperationProducerSink<TValue, TError, TProgress> {
  readonly reportProgress: (value: TProgress) => undefined;
  readonly commitTerminal: (
    outcome: OperationOutcome<TValue, TError>,
  ) => OperationTerminalPublication;
  readonly reportTerminal: (
    outcome: OperationOutcome<TValue, TError>,
  ) => undefined;
  readonly reportUnexpectedTerminal: (
    error: TError,
    diagnostic: unknown,
  ) => undefined;
  readonly reportQuiesced: () => undefined;
  readonly reportUnexpectedFailure: (error: unknown) => undefined;
}

interface PreparedOperationProducer {
  readonly requestCancellation: (
    reason: OperationCancelReason,
  ) => undefined;
  readonly activate: () => undefined;
  readonly abandon: () => undefined;
}

type OperationPreparation<TPrepareError> =
  | {
      readonly kind: "prepared";
      readonly binding: PreparedOperationProducer;
    }
  | {
      readonly kind: "rejected";
      readonly error: TPrepareError;
    };

interface OperationProducerAdapter<
  TInput,
  TValue,
  TError,
  TProgress,
  TPrepareError,
> {
  readonly prepare: (
    identity: OperationIdentity,
    input: TInput,
    sink: OperationProducerSink<TValue, TError, TProgress>,
  ) => OperationPreparation<TPrepareError>;
}

interface OperationSession<
  TInput,
  TValue,
  TError,
  TProgress,
  TPrepareError,
> {
  start(
    input: TInput,
    adapter: OperationProducerAdapter<
      TInput,
      TValue,
      TError,
      TProgress,
      TPrepareError
    >,
  ): OperationStartResult<TValue, TError, TPrepareError>;
  cancelCurrent(reason?: OperationCancelReason): OperationControlResult;
  dispose(): OperationControlResult;
}
```

These interfaces define only the immediate preparation and event handoff; the
adapter's transport-specific implementation remains producer-owned. Feature
code controls an immediate callback through `cancelCurrent()` or `dispose()`,
because the returned handle is not observable until `start()` returns.

The page owner creates a session with its feature and diagnostic observers.
The feature observer receives only owner-issued events and retains all
responsibility for the view's data shape, rendering, focus, and wording.

Feature-event delivery is synchronous, one event at a time, and guarded against
operation-authority reentrancy. The feature-delivery guard is the first
admission check for `start()`, handle cancellation, session cancellation, and
disposal, before disposed, terminal, canceled, or idempotency checks.
`start()` therefore returns `feature-observer-active`, and the other operations
return a rejected `OperationControlResult`, during every feature event; none
changes authority state.

All immediate callback interfaces use readonly function properties, not
TypeScript methods returning `void`. Observer and producer-callout returns are
exactly `undefined`; `commitTerminal` synchronously returns only its
owner-issued publication capability. Under strict function types, this rejects
Promise-returning implementations and implementations whose event, diagnostic,
input, sink, outcome, or cancellation-reason parameter is narrower than the
owner-issued type. This is an internal typed TypeScript boundary; an adapter
that admits untyped JavaScript must validate equivalent synchronous behavior
before constructing these values.

Product feature observers are required to return normally. If one throws, the
authority performs an internal fault transition rather than reentering the
public `dispose()` operation: it catches the exception, faults and disposes that
session before any later producer callout, resolves a still pending current
outcome as `canceled("feature-observer-failed")`, preserves an already reserved
cancellation forwarding or reserves one for an activated producer, abandons a
prepared but unactivated producer, detaches the failed observer without
publishing another feature event, and reports one `feature-observer`
diagnostic. An outcome that was already terminal is not replaced. The exception
does not escape through the public session API.

The diagnostic observer is also synchronous. Authority state is final before
diagnostic delivery, so the observer may reenter operation APIs. If diagnostic
delivery throws, the authority catches it and writes the original diagnostic
plus observer failure to the browser's last-resort console sink without
recursively invoking the observer.

`commitTerminal(outcome)` separates ordinary terminal authority from observer
publication. It synchronously validates the producer record, commits the
authorized outcome, and returns an opaque one-shot
`OperationTerminalPublication` without invoking a feature or diagnostic
observer. Calling `publish()` exercises only the already-reserved feature event
or deferred producer-contract diagnostic. The capability remains valid after
the outcome commit, but replacement or disposal before publication suppresses
the stale feature event without replacing that committed outcome.

A producer that needs cross-operation atomicity first calls `commitTerminal`
for every affected sink and only then exercises the returned capabilities.
Observer failure or diagnostic reentrancy from one publication therefore sees
every sibling outcome as final. The producer must exercise each returned
capability exactly once, synchronously before returning control from the
producer callback that performed the final commit and before reporting
quiescence. Repeated publication or quiescence with an outstanding capability
is a producer-contract failure. Dropping a capability cannot be diagnosed until
the producer attempts quiescence or another physical-liveness owner detects the
missing release. `reportTerminal(outcome)` remains the one-step convenience
path implemented as commit followed by immediate publication.

Capability publication uses the same feature-event callout path as ordinary
producer reports. A nested producer publication therefore retains the existing
page-wide feature-delivery guard until the outermost event returns; the new
capability does not add a separate operation-authority reentrancy entry point.

`reportUnexpectedTerminal(error, diagnostic)` is the producer's atomic form
for a terminal failure that also requires unexpected-failure reporting. It
validates the producer record once, commits the failed outcome, and reserves
the terminal feature event before delivering the diagnostic. The observer
therefore sees final authority state: cancellation cannot replace the failure,
while replacement or disposal may proceed normally. After diagnostic delivery
or its contained exception, the owner exercises the already-reserved terminal
event even if replacement changed current authority. Reentrant disposal remains
the stronger feature-publication transition: it publishes its reserved
`disposed` event and suppresses the pending terminal feature event after
detaching ordinary publication, without replacing the committed failed outcome
or diagnostic. Physical quiescence remains a later independent producer
report. `reportUnexpectedFailure` remains the diagnostic-only path for a late
failure that is not itself a terminal result.

`OperationId` is opaque. Feature code cannot construct one from a request,
package identity, display string, or local counter. The page owner atomically
allocates it with a monotonically increasing safe-integer `sequence`.
Every allocated ID is different from every ID previously allocated by that page
owner, including identities whose producer preparation is rejected. Neither ID
nor sequence allocation resets or wraps during the page lifetime. Exhausting
the safe-integer range rejects the start before producer preparation and leaves
every existing session and cancellation count unchanged.

An ID-source collision is visible through the same `identity-exhausted` result.
The page owner consumes the attempted sequence, permanently exhausts that
identity source, and does not prepare a producer or ask the source for another
ID. This keeps the public result vocabulary closed while refusing to trust an
allocator after it proposes a page-lifetime reuse.

The sequence is allocation evidence for producer adapters that need bounded
ordering or replay checks. It is not encoded into, parsed from, or compared
through the opaque ID. A producer may ignore it when its own contract does not
need ordering.

The cancellation-reason union classifies why the logical operation lost
authority. It does not claim that physical work has stopped. Adjacent owners
may initiate a listed reason, but they do not add values or reinterpret its
meaning.

## Session lifecycle

### Start

Producer binding uses a two-phase handoff. `prepare(identity, input, sink)`
returns either:

- a typed producer rejection after releasing any temporary resources, without
  retaining or invoking the sink; or
- a prepared binding with an already-usable cancellation endpoint and an
  `activate()` operation plus an `abandon()` operation.

Preparation cannot report producer events and does not throw; its failures are
represented by the typed preparation result. `abandon()` does not throw and
releases every prepared resource synchronously without activating the producer,
retaining the sink, or reporting an event. Its successful return is the
adapter's quiescence acknowledgement for that never-activated binding. If the
candidate was already installed, the authority resolves its handle's
`quiesced` promise exactly once from that return. `activate()` does not throw;
an activation failure is reported through the installed sink as producer
failure followed by quiescence. These are immediate typed boundary obligations
on an adapter, not definitions of its transport or physical implementation.

Starting an operation follows one owner-controlled synchronous sequence:

1. return `feature-observer-active` if feature-event delivery is active;
2. return `session-disposed` if the feature session is disposed;
3. return `identity-exhausted` if no safe identity remains, before producer
   preparation;
4. allocate the next page-wide identity and unpublished candidate record, and
   capture the session revision plus current identity;
5. ask the adapter to prepare against that identity and candidate sink;
6. compare the session revision and current identity with the captured values
   regardless of the preparation result;
7. if the session was disposed, abandon a prepared binding and return
   `session-disposed`;
8. if another session transition occurred, abandon a prepared binding and
   return `session-changed`;
9. if the unchanged session received producer rejection, return
   `producer-rejected` and discard the candidate without an authority
   transition;
10. otherwise atomically install the prepared candidate as current, capture its
   exact handle for the outer return, complete the prior pending outcome as
   `canceled("superseded")`, and reserve the prior operation's one cancellation
   forwarding plus one `started` or `replaced` feature event;
11. publish the reserved feature event;
12. if publication succeeded, activate the prepared current binding; otherwise
   abandon it and resolve its `quiesced` promise under the observer-failure
   rule;
13. invoke the reserved prior cancellation endpoint; and
14. return `OperationStartResult.started` with the captured handle.

The candidate is current before activation, and the cancellation endpoint
exists before callbacks can reenter. A synchronous activation callback uses
the session's `cancelCurrent()` or `dispose()` operation and therefore observes
the same installed record as a deferred callback. It may supersede the
candidate again; in that case `start()` returns the exact captured candidate
handle, already canceled, without reading the session's new current record or
overwriting the reentrant replacement. Disposal during activation similarly
returns the captured canceled handle while leaving the session disposed.

Preparation may synchronously reenter `start()`, `cancelCurrent()`, or
`dispose()`. The revision check ensures the outer attempt never overwrites that
nested transition. A prepared candidate that lost the comparison is abandoned
before the outer call returns and never becomes current. A producer rejection
from a stale preparation is consumed without feature publication; the returned
start reason reflects `session-disposed` or `session-changed`, so feature code
cannot publish the stale producer error into the newer view.

The authority commit in step 10 completes before all three later external
callouts: feature publication, producer activation, and prior-operation
cancellation.
Cancellation-forwarded state is reserved before invoking its endpoint.
The feature event publishes before producer activation, so feature-owned
loading or replacement state exists before an immediate producer report.
`activate()` is non-throwing by the prepared-binding contract. A cancellation
endpoint exception is caught at that exact boundary, reported to the diagnostic
observer, and cannot roll back logical cancellation, prevent handle return, or
cause another forwarding attempt. Producer events and diagnostic reentrancy
may run during later callouts, but the outer start writes no authority state
after its commit.

Each operation is bound to one producer adapter. Retrying creates a new
operation identity; neither the feature nor the adapter redispatches an old
identity.

### Publication authority

An operation may publish into its feature view only while all three conditions
hold:

- its session is active;
- it is that session's current operation; and
- its logical outcome is pending.

Progress and the acquisition of a success or failure terminal-event reservation
use this one authority predicate. Cancellation, replacement, disposal, and
observer-failure cleanup instead acquire their one-time feature-event or
cancellation-forwarding reservations atomically with the transition that
revokes authority. A reservation authorizes only its captured callout; it does
not restore general publication authority. Request equality, a loading flag,
or an independently maintained generation number is not a substitute.

Progress with authority reserves one `progress` event before calling the
feature observer; no authority state is written after the callout. Progress
without authority is discarded. A producer success or expected failure without
authority is consumed without publication. An unexpected late producer failure
also cannot mutate the stale view, but the authority component forwards it to
the diagnostic observer so stale suppression does not become silent failure
suppression.

### Logical completion

The first authorized logical-completion transition atomically resolves
`outcome` exactly once and reserves exactly one corresponding feature event:

- ordinary producer success or failure may reserve `terminal` without
  publication through `commitTerminal`, while `reportTerminal` immediately
  exercises that reservation;
- atomic unexpected terminal failure reserves `terminal` before delivering its
  diagnostic and exercises that reservation after diagnostic delivery;
- direct cancellation reserves `canceled`;
- replacement reserves `replaced`, which also announces the new operation; and
- disposal reserves `disposed`.

These variants do not stack: replacement does not additionally publish
`canceled` plus `started`, and disposal does not additionally publish
`canceled`. A producer-reported canceled outcome uses `canceled` with its typed
reason. Except for the two publication-suppression rules below, each reserved
event remains authorized after the outcome or current operation changes,
publishes after the authority commit, and permits no later authority write from
that transition:

- replacement or disposal suppresses an ordinary terminal reservation returned
  by `commitTerminal` if its capability has not yet published, without changing
  the already-committed outcome; and
- disposal's atomic feature-publication transition suppresses an
  unexpected-terminal reservation waiting behind diagnostic delivery and
  publishes only `disposed`, without changing the already-committed failed
  outcome.

Physical producer completion after logical cancellation does not replace the
canceled outcome. Duplicate terminal reports are producer-contract failures
reported diagnostically; they do not
resolve the handle again or regain publication authority.

### Cancellation and supersession

`cancel()` first applies the feature-delivery guard. During feature publication
it returns `rejected` with reason `feature-observer-active` without consulting
or changing the operation record, even if the outcome is already terminal or
canceled. Outside feature publication it is idempotent. Its first effective
call:

- normalizes an omitted reason to `"user"`;
- resolves the logical outcome as canceled;
- revokes publication authority;
- reserves one `canceled` feature event; and
- asks the bound producer adapter to cancel exactly once.

A later call returns `no-op`, changes no reason or state, and sends no producer
request. A call after success, failure, or cancellation is also a strict local
no-op.
`cancelCurrent()` applies the same guard and transition through the session.

Handle cancellation and session cancellation use one callout rule: the logical
outcome, authority revocation, reason, and forwarding flag commit before the
feature event publishes; the external cancellation endpoint is invoked only
after that feature callout. Endpoint exceptions are caught at that boundary,
emitted to the diagnostic observer, and do not escape, undo the transition, or
permit another forwarding attempt. Reentrant producer events therefore observe
the canceled outcome.
Each handle remains bound to its originating operation record. Calling an old
handle never delegates to the session's current operation and cannot change a
replacement's outcome, authority, cancellation count, or producer endpoint.

Starting a replacement operation applies the same transition to the prior
current operation with reason `"superseded"`, but publishes the single
`replaced` event instead of separate `canceled` and `started` events. The
replacement becomes current in the same synchronous transaction, so a callback
from the prior producer cannot publish between supersession and installation.

### Quiescence

`quiesced` is independent of `outcome`. It resolves exactly once when the
producer adapter reports that physical work settled and all operation-scoped
callbacks, subscriptions, registrations, and payload references are released,
or when successful synchronous `abandon()` acknowledges that a never-activated
installed binding released every prepared resource.
For an activated producer, the authority accepts that report only after a
physical terminal settlement is reported and every returned terminal
publication capability is exercised. An earlier report is a producer-contract
failure and does not resolve `quiesced`.
The authority component does not infer quiescence from logical cancellation, a
terminal outcome, elapsed time, or feature cleanup.

Quiescence never publishes feature state. An old operation may quiesce after a
replacement is current without changing the replacement's loading, error,
result, progress, or focus state.

### Disposal

`dispose()` first applies the feature-delivery guard. During feature
publication it returns `rejected` with reason `feature-observer-active` without
consulting or changing session state, including while the reserved `disposed`
event is being delivered. Outside feature publication, repeated disposal
returns `no-op`. The first disposal:

- atomically marks the session disposed, detaches ordinary feature publication,
  captures the observer only for one reserved `disposed` event, cancels its
  pending current operation as `"disposed"`, clears current authority, and
  reserves cancellation forwarding for an active current producer, if any,
  instead of a separate `canceled` event;
- prevents new starts from the instant of that commit;
- publishes the reserved disposal event;
- invokes the reserved cancellation endpoint only after the complete commit,
  feature publication, and the same diagnostic exception containment as direct
  cancellation; and
- leaves callers that require physical resource release to await the retained
  handles' `quiesced` promises.

No authority state is written after the disposal callout. A synchronous
producer callback or attempted reentrant start therefore observes a disposed
session and cannot install another operation.

Producer events remain consumable by the authority component until their
records quiesce, but none can publish after disposal.

## Current inspect-web seams

Current feature coordinators independently approximate parts of this contract:

- `source-inspection.ts` uses one source generation across member, type, and
  graph source plus a singleton engine cancellation call;
- `metadata-inspection.ts` uses generation, keys, object identity, and
  per-window request sequences;
- `member-detail-inspection.ts` combines `isCurrent()` callbacks, keys, and a
  local request ID; and
- `spotlight-package-search.ts` uses a local generation around browser
  `fetch`.

These mechanisms correctly protect several current scenarios, but their IDs,
terminal behavior, cancellation, and cleanup rules are independently
maintained. The new component replaces only those authority mechanics.

The first adoption target is one source view because it already demonstrates
logical cancellation, stale-result suppression, focus-preserving rendering,
and a physical cancellation adapter. The migration must preserve its query
input, rendering, error wording, focus restoration, visibility checks, and
engine placement.

Metadata windows and shared package acquisition are not forced into one
latest-request-wins session. Each independently replaceable metadata window
may use its own session, while a shared producer remains behind an adapter
whose physical policy belongs to its owner.

## Mock interaction

This trace is the docs-only demo for the intended value:

```text
Source session starts A
  A is current
  started(A) publishes before producer A activates

Source session starts B
  A outcome = canceled("superseded")
  B is current
  replaced(A, B) publishes before producer B activates
  producer A receives one cancellation request

Producer A reports progress, success, and release
  progress and success do not publish
  A.quiesced resolves
  B state is unchanged

Producer B reports success, then release
  B outcome = succeeded(value)
  terminal(B, succeeded(value)) publishes once
  B.quiesced resolves
```

The neighboring browser-`fetch` case follows the same logical trace without a
worker or managed producer. That proves operation authority is independent of
execution placement rather than fitted to the future worker design.

## Model evidence

[`InspectWebOperationAuthority.tla`](models/inspect-web-operation-authority/InspectWebOperationAuthority.tla)
models two ordered operations in one feature session. It separates logical
outcome, physical producer state, publication authority, cancellation
forwarding, and release.

The model checks:

- one logical outcome;
- at-most-once cancellation forwarding;
- progress and terminal publication only with current authority;
- old release preserving the newer visible owner;
- release only after producer settlement;
- no callback delivery after release;
- disposal preventing another start; and
- eventual logical completion, physical settlement, and release under stated
  fairness assumptions.

Mutation configurations establish counterexamples for stale progress, late
success, late failure, duplicate logical completion, cleanup mutating the
newer view, callback delivery after release, and start after disposal.

The model permits a producer whose work was queued at logical cancellation
either to settle canceled without running or to begin physical work. Both paths
must preserve the canceled logical outcome, suppressed publication, and
eventual producer quiescence. Choosing between them is producer policy.

The model deliberately abstracts page-wide allocation, multiple sessions,
TypeScript implementation and observer callouts, browser queues, producer
internals, worker transport, managed interop, arbitrary operation cardinality,
and the two-phase commit/publication interval. In particular, exercising a
reserved event after its commit is checked by focused implementation gates,
not by the model's atomic completion action.

## Required implementation gate

`inspect-web-operation-authority` is the Release TypeScript gate implemented by
`prototypes/inspect-web/test/operation-authority.test.ts`, with first-consumer
coverage in `prototypes/inspect-web/test/source-inspection.test.ts`. Both run
under the ordinary inspect-web `npm test` gate and include:

- concurrent and sequential sessions receiving opaque IDs never previously
  allocated by the page owner, plus strictly increasing safe-integer sequences;
- injected ID-reuse collisions after completion, quiescence, disposal, and
  session recreation;
- reuse of an identity observed by a rejecting producer in the same and a
  recreated session;
- reuse of an identity observed by a prepared binding later abandoned for
  `session-changed` or `session-disposed`, in the same and a recreated session;
- exhaustion after a rejected or abandoned preparation consumes the final
  available sequence without rolling allocation state back;
- visible allocation exhaustion without adapter preparation and without
  changing any existing current operation or cancellation count;
- typed `session-disposed`, `session-changed`, `identity-exhausted`,
  `feature-observer-active`, and `producer-rejected` results;
- strict compile-time acceptance of synchronous full-parameter callback
  function properties, plus negative TypeScript cases for Promise-returning
  and narrowed-parameter feature observers, diagnostic observers, producer
  sinks, adapters, and prepared bindings;
- typed resource-free preparation rejection that leaves the prior current
  operation unchanged in the absence of reentrancy and produces no handle;
- successful preparation that reenters start, cancellation, or disposal,
  followed by revision mismatch, non-throwing abandonment, no activation, and
  no outer authority commit;
- producer rejection after preparation reentrancy preserving the nested
  transition, returning the session-change reason, publishing no stale
  producer error, and producing no outer authority commit;
- prepared cancellation availability before activation, immediate callback
  cancellation through `OperationSession`, and activation failure through
  terminal plus quiescence events;
- activation callbacks that start a replacement or dispose the session,
  requiring the outer start to return its exact captured candidate handle while
  the nested transition remains authoritative;
- prior cancellation endpoints that throw, report synchronously, or reenter
  start/disposal after the replacement authority commit;
- throwing cancellation endpoints reached through `OperationHandle.cancel()`,
  `OperationSession.cancelCurrent()`, disposal, and supersession, each proving
  commit-before-callout, one diagnostic, no escaping exception, no retry, and
  an unchanged first outcome;
- synchronous and deferred producer completion;
- two-phase terminal commit across independent sessions, with every handle
  outcome final before the first feature or diagnostic observer callout and
  diagnostic-reentrant sibling cancellation remaining a no-op;
- one-shot terminal publication, deferred producer-contract diagnostics, and
  quiescence rejection while any publication capability remains outstanding;
- replacement and disposal between commit and publication suppressing the
  stale feature event without replacing the committed outcome;
- one logical outcome and one quiescence resolution;
- omitted cancellation normalization, reason immutability, and exactly one
  producer cancellation request;
- supersession atomically replacing current authority;
- progress, success, failure, and cleanup mutations that try to update a stale
  view;
- exact `started`, `replaced`, `progress`, `terminal`, `canceled`, and
  `disposed` feature events, including start/replacement publication before
  producer activation and cancellation/disposal publication before producer
  cancellation, with no stacked cancellation/start events for replacement or
  disposal;
- terminal publication through its reserved event after logical completion,
  with no later authority write;
- feature observers that attempt reentrant start, handle cancellation, session
  cancellation, or disposal during each event kind, requiring the
  feature-delivery rejection to win over disposed, terminal, canceled, and
  idempotency results with no authority change;
- feature observers that throw during every event kind, requiring session
  fault/disposal, typed cancellation of a pending operation, abandonment before
  activation or one cancellation forwarding after activation, exactly one
  quiescence resolution for an abandoned installed candidate, no further
  feature event, one diagnostic, and no escaping exception;
- diagnostic observers that throw or reenter, requiring final authority state,
  no escaping exception, and one last-resort console report without recursion;
- atomic unexpected terminal failure committing the failed outcome before
  diagnostic reentrancy, rejecting outcome replacement by cancellation,
  preserving its terminal reservation across reentrant replacement, delivering
  diagnostic before terminal publication, and surviving observer failure;
- diagnostic-reentrant disposal retaining the committed failed outcome and
  diagnostic while the authoritative `disposed` event suppresses the pending
  terminal feature reservation and quiescence remains producer-reported;
- stale atomic unexpected terminal failure remaining diagnostic-only without
  replacing its prior logical cancellation or publishing feature state;
- unexpected stale failure reaching diagnostics without reaching feature
  state;
- terminal, cancel, and release races;
- stale-handle cancellation after supersession and after terminal/quiescence,
  leaving the replacement outcome, authority, cancellation count, and producer
  endpoint unchanged;
- duplicate producer reports remaining visible contract failures;
- disposal atomically preventing starts and publication before its endpoint
  callout, including a callout that synchronously attempts another start, while
  retaining event consumption through quiescence;
- disposal before any operation starts, producing exactly one `disposed(null)`
  event, followed by rejected start and no producer activity;
- one source-view adoption preserving its current feature behavior; and
- a neighboring browser-`fetch` adapter proving placement independence.

The gate derives its mutation set from the declared authority transitions so a
missing and a stale discriminator both fail. It includes a named non-vacuity
test that removes the common authority predicate and observes stale
publication.

## Non-claims

This owner does not claim:

- that a Promise yields a browser paint;
- that cancellation stops physical work promptly or at all;
- that a producer runs on the main thread, a worker, or another runtime;
- worker message validation, replay handling, liveness, restart, or hard
  termination;
- managed cancellation-token, progress-delegate, or result-envelope behavior;
- generated-facade construction or bootstrap;
- a CLI operation-authority abstraction;
- feature rendering, retry, cache, shared-work, or queue semantics; or
- browser responsiveness.

Those claims belong to #5093, #5094, #5003, #5005, individual feature owners,
and the thin composition map in #5095.
