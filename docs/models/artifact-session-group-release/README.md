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
- each group's owner-issued request, terminal receipt, and receipt result; and
- artifact cleanup and terminal report publication after every exact receipt.

The workspace-level close result and the group owner's terminal receipt are
separate currencies. Normally workspace close requests a group release and
observes that receipt. A bounded fault path instead settles the second
workspace-level close result as faulted without requesting its group owner.
Artifact cleanup must then use the stored exact dependent-group identity to
request and await that owner's terminal receipt. This is the case in which the
stored association is load-bearing rather than an idempotent happy-path
recheck.

The modeled interactions are:

- transfer only after current group admissions settle;
- complete, distinct, exact-set validation at transfer;
- a dependent group reaching terminal release before transfer;
- an unrelated group admitted after transfer;
- workspace close requesting and observing every current group;
- a workspace-level group-close fault with no owner request;
- artifact cleanup re-requesting every stored dependent group;
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
- exception payloads, cleanup-failure ordering, close-report serialization,
  or thread scheduling;
- later session-related group rejection, which remains gated by
  `RegisterArtifactSession_RejectsForeignOrIncompleteGroupSet`;
- product capability, product substrate, host, or rendering behavior; or
- implementation conformance.

The bounded TLC results establish properties of this model. Existing
`InspectionWorkspace` tests remain the implementation-conformance gates.

## Composition boundary

The consumer binds each owner module's `Group` parameter to one exact group
identity and invokes the imported request and completion actions as the sole
writers on ordinary paths. It supplies that group's quiescence bit to
`SafetySpec` and rechecks all three projected owner behaviors.

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
| `ArtifactCleanupResultRemainsVisible` | Terminal close publishes the artifact cleanup result, including failure. |
| `GroupCloseFailureRemainsVisible` | Terminal close preserves whether any workspace-level group close faulted. |
| `Dependent*CompletionMatchesRequest` and `Dependent*CompletionCarriesResult` | Each exact owner issues a receipt only for its requested group and publishes identity/result together. |
| `ForeignCompletionMatchesRequest` and `ForeignCompletionCarriesResult` | The unrelated owner issues its own exact, result-bearing receipt. |
| `Dependent*BehaviorRefinesOwner` and `ForeignBehaviorRefinesOwner` | Each projected request/completion behavior refines its owner module's `SafetySpec`. |
| `ClosingWorkspaceEventuallyCloses` | Once close starts, weak fairness drives group settlement, artifact cleanup, and terminal report publication. |
| `FaultedGroupCloseEventuallyCleansArtifacts` | A faulted workspace-level group result cannot strand the artifact session; the stored exact group request still drives cleanup. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `Safety.cfg` | Checks the complete bounded safety graph and all three imported owner refinements. |
| `Liveness.cfg` | Checks terminal close and artifact cleanup after a faulted group-close result. |
| `ReachabilityFaultRecoveryRequest.cfg` | Demonstrates the artifact registration requesting the exact owner after workspace-level close faults before making that request. |
| `ReachabilityAlreadyTerminalTransfer.cfg` | Demonstrates transfer accepting a complete set that includes an already-terminal group. |
| `ReachabilityUnrelatedAfterTransfer.cfg` | Demonstrates unrelated admission remaining available after transfer. |
| `ReachabilityMixedReceiptResults.cfg` | Demonstrates artifact cleanup after the two exact owners return different terminal results. |
| `BrokenForeignTransfer.cfg` | Stores an unrelated group in place of one exact dependency; it must violate complete exact-set transfer. |
| `BrokenIncompleteTransfer.cfg` | Stores only one exact dependency; it must violate complete exact-set transfer. |
| `BrokenDuplicateTransfer.cfg` | Supplies the same dependency twice; it must violate distinct complete exact-set transfer. |
| `BrokenTransferDuringAdmission.cfg` | Transfers while one admission is incomplete; it must violate the stable-current-set precondition. |
| `BrokenForeignReceipt.cfg` | Uses another owner's genuine receipt in place of the second dependency; it must violate exact-receipt authorization. |
| `BrokenPartialReceipt.cfg` | Releases after only one of two exact receipts; it must violate all-dependent authorization. |
| `BrokenMissingDependentRequest.cfg` | Omits the second stored request after its workspace-level close result faults; it must violate cleanup progress. |
| `BrokenImportedReceiptLifecycle.cfg` | Completes an owner before its supplied quiescence condition; it must violate imported behavior refinement. |
| `BrokenGroupCloseFaultOmission.cfg` | Publishes successful close after a group-close fault; it must violate failure visibility. |
| `BrokenCleanupOmission.cfg` | Drops a failed artifact cleanup result from terminal close; it must violate report visibility. |

The reachability configurations intentionally negate their named observations,
so exit code 12 means TLC reached the required neighboring positive behavior.
They remain unlisted in the sparse exact-outcome manifest; the positive
configurations and focused contract mutations are the repository gates.

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
  ReachabilityFaultRecoveryRequest ReachabilityAlreadyTerminalTransfer \
  ReachabilityUnrelatedAfterTransfer ReachabilityMixedReceiptResults \
  BrokenForeignTransfer BrokenIncompleteTransfer BrokenDuplicateTransfer \
  BrokenTransferDuringAdmission BrokenForeignReceipt BrokenPartialReceipt \
  BrokenMissingDependentRequest BrokenImportedReceiptLifecycle \
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
| `Safety.cfg` | No error | 30,637 | 9,003 | 23 |
| `Liveness.cfg` | No error | 30,637 | 9,003 | 23 |
| `ReachabilityFaultRecoveryRequest.cfg` | `NoFaultRecoveryRequestObserved` violated | 4,422 | 1,675 | 10 |
| `ReachabilityAlreadyTerminalTransfer.cfg` | `NoAlreadyTerminalTransferObserved` violated | 143 | 85 | 5 |
| `ReachabilityUnrelatedAfterTransfer.cfg` | `NoUnrelatedAfterTransferObserved` violated | 31 | 23 | 4 |
| `ReachabilityMixedReceiptResults.cfg` | `NoMixedReceiptCleanupObserved` violated | 21,124 | 6,238 | 14 |
| `BrokenForeignTransfer.cfg` | `TransferUsesCompleteExactSet` violated | 6 | 6 | 2 |
| `BrokenIncompleteTransfer.cfg` | `TransferUsesCompleteExactSet` violated | 6 | 6 | 2 |
| `BrokenDuplicateTransfer.cfg` | `TransferUsesDistinctGroups` violated | 6 | 6 | 2 |
| `BrokenTransferDuringAdmission.cfg` | `TransferWaitsForCompletedAdmissions` violated | 8 | 7 | 3 |
| `BrokenForeignReceipt.cfg` | `ArtifactReleaseWaitsForExactReceipts` violated | 29,435 | 8,189 | 18 |
| `BrokenPartialReceipt.cfg` | `ArtifactReleaseWaitsForExactReceipts` violated | 11,419 | 3,747 | 12 |
| `BrokenMissingDependentRequest.cfg` | `FaultedGroupCloseEventuallyCleansArtifacts` violated | 30,237 | 8,683 | 23 |
| `BrokenImportedReceiptLifecycle.cfg` | Imported owner action property violated | 17 | 15 | 3 |
| `BrokenGroupCloseFaultOmission.cfg` | `GroupCloseFailureRemainsVisible` violated | 25,229 | 7,205 | 15 |
| `BrokenCleanupOmission.cfg` | `ArtifactCleanupResultRemainsVisible` violated | 25,230 | 7,206 | 15 |

The positive safety and liveness runs explored the complete bounded state
graph. The fault-recovery trace settles the second workspace-level group close
as faulted with no owner request, enters artifact cleanup, and then requests
that exact owner from the stored dependency. Its paired liveness mutation
reaches the same fault but cannot release the artifact session after omitting
that request.

The foreign-receipt trace admits the unrelated group, drives its owner through
request, quiescence, completion, and workspace observation, faults the second
exact group close, and then attempts artifact cleanup with the first exact
receipt plus the unrelated receipt. The exact-receipt invariant rejects that
substitution. The mixed-result trace reaches cleanup with one successful and
one failed group receipt, demonstrating that receipt failure does not become
artifact cleanup failure or suppress cleanup.
