# Assembly context participant-resource lifecycle model

`AssemblyContextParticipantResourceLifecycle.tla` models participant-scoped
leases over group-owned prepared resources. It is executable evidence for
[InspectionSpace participant-scoped prepared-resource
leases](../../inspection-space.md#participant-scoped-prepared-resource-leases).

The model instantiates the reusable
`AssemblyContextGroupReleaseLifecycle` owner with one exact group identity.
Participant/resource registration and lease state remain local to this
InspectionSpace composition.

## Scope

The bounded model contains:

- two independently releasable participants;
- two registered resources with participant-local derived state;
- six leases covering every participant/resource pair and repeating two pairs;
- individual participant release and full-group release;
- resource retirement after pair-local lease quiescence;
- snapshot release after all participant resources retire; and
- exact retained-image accounting.

The join currency is the same conceptual product key: exact participant
registration plus exact registered resource identity.

## Non-claims

The model does not cover:

- snapshot acquisition or format admission;
- resource construction or producer semantics;
- cleanup failure payloads or aggregation;
- abandoned leases under a fairness assumption that requires disposal;
- QuerySpace capability selection;
- workspace ownership of multiple groups; or
- implementation conformance.

Release tests remain the implementation gates. The model checks the bounded
protocol and its composition with the exact-group terminal receipt owner.

## Checked properties

| Property | Claim |
| --- | --- |
| `NoLeaseAdmissionAfterRelease` | A lease is admitted only before participant and group release request. |
| `ParticipantResourcesWaitForLeases` | Participant-resource state does not retire while a lease for that pair remains active. |
| `ParticipantResourcesPrecedeSnapshots` | Snapshot release observes resource retirement and participant lease quiescence. |
| `SiblingReleaseUsesOwnState` | One participant cannot release because a sibling became quiescent. |
| `ActiveLeasesRetainParticipants` | Every active lease retains its participant snapshot and resource state. |
| `RetainedImageAccountingIsExact` | The retained image charge equals participants whose snapshot has not released. |
| `ResourcesPrecedeReleasedSnapshots` | Every released participant has retired resources and no active lease. |
| `GroupCompletionRequiresParticipantRelease` | Full-group completion follows every participant snapshot release. |
| `ReleasedGroupOwnsNothing` | A released group retains no image, resource state, or active lease. |
| `GroupReleaseBehaviorRefinesOwner` | The projected request/completion behavior refines the canonical exact-group lifecycle. |
| `EveryRequestedParticipantReleases` | Under lease-disposal and cleanup fairness, every requested participant eventually releases. |
| `RequestedGroupEventuallyCompletes` | Under the same fairness, an exact group release request eventually completes. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks the complete bounded safety contract and owner refinement. |
| `Liveness.cfg` | Checks participant and group progress under lease-disposal fairness. |
| `BrokenLateLease.cfg` | Admits a lease after participant or group release request; TLC must reject it. |
| `BrokenResourceWithLease.cfg` | Retires resource state while a pair-local lease is active; TLC must reject it. |
| `BrokenSnapshotOrder.cfg` | Releases a snapshot before resource and lease quiescence; TLC must reject it. |
| `BrokenSiblingRelease.cfg` | Releases one participant using a sibling's quiescence; TLC must reject it. |

## Running TLC

Use the repository-pinned TLA+ tool:

```bash
cd docs/models/assembly-context-participant-resource-lifecycle
for config in Safety Liveness; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -workers 1 -cleanup -config "$config.cfg" \
    AssemblyContextParticipantResourceLifecycle.tla
done
for config in BrokenLateLease BrokenResourceWithLease \
  BrokenSnapshotOrder BrokenSiblingRelease; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE -config "$config.cfg" \
    AssemblyContextParticipantResourceLifecycle.tla
done
```

Run these commands sequentially because TLC processes in one directory share
the default `states/` checkpoint path.

## TLC evidence

Checked on macOS 27 arm64 with Homebrew OpenJDK `25.0.4.1` and the
repository-pinned TLA+ `v1.8.0` build `2026.08.11.125311`
(`TLC2 2026.08.11.125311`, rev `0894c34`). The checked `tla2tools.jar` has
SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 81,265 | 20,064 | 23 |
| `Liveness.cfg` | No error | 81,265 | 20,064 | 23 |
| `BrokenLateLease.cfg` | `NoLeaseAdmissionAfterRelease` violated | 53 | 31 | 3 |
| `BrokenResourceWithLease.cfg` | `ParticipantResourcesWaitForLeases` violated | 88 | 60 | 4 |
| `BrokenSnapshotOrder.cfg` | `ParticipantResourcesPrecedeSnapshots` violated | 42 | 34 | 3 |
| `BrokenSiblingRelease.cfg` | `SiblingReleaseUsesOwnState` violated | 1,428 | 562 | 6 |

Both normal configurations explored their complete bounded state graphs. The
four mutations independently demonstrate that the checked properties detect a
late lease, pair-local resource retirement with a live lease, snapshot release
before participant quiescence, and release authorized by a sibling's state.
