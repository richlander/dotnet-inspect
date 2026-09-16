# Workspace Scope revision/publication model

## Owner, claim, and consumer

[Workspace Scope and Expansion](../../workspace-scope-and-expansion.md) is the
normative owner. This model addresses
[#5796](https://github.com/richlander/dotnet-inspect/issues/5796):

> For one exact accepting Workspace, every current Scope snapshot
> binds one complete logical revision to one parent-owned physical-composition
> epoch; mutations either publish one complete old-to-new transition through
> Artifact Acquisition or leave the prior logical state current and release
> provisional authority.

The immediate implementation consumer is
[#5821](https://github.com/richlander/dotnet-inspect/issues/5821): initial
`WorkspaceScopeSnapshot` and exact Replace/Clear with CLI snapshot adoption.
[#6151](https://github.com/richlander/dotnet-inspect/issues/6151) extends that
implementation with exact-package Add/Remove and CLI Add-batch adoption.
Browser adoption follows complete-restoration and host migration prerequisites;
expansion also follows later. The model deliberately retains the broader #5796 race acceptance rather
than making that first implementation slice implement every modeled operation.
[#5697](https://github.com/richlander/dotnet-inspect/issues/5697) owns the
end-to-end adoption path and
[#5634](https://github.com/richlander/dotnet-inspect/issues/5634) its sequencing.

This is bounded design evidence, **not implementation conformance**. It neither
claims shipped support nor substitutes for the named Release implementation
gates.

Issue [#7256](https://github.com/richlander/dotnet-inspect/issues/7256)
extends the same Scope owner with pre-effect operation handoff. The model now
checks that a complete request and fresh operation identity exist before
submission, that submission retains the existing validation/admission order,
and that every terminal result preserves the original operation association.
It also checks exact requested-occurrence projection and typed cancellation
control without importing or copying a Navigation state machine.

## Architecture and substitutions

`WorkspaceScopeRevisionsModel.tla` is one finite consumer harness. Its three
named `INSTANCE ArtifactRootPublicationLifecycle WITH ...` bindings are
`First`, `Second`, and `Third`. They share these actual, live variables:

| Artifact currency | Scope substitution |
| --- | --- |
| Exact Workspace | `"workspace"`; foreign completion uses `"foreign"` |
| Current physical epoch and complete Root set | `physical`, `physicalRoots` |
| Current Scope publication base | `base` |
| Process-lifetime issuance counts | `physicalIssues`, `baseIssues` |
| Expected physical epoch and Scope base | `plans[i].physical`, `plans[i].base` |
| Candidate physical epoch and Scope base | owner-reserved `i` and `20 + i`, with focused collision scenarios |
| Receipt, cancellation authority, finite deadline | distinct operation-indexed owner currencies `1`, `2`, `3` |
| Desired correspondence and prepared subset | `plans[i].desired`, `plans[i].prepared` |
| Receipt, participant, candidate, phase, and outcome | separate scalar state for each owner instance |

Scope operations use `ActivatePublication`, `BeginStaging`, `PrepareCommit`,
`CommitPublication`, and the owner's refusal/release actions. There is no copy
of Artifact publication assignments in Scope, including in negative controls.
Scope-only pointer swaps call `ScopeOnlyAdvance`; corresponding physical
movement calls `RefreshPhysical`. `RefreshScope` observes that already-current
physical epoch using only `ScopeOnlyAdvance`: it does not activate a receipt,
stage a Retain plan, or issue another physical epoch.

The existing Artifact model previously exposed only a closed, single-operation
behavior. Its supportive open-world boundary preserves the original
`SafetySpec` and all standalone configurations. `CompositionSafetySpec` adds
specific environmental actions, **not** a consumer-supplied `Next`:

- fresh Scope-only publication under the free gate;
- fresh corresponding physical refresh under that gate;
- reservation of one previously unissued currency; and
- another operation's paired commit of two reserved, never-current currencies,
  leaving this operation's receipt and participant state unchanged.

The Scope consumer calls the exact publishing instance's owner action; the
other instances observe only those constrained environmental transitions.
No environment step may interleave with a held publication gate.
`BrokenGate.cfg` demonstrates that the projected behavior check is substantive.

`OwnerAssumptionsHold` checks the owner's finite domains and prepared-subset
obligation under every instance substitution. `OwnerSafety` aliases inherited
type, freshness, authority release, receipt terminality, participant one-shot,
commit association, and cancellation/runtime checks. `ArtifactBehaviorRefinement`
checks all three `CompositionSafetySpec` projections again in this consumer.
Neither previous standalone bounded results nor their state counts transfer.

Shared pointers and histories remain live after settlement as well as before.
Only complete operation **result snapshots** are frozen, after irrevocable
settlement. Scope publication bases differ from logical revision identities:
preparation, progress, failure, cancellation, and supersession swap the Scope
base without changing the physical epoch or logical revision. Membership
publication changes the revision; closure and refresh retain it.

The handoff extension adds Scope-owned, operation-indexed records alongside
those existing Artifact instances:

- `requests[i]` freezes the exact Workspace, revision, optional Scope-base
  guard, finite deadline identity, operation kind, complete opaque input batch,
  exact activation target, and evidence bit at issuance;
- `requestStates`, `submissionCounts`, and `settlementCounts` distinguish
  unissued, issued, abandoned, submitted, and settled identities and enforce
  at most one submission and one settlement;
- `results[i]` retains the original operation, terminal arm, frozen result
  snapshot, exact requested occurrence identity, distinct superseder identity,
  and historical-only authority for `Unavailable`; and
- `cancellationResponses[i]` distinguishes accepted control, stale
  `ObservedNoEffect`, and return of the original mutation settlement.

Issuance and abandonment are separate inert actions. Their temporal property
checks that they do not change Artifact state, current membership, revision or
publication pointers, Preparing/admission state, physical epoch, plans, or
deadline/cancellation state. The retained descriptor contains only symbolic
identities, booleans, and opaque correspondence values; concrete object-graph
resource erasure remains an implementation gate.

## Finite behaviors

The bound uses one accepting Workspace plus a foreign identity, three
operation/receipt authorities, four opaque Root correspondences, five physical
epochs, and twenty Scope bases. Roots `a` and `d` deliberately share a display
label but not correspondence. An initial Replace requests the complete ordered
`a,b` set. The next operation can Add `c,d`, Replace with `b,d`, Remove the
first occurrence, or Clear.
The re-add profile then adds removed correspondence `a` again and requires a
new occurrence issuance rather than reviving the retired one.

Only one mutation owns `active`. A valid Replace or Clear may supersede the
first preparing operation; an ordinary request is refused Busy. Submission
validation is a pure, ordered decision over shape, deadline, Workspace,
revision, and evidence, before Busy or supersession. Malformed request
classification is abstract; package parsers and capacity algorithms are not
modeled.

Independent finite perturbation profiles cover progress, supersession,
failure, cancellation, deadline, close, and validation. Each profile explores
every enabled placement of its disturbance, rather than one scripted trace.
Profiles avoid an unnecessary cross-product of unrelated disturbances; this
is not exhaustive coverage of arbitrary combinations of failures.

The refresh profile first publishes a selectively open `a,b` revision, then
complete closure coverage. Artifact re-realization changes the physical epoch
and generation while retaining correspondence. Root `a` may instead become
Pending or Failed, with no generation reference; Root `b` remains Ready.
A Scope-only refresh publishes all projections together, keeps logical
occurrences and revision, and clears old coverage without changing the physical
epoch. The physical-race profile changes the epoch while a
second mutation waits: its stale plan releases, and a third receipt-free
refresh completes before the failed operation receives its current snapshot.
A stale retained snapshot is not returned as current between those steps.

Required physical refresh is modeled as a resource-free, fair owner
completion, not another acquisition that can fail indefinitely. Runtime close
may still interrupt it and produces `Unavailable`. Optional caller
cancellation/failure applies to user mutations, not that required refresh.

`Liveness.cfg` and the twenty `Liveness<Profile>.cfg` configurations assume
weakly fair adjacent completion and cleanup. They partition the original
eight-scenario matrix by its immutable initial scenario, retaining all eight
perturbation profiles and the same specification, invariants, and temporal
properties in every partition. Refresh further separates each of its eight
perturbation profiles, and its Progress and Validation profiles separate the four initial
`secondKind` values through `Spec` conjoined with each initial value. Their
union is exactly the original `Spec`; none changes `Init`, `Next`, or fairness.
Neither `scenario`, `perturbation`, nor `secondKind` changes in `Next`, so
these disjoint partitions change neither the explored behaviors nor their
fairness.
`DeadlineLiveness.cfg` separately removes fair acquisition/staging/commit:
the admitted operation must still settle through finite deadline observation
and fair release/Scope cleanup, even if preparation never completes.

`HandoffSpec` fixes the second operation to Add and adds nine focused scenarios:
issuance/abandonment, explicit Replace target, mixed Add with either an existing
or new target, duplicate-only Add with and without intent, revision drift,
deadline expiry, invalid target, and cancellation control. A focused
Scope-base-guard witness uses the existing Progress perturbation; superseder
association reuses the existing Replace supersession behavior. The existing
Close profile now settles a pre-issued closed submission as correlated
`Unavailable` with a historical snapshot.

The duplicate-only scenarios start retained correspondence `a` as Pending.
Their Add request contains only `a`, produces `NoEffect` without activating an
Artifact receipt or changing physical/Scope state, and preserves the existing
Pending occurrence. The mixed scenario's input batch contains existing `a` and
new `d`; issuance nondeterministically freezes either as the explicit target,
then the successful result must return that exact occurrence.

## Gates

All configurations are registered with their exact semantic verdict in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

| Claim | Positive / witness gate | Detecting mutation |
| --- | --- | --- |
| One complete current snapshot and ordered all-or-failure Add/Replace | `Safety`, `ReachabilityAdd` | `BrokenPartial` |
| Scope publication base is fresh and stale completion cannot rebase | `Safety`, `ReachabilityStaleScope`, `ReachabilityLateCompletion` | `BrokenScopeBase` |
| Validation precedes Busy and supersession | `ReachabilityValidation`, `ReachabilityInvalidReplace`, `ReachabilityBusy` | `BrokenValidation`, `BrokenSupersession` |
| Valid Replace and Clear supersede preparation | `ReachabilityReplaceSupersession`, `ReachabilityClearSupersession` | `BrokenScopeBase` |
| Failure, cancellation, deadline release authority | `ReachabilityFailure`, `ReachabilityCancellation`, `ReachabilityDeadline` | `BrokenCleanup` |
| Parent final atomic commit wins; terminal replay cannot republish | `ReachabilityReplayAndFinalCommit` | `BrokenFinalCommit` |
| Occurrence identity requires exact correspondence; re-add is fresh | `Safety`, `ReachabilityReadd` | `BrokenCorrespondence` |
| Complete physical refresh preserves Ready/Pending/Failed projections and invalidates old coverage | `CompositionSafety`, `ReachabilityRefresh`, `ReachabilityPhysicalRace` | `BrokenRefresh` |
| Foreign Workspace/receipt completion cannot publish | `ReachabilityForeignWorkspace`, `ReachabilityForeignReceipt` | inherited commit association invariants |
| Previously issued candidate identities cannot be reused | `ReachabilityScopeCandidate`, `ReachabilityPhysicalCandidate` | inherited freshness invariants |
| No new operation after runtime close | `CompositionSafety`, `ReachabilityClosed`, `NoAdmissionAfterClose` | inherited runtime commit invariant |
| Every admitted operation settles | `Liveness`, twenty `Liveness<Profile>` partitions, `DeadlineLiveness` | cleanup/final-commit safety mutations |
| Shared-gate composition refines Artifact behavior | every positive configuration | `BrokenGate` |
| Issuance/abandonment is inert and the request remains frozen | `OperationHandoffSafety`, `ReachabilityIssuedAbandoned` | temporal action checks in every handoff witness |
| Submission rechecks current revision, Scope-base guard, deadline, and activation validity before Busy/supersession | `ReachabilityIssuedRevisionRejected`, `ReachabilityIssuedBaseGuardRejected`, `ReachabilityIssuedDeadlineRejected`, `ReachabilityInvalidTargetRejected` | `BrokenHandoffValidation` |
| Every terminal arm retains its issued identity; supersession names a distinct operation | `OperationHandoffSafety`, all existing terminal-arm reachability profiles, `ReachabilitySupersederAssociation` | `BrokenOperationAssociation` |
| Add/Replace success returns the exact requested occurrence; no intent/non-success returns none | `ReachabilityRequestedReplace`, `ReachabilityRequestedMixedExisting`, `ReachabilityRequestedMixedNew`, `ReachabilityRequestedDuplicate`, `ReachabilityDuplicateNoIntent` | `BrokenRequestedOccurrence` |
| Duplicate-only Add performs no physical work and does not repair Pending | `ReachabilityRequestedDuplicate`, `ReachabilityDuplicateNoIntent` | exact NoEffect temporal/state properties |
| Cancellation control cannot manufacture mutation settlement and may return only the original association | `ReachabilityCancellationNoEffect`, `ReachabilityCancellationSettlement` | `BrokenCancellationControl` |

`Safety`, `CompositionSafety`, the twenty-one liveness partitions, and
`DeadlineLiveness` expect exit 0. `OperationHandoffSafety` also expects exit 0.
Reachability configurations expect exit 12 at `NoWitness`, with safety checks
still enabled. All mutations expect exit 12 at their named invariant except
`BrokenGate` and `BrokenCancellationControl`: their temporal action properties
expect exit 13.
Direct aliases and refinement are overlapping diagnostics, not independent
proofs of the same fact.

The Artifact standalone harness could not exercise candidate-freshness refusal.
The consumer now does: after operation 1 publishes, operation 2 proposes that
already issued physical identity or Scope base while retaining a fresh receipt
and current expected pointers. The actual imported refusal guards run.

`RejectConsumedParticipant` remains excluded from this Scope composition:
Scope constructs a fresh sealed participant per admitted operation, and a
completion for a terminal operation is rejected before a new staging attempt.
The same-participant and same-receipt replay events preserve terminal results;
malicious rebinding of a consumed participant into a fresh plan is an Artifact
API validation scenario, not an admitted Scope operation. No claim that this
otherwise unreachable guard was exercised is made.

## Demo

The named reachability configurations demonstrate these paths:

```text
Replace(a,b) preparing at scope base 1, physical epoch 0
  -> valid Clear supersedes: base 2, physical epoch still 0
  -> Replace settles Superseded at base 3 before its batch completes
  -> Clear prepares at base 4 without physical Root preparation
  -> Clear commits one empty revision at base 22, physical epoch 2
```

That is the shortest `ReachabilityClearSupersession` witness.
`ReachabilityStaleScope` separately forces the already-sealed, physically staged
old completion to reach the imported stale-base refusal. Cleanup witnesses
require an actually activated preparation, not only a dormant operation.

Neighboring `ReachabilityRefresh` witness:

```text
Replace(a,b) -> closure evaluates exact generation 0
  -> Artifact changes epoch: a is Ready, Pending, or Failed; b remains Ready
  -> one Scope-only refresh publishes every observed projection
  -> Artifact epoch stays unchanged; Scope publication base is fresh
  -> same logical occurrences and revision, empty evaluated coverage
```

The broken-base control recaptures the replacement base for the superseded
operation. Artifact then correctly accepts that newly presented base, exposing
the Scope bug: a superseded operation commits. The detecting invariant is
`SupersededCannotCommit`, not a weakened Artifact publication implementation.

Operation handoff adds these bounded demonstrations:

```text
Issue Replace(a,b), target b
  -> no Scope/Artifact state change
  -> submit and publish through the existing Artifact owner actions
  -> Committed(operation 1, exact b occurrence)

Commit Replace(a,b) with a Pending
  -> issue Add(a), target a
  -> NoEffect(operation 2, unchanged snapshot, exact existing a occurrence)

Issue invalid Add(d), target c while operation 1 is Preparing
  -> Rejected(InvalidTarget), not Busy and not supersession

Observe stale cancellation for an issued or settled identity
  -> control ObservedNoEffect, original mutation outcome/count unchanged
```

`BrokenRequestedOccurrence` deliberately uses `a` for requested `d`; both have
the same display label, so the exact-correspondence invariant rejects label
inference. `BrokenCancellationControl` manufactures mutation `NoEffect` from a
stale control observation. `BrokenOperationAssociation` replaces a superseded
operation's identity with its superseder. `BrokenHandoffValidation` returns
Busy before checking an invalid target.

## Running and limits

Use the repository-pinned TLA+ jar, with Java scratch and runner scratch under
ignored repository `artifacts/` when the environment forbids system temporary
directories:

```bash
mkdir -p artifacts/java
export TMPDIR="$PWD/artifacts/java"
export JAVA_TOOL_OPTIONS="-Djava.io.tmpdir=$PWD/artifacts/java"
TLA_TOOLS_JAR="$PWD/artifacts/tla2tools.jar" \
  ./eng/run-tla-checks.sh \
  docs/design/models/artifact-root-publication \
  docs/design/models/workspace-scope-revisions
```

For deterministic traces, run TLC from this model directory with the owner
directory on `TLA-Library` and `-workers 1 -seed 1 -fp 1`.

### Scope-only refresh correction

The Scope-only refresh correction rechecked all 50 configurations with the
repository-pinned jar and OpenJDK 25 through the directory runner at the existing
120-second per-configuration CI budget. Every exact
semantic verdict matched, including composition safety across Ready/Pending/Failed
observations and the existing closure-refresh and gate negative controls.
`CompositionSafety` explored 98,604 distinct states. These are bounded model
results, not implementation conformance or hosted-runner timing guarantees.

### Historical deterministic trace evidence

The following traces and counts predate the Scope-only refresh correction.
They record the original model and its liveness partitioning, not the current
model's state counts.

The direct probes used Linux, OpenJDK `21.0.12`, and immutable TLA+ mirror
build `2026.08.11.125311`, revision `0894c34`, SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
They used one worker, seed 1, fingerprint polynomial 1.

| Configuration | Observed verdict | Generated | Distinct | Depth |
| --- | --- | ---: | ---: | ---: |
| `ReachabilityClearSupersession` | `NoWitness`, exit 12 | 422 | 256 | 10 |
| `ReachabilityStaleScope` | `NoWitness`, exit 12 | 102 | 78 | 7 |
| `ReachabilityFailure` | `NoWitness`, exit 12 | 101 | 76 | 6 |
| `ReachabilityRefresh` | `NoWitness`, exit 12 | 7802 | 2351 | 20 |
| `ReachabilityPhysicalRace` | `NoWitness`, exit 12 | 3742 | 1318 | 19 |
| `ReachabilityReadd` | `NoWitness`, exit 12 | 1939 | 555 | 19 |
| `BrokenScopeBase` | `SupersededCannotCommit`, exit 12 | 228 | 170 | 9 |
| `BrokenGate` | projected behavior violation, exit 13 | 50 | 38 | 6 |

Witness runs stop at their first intended violation, not at exhaustion.
Worker scheduling can change counterexample counts without changing the
registered semantic verdict.

### Operation handoff extension

The extension was checked on Linux with `/usr/bin/java` OpenJDK `21.0.12` and
immutable TLA+ mirror build `2026.08.11.125311`, revision `0894c34`, SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
The directory runner used its automatic worker selection and a 120-second
per-configuration bound. These initial results predate the CI model-cost
correction below:

```bash
export TMPDIR="$PWD/artifacts/tla-scratch"
export JAVA_TOOL_OPTIONS="-Djava.io.tmpdir=$PWD/artifacts/java"
export TLA_TOOLS_JAR=/home/rich/.local/share/tlaplus/tla2tools-2026.08.11.125311.jar
export TLA_CHECK_TIMEOUT_SECONDS=120
./eng/run-tla-checks.sh docs/design/models/workspace-scope-revisions
```

It checked one module and all 68 configurations: 68 exact semantic outcomes
matched and none timed out or remained unverified. This includes all 50
pre-extension Scope profiles and rechecks every imported Artifact safety and
composition-refinement property under the new state and actions.

| New configuration | Exit | Generated | Distinct | Depth |
| --- | ---: | ---: | ---: | ---: |
| `OperationHandoffSafety` | 0 | 17,747 | 4,808 | 24 |
| `ReachabilityIssuedAbandoned` | 12 | 6 | 6 | 4 |
| `ReachabilityRequestedReplace` | 12 | 20 | 16 | 9 |
| `ReachabilityRequestedMixedExisting` | 12 | 708 | 300 | 22 |
| `ReachabilityRequestedMixedNew` | 12 | 807 | 328 | 22 |
| `ReachabilityRequestedDuplicate` | 12 | 108 | 62 | 14 |
| `ReachabilityDuplicateNoIntent` | 12 | 108 | 62 | 14 |
| `ReachabilityIssuedRevisionRejected` | 12 | 318 | 158 | 16 |
| `ReachabilityIssuedDeadlineRejected` | 12 | 48 | 37 | 11 |
| `ReachabilityIssuedBaseGuardRejected` | 12 | 249 | 158 | 13 |
| `ReachabilityInvalidTargetRejected` | 12 | 128 | 88 | 13 |
| `ReachabilityCancellationNoEffect` | 12 | 61 | 48 | 13 |
| `ReachabilityCancellationSettlement` | 12 | 167 | 108 | 14 |
| `ReachabilitySupersederAssociation` | 12 | 175 | 109 | 13 |
| `BrokenOperationAssociation` | 12 | 211 | 122 | 14 |
| `BrokenRequestedOccurrence` | 12 | 769 | 319 | 22 |
| `BrokenHandoffValidation` | 12 | 155 | 94 | 13 |
| `BrokenCancellationControl` | 13 | 148 | 91 | 14 |

Witness and mutation runs stop at their intended violation, so their queues
need not be exhausted. `OperationHandoffSafety` exhausts its bounded graph.

### Historical liveness partitioning

The initial, unpartitioned configuration set completed locally through the
existing runner: all 33 Scope exact outcomes and all 28 unchanged Artifact
exact outcomes matched under its default 600-second budget. The runner used
four TLC workers for these results:

| Positive configuration | Generated | Distinct | Depth |
| --- | ---: | ---: | ---: |
| `Safety` | 73609 | 23826 | 22 |
| `CompositionSafety` | 421862 | 108844 | 32 |
| `Liveness` | 1561195 | 391918 | 33 |
| `DeadlineLiveness` | 420 | 256 | 11 |

That initial `Liveness` matrix exceeded the existing 120-second CI budget in
[run 33929995717](https://github.com/richlander/dotnet-inspect/actions/runs/33929995717).
Local completion under a longer budget did not establish CI eligibility.
The eighteen disjoint partitions replaced that single matrix without raising
the budget or weakening its properties; before the operation-handoff extension
there were 50 Scope configurations.

All 50 final configuration outcomes were observed with a 120-second
per-configuration limit and two TLC workers. The eighteen successful liveness
partitions sum to exactly the original 1,561,195 generated and 391,918 distinct
states. Their individual elapsed times were at most 91 seconds in this local
run; this is not a guarantee of hosted-runner timing. The final Refresh
partitions used direct TLC invocations after the directory runner identified
the remaining oversized profile. The unchanged configurations completed in
that directory pass.

### CI model-cost correction

The operation-handoff extension's initial local pass did not establish hosted
timing. In [run 35121402048](https://github.com/richlander/dotnet-inspect/actions/runs/35121402048),
`LivenessRefreshValidation` exceeded the exact-outcome gate's 120-second
budget; `CompositionSafety` completed in approximately 119 seconds.

`RequestFor` now records `baseGuard = 0` when `hasBaseGuard = FALSE`, instead
of retaining the otherwise unused publication base at issuance. Both consumers
of that field test `hasBaseGuard`; guarded requests still freeze the exact
owner-issued base. This removes distinctions between absent guards, not
request-issuance interleavings, revision evidence, scenarios, perturbations,
fairness, or checked properties.

Before/after probes used the same pinned jar and OpenJDK `21.0.12`, two CPUs
(`taskset -c 0,1`), `-XX:ActiveProcessorCount=2`, `-XX:+UseParallelGC`, two TLC
workers, and the unchanged 120-second limit:

| Configuration | Before | After | After generated / distinct |
| --- | --- | --- | --- |
| `LivenessRefreshValidation` | Timeout, exit 124 | 77.50s, exit 0 | 353,560 / 81,704 |
| `CompositionSafety` | 73.38s, exit 0 | 45.15s, exit 0 | 384,148 / 107,470 |

The complete directory runner then matched all 68 exact semantic verdicts,
with zero timeouts, under the same CPU constraint and per-configuration limit.
However, `LivenessRefreshValidation` took 109 seconds in that pass. To provide
more headroom than the single-profile timing suggests, Validation now uses
the same four-way immutable `secondKind` partition as Progress. All four
initial values retain the same `Spec`, invariants, temporal properties, and
fairness; their disjoint union covers the unpartitioned Validation profile.
The shared specification aliases no longer include `Progress` in their names.
The three additional configurations bring the current total to 71.

The final directory pass matched all 71 exact verdicts with zero timeouts.
Validation's Add/Clear/Remove/Replace partitions finished in 18/21/20/21
seconds, respectively. Each explored 88,390 generated and 20,426 distinct
states; their totals exactly match the unpartitioned corrected profile's
353,560 generated and 81,704 distinct states. `CompositionSafety` finished
in 43 seconds with the same corrected state counts.

Run the complete partitioned set with:

```bash
TLA_CHECK_TIMEOUT_SECONDS=120 \
JAVA_TOOL_OPTIONS=-XX:ActiveProcessorCount=2 \
taskset -c 0,1 ./eng/run-tla-checks.sh \
  docs/design/models/workspace-scope-revisions
```

Set `TLA_TOOLS_JAR` to the pinned jar as above. CPU IDs must be available on
the machine running the probe. These local measurements provide comparative
cost evidence, not a guarantee of hosted-runner timing; CI remains required.

### Abstraction limits

The model abstracts Root bytes, package selection, resource erasure, detailed
Ready/Pending/Failed transitions, actual time units, query leases, budgets,
and dependency classification. Closure coverage is an exact
occurrence/generation relation; the full expansion algorithm remains outside
issue #5796. There are no Browser effects, Navigation, persistence, packets, or
multiple active Workspace behavior. Historical snapshots contain symbolic
facts; this does not prove implementation object-graph resource erasure.

The handoff extension additionally abstracts request payload objects to finite
opaque correspondence sequences, deadline passage to a boolean validity flip,
and cancellation responses to one operation-indexed observation record. It
does not prove API readiness/equivalence, Navigation acceptance or external
effect ordering, concrete binding disposal, website behavior, or producer
implementation conformance.
