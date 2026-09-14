# Inspect-web managed operation bridge model

This directory model-checks the managed operation bridge lifecycle owned by
[Inspect-web managed operation bridge](../../inspect-web-managed-operation-bridge.md).
It supplements that readable design and proves nothing about C#, TypeScript,
interop, browser scheduling, worker protocol, or feature implementation
behavior.

## Scope

`InspectWebManagedOperationBridge.tla` models two worker-issued operation IDs
admitted to one dynamic active-operation table, and one feature-owned shared
physical producer that both operations attach to as waiters.

The module retains its original `Progress*` identifiers as abstract callout
names. Each now represents any scoped nonterminal event callout; event-category
meaning and producer ordering remain outside this lifecycle model.

The modeled mechanism is:

- synchronous registration of a complete entry before the feature body starts;
- duplicate concurrently active ID rejection that installs no second entry;
- first-accepted normalized cancellation reason, drawn from two abstract
  reasons, with at-most-once token signaling;
- a cancellation-request result that distinguishes applied, already-requested,
  and not-active;
- entry-scoped callout leases: cancellation stores the first reason and claims
  token signaling, then takes a lease and calls `Cancel` outside the table
  guard, and every callout records its failure before releasing its lease;
- settlement that seals the entry against new cancellation and events, waits
  for every in-flight callout lease to drain, then classifies and releases;
- one atomic settlement transition and one typed terminal outcome;
- the release sequence — close the event callback lease, detach the shared
  subscription, remove the exact entry, then quiesce;
- independent waiter detachment from one shared producer, where a final detach
  that leaves the producer running is an atomic handoff;
- an epoch-work lease acquired before a producer outlives its final operation
  wrapper, with a managed-owner-issued monotonic non-reused work sequence and a
  finish that happens exactly once from producer finalization;
- later waiters reusing that exact active producer lease without another
  allocation; and
- visible work-sequence exhaustion under a finite `MaxWorkSequence` bound.

The model keeps these values abstract: request, nonterminal event, result, error,
diagnostic, and cancellation-reason payloads; producer keys and cache contents;
and the opaque liveness allowance.

Deliberately outside the model, and owned elsewhere:

- main-thread publication authority and DOM publication
  (`inspect-web-operation-authority`);
- worker epochs, restart, watchdogs, boundary message validation, and receiver
  replay detection (#5093);
- `ts-jsexport` facade generation and callback authentication;
- feature-specific phases, retry, cache policy, producer-key formation, and
  last-waiter cancellation policy;
- the broker-owned epoch-fault record path taken when a `started` callback
  itself fails; and
- multiple feature sessions, more than two operations, and more than one shared
  producer.

## Assumptions

- Both operations attach as waiters to the same shared producer. Producer
  ownership stays with the feature owner; the bridge models attachment and
  detachment only.
- The producer starts when its first waiter attaches, and feature policy always
  permits stopping it once no waiter remains. Modeling the permissive policy is
  the conservative choice: it makes premature producer termination reachable if
  the bridge allowed it.
- A body observation is nondeterministic: a returned value, an
  `OperationCanceledException`-shaped cancellation with or without an accepted
  bridge reason, or an expected or unexpected producer failure.
- At most one callout lease is in flight per entry. One is enough to expose
  drain ordering; the bound makes no concurrency claim.
- A failing token callback and a failing event callback are different
  failures. A token-callback failure is recorded on the entry and forces an
  unexpected-failure terminal outcome. An event-callback failure is a
  boundary failure: it closes further events on that entry and rejects the
  exported task after release. The model still classifies the feature-body
  observation once, but that classification is inert and not returned when the
  boundary failure exists.
- Event reporting, cancellation requests, duplicate-admission attempts, and
  post-close report attempts are globally budgeted rather than per operation.
  Each budget is a state-space bound, not a throughput or fairness claim.
- Registration and body start are one atomic step in the faithful model, so
  `RegistrationPrecedesManagedWork` is non-vacuous only through its mutation.
- `MaxWorkSequence` is finite. `MaxWorkSequence = 1` exercises successful
  allocation; `MaxWorkSequence = 0` exercises exhaustion at the first required
  lease.
- Weak fairness covers body observation, callout drain, settlement,
  classification, callback close, detachment, table removal, quiescence,
  producer settlement, and producer finalization. Admission, attachment,
  cancellation, event reporting, and producer stop are not fair.

## Checked properties

| Design property | Model property |
| --- | --- |
| Registration is synchronous before the body can wait | `RegistrationPrecedesManagedWork` |
| A duplicate concurrently active ID installs no second entry | `OneActiveEntryPerId` |
| The first accepted reason cannot be overwritten | `FirstCancellationReasonWins` |
| The token is signaled at most once | `CancellationSignalsAtMostOnce` |
| Cancellation returns not-active once settlement begins | `SettlingOperationRejectsCancellation` |
| The returned result distinguishes active from not-active | `CancellationResultMatchesEntryState` |
| The reason is stored before the cancel callout runs outside the table guard | `CancelCalloutFollowsStoredReason` |
| Settlement seals the entry against new event callouts | `SettlementSealsProgressAdmission` |
| Only post-drain state classifies the result | `CalloutsDrainBeforeClassification` |
| A recorded callout failure is visible to classification | `ClassificationObservesCalloutFailure` |
| No callout failure is still unrecorded at quiescence | `CalloutFailuresRecordedBeforeQuiescence` |
| An event-callback failure closes further events | `ProgressFailureClosesCallback` |
| At most one terminal classification per admitted operation | `OneTerminalClassification` |
| Canceled outcomes carry the exact first reason, and an unexpected failure is never hidden as cancellation | `CancellationReasonIsFaithful` |
| No callback invocation after callback close | `NoCallbackAfterClose` |
| Callback close precedes table removal | `CallbackClosesBeforeRemoval` |
| Removal and release precede quiescence | `QuiescenceRequiresRelease` |
| No callout lease is still in flight at quiescence | `QuiescenceRequiresCalloutDrain` |
| Exactly one quiescence per admitted operation | `OneQuiescencePerOperation` |
| One waiter detaching never stops a producer another waiter holds | `OneWaiterDoesNotStopSharedProducer` |
| A producer outliving its final wrapper holds a started lease | `OutlivingProducerHasEpochWorkLease` |
| Work sequences are monotonic and never reused | `WorkSequenceNeverReused` |
| Exhaustion is visible rather than wrapping or silently omitted | `WorkSequenceExhaustionIsVisible` |
| An already leased producer is not falsely exhausted | `VisibleExhaustionRequiresNoActiveLease` |
| One lease handle finishes exactly once | `WorkLeaseFinishesAtMostOnce`, `WorkLeaseFinishFollowsStart` |
| Every admitted operation eventually quiesces | `AdmittedEventuallyQuiesces` |
| A running producer eventually finalizes | `RunningProducerEventuallyFinalizes` |
| A started lease eventually finishes | `StartedLeaseEventuallyFinishes` |

## Running TLC

Use the repository-pinned TLA+ tools described by the
[setup runbook](../../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/inspect-web-managed-operation-bridge
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 4 -cleanup \
  -config InspectWebManagedOperationBridge.cfg \
  InspectWebManagedOperationBridge.tla
```

Substitute any other configuration filename in `-config` to run that scenario
or mutation.

The recorded runs used OpenJDK 21.0.12 and TLA+ tools 1.8.0
(`TLC2 2026.08.21.155922`, revision `9787e65`). The checked `tla2tools.jar` has
SHA-256 `eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

## Scenario configurations

Both scenario configurations check every invariant and every temporal property.

| Configuration | Bounds | Recorded result |
| --- | --- | --- |
| `InspectWebManagedOperationBridge.cfg` | `MaxProgress = 1`, `MaxCancelRequests = 2`, `MaxWorkSequence = 1` | 8,976,337 states generated, 2,172,533 distinct states, depth 29, no error |
| `InspectWebManagedOperationBridgeExhaustion.cfg` | as above with `MaxWorkSequence = 0` | 7,180,129 states generated, 1,769,693 distinct states, depth 29, no error |

The comprehensive configuration is the primary one: it reaches successful
allocation of an epoch-work lease and its single finish. The exhaustion
configuration is separate because a zero-sequence bound is the only way to make
first-allocation exhaustion reachable with one shared producer, and it keeps
the primary configuration free of an unreachable-lease branch.

## Non-vacuity probes

Seventeen scratch reachability probes confirmed that the interesting witnesses are
reachable rather than vacuously excluded. Each probe is a negated
reachability invariant run against the scenario constants; each produced its
expected violation. They are evidence, not checked-in configurations or
additional product behaviors.

| Witness | Configuration |
| --- | --- |
| A canceled terminal outcome carrying an abstract reason | comprehensive |
| Cancellation winning over a body value that arrived anyway | comprehensive |
| An unexpected failure classified as failed despite an accepted reason | comprehensive |
| One waiter detached while another still holds the running producer | comprehensive |
| A running producer with no waiter, held by a started lease | comprehensive |
| A later waiter detaching while the same producer lease remains active | comprehensive |
| A finished work lease | comprehensive |
| A `NotActive` cancellation-request result | comprehensive |
| An `AlreadyRequested` cancellation-request result | comprehensive |
| A callout still in flight when settlement begins | comprehensive |
| A cancel callout in flight, holding a lease outside the table guard | comprehensive |
| An event callout still draining while the entry is settling | comprehensive |
| A recorded event-callback failure carried to quiescence | comprehensive |
| An event-callback failure that leaves a succeeded terminal outcome | comprehensive |
| A recorded token-callback failure reaching terminal classification | comprehensive |
| A token-callback failure outranking an expected feature failure | comprehensive |
| Visible work-sequence exhaustion | exhaustion |

## Counterexample mutations

Each mutation configuration sets `Mutation` to one deliberate defect and checks
only the invariant that defect must break. A successful mutation check is a
concrete invariant violation; a clean exit means the gate is vacuous or broken.

| Configuration suffix | Deliberate defect | Expected violation |
| --- | --- | --- |
| `BodyBeforeRegistration` | Starts the feature body before the entry is installed | `RegistrationPrecedesManagedWork` |
| `DuplicateAdmission` | Installs a second entry for an already active ID | `OneActiveEntryPerId` |
| `ReasonOverwrite` | Lets a later request overwrite the stored reason | `FirstCancellationReasonWins` |
| `SettlingAcceptsCancel` | Accepts cancellation after settlement began | `SettlingOperationRejectsCancellation` |
| `ClassifyBeforeDrain` | Classifies before entry callouts drain | `CalloutsDrainBeforeClassification` |
| `ProgressAfterSeal` | Admits an event callout after settlement sealed the entry | `SettlementSealsProgressAdmission` |
| `ProgressAfterSealQuiescence` | The same defect, observed as an undrained callout at quiescence | `QuiescenceRequiresCalloutDrain` |
| `ReleaseLeaseBeforeRecordingFailure` | Releases a callout lease before recording its failure | `ClassificationObservesCalloutFailure` |
| `ReleaseLeaseBeforeRecordingFailureQuiescence` | The same defect, observed as an unrecorded failure at quiescence | `CalloutFailuresRecordedBeforeQuiescence` |
| `CallbackAfterClose` | Invokes the event callback after close | `NoCallbackAfterClose` |
| `RemoveBeforeCallbackClose` | Removes the table entry before closing the callback | `CallbackClosesBeforeRemoval` |
| `QuiesceBeforeRelease` | Quiesces before removal and release | `QuiescenceRequiresRelease` |
| `FirstWaiterStopsProducer` | Stops the shared producer on a non-final detach | `OneWaiterDoesNotStopSharedProducer` |
| `MissingEpochLease` | Removes the final waiter without acquiring a lease | `OutlivingProducerHasEpochWorkLease` |
| `NonAtomicFinalDetach` | Commits the final waiter removal and installs the lease afterwards | `OutlivingProducerHasEpochWorkLease` |
| `IgnoreExistingLease` | Treats an already leased producer as needing another allocation and falsely exhausts it | `VisibleExhaustionRequiresNoActiveLease` |
| `DuplicateWorkFinish` | Reports `finished` twice for one lease handle | `WorkLeaseFinishesAtMostOnce` |
| `WorkSequenceReuse` | Allocates without advancing the work sequence | `WorkSequenceNeverReused` |
| `SilentExhaustion` | Continues unleased work when sequences are exhausted | `WorkSequenceExhaustionIsVisible` |

All nineteen mutations were run with the environment above and four TLC
workers; each produced its named violation:

| Configuration suffix | States generated | Distinct states | Depth |
| --- | --- | --- | --- |
| `BodyBeforeRegistration` | 24 | 21 | 4 |
| `DuplicateAdmission` | 8 | 7 | 4 |
| `ReasonOverwrite` | 971 | 496 | 6 |
| `SettlingAcceptsCancel` | 927 | 480 | 6 |
| `ClassifyBeforeDrain` | 3,443 | 1,439 | 7 |
| `ProgressAfterSeal` | 4,109 | 1,659 | 7 |
| `ProgressAfterSealQuiescence` | 214,953 | 57,110 | 11 |
| `ReleaseLeaseBeforeRecordingFailure` | 49,506 | 15,493 | 9 |
| `ReleaseLeaseBeforeRecordingFailureQuiescence` | 681,608 | 165,967 | 12 |
| `CallbackAfterClose` | 14,227 | 4,845 | 8 |
| `RemoveBeforeCallbackClose` | 4,102 | 1,670 | 7 |
| `QuiesceBeforeRelease` | 4,110 | 1,672 | 7 |
| `FirstWaiterStopsProducer` | 64,307 | 19,264 | 10 |
| `MissingEpochLease` | 21,055 | 7,091 | 9 |
| `NonAtomicFinalDetach` | 22,599 | 7,533 | 9 |
| `IgnoreExistingLease` | 1,247,441 | 323,586 | 15 |
| `DuplicateWorkFinish` | 308,354 | 81,873 | 12 |
| `WorkSequenceReuse` | 22,852 | 7,593 | 9 |
| `SilentExhaustion` | 25,950 | 8,512 | 9 |

Mutation counts are the search prefix explored before the violating state was
reported, not a complete state graph.
