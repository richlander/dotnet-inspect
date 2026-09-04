# Artifact Root publication model

`ArtifactRootPublicationLifecycle.tla` is the reusable Artifact Acquisition
owner model for
[Artifact Root preparation and scope publication](../../artifact-acquisition-and-workspaces.md#artifact-root-preparation-and-scope-publication).
It checks one complete prepared physical Root batch and one sealed Scope
publication participant against one exact runtime Workspace.

`ArtifactRootPublicationModel.tla` supplies the finite model-checking bound and
the deliberately broken transitions. Scenario selection and mutations remain
outside the reusable owner module so the Workspace Scope model tracked by
[#5796](https://github.com/richlander/dotnet-inspect/issues/5796) can consume
the owner with a named `INSTANCE` and its own variables, bounds, and fairness.

## Owner claim

Given one exact runtime Workspace, one current physical Root composition, one
complete prepared batch, and one sealed Scope publication participant,
publication changes the physical composition and opaque Scope pointer together
exactly once, or preserves both old pointers and releases provisional
authority.

The model preserves these owner-issued join currencies:

- exact runtime Workspace identity;
- current, expected, and reserved physical-composition generations;
- one preparation receipt and its `Prepared`, `Publishing`, `Published`, or
  `Released` state;
- the complete desired physical Root set and exact prepared subset;
- current, expected, and candidate opaque Scope publication bases;
- participant availability, refusal, prepared-token, and committed states;
- exact plan/receipt cancellation authority and finite-deadline association;
- cancellation and deadline status at the final commit linearization; and
- process-lifetime issuance counts for physical generations and Scope bases.

The final `CommitPublication` action is one atomic, non-yielding state
transition. It publishes the candidate physical generation, complete desired
Root set, Scope base, receipt result, and participant result together.
Cancellation, expiry, or runtime close can win before that action; after it
runs, later cancellation cannot change the published result.

## Bounded harness

The finite harness uses:

- one live and one foreign Workspace;
- one live and one foreign receipt;
- an initial, candidate, and stale physical-composition identity;
- an initial, candidate, and stale opaque Scope base;
- one omitted old Root, one retained Root, and one prepared Root; and
- exact, stale-composition, stale-Scope, foreign-Workspace, foreign-receipt,
  cancellation-authority-mismatch, deadline-mismatch, and
  incomplete-desired-set scenarios.

The successful desired physical set retains one old Root, adopts the complete
one-entry prepared batch, and omits another old Root. Root identities and sets
are opaque: the model does not interpret logical membership, revision
contents, occurrence order, policy, closure, or operation results.

Before staging, the owner validates exact Workspace and receipt identity,
plan/receipt cancellation authority and deadline association, plus the complete
desired and prepared sets. Under the shared composition gate it then validates
runtime openness, cancellation, deadline, expected physical generation, and
candidate-generation freshness. Participant preparation validates the expected
opaque Scope base and consumes the participant exactly once. A refusal releases
the receipt and all staging; an accepted token enables only the atomic final
commit.

Malformed input is rejected before receipt or participant consumption. The
receipt remains caller-owned until explicit cancellation, runtime close, or
the weakly fair finite-deadline expiry releases it. Applicability or
participant refusal after publication processing begins instead releases the
receipt immediately.

The finite harness has one publication operation. Its ordinary owner actions
therefore cannot independently consume the participant or reserve the
candidate identities before their corresponding lifecycle phase:
`RejectConsumedParticipant`, `RejectScopeCandidateIdentity`, and the two
candidate-freshness refusal guards are consumer-composition boundaries rather
than reachable standalone scenarios. #5796 must exercise them when its
additional operations can make those states current. The standalone freshness
mutations still demonstrate that reissuing either committed identity violates
the owner invariant.

## Non-claims

The model deliberately excludes:

- logical Workspace membership and revision contents;
- Root occurrence identity or ordering;
- Add, Replace, Remove, Clear, and dependency-expansion policy;
- closure evidence and operation-result structure;
- Navigation or browser behavior;
- packages, bytes, sources, and source authorization;
- context construction, query leases, retirement drainage, and budgets; and
- implementation conformance or a particular synchronization primitive.

The current-pointer invariant represents the shared gate's observation
contract, not a memory-model proof. Browser/Wasm non-blocking gate
implementation and old-generation lease drainage remain separate
implementation gates.

## Checked properties

| Property | Claim |
| --- | --- |
| `CompositionIdentityNeverReused` | The initial generation and every reserved candidate physical generation are issued at most once. |
| `ScopeBaseNeverReused` | The initial Scope base and every candidate committed base are issued at most once. |
| `CurrentPointersArePaired` | Current physical and Scope pointers are either both old or both new. |
| `CurrentRootsMatchComposition` | The initial generation names the old Root set and the candidate generation names the complete desired set. |
| `UnpublishedCandidateIsNotCurrent` | Staging, refusal, cancellation, expiry, and malformed input expose no candidate pointer. |
| `PublishedPairIsExact` | Publication exposes the candidate physical generation, candidate Scope base, and complete desired set together. |
| `TerminalReceiptReleasesProvisionalAuthority` | A `Published` or `Released` receipt retains no provisional or staged physical authority. |
| `ParticipantRefusalReleasesStaging` | A consumed participant refusal leaves a released receipt and no staging. |
| `ReceiptHasExactlyOneTerminalOutcome` | The receipt reaches exactly one terminal `Published` or `Released` transition. |
| `ReceiptPublishesAtMostOnce` | Receipt replay cannot publish a second time. |
| `ParticipantIsSingleUse` | `PrepareCommit` invocation or replay consumes the participant at most once. |
| `ParticipantCommitsAtMostOnce` | A participant token swaps its Scope pointer at most once. |
| `PublicationCommitsAtMostOnce` | The paired publication linearization occurs at most once. |
| `PublishedReceiptMatchesCommit` | Published receipt, participant commit, and paired publication counts agree. |
| `RefusalPreservesBothPointers` | Every modeled rejection or refusal preserves both old current pointers and the old physical Root set. |
| `MalformedRejectionPreservesCallerAuthority` | Pre-consumption rejection leaves the prepared receipt, participant, and provisional authority under caller ownership. |
| `CommittedCancellationAuthorityWasExact` | A commit witnessed the receipt's exact plan cancellation authority. |
| `CommittedDeadlineWasExact` | A commit witnessed the receipt's exact plan deadline. |
| `Committed*Was*` properties | A commit witnessed exact Workspace, physical generation, Scope base, receipt, complete desired set, prepared participant, cancellation, deadline, and open runtime. |
| `NoPublicationAfterRuntimeClose` | A runtime that stopped accepting work before publication cannot later publish. |
| `FinalCommitWins` | Once the atomic commit occurs, later cancellation or deadline expiry cannot rewrite the published outcome. |
| `ReplayDoesNotRepublish` | Observed receipt or participant replay leaves all commit counts at one. |
| `EveryPreparedReceiptEventuallySettles` | Weak fairness and finite deadline expiry eventually produce `Published` or `Released`. |
| `EveryStartedPublicationEventuallySettles` | Staged publication eventually commits or releases. |
| `EveryPreparedTokenEventuallySettles` | A no-fail commit token eventually commits or is refused by a pre-commit cancellation, expiry, or close. |

## Configurations

| Configuration | Expected result and purpose |
| --- | --- |
| `Safety.cfg` | Success; checks the complete safety graph over success, participant refusal, cancellation, expiry, runtime close, and replay. |
| `Liveness.cfg` | Success; checks receipt, staged-publication, and prepared-token settlement under weak fairness. |
| `ReachabilityPublication.cfg` | Exit 12; reaches a complete paired publication. |
| `ReachabilityStaleCompositionRefusal.cfg` | Exit 12; reaches stale physical-generation refusal while pointer-preservation and release invariants remain enabled. |
| `ReachabilityStaleScopeBaseRefusal.cfg` | Exit 12; stages physically, then reaches participant refusal of the stale Scope base without publishing. |
| `ReachabilityForeignWorkspaceRejection.cfg` | Exit 12; reaches pre-consumption foreign-Workspace rejection. |
| `ReachabilityForeignReceiptRejection.cfg` | Exit 12; reaches pre-consumption foreign-receipt rejection. |
| `ReachabilityCancellationAuthorityMismatchRejection.cfg` | Exit 12; reaches pre-consumption rejection when the receipt's cancellation authority differs from the plan while caller authority remains intact. |
| `ReachabilityDeadlineMismatchRejection.cfg` | Exit 12; reaches pre-consumption rejection when the receipt's finite deadline differs from the plan while caller authority remains intact. |
| `ReachabilityIncompleteDesiredSetRejection.cfg` | Exit 12; reaches pre-consumption rejection of an incomplete desired set. |
| `ReachabilityCancellationRelease.cfg` | Exit 12; cancellation wins before commit and releases the prepared receipt. |
| `ReachabilityDeadlineRelease.cfg` | Exit 12; finite deadline expiry releases the prepared receipt. |
| `ReachabilityParticipantRefusal.cfg` | Exit 12; participant refusal after physical staging releases every provisional authority. |
| `ReachabilityCommittedReplayAndCancellation.cfg` | Exit 12; reaches successful publication, both replay attempts, and cancellation after the commit linearization. |
| `ReachabilityRuntimeCloseRefusal.cfg` | Exit 12; runtime close wins before commit and releases the prepared receipt. |
| `BrokenCompositionIdentityReuse.cfg` | Exit 12; replay reissues the committed physical generation and violates freshness. |
| `BrokenScopeBaseReuse.cfg` | Exit 12; replay reissues the committed Scope base and violates freshness. |
| `BrokenStaleCompositionCommit.cfg` | Exit 12; bypassing the physical-generation check violates its commit witness. |
| `BrokenStaleScopeBaseCommit.cfg` | Exit 12; bypassing the Scope-base check violates its commit witness. |
| `BrokenCancellationAuthorityMismatchCommit.cfg` | Exit 12; bypassing shape validation commits a receipt bound to another cancellation authority. |
| `BrokenDeadlineMismatchCommit.cfg` | Exit 12; bypassing shape validation commits a receipt bound to another finite deadline. |
| `BrokenCancellationRetainsAuthority.cfg` | Exit 12; cancelled release retains provisional authority. |
| `BrokenParticipantRefusalRetainsStaging.cfg` | Exit 12; participant refusal retains staged authority. |
| `BrokenReceiptReplay.cfg` | Exit 12; a second receipt publication violates one-shot receipt use. |
| `BrokenParticipantReplay.cfg` | Exit 12; a second participant commit violates single-use participation. |
| `BrokenTornPairedPublication.cfg` | Exit 12; the physical pointer changes without the Scope pointer. |
| `BrokenIncompleteDesiredSetCommit.cfg` | Exit 12; an incomplete submitted set reaches current state. |
| `BrokenPublicationAfterRuntimeClose.cfg` | Exit 12; publication occurs after the runtime stopped accepting work. |

All exact outcomes are registered in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md), then run:

```bash
TLA_TOOLS_JAR="$HOME/.local/share/tlaplus/tla2tools.jar" \
  ./eng/run-tla-checks.sh docs/design/models/artifact-root-publication
```

The runner executes configurations sequentially. Direct deterministic runs
used `-workers 1 -seed 1 -fp 1`.

## TLC evidence

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned immutable
TLA+ v1.8.0 mirror build `2026.08.11.125311` (revision `0894c34`), SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
The artifact model contributed two modules and 28 configurations, all with
registered exact outcomes and none unverified within budget. Because this
change also extends the exact-outcome manifest, the complete changed-file gate
checked 10 modules and 117 configurations: 63 exact outcomes, no timeouts or
unverified results, and no unexpected semantic verdicts.

`Safety.cfg` and `Liveness.cfg` exhaust the base scenario. The five original
non-base scenarios plus the cancellation-authority and deadline mismatch
scenarios use focused reachability configurations that stop at their named
witness. Exact-head review additionally ran the full safety and liveness sets
over each original non-base scenario without finding a violation; those probes
are supporting evidence, not additional registered gates. The harness does not
vary `SubmittedPreparedRoots` independently: its incomplete-desired-set
scenario exercises desired-set inequality and the prepared-subset violation,
which are the shape properties claimed by that configuration.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `Safety.cfg` | No error | 211 | 121 | 9 |
| `Liveness.cfg` | No error | 211 | 121 | 9 |
| `ReachabilityPublication.cfg` | `NoPublicationObserved` violated | 131 | 90 | 7 |
| `ReachabilityStaleCompositionRefusal.cfg` | `NoStaleCompositionRefusalObserved` violated | 21 | 16 | 5 |
| `ReachabilityStaleScopeBaseRefusal.cfg` | `NoStaleScopeBaseRefusalObserved` violated | 83 | 55 | 6 |
| `ReachabilityForeignWorkspaceRejection.cfg` | `NoForeignWorkspaceRejectionObserved` violated | 21 | 16 | 5 |
| `ReachabilityForeignReceiptRejection.cfg` | `NoForeignReceiptRejectionObserved` violated | 21 | 16 | 5 |
| `ReachabilityCancellationAuthorityMismatchRejection.cfg` | `NoCancellationAuthorityMismatchRejectionObserved` violated | 21 | 16 | 5 |
| `ReachabilityDeadlineMismatchRejection.cfg` | `NoDeadlineMismatchRejectionObserved` violated | 21 | 16 | 5 |
| `ReachabilityIncompleteDesiredSetRejection.cfg` | `NoIncompleteDesiredSetRejectionObserved` violated | 21 | 16 | 5 |
| `ReachabilityCancellationRelease.cfg` | `NoCancelledRefusalObserved` violated | 185 | 106 | 9 |
| `ReachabilityDeadlineRelease.cfg` | `NoExpiredRefusalObserved` violated | 185 | 106 | 9 |
| `ReachabilityParticipantRefusal.cfg` | `NoParticipantRefusalObserved` violated | 83 | 55 | 6 |
| `ReachabilityCommittedReplayAndCancellation.cfg` | `NoCommittedReplaySequenceObserved` violated | 207 | 121 | 9 |
| `ReachabilityRuntimeCloseRefusal.cfg` | `NoRuntimeClosedRefusalObserved` violated | 185 | 106 | 9 |
| `BrokenCompositionIdentityReuse.cfg` | `CompositionIdentityNeverReused` violated | 243 | 137 | 10 |
| `BrokenScopeBaseReuse.cfg` | `ScopeBaseNeverReused` violated | 243 | 137 | 10 |
| `BrokenStaleCompositionCommit.cfg` | `CommittedCompositionWasCurrent` violated | 64 | 39 | 5 |
| `BrokenStaleScopeBaseCommit.cfg` | `CommittedScopeBaseWasCurrent` violated | 99 | 66 | 6 |
| `BrokenCancellationAuthorityMismatchCommit.cfg` | `CommittedCancellationAuthorityWasExact` violated | 91 | 58 | 6 |
| `BrokenDeadlineMismatchCommit.cfg` | `CommittedDeadlineWasExact` violated | 91 | 58 | 6 |
| `BrokenCancellationRetainsAuthority.cfg` | `TerminalReceiptReleasesProvisionalAuthority` violated | 215 | 125 | 9 |
| `BrokenParticipantRefusalRetainsStaging.cfg` | `ParticipantRefusalReleasesStaging` violated | 212 | 122 | 9 |
| `BrokenReceiptReplay.cfg` | `ReceiptPublishesAtMostOnce` violated | 243 | 137 | 10 |
| `BrokenParticipantReplay.cfg` | `ParticipantIsSingleUse` violated | 243 | 137 | 9 |
| `BrokenTornPairedPublication.cfg` | `CurrentPointersArePaired` violated | 212 | 122 | 9 |
| `BrokenIncompleteDesiredSetCommit.cfg` | `CommittedDesiredSetWasComplete` violated | 91 | 58 | 6 |
| `BrokenPublicationAfterRuntimeClose.cfg` | `NoPublicationAfterRuntimeClose` violated | 215 | 125 | 9 |

The two positive checks explored their complete bounded graphs. Each
reachability configuration stopped only after reaching its named path, and
each mutation stopped at its intended invariant.
