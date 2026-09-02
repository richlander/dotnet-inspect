# Inspection subject navigation design models

These are executable TLA+ models of the concurrency and authority mechanisms
described in [`../../inspection-subject-navigation.md`](../../inspection-subject-navigation.md).
They replace prose state-machine description with specifications a model
checker can exhaust.

There are three independent models. None imports another, and each is small
enough for TLC to explore its entire state space within seconds in the recorded
environment.

| Model | Mechanism |
| --- | --- |
| `NavigationSession.tla` | Retained session: intent, supersession, maintenance order, effect authority, consumer synchronization |
| `AtomicRestoration.tla` | Canonical restoration participant: one exact requested subject+lens pair published as a prepared snapshot |
| `SnapshotAuthority.tla` | Retained versus stateless execution and the prior state each may read |

## What these models cover

Each model is a design specification. TLC checks that the design's own rules
are mutually consistent across every interleaving of a small finite instance:
every ordering of user intent, background maintenance, preparation readiness,
fact completion, failure, and consumer acknowledgement that the rules permit.

## What these models do not cover

They are not evidence for any of the following, and no claim here should be
read that way:

- **Identity ranking.** Initial subject recommendation, Type candidate tiers,
  Library declaration order, and lens preference are not modelled. Subjects
  and lenses appear only as opaque values.
- **Workspace isolation and structural ancestry.** Workspace identity,
  retained-coordinate occurrence identity, the
  `Workspace -> (Package | Root) -> Library -> Type -> Member` grammar, and
  complete descendant binding are not modelled. Each retained-session instance
  assumes one exact Workspace boundary; implementation gates must reject
  foreign-Workspace subject actions and restoration payloads and prevent
  foreign evidence from entering a snapshot.
- **Availability classification.** Descriptor classification and the
  reconciliation tables are not modelled. `NavigationSession.tla` does model
  the narrower rule that a completed `Unavailable` or `Failed` result advances
  revision exactly when its complete returned snapshot changed. The model
  distinguishes Navigation preparation failure, which has no installable
  replacement snapshot, from a failed Registry or policy evaluation. It does
  not distinguish Registry failure from policy failure.
- **External membership effects.** The protected admission barrier around
  Workspace-owner admission, removal, replacement, Close, and invalidation is
  not modelled. The model's opaque `coordinate` intent represents
  Navigation-local coordinate activation and variation, not an external effect
  that must consume its correlated result before another explicit command can
  be admitted. Named implementation gates enforce that barrier.
- **UI accessibility.** Focus, roving `tabindex`, menu and tablist semantics,
  and rendering belong to [Inspect Web Navigation
  Presentation](../../inspect-web-navigation-presentation.md); focus movement
  and history belong to [Inspect Web Navigation
  Consumer](../../inspect-web-navigation-consumer.md). Both appear here only
  as an abstract "consumer installs the complete current snapshot under
  authority and executes a visible effect" step.
- **Implementation correctness.** Nothing here proves that a future C# or
  TypeScript implementation conforms to these specifications. Conformance is
  the job of the named implementation gates in the owning document.
- **Complete restoration coordination.** `AtomicRestoration.tla` covers only
  the navigation participant's subject+lens preparation. Other participants,
  transaction commit, and installation belong to
  [Workspace Definitions](../../workspace-definitions.md). #4787 established
  the current version-2 shape; #5525 tracks Workspace/Package subject and
  retained-context adoption.
- **Retained occurrence context.** `AtomicRestoration.tla` does not model the
  exact retained occurrence or descendant Library/Type/Member path supplied
  independently from an active Workspace subject. It also does not model the
  Type-inventory Library context Navigation derives from that path and current
  realized facts. Implementation gates check internal context consistency and
  reconciliation, active-subject/path compatibility, and distinct
  same-occurrence Workspace restorations with different lower context.
- **Acquisition, security, or performance.** Coordinate realization appears
  only as an external prerequisite that may abort.

The models are also finite by construction: a bounded number of intents,
maintenance requests, and operations. They establish that no permitted
interleaving within those bounds violates the invariants; they are not
inductive proofs for unbounded runs.

## Correlated claims

A claim that says "an operation like this eventually reaches an outcome like
that" can be discharged by some *other* operation's outcome. Every claim in
these models that could fall into that trap names the thing it is talking
about: a maintenance request number, an intent token, a restoration token and
its retained payload, or a navigation operation ID that the result carries
back. The three models therefore carry three correlation currencies:

| Model | Currency | Used by |
| --- | --- | --- |
| `NavigationSession.tla` | maintenance and synchronization request numbers, exact settled-request sets, intent token, consumer-installed revision, acknowledged-consumer revision | per-request admission, per-token settlement, per-authority installation, and product/consumer synchronization |
| `AtomicRestoration.tla` | restoration token plus an independently retained request payload | per-attempt settlement and exact prepared result |
| `SnapshotAuthority.tla` | operation ID plus independently retained requested lens | per-operation resolution, rejection, and exact applied result |

## `NavigationSession.tla`

One retained navigation session holding zero or one installed snapshot,
consumer-installed state, and the complete snapshot revision last acknowledged
by its retained consumer. The product issues monotonic explicit intent tokens
for subject, lens, Navigation-local coordinate, and canonical-restoration work.
The owner issues maintenance request numbers for standalone inventory refresh and
reconciliation and retains the exact identities admitted. The bounded
environment issues exact synchronization request numbers. Every admitted
result returns four-part effect authority: session identity, snapshot state
revision, intent token, and effect epoch. A consumer validates that authority,
installs the complete result snapshot under the exact epoch, then acknowledges
or abandons it. Installation does not itself advance the acknowledgement
receipt.

Modelled behaviour includes: a newer explicit intent superseding older explicit
and maintenance work; a superseded operation returning late; an external
prerequisite abort that ends an intent without a navigation result; standalone
maintenance queued in request order while its facts complete in any order; and
a consumer holding authority that has since stopped being current. Completed
`Unavailable` and Registry or policy `Failed` results carry complete returned
snapshots; Navigation preparation failure is a distinct retaining action.
It also models abandonment preserving acknowledgement debt before or after
installation, a later non-installing result synchronizing that debt, and a
dedicated fresh-authority synchronization result after queued maintenance
drains. Synchronization request generation is finite; the product response path
has no modeled retry ceiling.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `LatestIntentSafety` | Unresolved explicit work carries the current token, every superseded operation carries a strictly older one, and unconsumed authority is the current intent's |
| `ExactCurrentAuthority` | Unconsumed authority matches the session identity, installed revision, current intent, and current epoch exactly |
| `MaintenanceAdmissionDiscipline` | No maintenance was admitted while explicit work was unresolved or an effect was unconsumed |
| `MaintenanceRequestOrder` | Maintenance was admitted in owner-issued request order, never fact-completion order, and the queue stays ordered and outstanding |
| `NoStaleVisibleEffect` | Every consumer-visible effect executed under exactly the session's current unconsumed authority |
| `MaintenanceRegatherDiscipline` | A stale request cannot become ready or admit until rebuilding requires and gathering completes a re-gather |
| `NonSuccessRevisionMatchesSnapshotChange` | A completed unavailable or failed result advances revision exactly when the complete returned snapshot changed |
| `PreparationFailureRetainsSnapshotAndRevision` | Navigation preparation failure retains the complete installed snapshot and revision, identifies its source, and returns fresh retained product and host authority |
| `ConsumerSynchronizationShape` | The acknowledged receipt never leads consumer-installed state, consumer-installed state never leads the product, and equal revisions carry equal complete snapshots |
| `ConsumerVisibleEffectSynchronizes` | A current visible effect installs the complete result snapshot, revision, and exact effect epoch before acknowledgement |
| `AcknowledgementRequiresConsumerSynchronization` | Acknowledgement advances the receipt only after the complete current snapshot was installed under the current effect epoch |
| `AbandonmentPreservesAcknowledgement` | Abandonment never advances the product-owned acknowledgement receipt, including after consumer installation |
| `CurrentResultDispositionIsExact` | Every current result copies and derives `Current` or `Synchronization required` from the pre-state product-owned acknowledged receipt, independently of semantic outcome |
| `SynchronizationRequestDiscipline` | Every settled synchronization request names one exact issued request, receives a complete current result, waits for explicit work to resolve, and follows queued maintenance |
| `SynchronizationAuthorityIsCurrent` | A dedicated synchronization result and its authority name the complete current product snapshot and revision |

Progress is stated per request and per intent token rather than only for the
session as a whole. Each property below names a specific blocker or a specific
token, so another request's admission or a newer intent's resolution cannot
discharge it.

| Liveness property | Claim |
| --- | --- |
| `ExplicitWorkEventuallyResolves` | Explicit work always reaches a result, a retained outcome, or an abort |
| `EveryExplicitIntentSettles` | Each intent token's own operation stops being in flight and stops being an outstanding superseded result, so supersession settles rather than dangles |
| `EffectEventuallyConsumed` | Unconsumed authority is eventually acknowledged, abandoned, or superseded |
| `MaintenanceEventuallyDrains` | The whole queue eventually drains |
| `EveryQueuedRequestIsAdmitted` | Every queued request's exact identity eventually appears in the admitted-request set |
| `EverySynchronizationRequestSettles` | Every bounded external synchronization request receives dedicated fresh authority or is discharged by acknowledgement of another current result |
| `BlockedMaintenanceResumes` | A request blocked by unresolved explicit work or an unconsumed effect is still admitted once that work resolves and that effect is released |
| `MaintenanceResumesAfterAbort` | A request blocked behind an external prerequisite abort is admitted after that abort effect is acknowledged or abandoned |
| `StaleBasisMaintenanceResumes` | The same request whose basis a newer snapshot invalidated rebuilds, re-gathers, and is admitted in original request order |

Liveness uses weak fairness on explicit resolution, discarding superseded
results, per-request fact gathering and rebuilding, maintenance admission,
synchronization authority, consumer-visible snapshot installation, and
acknowledgement. Beginning a new explicit intent is deliberately unfair and
bounded, so TLC can show that the queue drains once intents stop arriving.

## `AtomicRestoration.tla`

Canonical restoration gives Inspection Subject Navigation one exact requested
subject+lens payload. The owner retains that request independently while its
subject and lens halves resolve, then publishes one prepared snapshot only
when both halves are ready. Complete restoration coordination and installation
are deliberately absent.

The two halves are each working, ready, or failed. A half-failure settles as
aborted, including after the other half became ready. A newer intent prevents
an older live preparation from publishing and settles it as discarded unless
it had already failed.

Requests and results are separate records. This makes exact payload correlation
an executable claim rather than trusting mutable preparation state or an
operation token: a prepared result must contain the independently retained
subject and lens requested for that same token.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `PreparationRequiresReadyPairAndCurrentIntent` | A prepared result was published only for the current token after both halves became ready |
| `PreparedPairEqualsRequestedPayload` | A prepared result contains the exact independently retained requested subject and lens |
| `NoSupersededPreparationResult` | A preparation replaced by a newer intent was never published |
| `FailedPreparationNeverPrepared` | A preparation that failed in either half was never published |
| `PreparationIsInvisibleUntilPublished` | A live preparation exposes no partial result |

| Liveness property | Claim |
| --- | --- |
| `EveryAttemptSettles` | Every attempt reaches its own prepared, aborted, or discarded result |
| `FailedAttemptsAbort` | A half-failed attempt settles specifically as aborted |

## `SnapshotAuthority.tla`

Retained and stateless execution of the same navigation work. A retained
operation reads prior state only from its session's installed snapshot and
rejects explicitly supplied prior state with a typed outcome. A stateless
evaluation may consume an explicit prior snapshot as data and retains nothing.

Snapshots carry **custody** as well as origin. Custody says who holds the
value: `sessionInstalled` is the snapshot the session installed, and `supplied`
is any value handed in by a consumer. A stale copy of this session's own
earlier snapshot has session origin and a session lens, so origin and lens
alone would accept it; its custody keeps it detectably supplied. Each installed
snapshot also records the origin and the custody of the snapshot it was derived
from, so adopting a supplied value shows up in the installed record rather than
having to be inferred.

Operations and results are correlated by an **operation ID** that the session
assigns on submission and every result carries back. That is what lets a claim
name one operation's own outcome instead of settling for some outcome having
occurred: a later operation that is wrongly applied is not excused by an
earlier one having been rejected.

The requested lens is also retained independently by operation ID. An applied
result must return that exact lens, and retained execution must install it
rather than another lens that happens to be admissible for the session.

A retained typed rejection is still a retained result. It changes no installed
state, but it advances the effect epoch and returns current retained
authority, so a consumer renders and acknowledges a rejection exactly as it
does an applied outcome. Stateless evaluation is unaffected: it issues no
retained authority, whether it succeeds or is rejected.

| Invariant | Claim |
| --- | --- |
| `TypeOK` | State stays within its declared shape |
| `InstalledSnapshotIsSessionCustody` | The installed snapshot is in session custody, session-owned, carries a session lens, and was derived from a snapshot that was itself in session custody, so no supplied value — including a stale same-session copy — becomes retained state |
| `OnlyRetainedExecutionInstalls` | The installed revision advances only through retained execution |
| `RetainedPriorStateIsInstalledSnapshot` | Every retained operation used the session-custody installed snapshot as its only prior state |
| `OperationAndResultAreCorrelated` | Every result names a submitted operation, an in-flight operation is one past the outstanding result, and with nothing in flight the outstanding result belongs to the most recent operation |
| `NonApplyStepsPreserveInstalledSnapshot` | Stateless execution, stateless rejection, retained rejection, and the authority-only steps left the whole installed snapshot record unchanged, compared field by field rather than by revision counting |
| `RetainedRejectionHasExactAuthorityAndInstallsNothing` | Every retained rejection advanced the effect epoch, returned authority naming this session, the unchanged installed revision, the current operation, and the new epoch, and installed nothing |
| `RetainedCommittedLensEqualsInstalledLens` | The retained committed lens equals the installed snapshot's lens and is never a lens only supplied data could carry |
| `AppliedResultEqualsExactRequest` | Every applied result returns the independently retained requested lens, and retained execution installs that exact lens |
| `StatelessIssuesNoRetainedAuthority` | A stateless evaluation issues no retained effect authority |
| `ExactCurrentAuthority` | Unconsumed authority matches the session identity, installed revision, current operation, and epoch exactly |
| `StaleOrForeignAuthorityNeverExecutes` | Stale or foreign authority never executes deferred work |

| Liveness property | Claim |
| --- | --- |
| `EveryCommandResolves` | Each operation reaches the result carrying its own operation ID |
| `EffectEventuallyConsumed` | Unconsumed authority is eventually acknowledged, abandoned, or superseded |
| `SuppliedRetainedPriorStateIsAlwaysRejected` | Every retained operation carrying explicitly supplied prior state reaches its own typed rejection, identified by that operation's ID |

## Alignment with the owning document

The three models were scanned against the current owning document. The
remaining differences are deliberate abstractions rather than disagreements:

- **Outcome classes.** Effect authority still uses the internal `applied` and
  `retained` execution classes, but `NavigationSession.tla` now separately
  records semantic outcome, complete-snapshot change, prior revision, and
  result revision. Changed `Unavailable` and Registry or policy `Failed`
  results advance revision; unchanged ones retain it. `Rejected` and
  Navigation preparation `Failed` results always retain revision; superseded
  work returns no visible effect. The model-only `synchronize` class changes no
  semantic navigation state and carries the complete installed snapshot under
  fresh authority. A model-only occurrence field identifies a result produced
  by the Navigation preparation-failure action independently from its reported
  source.
- **Consumer receipt.** The model records the complete snapshot and revision
  last acknowledged by one retained consumer separately from the consumer's
  installed snapshot, revision, and effect epoch. It abstracts host rendering
  and history, but it does not abstract whether the consumer installed the
  current result under current authority before acknowledgement.
- **Synchronization demand.** `MaxSynchronization` bounds external request
  generation so repeated request/abandon cycles remain finite. It does not
  bound product responses: every issued request has a per-request settlement
  property, and another current result may discharge a pending request.
- **Superseded maintenance results.** A newer explicit intent invalidates
  already gathered maintenance facts. The queued request remains, rebuilds
  from the replacement snapshot, and re-gathers before admission.
- **Coordinate intent scope.** The model's coordinate kind covers
  Navigation-local activation and variation with ordinary latest-admitted
  supersession. It does not abstract Workspace-owner admission, removal,
  replacement, Close, or invalidation because those external effects require a
  protected admission barrier and mandatory correlated-result consumption.
  Implementation gates check that barrier, exact occurrence, owner-result
  correlation, and Workspace containment.
- **Optional restoration inputs.** Canonical restoration's subject and lens are
  optional in the packet, and retained occurrence context is independently
  optional. A subject-less request may carry root-only occurrence context but
  not a retained Library/Type/Member path. The model always receives both
  resolved subject and lens values because the claim under test begins at the
  narrower pair-publication boundary: one exact pair is prepared and published
  together. It models neither retained occurrence context, valid optional-input
  combinations, other restoration participants, nor installation.
- **Retained request payload.** `SnapshotAuthority.tla` instantiates exact
  retained-result correlation for a lens request. Exact subject-result
  correlation remains a named implementation gate; canonical preparation
  checks the complete subject+lens pair.
- **Unmodelled currencies.** Action IDs, generations, descriptor states,
  diagnostics, and correspondence are not modelled. Subjects, lenses, and
  snapshots are opaque values. Operation IDs, synchronization request numbers,
  retained request maps, and the preparation-failure occurrence field are model
  correlation currencies, not proposed product fields.

## Guard witnesses

Some claims are about a step rather than a state: "no maintenance was admitted
while an effect was unconsumed" is not visible in any single state after the
fact. Those claims use latching boolean witness variables, named
`admissionWitness`, `regatherWitness`, `revisionWitness`, `orderWitness`,
`visibleWitness`, `consumerSyncWitness`, `consumerAckWitness`,
`abandonmentWitness`, `dispositionWitness`, `synchronizationWitness`,
`readinessWitness`, `payloadWitness`, `basisWitness`,
`snapshotStabilityWitness`, `rejectionAuthorityWitness`, and `executeWitness`.

A witness re-derives, in the pre-state and independently of the action's own
guard, the exact condition the design requires for the step being taken, and
conjoins it into the witness. The paired invariant then asserts the witness was
never falsified. If a future edit weakens an action guard, the witness still
evaluates the real pre-state, so TLC reports a counterexample. Witnesses are
model bookkeeping, not product state.

`revisionWitness` independently compares complete non-success results with
their pre-state installation. For Navigation preparation failure it also
latches the exact failure source and fresh retained product and host authority,
so removing authority or coherently rewriting result and authority to another
post-state is still caught. The result's model-only
`preparationFailureOccurred` field records that the preparation-failure action
ran without deriving that fact from the reported source. The dedicated
invariant correlates the independently recorded occurrence with the exact
source.

`snapshotStabilityWitness` compares the whole installed snapshot record rather
than its revision, so a step that rewrote the snapshot's lens or provenance
while leaving the revision alone is still caught. Probe `SA5`/`SA6` below
demonstrates exactly that gap in revision arithmetic.

`AtomicRestoration.tla` uses `readinessWitness` and `payloadWitness` for the
navigation participant's publish step. Its request map is ordinary modelled
state written only when an intent begins; the prepared result is maintained
separately and must equal that exact request.

## Running TLC

The models are plain TLA+ and need only a JVM and `tla2tools.jar`. TLC reads
`<Module>.cfg` from beside the module, so no extra arguments are required.

From this directory, with `tla2tools.jar` in `$TLA_TOOLS`:

```sh
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -cleanup NavigationSession.tla
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -cleanup AtomicRestoration.tla
java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
  -workers auto -cleanup SnapshotAuthority.tla
```

`tla2tools.jar` is the official release asset from
`https://github.com/tlaplus/tlaplus/releases`. Neither it nor a JVM is
vendored in this repository, and no repository build or test target depends on
them.

### Locally used tools

The results below came from:

- TLC `TLC2 Version 2026.08.21.155922 (rev: 9787e65)`, from the official
  `v1.8.0` `tla2tools.jar` asset downloaded on 2026-08-27.
- OpenJDK `21.0.12+8-1-24.04`, Ubuntu Linux `amd64`.
- Run on 2026-08-29.

## Evidence

Three different things are recorded below, and they are not interchangeable.
Exhaustive checking says the shipped configuration has no reachable
counterexample. Action coverage says each modelled step actually occurs in that
exploration. Mutation probes say a named claim would catch a specific broken
rule.

### Exhaustive model checking

Each run is an exhaustive breadth-first exploration of the shipped `.cfg`.
Generated and distinct state counts are stable across repeated runs on the same
tools version. All three report
`Model checking completed. No error has been found.`

| Model | States generated | Distinct states | Search depth |
| --- | --- | --- | --- |
| `NavigationSession.tla` | 668,938 | 116,745 | 23 |
| `AtomicRestoration.tla` | 8,081 | 2,333 | 9 |
| `SnapshotAuthority.tla` | 36,755 | 13,790 | 9 |

The recorded `NavigationSession.tla` depth is from the single-worker action
coverage run. Automatic-worker runs produced the same generated and distinct
state counts with reported depths 23 and 24, so parallel traversal depth is not
treated as stable evidence.

The additional state records semantic unavailable and failed outcomes
independently from their source and apply/retain execution class, retains
canonical request payloads independently from prepared results, and retains
each operation's requested lens independently from its result. Stale
maintenance also records whether the same request still owes a re-gather
before admission. `NavigationSession.tla` now separately records the
product-installed, consumer-installed, and product-acknowledged complete
snapshots. `MaxSynchronization = 2` bounds external request generation while
preserving two request/abandon cycles; it does not bound product responses.

Deadlock checking is disabled in all three configs. A behaviour that has issued
every intent, drained its queue, and consumed its last effect has nothing left
to do; termination is the intended end state, not a defect.

### Action coverage

`tlc2.TLC -coverage 1` reports that every action in every model contributes
transitions in the shipped configuration, so no modelled step is dead. In the
single-worker `NavigationSession.tla` run,
`RequestConsumerSynchronization` contributes 12,786 distinct transitions
across 20,923 invocations, `SynchronizeConsumer` contributes 477 across 8,764,
`VisibleEffect` contributes 5,938 across 29,629, and `AcknowledgeEffect`
contributes 5,804 across 5,938. `ExecuteEffectWork` in
`SnapshotAuthority.tla` contributes transitions but no new distinct states
because it only latches a witness that is already true.

### Mutation probes

Coverage and exhaustive checking do not show that a claim would catch anything.
Each probe below breaks one design rule in a scratch copy and re-runs TLC with
a configuration that enables exactly one claim, so the reported violation names
the claim under test rather than whichever claim happens to be listed first.

Every safety invariant and liveness property in the tables above has a probe,
with one deliberate exception: `TypeOK` in each model is a typing guard, not a
headline claim, and is not probed. The probes are not committed; the table
records how to reproduce them.

| Probe | Mutation | Claim | Result |
| --- | --- | --- | --- |
| NS1 | Admit maintenance while an effect is unconsumed | `MaintenanceAdmissionDiscipline` | violated |
| NS2 | Admit the newest ready request instead of the oldest | `MaintenanceRequestOrder` | violated |
| NS3 | Let a superseded explicit result return effect authority | `LatestIntentSafety` | violated |
| NS4 | Return a retained outcome under the previous effect epoch | `ExactCurrentAuthority` | violated |
| NS5 | Run a visible effect without revalidating authority | `NoStaleVisibleEffect` | violated |
| NS6 | Stop releasing the effect on acknowledgement | `EveryQueuedRequestIsAdmitted` | violated |
| NS7 | Add an admission condition with no disclosed release path | `BlockedMaintenanceResumes` | violated |
| NS8 | Make an abort effect impossible to acknowledge | `MaintenanceResumesAfterAbort` | violated |
| NS9 | Remove the rebuild path for a request with a stale basis | `StaleBasisMaintenanceResumes` | violated |
| NS10 | Drop fairness on explicit resolution | `ExplicitWorkEventuallyResolves` | violated |
| NS11 | Stop releasing the effect on acknowledgement | `EffectEventuallyConsumed` | violated |
| NS12 | Stop releasing the effect on acknowledgement | `MaintenanceEventuallyDrains` | violated |
| NS13 | Let a superseded operation stay outstanding forever | `EveryExplicitIntentSettles` | violated |
| NS14 | Keep the revision unchanged for a changed-snapshot unavailable result | `NonSuccessRevisionMatchesSnapshotChange` | violated |
| NS15 | Advance the revision for an unchanged-snapshot unavailable result | `NonSuccessRevisionMatchesSnapshotChange` | violated |
| NS16 | Install an unavailable result at a revision different from its recorded result revision | `NonSuccessRevisionMatchesSnapshotChange` | violated |
| NS17 | Mark a stale request ready and clear its re-gather debt during rebuild | `MaintenanceRegatherDiscipline` | violated |
| NS18 | Drop an earlier queued request while allowing a later request to be admitted | `EveryQueuedRequestIsAdmitted` | violated |
| NS19 | Acknowledge while the consumer still holds an older snapshot | `AcknowledgementRequiresConsumerSynchronization` | violated |
| NS20 | Apply the result's prior snapshot instead of its complete current snapshot | `ConsumerVisibleEffectSynchronizes` | violated |
| NS21 | Mint dedicated synchronization authority for the prior revision | `SynchronizationAuthorityIsCurrent` | violated |
| NS22 | Advance the acknowledged receipt during abandonment without installing the snapshot | `ConsumerSynchronizationShape` | violated |
| NS23 | Derive every result disposition as `Current` | `CurrentResultDispositionIsExact` | violated |
| NS24 | Drop a synchronization request without recording settlement | `EverySynchronizationRequestSettles` | violated |
| NS25 | Acknowledge a current snapshot installed under an older effect epoch | `AcknowledgementRequiresConsumerSynchronization` | violated |
| NS26 | Settle a different synchronization request identity | `SynchronizationRequestDiscipline` | violated |
| NS27 | Forge both the copied receipt and `Current` disposition from the result snapshot | `CurrentResultDispositionIsExact` | violated |
| NS28 | Advance the acknowledgement receipt during post-install abandonment | `AbandonmentPreservesAcknowledgement` | violated |
| NS29 | Admit dedicated synchronization while explicit work is unresolved | `SynchronizationRequestDiscipline` | violated |
| NS30 | Admit dedicated synchronization before queued maintenance drains | `SynchronizationRequestDiscipline` | violated |
| NS31 | Keep the revision unchanged for a changed-snapshot failed result | `NonSuccessRevisionMatchesSnapshotChange` | violated |
| NS32 | Advance the revision for an unchanged-snapshot Registry or policy failed result | `NonSuccessRevisionMatchesSnapshotChange` | violated |
| NS33 | Advance the Navigation preparation-failure revision and coherently rewrite its result and authority to the post-state | `PreparationFailureRetainsSnapshotAndRevision` | violated |
| NS34 | Rewrite the Navigation preparation-failure source as evaluation | `PreparationFailureRetainsSnapshotAndRevision` | violated |
| NS35 | Omit product and host authority from Navigation preparation failure | `PreparationFailureRetainsSnapshotAndRevision` | violated |
| NS36 | Rewrite both Navigation preparation failure and its witness to use applied authority | `PreparationFailureRetainsSnapshotAndRevision` | violated |
| NS37 | Rewrite both Navigation preparation failure and its source witness as evaluation | `PreparationFailureRetainsSnapshotAndRevision` | violated |
| NS38 | Coherently install a changed snapshot and revision during Navigation preparation failure | `PreparationFailureRetainsSnapshotAndRevision` | violated |
| AR1 | Drop the current-intent requirement from preparation publication | `PreparationRequiresReadyPairAndCurrentIntent` | violated |
| AR2 | Publish a different subject than the independently retained request | `PreparedPairEqualsRequestedPayload` | violated |
| AR3 | Allow a superseded preparation to publish | `NoSupersededPreparationResult` | violated |
| AR4 | Allow a failed half to publish a prepared result | `FailedPreparationNeverPrepared` | violated |
| AR5 | Expose a prepared result while its preparation remains live | `PreparationIsInvisibleUntilPublished` | violated |
| AR6 | Drop fairness on attempt settlement | `EveryAttemptSettles` | violated |
| AR7 | Remove the abort path for a failed preparation | `FailedAttemptsAbort` | violated |
| SA1 | Take the retained basis from supplied prior state | `InstalledSnapshotIsSessionCustody` | violated |
| SA2 | Take the retained basis from supplied prior state | `RetainedPriorStateIsInstalledSnapshot` | violated |
| SA3 | Take the retained basis from supplied prior state | `SuppliedRetainedPriorStateIsAlwaysRejected` | violated |
| SA4 | Let stateless evaluation install session state | `OnlyRetainedExecutionInstalls` | violated |
| SA5 | Rewrite the installed lens from a stateless step, leaving the revision alone | `NonApplyStepsPreserveInstalledSnapshot` | violated |
| SA6 | The same lens rewrite, checked against revision arithmetic only | `OnlyRetainedExecutionInstalls` | not violated |
| SA7 | Return a retained rejection without advancing the effect epoch | `RetainedRejectionHasExactAuthorityAndInstallsNothing` | violated |
| SA8 | Report an applied retained lens the installed snapshot does not carry | `RetainedCommittedLensEqualsInstalledLens` | violated |
| SA9 | Issue retained effect authority from a stateless evaluation | `StatelessIssuesNoRetainedAuthority` | violated |
| SA10 | Return applied authority naming the previous snapshot revision | `ExactCurrentAuthority` | violated |
| SA11 | Skip authority revalidation before deferred work | `StaleOrForeignAuthorityNeverExecutes` | violated |
| SA12 | Drop fairness on command resolution | `EveryCommandResolves` | violated |
| SA13 | Drop fairness on acknowledgement | `EffectEventuallyConsumed` | violated |
| SA14 | Apply a later supplied-prior operation once an earlier one was rejected | `SuppliedRetainedPriorStateIsAlwaysRejected` | violated |
| SA15 | Adopt only the stale same-session supplied snapshot | `InstalledSnapshotIsSessionCustody` | violated |
| SA16 | Adopt only the stale same-session supplied snapshot | `RetainedPriorStateIsInstalledSnapshot` | violated |
| SA17 | Return a result that does not record its operation ID | `OperationAndResultAreCorrelated` | violated |
| SA18 | Install and return another admissible session lens instead of the exact requested lens | `AppliedResultEqualsExactRequest` | violated |

Sixty-three probes, sixty-two expected violations and one expected pass. `SA6`
is the one probe expected not to fire: it applies the same mutation as `SA5`
and checks the revision-arithmetic invariant instead, which does not notice a
snapshot rewritten in place. That pair is why
`NonApplyStepsPreserveInstalledSnapshot` compares the record.

Twenty-five probes exist specifically because a claim used to be satisfiable
by the wrong thing. `NS16` separates installed revision from a self-consistent
result,
`NS17` admits stale work without re-gathering, `NS18` lets a later admission
stand in for a lost earlier request, `NS19` prevents acknowledgement from
standing in for snapshot consumption, `NS20` distinguishes the result snapshot
from its prior snapshot, `NS21` separates fresh authority from a
self-consistent stale result, `NS22` prevents abandonment from forging an
acknowledgement receipt, `NS23` prevents an incorrect disposition from
disabling the result action instead of failing a witness, `NS24` prevents a
response bound from standing in for per-request progress, `NS25` distinguishes
current snapshot contents from current-authority installation, and `NS26`
prevents another request's settlement from discharging the named request.
`NS27` prevents correlated receipt and disposition fields from replacing the
product-owned pre-state receipt, `NS28` makes post-install abandonment directly
observable, and `NS29`/`NS30` protect synchronization admission relative to
explicit work and queued maintenance. `NS33` rewrites preparation-failure
authority and result to the wrong post-state, `NS34` erases its distinct
source, `NS35` erases its returned authority, and `NS36` changes both the
action and shared witness so only the dedicated preparation-failure invariant
rejects the wrong authority class. `NS37` changes both the action and source
witness while the independent occurrence field still identifies preparation
failure, proving the dedicated invariant rejects the source rewrite. `NS38`
coherently rewrites the action, result, disposition, authority, and
shared revision witness so only the dedicated invariant's named retention
clauses reject the changed snapshot and revision. `AR2` publishes a payload
that differs from the retained request, `SA14` applies a later supplied-prior
operation after an earlier one was rejected, `SA15` adopts only the stale
same-session supplied snapshot whose origin and lens resemble session data,
and `SA18` returns another admissible session lens under the correct operation
ID.

## Changing a model

Keep each model independent and finite. Raising `MaxIntent`, `MaxMaintenance`,
`MaxSynchronization`, `MaxCommands`, or the `Subjects` and `Lenses` sets grows
the state space quickly and buys little: the shipped bounds are the smallest
that reach supersession, out-of-order fact completion, repeated
synchronization request and abandonment, half-failed preparation, stale
authority, abort, and one operation's outcome standing next to another's. When
a design rule changes, change the action that states it, keep the paired
witness an independent re-derivation, and re-run TLC before updating the counts
above. A claim added to a table needs a probe added with it, and a claim about
one operation's outcome should name that operation.
