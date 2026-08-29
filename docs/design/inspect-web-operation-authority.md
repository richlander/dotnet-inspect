# Inspect-web operation authority

## Status

This document defines the target main-thread operation-authority component for
[issue #5092](https://github.com/richlander/dotnet-inspect/issues/5092).
The component and its implementation gates have not landed, so operation-ID
uniqueness, shared cancellation semantics, stale-publication safety, and
quiescence remain **unverified** in the product.

The checked
[operation-authority model](models/inspect-web-operation-authority/README.md)
establishes only the bounded abstract properties recorded with that model. It
does not prove TypeScript, browser, producer, worker, or managed behavior.

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
- feature callbacks for admitted progress and a terminal result.

The authority component synchronously returns an operation handle and gives the
producer adapter an owner-issued identity plus a producer-event sink. The
adapter may represent browser `fetch`, a worker transport, or another
asynchronous producer. This component does not inspect or distinguish those
placements.

The producer adapter accepts one cancellation request and reports:

- optional typed progress;
- exactly one physical terminal result;
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

type OperationStartResult<TValue, TError, TBindError> =
  | {
      readonly kind: "started";
      readonly handle: OperationHandle<TValue, TError>;
    }
  | {
      readonly kind: "rejected";
      readonly error: TBindError;
    };
```

`OperationId` is opaque. Feature code cannot construct one from a request,
package identity, display string, or local counter. The page owner atomically
allocates it with a monotonically increasing safe-integer `sequence`.
Every allocated ID is different from every ID previously allocated by that page
owner, including identities whose producer preparation is rejected. Neither ID
nor sequence allocation resets or wraps during the page lifetime. Exhausting
the safe-integer range rejects the start before producer preparation and leaves
every existing session and cancellation count unchanged.

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

- a typed rejection after releasing any temporary resources, without retaining
  or invoking the sink; or
- a prepared binding with an already-usable cancellation endpoint and an
  `activate()` operation.

Preparation cannot report producer events. `activate()` does not throw; an
activation failure is reported through the installed sink as producer failure
followed by quiescence. These are immediate typed boundary obligations on an
adapter, not definitions of its transport or physical implementation.

Starting an operation follows one owner-controlled synchronous sequence:

1. reject the start if the feature session is disposed;
2. allocate the next page-wide identity and unpublished candidate record;
3. ask the adapter to prepare against that identity and candidate sink;
4. on rejection, return `OperationStartResult.rejected`, leave the prior
   current operation unchanged, and discard the candidate;
5. on preparation, install the candidate record and its promises as current
   while superseding the prior pending operation;
6. expose `OperationStartResult.started` with the new handle; and
7. activate the prepared binding.

The candidate is current before activation, and the cancellation endpoint
exists before callbacks can reenter. A synchronous activation callback
therefore observes the same installed record and authority checks as a deferred
callback. Reentrant cancellation, disposal, or replacement during activation
can forward through the prepared cancellation endpoint.

Each operation is bound to one producer adapter. Retrying creates a new
operation identity; neither the feature nor the adapter redispatches an old
identity.

### Publication authority

An operation may publish into its feature view only while all three conditions
hold:

- its session is active;
- it is that session's current operation; and
- its logical outcome is pending.

Progress, success, failure, and feature-state cleanup all use this one
authority predicate. Request equality, a loading flag, or an independently
maintained generation number is not a substitute.

Progress from an operation without authority is discarded. A producer success
or expected failure without authority is consumed without publication. An
unexpected late producer failure also cannot mutate the stale view, but the
authority component forwards it to the producer diagnostic observer so stale
suppression does not become silent failure suppression.

### Logical completion

The first authorized terminal transition resolves `outcome` exactly once:

- producer success resolves `succeeded`;
- producer failure resolves `failed`; or
- cancellation resolves `canceled` with the first normalized reason.

Physical producer completion after logical cancellation does not replace the
canceled outcome. Duplicate terminal reports are producer-contract failures
reported diagnostically; they do not resolve the handle again or regain
publication authority.

### Cancellation and supersession

`cancel()` is idempotent. Its first effective call:

- normalizes an omitted reason to `"user"`;
- resolves the logical outcome as canceled;
- revokes publication authority; and
- asks the bound producer adapter to cancel exactly once.

A later call changes no reason or state and sends no producer request. A call
after success, failure, or cancellation is a strict local no-op.

Starting a replacement operation applies the same transition to the prior
current operation with reason `"superseded"`. The replacement becomes current
in the same synchronous transaction, so a callback from the prior producer
cannot publish between supersession and installation.

### Quiescence

`quiesced` is independent of `outcome`. It resolves exactly once when the
producer adapter reports that physical work settled and all operation-scoped
callbacks, subscriptions, registrations, and payload references are released.
The authority component does not infer quiescence from logical cancellation, a
terminal outcome, elapsed time, or feature cleanup.

Quiescence never publishes feature state. An old operation may quiesce after a
replacement is current without changing the replacement's loading, error,
result, progress, or focus state.

### Disposal

Disposing a feature session:

- cancels its pending current operation as `"disposed"`;
- removes all feature publication callbacks;
- prevents new starts; and
- optionally awaits the session's outstanding `quiesced` promises when its
  caller requires physical resource release.

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
  A is current; loading may publish

Source session starts B
  A outcome = canceled("superseded")
  producer A receives one cancellation request
  B is current

Producer A reports progress, success, and release
  progress and success do not publish
  A.quiesced resolves
  B state is unchanged

Producer B reports success, then release
  B outcome = succeeded(value)
  success publishes to the current source view
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
TypeScript implementation, browser queues, producer internals, worker
transport, managed interop, and arbitrary operation cardinality. Those are
covered by focused implementation gates or adjacent owners.

## Required implementation gate

`inspect-web-operation-authority` is a Release TypeScript gate. It does not yet
exist and must include:

- concurrent and sequential sessions receiving opaque IDs never previously
  allocated by the page owner, plus strictly increasing safe-integer sequences;
- injected ID-reuse collisions after completion, quiescence, disposal, and
  session recreation;
- visible allocation exhaustion without adapter preparation and without
  changing any existing current operation or cancellation count;
- typed resource-free preparation rejection that leaves the prior current
  operation unchanged and produces no handle;
- prepared cancellation availability before activation, immediate callback
  reentrancy, and activation failure through terminal plus quiescence events;
- synchronous and deferred producer completion;
- one logical outcome and one quiescence resolution;
- omitted cancellation normalization, reason immutability, and exactly one
  producer cancellation request;
- supersession atomically replacing current authority;
- progress, success, failure, and cleanup mutations that try to update a stale
  view;
- unexpected stale failure reaching diagnostics without reaching feature
  state;
- terminal, cancel, and release races;
- duplicate producer reports remaining visible contract failures;
- disposal preventing starts and publication while retaining event
  consumption through quiescence;
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
- feature rendering, retry, cache, shared-work, or queue semantics; or
- browser responsiveness.

Those claims belong to #5093, #5094, #5003, #5005, individual feature owners,
and the thin composition map in #5095.
