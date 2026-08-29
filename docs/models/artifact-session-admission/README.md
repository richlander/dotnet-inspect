# Artifact session admission model

`ArtifactSessionAdmission.tla` models the owner-managed admission lifecycle for
`ArtifactSetSession`. It is the executable interaction companion to
[Artifact acquisition and workspace composition](../../design/artifact-acquisition-and-workspaces.md#artifactsetsession).

## Scope

The model contains three demands, two admission generations, one admission
reservation, and at most one published group's lease lifecycle. It explores:

- pending demands that carry one immutable requested generation;
- single-flight start and compatible join, with incompatible demands remaining
  pending until the active admission terminates;
- caller cancellation as a recorded owner-visible request;
- cancellation before attachment, while attached to an in-flight admission,
  and after disposal has moved that admission to draining;
- adapter success and failure, final-waiter draining, disposal-forced draining,
  and late-result suppression;
- atomic publication to every still-authorized waiter; and
- group quiescence and lease release after workspace disposal.

An adapter cannot complete while an attached waiter has a recorded
cancellation request. The owner first detaches and resolves that waiter; the
adapter may then resolve the remaining waiters or drain if none remain. A
cancelled pending demand loses its requested generation, so it cannot later
start, join, replan, reserve, or acquire.

## Non-claims

The model abstracts:

- budget arithmetic, local-path identity, adapter capability identity, content
  digests, participant roles, and query-lease authorization;
- several source acquisitions within one aggregate admission;
- more than one simultaneously retained published group;
- a caller changing the generation requested by one demand;
- adapter cancellation mechanics and cleanup substeps; and
- implementation conformance.

The one-retained-group bound keeps the state space finite; it is not a product
restriction. All-or-nothing publication across several source acquisitions is
owned by an implementation gate, not inferred from this model.

## Checked properties

| Property | Claim |
| --- | --- |
| `AdmissionCoherence` | Idle, in-flight, and draining admission states have coherent generation, reservation, and waiter state. |
| `WaiterGenerationInvariant` | Every attached demand requested the active generation. |
| `DisposalPreventsPublication` and `PublishSafetyWitnessHolds` | Disposal closes in-flight publication, and successful publication independently records that disposal had not begun. |
| `OutcomeStableWitnessHolds` | A terminal demand outcome never changes. |
| `AuthorizedOutcomeWitnessHolds` | Only a demand attached immediately before adapter completion receives its outcome. |
| `LeaseSafetyWitnessHolds` | A published group's leases release only after disposal and group quiescence. |
| `CancellationRequestCoherence` | A demand with recorded cancellation is unresolved or terminally cancelled, never published, failed, or disposed. |
| `CancelledDemandsAreDetached` | Cancellation clears pending and attached eligibility. |
| `PendingCancellationWitnessHolds` | The pending-cancellation transition produces a detached terminal demand. |
| `DrainingCancellationWitnessHolds` | Cancellation after disposal moved an attached admission to draining produces a detached terminal demand. |
| `WaitingDemandsEventuallyResolve` | Every attached waiter eventually receives a terminal outcome. |
| `PendingDemandsEventuallyAttachOrResolve` | Every pending demand eventually attaches or resolves. |
| `CancellationRequestsEventuallyCancel` | Every recorded cancellation request eventually reaches `cancelled`. |
| `DisposalEventuallyReleasesLeases` | Disposal eventually releases a published group's leases after quiescence. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `ArtifactSessionAdmission.cfg` | Enables both cancellation paths and checks the complete safety and liveness set. |
| `BrokenPendingCancellation.cfg` | Disables cancellation before attachment; it must violate `CancellationRequestsEventuallyCancel`. |
| `BrokenDrainingCancellation.cfg` | Disables attached cancellation after admission enters draining; it must violate `CancellationRequestsEventuallyCancel`. |
| `ReachabilityPendingCancellation.cfg` | Negates the pending-cancellation witness; it must fail when that transition executes. |
| `ReachabilityDrainingCancellation.cfg` | Negates the draining-cancellation witness; it must fail when that transition executes. |

## Running TLC

Follow the repository
[TLA+ setup runbook](../../runbooks/tla-plus-setup.md) for the pinned toolchain.
Run configurations sequentially because concurrent TLC processes share the
default `states/` path unless each receives a distinct `-metadir`.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/models/artifact-session-admission

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -coverage 1 \
  -config ArtifactSessionAdmission.cfg \
  ArtifactSessionAdmission.tla

for config in BrokenPendingCancellation BrokenDrainingCancellation \
  ReachabilityPendingCancellation ReachabilityDrainingCancellation; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" \
    ArtifactSessionAdmission.tla
done
```

The first command must complete without error. Each command in the loop must
exit unsuccessfully on the property named in the configuration table; a
successful mutation or reachability run means its probe no longer demonstrates
the intended rule.

## TLC evidence

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned TLA+ `v1.8.0`
prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The repository runbook prefers Java 25, but it was not installed on this shared
host. Java 21 meets the tool's Java 11-or-later requirement, so the central
runtime was not replaced.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `ArtifactSessionAdmission.cfg` | No error | 49,508 | 18,395 | 16 |
| `BrokenPendingCancellation.cfg` | `CancellationRequestsEventuallyCancel` violated | 38,431 | 15,609 | 16 |
| `BrokenDrainingCancellation.cfg` | `CancellationRequestsEventuallyCancel` violated | 40,086 | 15,248 | 15 |
| `ReachabilityPendingCancellation.cfg` | `PendingCancellationNotReached` violated | 97 | 67 | 4 |
| `ReachabilityDrainingCancellation.cfg` | `DrainingCancellationNotReached` violated | 1,496 | 711 | 6 |

The positive run explored its complete bounded state graph. The broken pending
trace records cancellation for an incompatible demand that has not joined; with
the transition disabled, the demand remains pending and may never resolve. The
broken draining trace starts an admission, begins disposal, records the
attached waiter's cancellation, and then cannot detach it. The reachability
traces respectively execute cancellation directly from pending state and after
an attached admission has moved to draining.
