# Inspection workspace close model

`InspectionWorkspaceClose.tla` models the target
`InspectionWorkspace` admission, group-release ownership, and asynchronous
close protocol described by
[`../../inspection-space.md`](../../inspection-space.md#workspace-close-and-group-release-authority).

## Scope

The model contains a bounded set of direct and coordinated group constructions.
A direct group uses a workspace-owned release completion. A coordinated group
uses an adjacent owner-issued completion and may have active external leases.
Each group also has one abstract busy bit representing the callback and
owned-resource quiescence protocol checked in more detail by
[`AssemblyContextGroupLifecycle.tla`](../assembly-context-group-lifecycle/AssemblyContextGroupLifecycle.tla).

The modeled interactions are:

- construction admission before workspace close;
- completion that either publishes while open or enters cleanup-only routing
  after close;
- failed or canceled construction that settles admission without a group;
- coordinated lease acquisition and return;
- lease-backed group work that begins after workspace close;
- adjacent-owner release before workspace close;
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
| `ReleaseBeginsAtMostOnce` | Each group starts terminal release at most once. |
| `ActiveLeasesPreventRelease` | A coordinated group is not released while an existing lease remains active. |
| `ClosedWorkspaceIsDrained` | A closed workspace has no in-flight construction and has complete terminal group reports. |
| `EveryStartedBuildFinishes` | Under weak fairness, every admitted construction reaches its owner-visible completion. |
| `EveryRequestedReleaseCompletes` | Under weak fairness, every release request reaches a terminal group outcome. |
| `ClosingWorkspaceEventuallyCloses` | Under weak fairness, a closing workspace eventually reaches terminal close. |

Admission, authority, lease-drain, late-routing, group-quiescence,
workspace-completion, and report-completeness claims use monotonic witnesses
that record the required action pre-state. Mutation configurations weaken the
corresponding transition and falsify the witness rather than merely restating a
mutated guard.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks all safety properties with one direct group, one coordinated group, and up to two leases. |
| `Liveness.cfg` | Checks construction, release, and workspace-close progress under weak fairness. |
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
| `BrokenLatePublication.cfg` | Publishes a construction result after close; TLC must violate `LateCompletionRoutesToCleanup`. |
| `BrokenEarlyWorkspaceCompletion.cfg` | Completes workspace close before group release; TLC must violate `WorkspaceCloseWaitsForQuiescence`. |
| `BrokenCleanupOmission.cfg` | Completes workspace close without every group report; TLC must violate `CleanupFailuresRemainVisible`. |
| `BrokenDoubleRelease.cfg` | Starts terminal release more than once; TLC must violate `ReleaseBeginsAtMostOnce`. |
| `BrokenStrandedNoGroupCompletion.cfg` | Selects failed construction without settling admission; TLC must violate `EveryStartedBuildFinishes`. |

## Running TLC

Use the repository-pinned `v1.8.0` `tla2tools.jar`:

```bash
cd docs/models/inspection-workspace-close
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Safety.cfg InspectionWorkspaceClose.tla
java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
  -cleanup -config Liveness.cfg InspectionWorkspaceClose.tla
for config in ReachabilityDirectRelease ReachabilityCoordinatedDrain \
  ReachabilityOwnerFirstRelease ReachabilityPostCloseLeaseWork \
  ReachabilityNoGroupCompletion ReachabilityLateCleanup \
  ReachabilityCleanupFailure BrokenAdmissionAfterClose \
  BrokenLeaseAfterClose BrokenWrongDirectAuthority \
  BrokenWrongCoordinatedAuthority BrokenReleaseWithLease \
  BrokenReleaseBeforeQuiescence BrokenLatePublication \
  BrokenEarlyWorkspaceCompletion BrokenCleanupOmission \
  BrokenDoubleRelease BrokenStrandedNoGroupCompletion; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -cleanup -noGenerateSpecTE -config "$config.cfg" \
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

Checked on macOS with Homebrew OpenJDK `25.0.4.1` and the repository-pinned
TLA+ `v1.8.0` prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The
checked `tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 2,330 | 1,215 | 17 |
| `Liveness.cfg` | No error | 2,330 | 1,215 | 17 |
| `ReachabilityDirectRelease.cfg` | Direct release reached | 194 | 136 | 6 |
| `ReachabilityCoordinatedDrain.cfg` | Coordinated post-lease drain reached | 363 | 247 | 7 |
| `ReachabilityOwnerFirstRelease.cfg` | Owner-first release reached | 37 | 32 | 4 |
| `ReachabilityPostCloseLeaseWork.cfg` | Post-close lease work reached | 203 | 142 | 6 |
| `ReachabilityNoGroupCompletion.cfg` | No-group completion reached | 31 | 27 | 4 |
| `ReachabilityLateCleanup.cfg` | Late-result cleanup reached | 196 | 138 | 6 |
| `ReachabilityCleanupFailure.cfg` | Failed cleanup report reached | 101 | 77 | 5 |
| `BrokenAdmissionAfterClose.cfg` | `NoBuildAdmissionAfterClose` violated | 19 | 14 | 3 |
| `BrokenLeaseAfterClose.cfg` | `NoLeaseAdmissionAfterClose` violated | 100 | 74 | 5 |
| `BrokenWrongDirectAuthority.cfg` | `ReleaseUsesSingleOwner` violated | 82 | 64 | 5 |
| `BrokenWrongCoordinatedAuthority.cfg` | `ReleaseUsesSingleOwner` violated | 37 | 32 | 4 |
| `BrokenReleaseWithLease.cfg` | `CoordinatedReleaseWaitsForLeases` violated | 94 | 72 | 5 |
| `BrokenReleaseBeforeQuiescence.cfg` | `ReleaseWaitsForGroupQuiescence` violated | 217 | 145 | 6 |
| `BrokenLatePublication.cfg` | `LateCompletionRoutesToCleanup` violated | 30 | 26 | 4 |
| `BrokenEarlyWorkspaceCompletion.cfg` | `WorkspaceCloseWaitsForQuiescence` violated | 84 | 65 | 5 |
| `BrokenCleanupOmission.cfg` | `CleanupFailuresRemainVisible` violated | 375 | 243 | 7 |
| `BrokenDoubleRelease.cfg` | `ReleaseBeginsAtMostOnce` violated | 216 | 150 | 6 |
| `BrokenStrandedNoGroupCompletion.cfg` | `EveryStartedBuildFinishes` violated | 34 | 29 | 8 |

The normal configurations explored their complete bounded state graphs. Each
reachability configuration stopped when its intended direct, coordinated,
owner-first, post-close lease-work, no-group, late-result, or failure path
became observable. Every mutation stopped at the property named in the table:
admission or lease creation crossed close, either release kind used the wrong
owner, release preceded external or group quiescence, a late group published,
the workspace closed early, a cleanup result disappeared, release began twice,
or a failed construction stranded its admission.
