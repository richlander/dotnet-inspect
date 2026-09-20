# Inspect Web retained Workspace completion

## Owner and claim

[Inspect Web retained Workspace
realization](../../inspect-web-retained-workspace-realization.md#selection-and-activation)
owns selection acceptance and completion. Its
[deletion contract](../../inspect-web-retained-workspace-realization.md#deletion)
owns managed-to-compatibility retirement and sole-active deletion.

This focused S4 model checks that a current prepared operation becomes the
sole accepted Workspace-changing operation and retains completion ownership
until both its exact managed outcome and its matching consumer completion are
terminal. A managed no-cutover failure also waits for candidate settlement.
Managed-to-compatibility retirement and sole-active deletion wait for terminal
coordinator close, including failed settlement, and matching consumer
completion. Successful managed replacement may release while its predecessor
continues draining.

The model composes the coordinator-owned
`WorkspaceRealizationCutover` module through the named `Coordinator` instance.
It uses imported candidate, cutover, close, drainage, settlement, and terminal
close actions rather than reproducing them. `Coordinator!Safety`,
`Coordinator!Spec`, `Coordinator!DrainageTerminates`, and
`Coordinator!CoordinatorCloseTerminates` are rechecked in the consumer state
space.

## Join currencies

The coordinator's realization identity remains the lifecycle authority for
candidate construction, active selection, cutover, and retirement. The S4
owner's transition identity associates one current preparation, its captured
incumbent, the accepted managed outcome, and matching consumer completion.
Older transition completion cannot release a newer accepted transition.

Successful managed cutover also records the coarse posting tuple required at
this boundary:

- exact realization identity;
- publication ordinal; and
- effect authority associated with the accepted transition.

Consumer completion must return that tuple for managed success. Other terminal
outcomes use the explicit no-posting value. The transition identity correlates
completion but does not itself grant Navigation posting authority.

## Modeled behavior

The model covers:

- managed preparation, candidate readiness, current acceptance, cutover, and
  no-cutover failure;
- preparation supersession, stale completion suppression, and candidate
  settlement;
- rejection or cancellation before acceptance while preserving the incumbent;
- ordinary cancellation after acceptance without revoking ownership;
- explicit rejection of another Workspace-changing request while an operation
  is accepted;
- managed replacement completion before predecessor settlement;
- compatibility retirement and sole-active deletion through imported
  `CloseCoordinator` and `FinishCoordinatorClose`;
- failed compatibility retirement leaving no-Workspace presentation whether
  consumer completion happens before or after the failure is observed;
- successful and failed consumer completion with visible terminal failure;
- delayed or unknown transport response without rollback;
- old completion arriving while a newer operation remains accepted; and
- permanent non-revival after managed cutover or terminal coordinator close.

F01 permits release before matching presentation completion. F05 accepts stale
compatibility preparation and lets imported coordinator close retire the newer
incumbent. F06 bypasses accepted-operation exclusion with deletion and invokes
the imported close transition. F17 treats an unknown deletion response as
permission to revive the retired sole-active presentation while the
coordinator remains closed. Each mutation follows an otherwise reachable
operation path.

## Bounds, fairness, and abstractions

Two retained definitions, two transition identities, the coordinator's three
realization identities, two intent values, and two publication ordinals are
enough to exercise incumbent replacement, stale preparation, old completion,
and all four historical mutations. These are bounded checks, not an unbounded
proof.

`FairSpec` weakly schedules enabled coordinator close requests, settlement,
terminal close, accepted managed outcomes, retirement observation, matching
consumer completion, and accepted-operation release. It does not require
preparation to be accepted, choose consumer success over visible failure, or
guarantee a new request admission while another operation owns completion.

One coordinator lifetime is modeled. Compatibility retirement or sole-active
deletion closes it permanently; coordinator reopening or another generation
would require a separately owned contract. Navigation history policy, effect
internals, compatibility packet interpretation, saved-storage behavior, and
S5/S6 implementation are abstract. Consumer completion is one coarse terminal
event. The model does not claim that completion grants posting authority or
that product enforcement already exists.

## Checked properties

`Safety` checks:

- the selected definition and managed presentation match the exact active
  coordinator realization;
- at most one transition is accepted;
- release requires terminal managed outcome, required settlement, and matching
  consumer completion;
- consumer completion matches the accepted transition and posting association;
- no-cutover failure preserves the incumbent;
- successful cutover and post-cutover failure never restore the predecessor;
- retirement removes managed selection and presentation;
- failed compatibility retirement leaves no presentation for its associated
  current operation while preserving any already-terminal consumer outcome;
- pre-acceptance cancellation preserves the incumbent;
- post-acceptance cancellation preserves completion ownership;
- terminal failure remains visible; and
- F01, F05, F06, and F17 witnesses remain absent.

`AcceptedCompletionTerminates` checks conditional release of every accepted
transition under `FairSpec`. The imported coordinator drainage and close
liveness properties are checked under the same fairness assumptions.

## Configurations

Every configuration is pinned in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

| Configuration | Exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Complete bounded safety state space and owner refinement |
| `Liveness.cfg` | 0 | Conditional accepted completion, drainage, and close progress |
| `BrokenF01PrematureRelease.cfg` | 12 | Release cannot precede matching consumer presentation completion |
| `BrokenF05StaleCompatibilityRetirement.cfg` | 12 | Stale compatibility acceptance cannot retire a newer incumbent |
| `BrokenF06DeleteBypassesAcceptedExclusion.cfg` | 12 | Deletion cannot replace an already accepted operation |
| `BrokenF17RollbackRevivesSoleActive.cfg` | 12 | Unknown response cannot revive retired sole-active presentation |
| `ReachabilityAcceptedRequestRejected.cfg` | 12 | Another Workspace-changing request is rejected while completion is owned |
| `ReachabilityCancellationBeforeAcceptance.cfg` | 12 | Pre-acceptance cancellation settles its unpublished candidate |
| `ReachabilityCancellationAfterAcceptance.cfg` | 12 | Post-acceptance cancellation leaves completion owned |
| `ReachabilityCompatibilityCompletion.cfg` | 12 | Compatibility completion follows terminal managed retirement |
| `ReachabilityCompatibilityCompletionBeforeRetirementFailure.cfg` | 12 | Later failed retirement clears an earlier successful compatibility presentation |
| `ReachabilityCompatibilityRetirementFailureBeforeCompletion.cfg` | 12 | Successful consumer completion after failed retirement remains no-Workspace |
| `ReachabilityManagedReleaseBeforePredecessorSettlement.cfg` | 12 | Managed success releases while its predecessor still drains |
| `ReachabilityNoCutoverFailureCompletion.cfg` | 12 | No-cutover failure releases after candidate settlement and consumer completion |
| `ReachabilityOldCompletionIgnored.cfg` | 12 | Old completion does not release the newer accepted operation |
| `ReachabilitySoleDeletionCompletion.cfg` | 12 | Sole deletion completes after terminal close and consumer completion |
| `ReachabilityStalePreparationCompletion.cfg` | 12 | Superseded preparation completion is ignored |
| `ReachabilityUnknownPostCutoverFailure.cfg` | 12 | Unknown post-cutover response surfaces failure without rollback |

Exit 12 is the expected invariant counterexample for a broken policy or a
negated reachability witness.

## Run

From the repository root, with the pinned tools:

```bash
model=docs/design/models/inspect-web-retained-workspace-completion
mkdir -p "$model/.scratch"
TMPDIR="$PWD/$model/.scratch" \
  JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
npx --no-install markdownlint-cli "$model/README.md"
```

The focused run on 2026-09-20 used TLA Tools `2026.08.11.125311` with OpenJDK
`21.0.12`. Safety and liveness each explored 406,749 generated states and
168,869 distinct states to depth 27. All 18 configured semantic verdicts
matched the manifest.
