# Artifact Root preparation and scope publication model

[`ArtifactRootPublication.tla`](ArtifactRootPublication.tla) models the
Artifact Acquisition owned handoff from provisional physical Root preparation
to current runtime Workspace composition, specified by
[Artifact Root preparation and scope publication](../../artifact-acquisition-and-workspaces.md#artifact-root-preparation-and-scope-publication).

It checks one focused claim: `PublishArtifactRootComposition` either commits
the logical Scope pointer and the physical Root composition together, with one
fresh owner-issued composition identity and terminal `Published` receipts, or
commits neither, releases every listed prepared batch, and preserves both old
current states — while every preparation settles under its finite deadline,
retired Roots reject new query entry, and an already admitted lease drains.

## Scope

The harness contains one runtime Workspace, four physical Roots, two
preparation receipts, one publication plan, and one sealed Scope publication
participant. `RootA` is a current Root the plan retains, `RootB` is a current
Root the plan omits (and therefore retires), and `RootC` and `RootD` are
adopted from `PreparationOne` and `PreparationTwo`. Two receipts are the
smallest topology that distinguishes "publish all listed receipts together"
from "publish a subset", and that exercises the receipt-state precedence order
over a plan-ordered receipt sequence.

The modeled interactions are:

- the six-step publication order: shape validation before any receipt is
  consumed, gate entry and applicability revalidation, staging plus candidate
  identity reservation, participant `PrepareCommit`, final recheck, and the
  non-yielding both-pointer commit;
- the exact applicability check precedence — listed receipt states in plan
  order, the open Workspace, cancellation, deadline, expected composition
  generation, retained generation reference, then admission budget;
- `ReleaseArtifactRootPreparation` racing a `Publishing` receipt, and its
  idempotent `Released`/`NoEffect` behaviour;
- owner deadline settlement of an abandoned `Prepared` receipt whose caller
  never submits a plan;
- participant refusal from a stale Scope publication base, a consumed
  participant, cancellation, or deadline expiry;
- an unrelated owner-internal Root replacement settling while a plan waits,
  advancing the physical-composition identity;
- an independent retain-only Scope publication that supersedes a slow prepared
  plan and advances both the Scope publication base and physical-composition
  epoch, even though the physical set is unchanged;
- gate-observing Scope reads and new query-entry attempts;
- an already admitted query lease on the retired `RootB` draining after
  publication; and
- a second publication attempt, either reusing a listed receipt or
  receipt-free, with the same or a separately constructed equivalent
  participant.

## Owner-issued join currencies

The bounded harness represents these join currencies without reproducing their
concrete opaque representations. Its omitted cases are listed below.

| Design currency | Model representation | Preserved distinction |
| --- | --- | --- |
| `ArtifactRootPreparationReceipt` state | `receiptState[p]` over `Prepared`/`Publishing`/`Published`/`Released` | who owns the batch at each moment, and which terminal outcome it reached |
| Receipt-local entry adoption | `adoptionCount[p]` | one listed receipt is adopted at most once |
| `ArtifactRootCompositionGenerationIdentity` (current) | `currentCompositionId`, sourced from the monotone `nextCompositionId` | owner issuance, freshness, and non-reuse across every physical change |
| Reserved candidate identity | `reservedCompositionId`, `discardedCandidateId` | reserved is unpublished until commit; a discarded candidate never becomes current and is never reused |
| Retained `ArtifactRootGenerationReference` | `planRetainedGeneration` compared with `currentRoots[RootA]` | a retained reference proves nothing by itself; currentness is established only by comparison |
| Scope publication base | `participantExpectedBase` compared with `currentScopeBase` | every successful logical swap issues a fresh non-reused base, so a stale or equivalent participant cannot become current again through ABA |
| Participant single use | `participantState` over `Available`/`TokenIssued`/`Committed`/`Refused` | a consumed participant is terminal in either direction |
| Current logical/physical pair | `pointerSwapPhase` over `None`/`PhysicalOnly`/`Complete` | an observer sees the complete old pair or the complete new pair |
| Runtime composition gate | the `GateHeld` predicate | one asynchronous exclusion boundary shared by publication, scope reads, and new query entry |

`GateHeld` holds exactly while the operation is `Staged` or `TokenIssued`.
Every gate-observing action — Scope/composition reads, new query entry,
owner-internal replacement, Scope supersession, Workspace close, and budget
pressure — is guarded by `~GateHeld`, so the commit region admits no
interleaving. The `CommitYield` phase exists only under the
`AllowYieldingCommit` mutation and is deliberately *not* gate-held: that is
what makes the half-state externally visible.

## Checked properties

### Invariants

| Invariant | Claim | Design gate |
| --- | --- | --- |
| `TypeOK` | Every variable stays in its declared domain. | — |
| `MalformedPlanReleasesNoReceipt` | Shape validation precedes receipt consumption, so a malformed plan leaves every matching `Prepared` receipt under caller ownership. | `ValidatesCompleteDesiredSetBeforeConsumption` |
| `GateRefusalIsFirstApplicable` | The gate reports the first applicable check in the owner's exact order, not merely some applicable one. | `StalePhysicalOrLogicalCandidateCannotCommit` |
| `ApplicabilityRefusalReleasesEveryListedBatch` | Any refusal once applicability validation starts releases every listed still-`Prepared` batch. | `StalePhysicalOrLogicalCandidateCannotCommit` |
| `RefusedParticipantPublishesNothing` | A typed participant refusal after staging publishes nothing, releases every provisional resource, and permanently discards the candidate identity. | `ParticipantRefusalReleasesStaging` |
| `HalfStateIsNeverGateVisible` | No gate-observing action can run while only the physical pointer has moved. | `OldOrNewCompositionIsObserved` |
| `OldOrNewCompositionIsObserved` | A reader that runs in every reachable non-gated state never records a half-state. | `OldOrNewCompositionIsObserved` |
| `NoQueryEntersRetiredRoot` | New query entry is admitted only against a current generation; a retired or staged Root is rejected. | `RetirementStopsNewEntryAndDrainsLeases` |
| `PublishedReceiptWasNotCallerReleased` | A caller release racing a `Publishing` receipt never drains the staged batch that publication alone owns. | `ReleaseIsIdempotentAndTerminal` |
| `ReceiptPublishesAtMostOnce` | Each listed receipt is adopted at most once. | `ReceiptPublishesAtMostOnce` |
| `ReceiptOutcomeIsExactlyOneTerminal` | `Published` implies exactly one adoption and `Released` implies none. | `PreparationSetPublishesAtomically` |
| `LogicalPublicationIsSingleUse` | The single-use participant and non-reused Scope base permit at most one logical publication, including across a receipt-free retry. | `ReceiptFreePlanCommitsOrRefusesOnce` |
| `CancellationNeverLeavesASwappedPointer` | Cancellation or deadline expiry before the final recheck never leaves a swapped pointer behind. | `StalePhysicalOrLogicalCandidateCannotCommit` |
| `DiscardedCandidateIdentityNeverBecomesCurrent` | A reserved identity that does not commit never becomes current and is never re-reserved. | `CandidateIdentityPrecedesParticipantCommit` |
| `PublishedResultImpliesCompleteSwap` | A `Published` result is reported only with both pointers swapped. | `PreparationSetPublishesAtomically` |

`HalfStateIsNeverGateVisible` entails `OldOrNewCompositionIsObserved`: if the
half-state is never externally visible, no observer can record it. They are
not independent evidence. The state predicate is the primary claim and is
checked in every configuration; the observed form is the focused diagnostic
that proves a reader really runs across the commit.

### Action properties

The commit step is the only transition that turns a reserved candidate
composition into the current one. The exact published pair is therefore stated
over that transition rather than over every later state, because the owner may
legitimately retire or replace a Root afterwards, which advances the current
composition again.

| Action property | Claim | Design gate |
| --- | --- | --- |
| `CommitPublishesExactlyTheReservedComposition` | The transition that reports `Published` swaps both pointers, makes the exact reserved candidate identity current, publishes the complete desired set (`RootA` at the plan's retained generation, `RootC` and `RootD` adopted, `RootB` retired), and leaves no staging. | `CandidateIdentityPrecedesParticipantCommit`, `PreparationSetPublishesAtomically` |
| `CompositionIdentityAdvancesOnEveryPhysicalChange` | Every change to current physical Root admission — owner-internal replacement as well as scope-requested publication — moves the composition identity strictly forward, so no physical change reuses or reverts to an already-current identity. | `CompositionIdentityCoversEveryPhysicalChange`, `CompositionIdentityIsOwnerIssued` |

### Liveness

| Property | Claim | Design gate |
| --- | --- | --- |
| `EveryPreparationEventuallySettles` | Every receipt reaches `Published` or `Released`, including when the caller abandons it and only the owner's finite deadline settles it. | `ReleaseIsIdempotentAndTerminal` |
| `SubmittedPublicationEventuallySettles` | A submitted publication always reaches `Committed` or `Rejected`; no step of the six-step order can stall. | `PreparationSetPublishesAtomically` |
| `AdmittedLeaseEventuallyDrains` | An already admitted query lease always completes. | `RetirementStopsNewEntryAndDrainsLeases` |
| `RetiredRootLeaseEventuallyDrains` | Once publication retires `RootB`, the lease admitted before publication still drains. | `RetirementStopsNewEntryAndDrainsLeases` |

`SubmitPlan` is deliberately unfair: no fairness assumption forces a caller to
submit a plan. `EveryPreparationEventuallySettles` therefore genuinely rests
on owner-observed deadline settlement rather than on publication running.

## Configurations

Scenario switches (`Enable*`) bound which independent concurrent actor a
configuration explores. They never relax a guard, a check order, or an
invariant — each one only removes an orthogonal actor from `Next`, so a
configuration that disables an actor explores a subgraph of the configuration
that enables it. Mutation switches (`Allow*` false, `Enforce*` true in every
positive configuration) are the negative controls.

Splitting the four actors keeps every positive configuration inside the
repository's 600-second budget. The earlier combined liveness graph exceeded
that budget. Each property is checked on a bounded graph containing its named
actors; these separate runs do not establish the full combined cross-product.

| Configuration | Actors enabled | Checks | Expected exit |
| --- | --- | --- | --- |
| `Safety.cfg` | caller release, retry | The thirteen core invariants plus both action properties | 0 |
| `ObserverSafety.cfg` | observers | Old-or-new visibility, retired-entry exclusion, and both action properties under gate-observing readers | 0 |
| `PreparationSettlementLiveness.cfg` | caller release | `EveryPreparationEventuallySettles` | 0 |
| `PublicationSettlementLiveness.cfg` | caller release | `SubmittedPublicationEventuallySettles` | 0 |
| `LeaseDrainLiveness.cfg` | lease | `AdmittedLeaseEventuallyDrains`, `RetiredRootLeaseEventuallyDrains` | 0 |
| `BrokenHalfStateCommit.cfg` | observers | `AllowYieldingCommit` yields between the two pointer assignments | 12 |
| `BrokenRefusedParticipantPublishes.cfg` | — | `AllowRefusedParticipantPublish` publishes after a product-level refusal | 12 |
| `BrokenRetiredRootEntry.cfg` | observers | `AllowRetiredRootEntry` admits a stale retained generation reference | 12 |
| `BrokenReceiptReuse.cfg` | retry | `AllowReceiptReuse` adopts an already `Published` receipt again | 12 |
| `BrokenParticipantReuse.cfg` | retry | `AllowParticipantReuse` repeats the logical pointer swap | 12 |
| `BrokenReleaseDuringPublishing.cfg` | caller release | `AllowReleaseDuringPublishing` drains a `Publishing` batch from caller authority | 12 |
| `BrokenShapeValidationOrder.cfg` | — | `AllowConsumptionBeforeShapeValidation` consumes receipts before shape validation | 12 |
| `BrokenCheckPrecedence.cfg` | — | `EnforceCheckPrecedence` false reports any applicable refusal instead of the first | 12 |
| `BrokenSynthesizedCompositionIdentity.cfg` | — | `AllowSynthesizedCompositionIdentity` synthesizes a fresh identity at commit instead of publishing the reserved candidate the participant already saw | 13 |
| `BrokenDeadlineSettlement.cfg` | caller release | `AllowOmittedDeadlineSettlement` never settles an abandoned `Prepared` receipt | 13 |
| `BrokenLeaseDrainage.cfg` | lease | `AllowRetirementToCutLease` lets retirement cut an already admitted lease | 13 |
| `ReachabilityPublishedComposition.cfg` | — | Witness: a plan actually commits | 12 |
| `ReachabilityOldAndNewPairsObserved.cfg` | observers | Witness: a reader observes both the complete old and the complete new pair | 12 |
| `ReachabilityReleaseRacesPublishing.cfg` | caller release | Witness: an explicit release really races a `Publishing` receipt and gets the typed result | 12 |
| `ReachabilityRetiredLeaseDrains.cfg` | lease | Witness: the lease drains after `RootB` is retired | 12 |

Every configuration above is listed in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt),
so the per-PR gate requires each one to produce that exact TLC semantic
verdict. A different coherent verdict and a timeout both fail.

The reachability configurations are expected to fail: the counterexample is
the evidence that the modeled interleaving is really explored rather than
vacuously excluded. `ReachabilityPublishedComposition.cfg` in particular
proves that `opResult = "Published"` is reachable, which is what makes the
`CommitPublishesExactlyTheReservedComposition` action property non-vacuous.

## Abstractions and non-claims

The model deliberately abstracts:

- **Opaque representations.** Composition identities, Scope publication bases,
  and generations are monotone integers. They preserve issuance freshness,
  non-reuse, and comparison, not the product's opaque handle types. Receipt
  and Root identities are uninterpreted constants.
- **Plan shape.** One concrete initial plan shape is modeled — retain `RootA`, adopt
  one entry from each of two receipts, omit `RootB`. Entry-uniqueness,
  receipt-uniqueness, correspondence-uniqueness, `Adopt`-without-preparations,
  and other malformed-shape failures are collapsed into the single
  `planMalformed` bit. The model checks rejection ordering, not the enumeration
  of malformed shapes. The valid empty `Clear` plan and successful initial
  receipt-free publication are not modeled. Receipt-free retry refusal is
  represented separately; it is not evidence for the complete Clear contract.
- **Resource lifetime.** Draining is modeled as a receipt state change.
  `ArtifactRootPreparation_TerminalReceiptRetainsNoResources` and
  `BrowserArtifactRootPreparation_ReleaseDoesNotRetainPackageBytes` are not
  modeled; the model says when a batch is released, not that no byte survives.
- **Deadline and cancellation.** Both are one-way latches, not a clock. The
  model claims that expiry is observed at the specified points and that
  settlement follows, not any timing bound.
- **Host scheduling.** The gate is a logical exclusion predicate.
  `BrowserArtifactRootPublication_GateDoesNotBlockOrYieldDuringCommit` is only
  partly represented: the model shows the commit region admits no
  interleaving, but says nothing about thread blocking in a single-threaded
  Browser/Wasm host.

This is a finite harness, not yet the reusable owner transition for a Scope
composition model. Extract that transition without scenario bounds, mutation
switches, or harness fairness when #5701's scope-revision model consumes it;
the consumer must recheck the imported behavior in its own composition.

The model does not cover source resolution, logical Root membership or order,
expansion policy, closure, Navigation focus, browser effects, portable schema,
arbitrary transaction participants, durable recovery, process termination, or
a second query-access protocol. #5701 owns the scope-revision side and should
instantiate this owner-issued publication transition rather than copying it.

Every design target in the owning section remains **unverified** until its
named Release gate exists. This model is design evidence bounding the
contract, not a conformance gate on an implementation that does not yet exist.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain, then run the repository gate:

```bash
TLA_TOOLS_JAR="$HOME/.local/share/tlaplus/tla2tools.jar" \
  ./eng/run-tla-checks.sh docs/design/models/artifact-root-publication
```

For deterministic local evidence, run the configurations sequentially:

```bash
cd docs/design/models/artifact-root-publication

for config in Safety ObserverSafety \
  PreparationSettlementLiveness PublicationSettlementLiveness \
  LeaseDrainLiveness \
  BrokenHalfStateCommit BrokenRefusedParticipantPublishes \
  BrokenRetiredRootEntry BrokenReceiptReuse BrokenParticipantReuse \
  BrokenReleaseDuringPublishing BrokenShapeValidationOrder \
  BrokenCheckPrecedence BrokenSynthesizedCompositionIdentity \
  BrokenDeadlineSettlement BrokenLeaseDrainage \
  ReachabilityPublishedComposition ReachabilityOldAndNewPairsObserved \
  ReachabilityReleaseRacesPublishing ReachabilityRetiredLeaseDrains; do
  java -XX:+UseParallelGC -cp /path/to/tla2tools.jar tlc2.TLC \
    -workers 1 -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" ArtifactRootPublication
done
```

## TLC evidence

Checked on Linux with OpenJDK `21.0.12` and repository-pinned TLA+ `v1.8.0`
(`TLC2 2026.08.11.125311`, rev `0894c34`). The checked `tla2tools.jar` has
SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.

Counts below are from the repository gate on 2026-09-04 with `-workers auto`.
Positive configurations explored their complete bounded state graph; each
negative and reachability configuration stopped at its first counterexample,
so its counts describe the partial search that reached the violation and may
vary between runs. This run includes retain-only Scope publication advancing
the physical-composition epoch and compares commit against the pre-step
reserved identity.

| Configuration | Result | Exit | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: | ---: |
| `Safety.cfg` | No error | 0 | 676,593 | 135,168 | 16 |
| `ObserverSafety.cfg` | No error | 0 | 430,641 | 77,568 | 17 |
| `PreparationSettlementLiveness.cfg` | No error | 0 | 298,593 | 69,312 | 15 |
| `PublicationSettlementLiveness.cfg` | No error | 0 | 298,593 | 69,312 | 15 |
| `LeaseDrainLiveness.cfg` | No error | 0 | 69,197 | 19,008 | 14 |
| `BrokenHalfStateCommit.cfg` | `HalfStateIsNeverGateVisible` violated | 12 | 65,620 | 14,449 | 8 |
| `BrokenRefusedParticipantPublishes.cfg` | `RefusedParticipantPublishesNothing` violated | 12 | 11,323 | 4,284 | 9 |
| `BrokenRetiredRootEntry.cfg` | `NoQueryEntersRetiredRoot` violated | 12 | 1,399 | 576 | 6 |
| `BrokenReceiptReuse.cfg` | `ReceiptPublishesAtMostOnce` violated | 12 | 22,337 | 7,467 | 10 |
| `BrokenParticipantReuse.cfg` | `LogicalPublicationIsSingleUse` violated | 12 | 56,231 | 11,875 | 12 |
| `BrokenReleaseDuringPublishing.cfg` | `PublishedReceiptWasNotCallerReleased` violated | 12 | 62,739 | 16,715 | 9 |
| `BrokenShapeValidationOrder.cfg` | `MalformedPlanReleasesNoReceipt` violated | 12 | 523 | 324 | 6 |
| `BrokenCheckPrecedence.cfg` | `GateRefusalIsFirstApplicable` violated | 12 | 2,979 | 1,328 | 6 |
| `BrokenSynthesizedCompositionIdentity.cfg` | `CommitPublishesExactlyTheReservedComposition` violated | 13 | 17,134 | 6,111 | 10 |
| `BrokenDeadlineSettlement.cfg` | `EveryPreparationEventuallySettles` violated | 13 | 10,341 | 3,376 | 7 |
| `BrokenLeaseDrainage.cfg` | `RetiredRootLeaseEventuallyDrains` violated | 13 | 69,005 | 19,008 | 14 |
| `ReachabilityPublishedComposition.cfg` | `NoPublishedCompositionObserved` violated | 12 | 20,278 | 6,818 | 10 |
| `ReachabilityOldAndNewPairsObserved.cfg` | `NoBothPairsObserved` violated | 12 | 139,674 | 28,366 | 9 |
| `ReachabilityReleaseRacesPublishing.cfg` | `NoPublishingReleaseRaceObserved` violated | 12 | 13,988 | 4,555 | 7 |
| `ReachabilityRetiredLeaseDrains.cfg` | `NoRetiredLeaseDrainObserved` violated | 12 | 40,369 | 12,364 | 10 |

The slowest positive configuration finished in 33 seconds under the gate's
`-workers auto`, inside the 600-second per-configuration budget.

Notable traces. The half-state trace stages, issues the token, assigns the
physical pointer, and yields, leaving a half-state available to gate-observing
readers. The check-precedence trace makes
cancellation and a composition mismatch applicable simultaneously and reports
the later check, which the owner's order forbids. The synthesized-identity
trace commits an identity the participant never saw, so the snapshot it
preconstructed against the reserved candidate would describe a composition
that never became current. The deadline trace abandons a `Prepared` receipt
that no publication ever consumes and never settles it. The lease trace
commits a plan that retires `RootB` before the admitted lease completes, after
which the lease can never drain.
