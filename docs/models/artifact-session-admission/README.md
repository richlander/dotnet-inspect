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
- exact scenario witnesses for an incompatible pending demand and for an
  attached waiter whose request arrives only after disposal enters draining;
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

Exact multi-group lifetime orchestration now ships in
`InspectionWorkspace`, which retains a transferred artifact session until
every stored exact dependent-group receipt completes. The focused
[`ArtifactSessionGroupRelease.tla`](../artifact-session-group-release/ArtifactSessionGroupRelease.tla)
model checks that adjacent handoff. This admission model deliberately retains
its one-group abstraction and continues to describe future single-flight,
generation, and cancellation behavior rather than duplicating the shipped
workspace-close model.

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
| `CancelledDemandsWereRequested` | Every cancelled outcome follows a recorded caller request. |
| `CancelledDemandsAreDetached` | Cancellation clears pending and attached eligibility. |
| `PendingCancellationGuardWitnessHolds` and `AttachedCancellationGuardWitnessHolds` | Cancellation actions independently recheck their request and lifecycle guards. |
| `ScenarioCancellationRequestsAreRecorded` | Both exact-race request witnesses remain part of the owner-recorded cancellation set. |
| `PendingCancellationWitnessHolds` | An incompatible pending request reaches a detached terminal cancellation. |
| `DrainingCancellationWitnessHolds` | A request recorded after disposal entered draining reaches a detached terminal cancellation. |
| `WaitingDemandsEventuallyResolve` | Every attached waiter eventually receives a terminal outcome. |
| `PendingDemandsEventuallyAttachOrResolve` | Every pending demand eventually attaches or resolves. |
| `CancellationRequestsEventuallyCancel` | Every recorded cancellation request eventually reaches `cancelled`. |
| `IncompatiblePendingCancellationEventuallyCompletes` | Every request witnessed behind an incompatible active generation reaches its matching cancellation completion. |
| `PostDisposalDrainingCancellationEventuallyCompletes` | Every request witnessed after disposal entered draining reaches its matching cancellation completion. |
| `DisposalEventuallyReleasesLeases` | Disposal eventually releases a published group's leases after quiescence. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `ArtifactSessionAdmission.cfg` | Enables both cancellation paths and checks the complete safety and liveness set. |
| `BrokenPendingCancellation.cfg` | Disables cancellation before attachment; it must violate `IncompatiblePendingCancellationEventuallyCompletes` from the exact incompatible-pending request witness. |
| `BrokenDrainingCancellation.cfg` | Disables attached cancellation after admission enters draining; it must violate `PostDisposalDrainingCancellationEventuallyCompletes` from the exact post-disposal request witness. |
| `BrokenPendingCancellationGuard.cfg` | Allows pending cancellation without a recorded request; it must violate `PendingCancellationGuardWitnessHolds`. |
| `BrokenAttachedCancellationGuard.cfg` | Allows attached cancellation without a recorded request; it must violate `AttachedCancellationGuardWitnessHolds`. |
| `ReachabilityPendingCancellation.cfg` | Negates completion of the exact incompatible-pending scenario; it must fail only after that request and cancellation execute. |
| `ReachabilityDrainingCancellation.cfg` | Negates completion of the exact post-disposal-draining scenario; it must fail only after disposal, request, and cancellation execute in that order. |

## Running TLC

Follow the repository
[TLA+ setup runbook](../../runbooks/tla-plus-setup.md) for the pinned toolchain.
Run configurations sequentially because concurrent TLC processes share the
default `states/` path unless each receives a distinct `-metadir`.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/models/artifact-session-admission

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -seed 1 -fp 1 -cleanup -coverage 1 \
  -config ArtifactSessionAdmission.cfg \
  ArtifactSessionAdmission.tla

for config in BrokenPendingCancellation BrokenDrainingCancellation \
  BrokenPendingCancellationGuard BrokenAttachedCancellationGuard \
  ReachabilityPendingCancellation ReachabilityDrainingCancellation; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
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
| `ArtifactSessionAdmission.cfg` | No error | 65,395 | 24,305 | 16 |
| `BrokenPendingCancellation.cfg` | `IncompatiblePendingCancellationEventuallyCompletes` violated | 49,489 | 21,311 | 16 |
| `BrokenDrainingCancellation.cfg` | `PostDisposalDrainingCancellationEventuallyCompletes` violated | 51,071 | 20,378 | 14 |
| `BrokenPendingCancellationGuard.cfg` | `PendingCancellationGuardWitnessHolds` violated | 15 | 15 | 3 |
| `BrokenAttachedCancellationGuard.cfg` | `AttachedCancellationGuardWitnessHolds` violated | 90 | 64 | 4 |
| `ReachabilityPendingCancellation.cfg` | `PendingCancellationNotReached` violated | 1,319 | 680 | 6 |
| `ReachabilityDrainingCancellation.cfg` | `DrainingCancellationNotReached` violated | 1,529 | 768 | 6 |

The positive run explored its complete bounded state graph. The broken pending
trace starts one generation, records cancellation for a demand pending on the
other generation, and leaves that exact scenario incomplete when the active
admission terminates. The broken draining trace starts an admission, begins
disposal, records the attached waiter's cancellation only after admission is
draining, and then cannot detach it. The request-guard mutations cancel an
unrequested pending or attached demand and are rejected by their independently
latched guard witnesses.

The pending reachability trace is `DemandArrives(g1)`,
`DemandArrives(g2)`, `DemandStartsAdmission(g1)`,
`CallerRequestsCancellation(g2)`, then `PendingDemandCancels(g2)`. The draining
trace is `DemandArrives`, `DemandStartsAdmission`, `DisposalBegins`,
`CallerRequestsCancellation`, then `AttachedDemandCancels`. Their intentional
invariant failures therefore require the exact races rather than simpler idle
or pre-disposal cancellation.
