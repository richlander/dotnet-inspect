# Inspection workspace close model

`InspectionWorkspaceClose.tla` models the shipped asynchronous
`InspectionWorkspace` admission, group-release ownership, and asynchronous
close protocol described by
[`../../inspection-space.md`](../../inspection-space.md#workspace-close-and-group-release-authority).

## Scope

The model contains a bounded set of direct and coordinated group constructions.
A direct group uses a workspace-owned release completion. For one distinguished
direct registration, the model instantiates
[`AssemblyContextGroupReleaseLifecycle.tla`](../assembly-context-group-lifecycle/AssemblyContextGroupReleaseLifecycle.tla)
and preserves the product's join currency: the exact group identity, that
group's terminal receipt, and its terminal result. A coordinated group uses an
adjacent owner-issued completion and may have active external leases. Each
group also has one abstract busy bit representing the callback and
owned-resource quiescence protocol checked in more detail by
[`AssemblyContextGroupLifecycle.tla`](../assembly-context-group-lifecycle/AssemblyContextGroupLifecycle.tla).

The consumer binds `Group` to the exact direct registration, invokes the
owner's request and completion actions, supplies `~groupBusy[g]` as the
owner-issued quiescence predicate, and rechecks the imported identity/result,
progress, and behavior contracts. It does not import callback, image, or
resource internals. Coordinated registrations retain their existing
owner-issued release authority and are not moved into the direct lifecycle.
The foreign-receipt control adds a second direct registration and second owner
instance; ordinary actions request and complete that owner-issued receipt
before the mutation associates it with the first registration.

The modeled interactions are:

- construction admission before workspace close;
- completion that either publishes while open or enters cleanup-only routing
  after close;
- failed or canceled construction that settles admission without a group;
- coordinated lease acquisition and return;
- lease-backed group work that begins after workspace close;
- explicit adjacent-owner release before workspace close;
- workspace close while construction, leases, or group work remain active;
- one release authority per direct or coordinated group;
- release after external leases and group work quiesce;
- successful or failed cleanup recorded in the workspace report; and
- terminal close only after every admitted construction and group release is
  complete.

## Non-claims

The model does not cover:

- package-realization keys, cache retention, capacity, or eviction;
- package selection, role topology, participant projection, or cleanup-record
  internals;
- artifact acquisition identity or content lifetime;
- `AssemblyContextGroup` image opening, resource ordering, or callback-local
  snapshots beyond the abstract busy state;
- exception payloads, report serialization, or implementation conformance; or
- the construction-time synchronous compatibility adapter, whose request-only
  disposal surface has no awaited close report;
- close-caller or report object identity, which is assigned to the named
  implementation gate in the owning design;
- thread scheduling. The design separately requires awaited progress without
  a blocking wait or background-thread dependency.

## Checked properties

| Property | Claim |
| --- | --- |
| `NoBuildAdmissionAfterClose` | A group construction starts only while the workspace is open. |
| `NoLeaseAdmissionAfterClose` | Coordinated lease admission stops when workspace close begins. |
| `ReleaseUsesSingleOwner` | Direct groups release through the workspace completion and coordinated groups through their owner-issued completion. |
| `CoordinatedReleaseWaitsForLeases` | Coordinated release is not requested before existing leases return. |
| `LateCompletionRoutesToCleanup` | A construction completed after close never publishes into the workspace. |
| `ReleaseWaitsForGroupQuiescence` | Actual group release waits for already-admitted group work to finish. |
| `WorkspaceCloseWaitsForQuiescence` | Terminal workspace close observes every known group released. |
| `CleanupFailuresRemainVisible` | Terminal workspace close has a complete report matching every group cleanup outcome. |
| `DirectReceiptMatchesRegistration` | A direct registration can settle only from its exact group's terminal receipt. |
| `DirectReceiptResultMatchesReportSource` | The exact receipt's result is the cleanup outcome later copied into the workspace report. |
| `DirectGroupReleaseCompletionMatchesRequest` | A terminal direct-group receipt implies a prior request for that exact group. |
| `DirectGroupReleaseCompletionCarriesResult` | Exact direct-group completion and its terminal result become visible together. |
| `DirectGroupReleaseBehaviorRefinesOwner` | The direct request/completion projection refines the reusable group-release lifecycle. |
| `ForeignDirectGroupReleaseCompletionMatchesRequest` | The foreign control's second direct receipt is issued only after requesting that exact group. |
| `ForeignDirectGroupReleaseCompletionCarriesResult` | The foreign control's second owner issues its terminal identity and result together. |
| `ReleaseBeginsAtMostOnce` | Each group starts terminal release at most once. |
| `ActiveLeasesPreventRelease` | A coordinated group is not released while an existing lease remains active. |
| `RegistrationHistoryMatchesBuildOutcome` | Every group-producing admission remains in the immutable report domain. |
| `NoCleanupWithoutRegisteredGroup` | Failed, canceled, or unstarted no-group slots never gain cleanup data. |
| `ClosedWorkspaceIsDrained` | A closed workspace has no in-flight construction and has complete terminal group reports. |
| `EveryStartedBuildFinishes` | Under weak fairness, every admitted construction reaches its owner-visible completion. |
| `EveryRequestedReleaseCompletes` | Under weak fairness, every release request reaches a terminal group outcome. |
| `ClosingWorkspaceEventuallyCloses` | Under weak fairness, a closing workspace eventually reaches terminal close. |
| `ComposedDirectReleaseEventuallyCompletes` | Under weak fairness, the exact requested direct group eventually produces its terminal receipt. |

Admission, authority, lease-drain, late-routing, group-quiescence,
workspace-completion, and report-completeness claims use monotonic witnesses
that record the required action pre-state. Mutation configurations weaken the
corresponding transition and falsify the witness rather than merely restating a
mutated guard.

The imported invariants are focused diagnostics implied by behavior refinement,
not independent evidence.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks all safety properties with one direct group, one coordinated group, and up to two leases. |
| `Liveness.cfg` | Checks construction, release, and workspace-close progress under weak fairness. |
| `TwoDirectSafety.cfg` | Checks the unmutated two-direct topology and behavior refinement for both owner lifecycle instances. |
| `ReachabilityDirectRelease.cfg` | Demonstrates workspace-owned direct release. |
| `ReachabilityCoordinatedDrain.cfg` | Demonstrates close waiting for an existing coordinated lease before release. |
| `ReachabilityOwnerFirstRelease.cfg` | Demonstrates adjacent-owner release while the workspace remains open. |
| `ReachabilityPostCloseLeaseWork.cfg` | Demonstrates an existing lease starting work after workspace close. |
| `ReachabilityNoGroupCompletion.cfg` | Demonstrates a failed or canceled late construction settling without a group. |
| `ReachabilityLateCleanup.cfg` | Demonstrates a construction completed after close reaching cleanup without publication. |
| `ReachabilityCleanupFailure.cfg` | Demonstrates cleanup failure remaining a terminal report outcome. |
| `BrokenAdmissionAfterClose.cfg` | Allows construction admission after close; TLC must violate `NoBuildAdmissionAfterClose`. |
| `BrokenLeaseAfterClose.cfg` | Allows a new coordinated lease after close; TLC must violate `NoLeaseAdmissionAfterClose`. |
| `BrokenWrongDirectAuthority.cfg` | Lets the completion claim direct release; TLC must violate `ReleaseUsesSingleOwner`. |
| `BrokenWrongCoordinatedAuthority.cfg` | Lets the workspace claim coordinated release; TLC must violate `ReleaseUsesSingleOwner`. |
| `BrokenReleaseWithLease.cfg` | Requests coordinated release with an active lease; TLC must violate `CoordinatedReleaseWaitsForLeases`. |
| `BrokenReleaseBeforeQuiescence.cfg` | Completes group release while group work remains active; TLC must violate `ReleaseWaitsForGroupQuiescence`. |
| `BrokenImportedDirectGroupReleaseLifecycle.cfg` | Lets direct completion use the quiescence mutation; TLC must violate owner behavior refinement. |
| `BrokenForeignDirectReceipt.cfg` | Issues another direct group's valid terminal receipt, then lets it settle the first registration; TLC must violate `DirectReceiptMatchesRegistration`. |
| `BrokenDirectReceiptResult.cfg` | Associates the exact direct receipt with the wrong result; TLC must violate `DirectReceiptResultMatchesReportSource`. |
| `BrokenLatePublication.cfg` | Publishes a construction result after close; TLC must violate `LateCompletionRoutesToCleanup`. |
| `BrokenEarlyWorkspaceCompletion.cfg` | Completes workspace close before group release; TLC must violate `WorkspaceCloseWaitsForQuiescence`. |
| `BrokenCleanupOmission.cfg` | Completes workspace close without every group report; TLC must violate `CleanupFailuresRemainVisible`. |
| `BrokenDoubleRelease.cfg` | Starts terminal release more than once; TLC must violate `ReleaseBeginsAtMostOnce`. |
| `BrokenStrandedNoGroupCompletion.cfg` | Selects failed construction without settling admission; TLC must violate `EveryStartedBuildFinishes`. |
| `BrokenOwnerFirstHistoryLoss.cfg` | Drops an owner-first group from report history; TLC must violate `RegistrationHistoryMatchesBuildOutcome`. |
| `BrokenNoGroupCleanupEntry.cfg` | Invents cleanup data for a no-group outcome; TLC must violate `NoCleanupWithoutRegisteredGroup`. |

## Running TLC

Use the repository-pinned `v1.8.0` `tla2tools.jar`:

```bash
cd docs/models/inspection-workspace-close
TLA_LIBRARY=/path/to/repo/docs/models/assembly-context-group-lifecycle
java -XX:+UseParallelGC "-DTLA-Library=$TLA_LIBRARY" \
  -cp /path/to/tla2tools.jar tlc2.TLC \
  -workers 1 -cleanup -config Safety.cfg InspectionWorkspaceClose.tla
java -XX:+UseParallelGC "-DTLA-Library=$TLA_LIBRARY" \
  -cp /path/to/tla2tools.jar tlc2.TLC \
  -workers 1 -cleanup -config Liveness.cfg InspectionWorkspaceClose.tla
java -XX:+UseParallelGC "-DTLA-Library=$TLA_LIBRARY" \
  -cp /path/to/tla2tools.jar tlc2.TLC \
  -workers 1 -cleanup -config TwoDirectSafety.cfg \
  InspectionWorkspaceClose.tla
for config in ReachabilityDirectRelease ReachabilityCoordinatedDrain \
  ReachabilityOwnerFirstRelease ReachabilityPostCloseLeaseWork \
  ReachabilityNoGroupCompletion ReachabilityLateCleanup \
  ReachabilityCleanupFailure BrokenAdmissionAfterClose \
  BrokenLeaseAfterClose BrokenWrongDirectAuthority \
  BrokenWrongCoordinatedAuthority BrokenReleaseWithLease \
  BrokenReleaseBeforeQuiescence \
  BrokenImportedDirectGroupReleaseLifecycle BrokenForeignDirectReceipt \
  BrokenDirectReceiptResult \
  BrokenLatePublication \
  BrokenEarlyWorkspaceCompletion BrokenCleanupOmission \
  BrokenDoubleRelease BrokenStrandedNoGroupCompletion \
  BrokenOwnerFirstHistoryLoss BrokenNoGroupCleanupEntry; do
  java -XX:+UseParallelGC "-DTLA-Library=$TLA_LIBRARY" \
    -cp /path/to/tla2tools.jar tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE -config "$config.cfg" \
    InspectionWorkspaceClose.tla
done
```

Run these commands sequentially. Concurrent TLC processes in one directory
share the default `states/` checkpoint path unless each receives a distinct
`-metadir`.

The normal configurations must complete without errors. Reachability and
broken configurations must stop at their intended counterexamples. A successful
mutation run means its probe no longer exercises the intended rule.

## TLC evidence

Checked on Linux with OpenJDK `21.0.12` and repository-pinned TLA+ `v1.8.0`
(`TLC2 2026.09.01.002747`, rev `95b800c`). The checked `tla2tools.jar` has
SHA-256
`dbcc75552f21978a4846688b8e23be1a6b6c0b3fcee35d78fec2df167958ec94`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 4,270 | 1,945 | 18 |
| `Liveness.cfg` | No error | 4,270 | 1,945 | 18 |
| `TwoDirectSafety.cfg` | No error | 87,986 | 30,821 | 23 |
| `ReachabilityDirectRelease.cfg` | Direct release reached | 198 | 136 | 6 |
| `ReachabilityCoordinatedDrain.cfg` | Coordinated post-lease drain reached | 394 | 255 | 7 |
| `ReachabilityOwnerFirstRelease.cfg` | Owner-first release reached | 103 | 77 | 5 |
| `ReachabilityPostCloseLeaseWork.cfg` | Post-close lease work reached | 208 | 143 | 6 |
| `ReachabilityNoGroupCompletion.cfg` | No-group completion reached | 31 | 27 | 4 |
| `ReachabilityLateCleanup.cfg` | Late-result cleanup reached | 200 | 138 | 6 |
| `ReachabilityCleanupFailure.cfg` | Failed cleanup report reached | 199 | 137 | 6 |
| `BrokenAdmissionAfterClose.cfg` | `NoBuildAdmissionAfterClose` violated | 19 | 14 | 3 |
| `BrokenLeaseAfterClose.cfg` | `NoLeaseAdmissionAfterClose` violated | 102 | 75 | 5 |
| `BrokenWrongDirectAuthority.cfg` | `ReleaseUsesSingleOwner` violated | 82 | 64 | 5 |
| `BrokenWrongCoordinatedAuthority.cfg` | `ReleaseUsesSingleOwner` violated | 100 | 76 | 5 |
| `BrokenReleaseWithLease.cfg` | `CoordinatedReleaseWaitsForLeases` violated | 216 | 145 | 6 |
| `BrokenReleaseBeforeQuiescence.cfg` | `ReleaseWaitsForGroupQuiescence` violated | 418 | 248 | 7 |
| `BrokenImportedDirectGroupReleaseLifecycle.cfg` | `DirectGroupReleaseBehaviorRefinesOwner` violated | 418 | 248 | 7 |
| `BrokenForeignDirectReceipt.cfg` | `DirectReceiptMatchesRegistration` violated | 8,653 | 4,254 | 10 |
| `BrokenDirectReceiptResult.cfg` | `DirectReceiptResultMatchesReportSource` violated | 200 | 138 | 6 |
| `BrokenLatePublication.cfg` | `LateCompletionRoutesToCleanup` violated | 30 | 26 | 4 |
| `BrokenEarlyWorkspaceCompletion.cfg` | `WorkspaceCloseWaitsForQuiescence` violated | 84 | 65 | 5 |
| `BrokenCleanupOmission.cfg` | `CleanupFailuresRemainVisible` violated | 400 | 249 | 7 |
| `BrokenDoubleRelease.cfg` | `ReleaseBeginsAtMostOnce` violated | 383 | 249 | 7 |
| `BrokenStrandedNoGroupCompletion.cfg` | `EveryStartedBuildFinishes` violated | 34 | 29 | 8 |
| `BrokenOwnerFirstHistoryLoss.cfg` | `RegistrationHistoryMatchesBuildOutcome` violated | 30 | 25 | 5 |
| `BrokenNoGroupCleanupEntry.cfg` | `NoCleanupWithoutRegisteredGroup` violated | 5 | 5 | 3 |

The normal configurations preserve the pre-composition complete state graph
exactly at 4,270 generated and 1,945 distinct states, depth 18. Coverage
comparison retained every pre-existing action and transition count, including
direct request `69:246`, coordinated request `168:392`, and terminal
completion `468:660`; the imported actions execute on those existing direct
transitions rather than pruning them.
The unmutated two-direct configuration separately proves that both owner
lifecycle instances satisfy their identity, result, and behavior contracts
before the foreign-receipt mutation is introduced.

Each reachability configuration stopped when its intended direct, coordinated,
owner-first, post-close lease-work, no-group, late-result, or failure path
became observable. Every mutation stopped at the property named in the table:
admission or lease creation crossed close, either release kind used the wrong
owner, release preceded external or group quiescence, a late group published,
the workspace closed early, a cleanup result disappeared, release began twice,
or a failed construction stranded its admission. The foreign-receipt mutation
also proves that a valid terminal receipt issued by another direct-group owner
cannot settle the exact registration, and the result mutation proves that the
correct receipt cannot carry the wrong cleanup result. The refinement control
proves that weakening the imported quiescence guard leaves the canonical owner
behavior. The final two mutations prove that owner-first release cannot erase
report history and no-group completion cannot invent cleanup data.
