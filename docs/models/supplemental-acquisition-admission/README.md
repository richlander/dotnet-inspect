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

The model uses two supplemental operations. The primary correctness
configuration starts with one required artifact and one retained-byte unit; a
second starts with no required artifacts or bytes. Three additional
correctness configurations independently bind artifact count, aggregate
retained bytes, and per-artifact bytes so none of those dimensions can be
satisfied accidentally by equal numeric bounds. One supplemental result is
smaller than the other, allowing their order to reach accepted, exhausted,
overrun, supplemental-only publication, and empty-only rejection paths.

The model checks:

- the permanent required-to-supplemental phase transition;
- an abstract successful or failed required-content checkpoint;
- explicit supplemental request and capacity-resolution states;
- one positive, exact remaining-capacity grant before adapter work;
- cumulative artifact-count, per-artifact, and retained-byte bounds;
- atomic nonempty batch acceptance after an abstract materialization result;
- empty-batch cleanup without artifacts or roles;
- adapter failure remaining terminal rather than becoming empty success;
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

`Requested` and its capacity resolution are two logical model steps for one
owner-gate transition. Seal or close cannot interleave between them because the
implementation contract resolves capacity under that gate before its first
await. A competing call or termination that reaches the gate first prevents
the request from entering this modeled state.

## Non-claims

The model does not cover:

- required-artifact materialization mechanics, stream interruption, or backing
  resource release, which are owned by
  [`ArtifactGenerationAccess.tla`](../../design/models/artifact-generation-access/ArtifactGenerationAccess.tla);
- multi-demand single-flight, workspace-wide whole-plan reservation, or
  dependent-group quiescence, which are owned by
  [`ArtifactSessionAdmission.tla`](../artifact-session-admission/ArtifactSessionAdmission.tla);
- derivation of the required-checkpoint result, per-stream materialization
  progress, temporary snapshot rollback, byte-failure precedence, scope
  ownership, or artifact identity validation;
- the rejected outcome of a competing concurrent call, or termination that
  wins the owner gate before a supplemental request is recorded;
- external exception payloads that project a late diagnostic after session
  close;
- local-directory enumeration, selection, or snapshot construction;
- artifact identity construction, metadata decode, assembly projection,
  binding, or query authorization;
- diagnostic payloads, API spelling, or implementation conformance.

## Checked properties

| Property | Claim |
| --- | --- |
| `PhaseCoherence` | The checkpoint occurs only after the permanent phase close, and seal states retain its result. |
| `OneActiveGrant` | Exactly one active supplemental operation owns the positive remaining-capacity grant. |
| `CapacityBounded` | Committed count and retained bytes plus the active grant remain bounded, and each grant preserves the session per-artifact ceiling. |
| `AcceptedArtifactsFitPerArtifactLimit` | Every accepted operation's largest artifact remains within the session per-artifact limit. |
| `CapacityGuardWitnessHolds` | Every issued grant re-observes positive count, retained-byte, and per-artifact capacity. |
| `ReturnedLeaseRetainsGrant` | A returned lease remains owned by the active operation until retention or cleanup attempt. |
| `CleanupReleaseWitnessHolds` | Grant release after a returned lease observes that lease's retention or cleanup attempt. |
| `BatchCommitIsAtomic` | A nonempty result contributes its complete configured count and roles or contributes nothing. |
| `EmptyBatchIsNoOp` | An empty result contributes no artifact or role and reaches a completed cleanup attempt. |
| `FailureIsVisible` | Adapter failure and capacity rejection remain failing model states rather than becoming empty success. |
| `LeaseOwnershipCoherent` | Each operation state has the required absent, returned, retained, or cleanup-completed lease state. |
| `RequiredPhaseStaysClosed` | A required add attempted after supplemental entry cannot be accepted. |
| `CheckpointGuardWitnessHolds` | Adapter work starts only after a successful required-content checkpoint. |
| `AcceptanceGuardWitnessHolds` | Acceptance observes an open supplemental phase, successful checkpoint, and fitting result. |
| `PublicationGuardWitnessHolds` | Publication observes successful checkpoint, no failure, no active call, and nonempty content. |
| `PublishedStateIsCoherent` | A published session has a successful checkpoint, no failure, no active call, and nonempty content. |
| `EveryRequestedCallEventuallyResolves` | Under weak fairness, an owner-gate-accepted request starts with positive capacity or rejects before adapter work. |
| `EveryStartedOperationEventuallySettles` | Under weak fairness, each adapter-started operation accepts, empties, or fails. |
| `EveryReturnedLeaseEventuallyTransfersOrCleans` | Under weak fairness, each returned lease is retained or reaches a cleanup attempt. |
| `RejectedSessionEventuallyCleansRetainedLeases` | Rejection eventually attempts cleanup of every retained supplemental lease. |
| `ClosedSessionEventuallyCleansRetainedLeases` | Close eventually attempts cleanup of every retained supplemental lease. |

Guard witnesses re-derive the required condition from the pre-step state when
adapter start, batch acceptance, publication, or cleanup release occurs. The
broken-policy configurations weaken one action rather than merely negating the
corresponding invariant.

## Configurations

The committed inventory contains 26 configurations: five complete correctness
runs, 12 reachability probes, and nine deliberate mutations.

| Configuration | Purpose |
| --- | --- |
| `SupplementalAcquisitionAdmission.cfg` | Checks every safety and liveness property over the two-operation bound. |
| `SupplementalAcquisitionAdmission_ZeroRequired.cfg` | Checks the complete property set when supplemental content starts from an empty required set. |
| `SupplementalAcquisitionAdmission_CountDimension.cfg` | Checks the complete property set with artifact count independently binding. |
| `SupplementalAcquisitionAdmission_RetainedBytesDimension.cfg` | Checks the complete property set with aggregate retained bytes independently binding. |
| `SupplementalAcquisitionAdmission_ArtifactBytesDimension.cfg` | Checks the complete property set with per-artifact bytes independently binding. |
| `ReachabilityCheckpointFailure.cfg` | Reaches a failed required-content checkpoint after the phase closes. |
| `ReachabilityCapacityRejection.cfg` | Reaches rejection before adapter invocation after prior content consumes the remaining capacity. |
| `ReachabilityCountCapacityRejection.cfg` | Reaches pre-adapter rejection with no artifact-count capacity remaining. |
| `ReachabilityByteCapacityRejection.cfg` | Reaches pre-adapter rejection with no retained-byte capacity remaining. |
| `ReachabilityEmptyBatch.cfg` | Reaches empty-batch cleanup with no artifact or role contribution. |
| `ReachabilityAcceptance.cfg` | Reaches atomic acceptance of a fitting nonempty batch. |
| `ReachabilityOverrun.cfg` | Reaches a result that exceeds the current operation's count, per-artifact, and retained-byte grant. |
| `ReachabilityLateOutcome.cfg` | Reaches a returned adapter result after session close. |
| `ReachabilityLateDiagnostic.cfg` | Reaches an adapter diagnostic returned after session close. |
| `ReachabilityRequiredRejection.cfg` | Reaches rejection of a required add after supplemental phase entry. |
| `ReachabilityEmptyOnlyRejection.cfg` | Reaches empty-session rejection after a supplemental result is empty and no nonempty batch is accepted. |
| `ReachabilitySupplementalOnlyPublication.cfg` | Reaches publication of a nonempty supplemental batch with no required artifacts. |
| `BrokenRequiredPhaseGuard.cfg` | Accepts a required add after supplemental phase entry. |
| `BrokenCheckpointGuard.cfg` | Starts adapter work before a successful checkpoint. |
| `BrokenCountGuard.cfg` | Accepts a batch that exceeds only its artifact-count grant. |
| `BrokenRetainedBytesGuard.cfg` | Accepts a batch that exceeds only its retained-byte grant. |
| `BrokenArtifactBytesGuard.cfg` | Accepts a batch that exceeds only its per-artifact-byte grant. |
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
`tla2tools.jar` v1.8.0) checked the five complete configurations with no
invariant violations or temporal counterexamples:

| Configuration | Generated | Distinct | Depth |
| --- | ---: | ---: | ---: |
| Primary | 1,097 | 581 | 15 |
| Zero required | 1,264 | 647 | 16 |
| Count dimension | 1,115 | 581 | 15 |
| Retained-byte dimension | 1,115 | 581 | 15 |
| Per-artifact dimension | 1,047 | 557 | 15 |

Each of the 12 reachability configurations violated only its intentional
`NotReached` invariant. The broken required-phase, checkpoint, count,
retained-byte, per-artifact, late-acceptance, cleanup-release,
failure-visibility, and empty-no-op configurations violated
`RequiredPhaseStaysClosed`, `CheckpointGuardWitnessHolds`, `CapacityBounded`,
`CapacityBounded`, `AcceptedArtifactsFitPerArtifactLimit`,
`AcceptanceGuardWitnessHolds`, `ReturnedLeaseRetainsGrant`,
`FailureIsVisible`, and `EmptyBatchIsNoOp`, respectively.
