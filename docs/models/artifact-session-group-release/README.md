# Artifact session group-release model

`ArtifactSessionGroupRelease.tla` models the shipped asynchronous
`InspectionWorkspace` handoff from one artifact session to the complete exact
set of dependent `AssemblyContextGroup` release receipts. It is the executable
interaction companion to
[Artifact acquisition and workspace composition](../../design/artifact-acquisition-and-workspaces.md#artifactsetsession).

## Scope

The model contains one transferred artifact session, two exact dependent
groups, one unrelated group, and one pending-admission bit. The group bound is
the smallest topology that distinguishes a complete set from a partial or
foreign set. The stored input remains an ordered pair so a separate witness
also detects duplicate references before set projection collapses them. The
pending-admission bit checks the precondition that makes the computed current
set stable at transfer.

Each group has a named instance of
[`AssemblyContextGroupReleaseLifecycle.tla`](../assembly-context-group-lifecycle/AssemblyContextGroupReleaseLifecycle.tla).
The consumer preserves the product's join currency:

- the exact transferred artifact-session registration;
- the complete exact set of current group identities projected from it;
- the modeled release-request origin for each group;
- each group's owner-issued terminal receipt and receipt result; and
- artifact cleanup and terminal report publication after every exact receipt.

The workspace-level close result and the group owner's terminal receipt are
separate currencies. Normally workspace close requests a group release and
observes that receipt. A bounded fault path instead settles the second
workspace-level close result as faulted without requesting its group owner.
Artifact cleanup enters a safe retained state and can only observe the stored
owner-issued receipt. An explicit adjacent-owner recovery action may later
request release; only after that owner reaches terminal physical settlement
may artifact cleanup release the query lease and session. The stored
association is therefore load-bearing for exact settlement observation, never
a fallback physical-release authority.

The modeled interactions are:

- transfer only after current group admissions settle;
- complete, distinct, exact-set validation at transfer;
- a dependent group reaching terminal release before transfer;
- an unrelated group admitted after transfer;
- workspace close requesting and observing every current group;
- a workspace-level group-close fault with no owner request;
- artifact cleanup waiting with the session retained and no owner receipt;
- an adjacent owner later requesting release after that fault;
- independent successful or failed group receipts;
- query-lease/session cleanup only after both exact dependent receipts;
- artifact cleanup failure visibility even when group close faults.

## Non-claims

The model does not cover:

- admission single-flight, generation selection, cancellation, reservation,
  adapter execution, or late adapter results, which remain in
  [`ArtifactSessionAdmission.tla`](../artifact-session-admission/ArtifactSessionAdmission.tla);
- artifact catalog entries, content identity, participant projection, or the
  concrete reference-equality implementation used to derive the set;
- group callbacks, images, resources, or quiescence internals beyond one
  owner-supplied boolean per group;
- a workspace-level result fault after the owner request has started; the
  bounded fault path covers failure before that request;
- an owner recovery request between that fault and artifact-cleanup start; the
  bounded recovery transition begins from the retained cleanup wait;
- exception payloads, cleanup-failure ordering, close-report serialization,
  or thread scheduling;
- later session-related group rejection, which remains gated by
  `RegisterArtifactSession_RejectsForeignOrIncompleteGroupSet` and
  `RegisterArtifactSession_RejectsLaterCoordinatedGroup`;
- product capability, product substrate, host, or rendering behavior; or
- implementation conformance.

The bounded TLC results establish properties of this model. Existing
`InspectionWorkspace` tests remain the implementation-conformance gates.

## Composition boundary

The consumer binds each owner module's `Group` parameter to one exact group
identity. Workspace close and the explicit adjacent-owner recovery transition
invoke imported request actions; physical settlement invokes imported
completion actions. Artifact cleanup only reads the owner state. The consumer
supplies each group's quiescence bit to `SafetySpec` and rechecks all three
projected owner behaviors.

The imported completion/request and completion/result invariants remain useful
focused diagnostics, but are implied by behavior refinement and are not
independent evidence. `BrokenImportedReceiptLifecycle.cfg` weakens the
consumer-supplied quiescence boundary and must leave the imported owner
behavior. `BrokenForeignReceipt.cfg` first drives the unrelated owner through
ordinary request and completion actions, then demonstrates that its genuine
terminal receipt cannot authorize release for the second exact dependency.

## Checked properties

| Property | Claim |
| --- | --- |
| `TransferUsesCompleteExactSet` | The stored transfer pair denotes exactly both current session-derived groups. |
| `TransferUsesDistinctGroups` | Transfer input does not repeat one group reference. |
| `TransferWaitsForCompletedAdmissions` | Transfer does not compute its exact current set while a group admission remains incomplete. |
| `ArtifactReleaseWaitsForExactReceipts` | Query-lease/session cleanup starts only after both exact dependent owners issue terminal receipts. |
| `ReleaseRequestsCarryOwnerAuthority` | Every imported owner request has a recorded issuer, and artifact cleanup is never that issuer. |
| `RecoveryPrecedesPostFaultRequest` | A requested second group after the modeled fault can arise only through the explicit adjacent-owner recovery transition. |
| `ArtifactCleanupResultRemainsVisible` | Terminal close publishes the artifact cleanup result, including failure. |
| `GroupCloseFailureRemainsVisible` | Terminal close preserves whether any workspace-level group close faulted. |
| `Dependent*CompletionMatchesRequest` and `Dependent*CompletionCarriesResult` | Each exact owner issues a receipt only for its requested group and publishes identity/result together. |
| `ForeignCompletionMatchesRequest` and `ForeignCompletionCarriesResult` | The unrelated owner issues its own exact, result-bearing receipt. |
| `Dependent*BehaviorRefinesOwner` and `ForeignBehaviorRefinesOwner` | Each projected request/completion behavior refines its owner module's `SafetySpec`. |
| `ClosingWorkspaceEventuallySettles` | Once close starts, weak fairness reaches either terminal close or the safe retained state awaiting an adjacent-owner request. |
| `RecoveredFaultedGroupCloseEventuallyCleansArtifacts` | Once the adjacent owner requests the faulted group's release, weakly fair physical settlement eventually permits artifact cleanup. |

Neither `SafetySpec` nor `Spec` assumes adjacent-owner recovery. They include
the valid pending state in which workspace group close has faulted, the exact
physical receipt is absent, and artifact resources remain retained. The
recovered-fault liveness property begins only after the adjacent owner has
issued that request.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks the complete bounded safety graph and all three imported owner refinements. |
| `Liveness.cfg` | Checks close reaching either terminal completion or the safe retained wait, plus cleanup after the adjacent owner has requested the faulted group. |
| `ReachabilityFaultedSettlementWait.cfg` | Demonstrates a faulted workspace close waiting safely with no physical owner request and the artifact session retained. |
| `ReachabilityOwnerRecoveryRequest.cfg` | Demonstrates the adjacent owner requesting the exact group after workspace-level close faults before making that request. |
| `ReachabilityAlreadyTerminalTransfer.cfg` | Demonstrates transfer accepting a complete set that includes an already-terminal group. |
| `ReachabilityUnrelatedAfterTransfer.cfg` | Demonstrates unrelated admission remaining available after transfer. |
| `ReachabilityMixedReceiptResults.cfg` | Demonstrates artifact cleanup after the two exact owners return different terminal results. |
| `BrokenForeignTransfer.cfg` | Stores an unrelated group in place of one exact dependency; it must violate complete exact-set transfer. |
| `BrokenIncompleteTransfer.cfg` | Stores only one exact dependency; it must violate complete exact-set transfer. |
| `BrokenDuplicateTransfer.cfg` | Supplies the same dependency twice; it must violate distinct complete exact-set transfer. |
| `BrokenTransferDuringAdmission.cfg` | Transfers while one admission is incomplete; it must violate the stable-current-set precondition. |
| `BrokenForeignReceipt.cfg` | Uses another owner's genuine receipt in place of the second dependency; it must violate exact-receipt authorization. |
| `BrokenPartialReceipt.cfg` | Releases after only one of two exact receipts; it must violate all-dependent authorization. |
| `BrokenArtifactCleanupReleaseAuthority.cfg` | Attributes the missing dependent request to artifact cleanup; it must violate the request-issuer authority boundary. |
| `BrokenImportedReceiptLifecycle.cfg` | Completes an owner before its supplied quiescence condition; it must violate imported behavior refinement. |
| `BrokenGroupCloseFaultOmission.cfg` | Publishes successful close after a group-close fault; it must violate failure visibility. |
| `BrokenCleanupOmission.cfg` | Drops a failed artifact cleanup result from terminal close; it must violate report visibility. |

The reachability configurations intentionally negate their named observations,
so exit code 12 means TLC reached the required neighboring positive behavior.
`Safety.cfg` enforces fault-before-request ordering through
`RecoveryPrecedesPostFaultRequest`.
The sparse exact-outcome manifest enforces the contract-defining retained-fault,
adjacent-owner recovery, and post-transfer unrelated-admission paths. In
particular, `ReachabilityOwnerRecoveryRequest.cfg` proves that the required
explicit recovery path remains reachable rather than making that safety
invariant vacuous. The already-terminal and mixed-result configurations remain
unlisted neighboring evidence.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../runbooks/tla-plus-setup.md) for the pinned toolchain.
The repository runner supplies the owner module through `TLA-Library`:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  eng/run-tla-checks.sh docs/models/artifact-session-group-release
```

For deterministic local evidence, run configurations sequentially:

```bash
cd docs/models/artifact-session-group-release
TLA_LIBRARY="$PWD/../assembly-context-group-lifecycle"

for config in Safety Liveness \
  ReachabilityFaultedSettlementWait ReachabilityOwnerRecoveryRequest \
  ReachabilityAlreadyTerminalTransfer \
  ReachabilityUnrelatedAfterTransfer ReachabilityMixedReceiptResults \
  BrokenForeignTransfer BrokenIncompleteTransfer BrokenDuplicateTransfer \
  BrokenTransferDuringAdmission BrokenForeignReceipt BrokenPartialReceipt \
  BrokenArtifactCleanupReleaseAuthority BrokenImportedReceiptLifecycle \
  BrokenGroupCloseFaultOmission BrokenCleanupOmission; do
  java -XX:+UseParallelGC "-DTLA-Library=$TLA_LIBRARY" \
    -cp /path/to/tla2tools.jar tlc2.TLC \
    -workers 1 -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" ArtifactSessionGroupRelease.tla
done
```

## TLC evidence

Checked on Linux with OpenJDK `21.0.12` and repository-pinned TLA+ `v1.8.0`
(`TLC2 2026.09.01.002747`, rev `95b800c`). The checked `tla2tools.jar` has
SHA-256
`dbcc75552f21978a4846688b8e23be1a6b6c0b3fcee35d78fec2df167958ec94`.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 57,596 | 19,429 | 23 |
| `Liveness.cfg` | No error | 57,596 | 19,429 | 23 |
| `ReachabilityFaultedSettlementWait.cfg` | `NoFaultedSettlementWaitObserved` violated | 2,570 | 1,161 | 9 |
| `ReachabilityOwnerRecoveryRequest.cfg` | `NoOwnerRecoveryRequestObserved` violated | 4,819 | 2,038 | 10 |
| `ReachabilityAlreadyTerminalTransfer.cfg` | `NoAlreadyTerminalTransferObserved` violated | 141 | 86 | 5 |
| `ReachabilityUnrelatedAfterTransfer.cfg` | `NoUnrelatedAfterTransferObserved` violated | 31 | 23 | 4 |
| `ReachabilityMixedReceiptResults.cfg` | `NoMixedReceiptCleanupObserved` violated | 21,489 | 7,646 | 13 |
| `BrokenForeignTransfer.cfg` | `TransferUsesCompleteExactSet` violated | 6 | 6 | 2 |
| `BrokenIncompleteTransfer.cfg` | `TransferUsesCompleteExactSet` violated | 6 | 6 | 2 |
| `BrokenDuplicateTransfer.cfg` | `TransferUsesDistinctGroups` violated | 6 | 6 | 2 |
| `BrokenTransferDuringAdmission.cfg` | `TransferWaitsForCompletedAdmissions` violated | 8 | 7 | 3 |
| `BrokenForeignReceipt.cfg` | `ArtifactReleaseWaitsForExactReceipts` violated | 42,892 | 14,097 | 16 |
| `BrokenPartialReceipt.cfg` | `ArtifactReleaseWaitsForExactReceipts` violated | 4,820 | 2,039 | 10 |
| `BrokenArtifactCleanupReleaseAuthority.cfg` | `ReleaseRequestsCarryOwnerAuthority` violated | 4,820 | 2,039 | 10 |
| `BrokenImportedReceiptLifecycle.cfg` | Imported owner action property violated | 17 | 15 | 3 |
| `BrokenGroupCloseFaultOmission.cfg` | `GroupCloseFailureRemainsVisible` violated | 30,214 | 10,337 | 14 |
| `BrokenCleanupOmission.cfg` | `ArtifactCleanupResultRemainsVisible` violated | 30,211 | 10,334 | 14 |

The positive safety and liveness runs explored the complete bounded state
graph. The fault-recovery trace settles the second workspace-level group close
as faulted with no owner request and enters artifact cleanup while retaining
the session. The neighboring recovery trace then lets the adjacent owner
request that exact group; owner-issued terminal settlement permits cleanup.
The focused authority mutation instead lets artifact cleanup issue the request
and violates `ReleaseRequestsCarryOwnerAuthority`.

The foreign-receipt trace admits the unrelated group, drives its owner through
request, quiescence, completion, and workspace observation, faults the second
exact group close, and then attempts artifact cleanup with the first exact
receipt plus the unrelated receipt. The exact-receipt invariant rejects that
substitution. The mixed-result trace reaches cleanup with one successful and
one failed group receipt, demonstrating that receipt failure does not become
artifact cleanup failure or suppress cleanup.
