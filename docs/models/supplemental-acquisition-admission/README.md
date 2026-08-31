# Supplemental acquisition admission model

`SupplementalAcquisitionAdmission.tla` models the focused
`ArtifactSetSession` bridge for supplemental artifact sources. It starts with
an exact required-artifact count and retained-byte charge, closes required
admission before checking those required snapshots, and then admits at most one
supplemental operation at a time against exact remaining session capacity.

The owning design is
[`docs/design/artifact-acquisition-and-workspaces.md`](../../design/artifact-acquisition-and-workspaces.md).
Implementation is tracked by
[#5010](https://github.com/richlander/dotnet-inspect/issues/5010).

## Scope

The main bound uses two supplemental operations. The session starts with one
required artifact and one retained-byte unit, and has room for two more
artifacts and two more byte units. One supplemental result consumes one unit;
the other consumes two. Their ordering therefore reaches accepted, exhausted,
and overrun paths without changing the bound.

The model checks:

- the permanent required-to-supplemental phase transition;
- an abstract successful or failed required-content checkpoint;
- one positive, exact remaining-capacity grant before adapter work;
- cumulative artifact-count, per-artifact, and retained-byte bounds;
- atomic nonempty batch acceptance after materialization;
- empty-batch cleanup without artifacts or roles;
- visible adapter failure and capacity rejection;
- sealing exclusion while acquisition or cleanup is active;
- late-result cleanup after session close; and
- retained-lease cleanup after rejection or close.

The capacity grant remains active until a returned lease is retained or its
cleanup attempt finishes. That state represents the externally observable
rule that another supplemental call cannot start while cleanup from the prior
call is incomplete; it does not claim a separately reusable reservation
service inside the current single-session implementation. Session close
abstracts caller cancellation and owner disposal after either has closed
admission; exception typing remains an implementation obligation.

## Non-claims

The model does not cover:

- required-artifact materialization mechanics, stream interruption, or backing
  resource release, which are owned by
  [`ArtifactGenerationAccess.tla`](../../design/models/artifact-generation-access/ArtifactGenerationAccess.tla);
- multi-demand single-flight, workspace-wide whole-plan reservation, or
  dependent-group quiescence, which are owned by
  [`ArtifactSessionAdmission.tla`](../artifact-session-admission/ArtifactSessionAdmission.tla);
- local-directory enumeration, selection, or snapshot construction;
- artifact identity construction, metadata decode, assembly projection,
  binding, or query authorization;
- diagnostic payloads, API spelling, or implementation conformance.

## Checked properties

| Property | Claim |
| --- | --- |
| `PhaseCoherence` | The checkpoint occurs only after the permanent phase close, and seal states retain its result. |
| `OneActiveGrant` | Exactly one active supplemental operation owns the positive remaining-capacity grant. |
| `CapacityBounded` | Committed content plus the active grant never exceeds count, per-artifact, or retained-byte limits. |
| `ReturnedLeaseRetainsGrant` | A returned lease remains owned by the active operation until retention or cleanup attempt. |
| `BatchCommitIsAtomic` | A nonempty result contributes its complete configured count and roles or contributes nothing. |
| `EmptyBatchIsNoOp` | An empty result contributes no artifact or role and reaches a completed cleanup attempt. |
| `FailureIsVisible` | Adapter failure and capacity rejection cannot become empty success. |
| `LeaseOwnershipCoherent` | Each operation state has the required absent, returned, retained, or cleanup-completed lease state. |
| `RequiredPhaseStaysClosed` | A required add attempted after supplemental entry cannot be accepted. |
| `CheckpointGuardWitnessHolds` | Adapter work starts only after a successful required-content checkpoint. |
| `AcceptanceGuardWitnessHolds` | Acceptance observes an open supplemental phase, successful checkpoint, and fitting result. |
| `PublicationGuardWitnessHolds` | Publication observes successful checkpoint, no failure, no active call, and nonempty content. |
| `EveryStartedOperationEventuallySettles` | Under weak fairness, each started operation accepts, empties, fails, or rejects for capacity. |
| `EveryReturnedLeaseEventuallyTransfersOrCleans` | Under weak fairness, each returned lease is retained or reaches a cleanup attempt. |
| `RejectedSessionEventuallyCleansRetainedLeases` | Rejection eventually attempts cleanup of every retained supplemental lease. |
| `ClosedSessionEventuallyCleansRetainedLeases` | Close eventually attempts cleanup of every retained supplemental lease. |

Guard witnesses re-derive the required condition from the pre-step state when
adapter start, batch acceptance, publication, or cleanup release occurs. The
broken-policy configurations weaken one action rather than merely negating the
corresponding invariant.

## Configurations

The committed inventory contains 15 configurations: one complete correctness
run, seven reachability probes, and seven deliberate mutations.

| Configuration | Purpose |
| --- | --- |
| `SupplementalAcquisitionAdmission.cfg` | Checks every safety and liveness property over the two-operation bound. |
| `ReachabilityCheckpointFailure.cfg` | Reaches a failed required-content checkpoint after the phase closes. |
| `ReachabilityCapacityRejection.cfg` | Reaches rejection before adapter invocation after prior content consumes the remaining capacity. |
| `ReachabilityEmptyBatch.cfg` | Reaches empty-batch cleanup with no artifact or role contribution. |
| `ReachabilityAcceptance.cfg` | Reaches atomic acceptance of a fitting nonempty batch. |
| `ReachabilityOverrun.cfg` | Reaches a result that exceeds the current operation's count, per-artifact, and retained-byte grant. |
| `ReachabilityLateOutcome.cfg` | Reaches a returned adapter result after session close. |
| `ReachabilityRequiredRejection.cfg` | Reaches rejection of a required add after supplemental phase entry. |
| `BrokenRequiredPhaseGuard.cfg` | Accepts a required add after supplemental phase entry. |
| `BrokenCheckpointGuard.cfg` | Starts adapter work before a successful checkpoint. |
| `BrokenCapacityGuard.cfg` | Accepts a batch that exceeds its remaining-capacity grant. |
| `BrokenLateAcceptanceGuard.cfg` | Accepts a materialized batch after session close. |
| `BrokenCleanupBeforeRelease.cfg` | Releases the active grant while its returned lease still lacks a cleanup attempt. |
| `BrokenFailureVisibility.cfg` | Converts an adapter failure into empty success. |
| `BrokenEmptyNoOp.cfg` | Commits an artifact and role after the adapter returned an empty batch. |

## Running TLC

From this directory, with the repository-pinned TLA+ tools:

```bash
java -XX:+UseParallelGC -cp ~/.local/share/tlaplus/tla2tools.jar \
  tlc2.TLC -cleanup SupplementalAcquisitionAdmission
```

TLC 2026.08.21.155922 (rev `9787e65`, from the repository-pinned
`tla2tools.jar` v1.8.0) checked the primary configuration: 1,590 states
generated, 807 distinct states, maximum depth 14, with no invariant violations
or temporal counterexamples.

Each reachability configuration violated only its intentional `NotReached`
invariant. The broken required-phase, checkpoint, capacity, late-acceptance,
cleanup-release, failure-visibility, and empty-no-op configurations violated
`RequiredPhaseStaysClosed`, `CheckpointGuardWitnessHolds`, `CapacityBounded`,
`AcceptanceGuardWitnessHolds`, `ReturnedLeaseRetainsGrant`,
`FailureIsVisible`, and `EmptyBatchIsNoOp`, respectively.
