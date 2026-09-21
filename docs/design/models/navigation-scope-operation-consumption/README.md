# Navigation protected Scope-operation consumption model

## Owner and claim

[Inspection Subject Navigation](../../navigation-scope-operation-consumption.md)
is the normative owner.

> Navigation accepts one exact Scope-issued operation association before the
> participating effect, protects that transition from later explicit commands
> and stale work, and consumes only the correlated Scope settlement to publish
> the authorized complete Navigation outcome and release protection.

This is finite design evidence, not C# or TypeScript conformance.

## Scope-owned instance

`NavigationScopeOperationConsumption.tla` defines the named `Scope` instance of
`WorkspaceScopeOperationHandoff` with explicit substitutions for two operation
identities, one Workspace, finite Scope snapshots, exact target projection, and
the live request/result/control variables.

The imported module remains in the Scope-owned
[`workspace-scope-revisions/`](../workspace-scope-revisions/) directory. It was
mechanically extracted from `WorkspaceScopeRevisionsModel.tla`: the existing
Scope harness now invokes its `Issue`, `Abandon`, `Submit`,
`SubmitAndSettle`, `Settle`, `SettleSet`, and cancellation-control actions and
rechecks its assumptions, safety, and behavior projection. The extraction
changes no Scope validation, admission, mutation, physical publication, or
result policy.

The Navigation composition uses those owner actions. It does not copy the
Scope state machine or construct successful Scope outcomes locally.

## Finite state

The model uses:

- two Scope operation identities so an unrelated result and a superseding
  operation remain distinct from the protected operation;
- one exact Workspace and resource-free Scope association;
- old, replacement, duplicate, Pending, and empty finite Scope snapshots;
- opaque old, destination, and forwarded Type subjects;
- opaque Markup, destination, and Base defining-Library contexts;
- one exact requested inspector and one optional Navigation-authorized
  successor;
- one stale pre-effect work item and one queued maintenance identity; and
- Navigation's existing semantic revision, action generation, effect epoch,
  consumer posting, and composite acknowledgement receipt distinctions.

Non-success settlement inputs deliberately carry a current inventory different
from Navigation's retained inventory. `NoEffectChangedInventory` retains the
active occurrence but includes another current occurrence, while
`DuplicateNoIntent` leaves Workspace active. These inputs distinguish the
effect of this request from the freshness of its complete result snapshot.

The safety configurations partition the immutable profiles:

| Configuration | Profiles |
| --- | --- |
| `Safety.cfg` | committed, duplicate, no-intent, authorized-successor, forwarded, and membership-preparation outcomes |
| `SafetyNonSuccess.cfg` | rejected, failed, cancelled, superseded, and unavailable |
| `SafetyInteractions.cfg` | later explicit and participating Scope refusal |
| `SafetyStaleAcceptance.cfg` | current-slot movement before protected acceptance |
| `SafetyForeignResult.cfg` | an unrelated already-settled Scope operation |

Their union is the full safety profile set. No profile changes in `Next`.
`Liveness.cfg` selects representative success, duplicate, superseded,
unavailable, and preparation-failure profiles without changing their actions
or fairness.

## Checked properties

| Property | Claim |
| --- | --- |
| `ProtectedAcceptancePrecedesSubmission` | The matching Scope submission exists only after protected Navigation acceptance |
| `ProtectionBindsExactScopeAssociation` | Protection binds the exact Workspace, original operation, and committed current-state slot |
| `OnlyCorrelatedSettlementPublishes` | Only the original association publishes and releases the protected transition |
| `CancellationControlCannotReleaseProtection` | `ObservedNoEffect` is control evidence, not mutation settlement |
| `LocalCancellationCannotAbandonSubmittedEffect` | Local cancellation cannot reopen the state while submitted Scope work remains unsettled |
| `LaterRefusalPreservesNavigationState` | The actual before/after Scope and Navigation state tuple is unchanged by refusal, excluding diagnostic observation |
| `QueuedMaintenanceSurvivesProtection` | The queued identity remains ordered and cannot apply during protection |
| `StaleWorkCannotReplaceDuringProtection` | Pre-acceptance work cannot replace protected state |
| `StaleAuthorityCannotExecuteDuringProtection` | Pre-acceptance authority cannot execute a visible effect |
| `CompleteScopeResultIsConsumed` | Every current settlement publishes its complete snapshot; unavailable evidence remains historical |
| `MembershipPreparationFailureIsCurrentFailure` | A committed membership change plus failed preparation exposes new membership and failure |
| `ForwardedPreparedContextIsPublished` | The exact prepared forwarded Type carries the Base defining-Library context |
| `ExactRequestedOccurrenceActivates` | Explicit committed and duplicate success uses Scope's exact returned occurrence |
| `OwnerPolicyDoesNotInventActivation` | Without explicit or Navigation-authorized retention, loss of the active occurrence selects Workspace |
| `AuthorizedSuccessorIsExact` | Navigation-owned retention can select only its exact prepared successor |
| `RevisionAndGenerationRemainDistinct` | Semantic revision follows semantic change while result action publication advances generation |
| `CurrentEffectAuthorityIsExact` | Effect authority binds session, current revision, protected intent, and epoch |
| `ConsumerPostingUsesCurrentAuthority` | Consumer posting copies the complete current publication under that epoch |
| `AcknowledgementUsesCompositePublication` | Acknowledgement records both revision and generation after posting |
| `ScopeBehaviorRefinesOwner` | Every composed Scope-variable transition is an imported owner transition or stutter |
| `ProtectedAttemptEventuallyReleases` | Fair submitted protected work reaches correlated publication and release |
| `MaintenanceEventuallyResumes` | The preserved maintenance identity eventually applies after release |

`ScopeOwnerSafety` also rechecks the imported one-shot lifecycle, complete
association, exact requested occurrence, superseder distinction, and typed
cancellation-control result under the Navigation composition.

## Reachability and negative controls

Reachability configurations force replacement, forwarded Type context,
duplicate activation, no-intent Workspace fallback, exact authorized-successor
selection, later Scope refusal, local cancellation, cancellation control
no-effect and original settlement, stale current-slot rejection,
membership-preparation failure, superseded settlement, and historical
unavailability.

The committed negative controls are:

| Configuration | Mutation | Detecting property |
| --- | --- | --- |
| `BrokenSubmitBeforeAcceptance` | Submit the Scope request before Navigation acceptance | `ProtectedAcceptancePrecedesSubmission` |
| `BrokenForeignSettlement` | Publish an unrelated Scope operation result | `OnlyCorrelatedSettlementPublishes` |
| `BrokenReleaseOnControlNoEffect` | Release on cancellation `ObservedNoEffect` | `CancellationControlCannotReleaseProtection` |
| `BrokenStaleWorkReplacement` | Let stale work replace the protected current slot | `ProtectionBindsExactScopeAssociation`, `StaleWorkCannotReplaceDuringProtection` |
| `BrokenStaleEffect` | Execute pre-acceptance authority during protection | `StaleAuthorityCannotExecuteDuringProtection` |
| `BrokenRefusalSupersedes` | Make a later refusal advance intent, focus, history, and action consumption | `LaterRefusalPreservesNavigationState` |
| `BrokenOldInventory` | Publish old inventory after committed membership and failed preparation | `CompleteScopeResultIsConsumed`, `MembershipPreparationFailureIsCurrentFailure` |
| `BrokenFailedInventory` | Preserve old inventory because the request failed | `CompleteScopeResultIsConsumed` |
| `BrokenNoEffectInventory` | Preserve old inventory because this request made no membership change | `CompleteScopeResultIsConsumed` |
| `BrokenWrongRequestedOccurrence` | Activate an occurrence other than Scope's exact result occurrence | `ExactRequestedOccurrenceActivates` |
| `BrokenForwardedContext` | Retain Markup rather than the prepared Base defining-Library context | `ForwardedPreparedContextIsPublished` |

Safety mutations and reachability witnesses expect TLC exit `12`. Safety and
liveness configurations expect exit `0`. All are registered in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

The refusal observation is derived from the transition's actual primed and
unprimed state, not assigned a verdict by the mutation switch. Stale-effect
attempts carry real pre-acceptance authority; the ordinary path compares it
with the current session, revision, intent, and epoch, while its mutation
bypasses that comparison.

## Abstractions and non-claims

The model does not check structural matching, Metadata forwarding, Registry
classification, lens recommendation, rendering, Browser control, or the full
ordinary Navigation session. The forwarded subject and defining-Library
context are invocation-local owner-supplied values; the model checks only their
correlated protected publication.

Scope snapshots are finite membership/status records. Concrete object-graph
resource erasure, host locking, `ReferenceEquals`, and C# type construction
remain implementation gates. Model checking proves no implementation or
unbounded execution.

## Validation

Run each affected model directory with the pinned TLA Tools jar:

```bash
mkdir -p artifacts/tla-tmp
export TMPDIR="$PWD/artifacts/tla-tmp"
export TLA_TOOLS_JAR="$HOME/.local/share/tlaplus/tla2tools-2026.08.11.125311.jar"
export TLA_CHECK_TIMEOUT_SECONDS=120
export JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/artifacts/tla-tmp"
taskset -c 0,1 eng/run-tla-checks.sh \
  docs/design/models/workspace-scope-revisions
taskset -c 0,1 eng/run-tla-checks.sh \
  docs/design/models/navigation-scope-operation-consumption
```

On Linux with OpenJDK `21.0.12`, immutable TLA+ mirror build
`2026.08.11.125311`, two visible processors, and the unchanged 120-second
per-configuration budget:

- the Scope directory matched all 71 exact outcomes with zero unverified
  configurations. Its extracted `OperationHandoffSafety` profile exhausted
  17,747 generated / 4,808 distinct states at depth 24; the largest affected
  liveness profiles, `LivenessPhysicalRace` and `LivenessReadd`, completed in
  91 and 79 seconds;
- this Navigation directory matched all 30 exact outcomes with zero unverified
  configurations. The five exhaustive safety partitions
  explored 21,276 / 8,523, 14,604 / 5,695, 7,676 / 2,835,
  4,728 / 1,894, and 2,399 / 950 generated / distinct states. `Liveness`
  explored 14,604 / 5,695 states; and
- every reachability witness and negative control reached its registered exit
  `12`, including the no-intent Workspace and exact authorized-successor
  selection witnesses.
