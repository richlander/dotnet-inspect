# Workspace Scope revision/publication model

## Owner, claim, and consumer

[Workspace Scope and Expansion](../../workspace-scope-and-expansion.md) is the
normative owner. This model addresses
[#5796](https://github.com/richlander/dotnet-inspect/issues/5796):

> For one exact accepting runtime Workspace, every current Scope snapshot
> binds one complete logical revision to one parent-owned physical-composition
> epoch; mutations either publish one complete old-to-new transition through
> Artifact Acquisition or leave the prior logical state current and release
> provisional authority.

The immediate implementation consumer is
[#5821](https://github.com/richlander/dotnet-inspect/issues/5821): initial
`WorkspaceScopeSnapshot` and exact Replace/Clear with CLI snapshot adoption.
Browser adoption follows its Add/Remove and complete-restoration prerequisites;
expansion also follows later. The model deliberately retains the broader #5796 race acceptance rather
than making that first implementation slice implement every modeled operation.
[#5697](https://github.com/richlander/dotnet-inspect/issues/5697) owns the
end-to-end adoption path and
[#5634](https://github.com/richlander/dotnet-inspect/issues/5634) its sequencing.

This is bounded design evidence, **not implementation conformance**. It neither
claims shipped support nor substitutes for the named Release implementation
gates.

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

`Liveness.cfg` and the seventeen `Liveness<Profile>.cfg` configurations assume
weakly fair adjacent completion and cleanup. They partition the original
eight-scenario matrix by its immutable initial scenario, retaining all eight
perturbation profiles and the same specification, invariants, and temporal
properties in every partition. Refresh further separates each of its eight
perturbation profiles, and its Progress profile separates the four initial
`secondKind` values through `Spec` conjoined with each initial value. Their
union is exactly the original `Spec`; none changes `Init`, `Next`, or fairness.
Neither `scenario`, `perturbation`, nor `secondKind` changes in `Next`, so
these disjoint partitions change neither the explored behaviors nor their
fairness.
`DeadlineLiveness.cfg` separately removes fair acquisition/staging/commit:
the admitted operation must still settle through finite deadline observation
and fair release/Scope cleanup, even if preparation never completes.

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
| Every admitted operation settles | `Liveness`, seventeen `Liveness<Profile>` partitions, `DeadlineLiveness` | cleanup/final-commit safety mutations |
| Shared-gate composition refines Artifact behavior | every positive configuration | `BrokenGate` |

`Safety`, `CompositionSafety`, the eighteen liveness partitions, and
`DeadlineLiveness` expect exit 0.
Reachability configurations expect exit 12 at `NoWitness`, with safety checks
still enabled. All mutations expect exit 12 at their named invariant except
`BrokenGate`: its temporal behavior-refinement property expects exit 13.
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

### Deterministic trace evidence

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
The eighteen disjoint partitions replace that single matrix without raising
the budget or weakening its properties; there are now 50 Scope configurations.

All 50 final configuration outcomes were observed with a 120-second
per-configuration limit and two TLC workers. The eighteen successful liveness
partitions sum to exactly the original 1,561,195 generated and 391,918 distinct
states. Their individual elapsed times were at most 91 seconds in this local
run; this is not a guarantee of hosted-runner timing. The final Refresh
partitions used direct TLC invocations after the directory runner identified
the remaining oversized profile. The unchanged configurations completed in
that directory pass.

### Abstraction limits

The model abstracts Root bytes, package selection, resource erasure, detailed
Ready/Pending/Failed transitions, actual time units, query leases, budgets,
and dependency classification. Closure coverage is an exact
occurrence/generation relation; the full expansion algorithm remains outside
issue #5796. There are no Browser effects, Navigation, persistence, packets, or
multiple live Workspace behavior. Historical snapshots contain symbolic
facts; this does not prove implementation object-graph resource erasure.
