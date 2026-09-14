# State-machine index completeness

A TLA+ model of the stateful fragment of
[`state-machine-relationship-index.md#completeness`](../../state-machine-relationship-index.md#completeness).

## What this model is for

Most of the completeness property is a sequential partition: every structural
async state machine gets exactly one of three results. A model that restated it
would add ceremony without adding information, and a reader would learn less
from the model than from the sentence.

Three fragments are not like that, and they are the ones that repeatedly went
wrong in review:

- construction can fail partway through, and that failure must be **absorbing**
  and **global** rather than leaving earlier results standing;
- rejection publications form components through several identity domains,
  while their diagnostic payload is accumulated separately;
- a whole-module failure must produce a **total** rejection, not a partial one
  that is textually indistinguishable from a per-claim refusal.

The third is exactly the invariant that a reviewer found by accident, after a
prose comment describing it had been read and cleared by two other reviewers.
`BrokenPartialFailure.cfg` shows TLC finding the same bug in under a second.

## What it does not establish

The model establishes evidence **about the model**. It is not evidence about
`StateMachineRelationshipIndex`. The gates named in each invariant of the design
document are that evidence.

C4 and C6 are deliberately out of scope. C4 is a statement about what a consumer
may infer from a value, not about system state. C6 is a statement about an
external observer's ability to recompute a population, which is not something a
state machine of the construction can express.

The two models also do not prove their composition. The completeness model
treats `Rejected` as one classification; the merge model begins from rejection
publications. Implementation conformance must bridge that seam by representing
a refused structural async machine as a publication projected through its
state-machine query. The models keep the seam explicit rather than putting
kickoff, state-machine, implementation, and claimed-name keys into C1's
structural-async-machine domain.

The completeness model's `result` domain corresponds only to structural async
`GetByStateMachine` queries. It does not model kickoff or implementation keys,
so `C2_FailureIsTyped` checks the async-state-machine-query fragment of C2
rather than all three keyed surfaces. It also does not model the public
`Relationships` enumeration. The implementation exposes that surface as a
closed `Available`/`Rejected` result, but its collection-level status and
payload remain implementation-gated rather than idealized here.

## Structure

There are two models because the invariants have different units.
`StateMachineCompleteness.tla` ranges over structural async machines:

| Variable | Meaning |
| --- | --- |
| `truth` | per machine: what it **actually** is, independent of the index |
| `phase` | `"Building"`, `"Built"`, or `"Failed"` |
| `cause` | independent whole-module trigger: malformed input or exhausted budget |
| `kind` | the typed failure kind once `phase = "Failed"` |
| `failureDetail` | abstract published whole-module failure detail |
| `result` | per machine: `Unclassified`, `Resolved`, `Absent`, `Rejected` |
| `budget` | construction steps remaining |

`RejectionComponentMerge.tla` ranges over published rejections:

| Variable | Meaning |
| --- | --- |
| `phase` | `"Building"` or `"Frozen"` |
| `published` | number of rejection publications |
| `links` | per publication: tagged keys that connect components |
| `payload` | per publication: contributed diagnostic evidence |
| `claimKind`, `claimDetail` | per publication: its own failure reason |
| `component` | per publication: abstract component identity |
| `frozenEvidence` | per publication: frozen evidence membership |
| `frozenKind`, `frozenDetail` | per publication: frozen component reason |

The completeness model derives `Visited` from non-`Unclassified` results. The
merge model derives each key's latest owner from publication history. Neither
is independent state.

`truth` is the most important variable, and the first draft did not have it.
Without it, C1 said only that no machine retained an `Unclassified` marker — and
a real index that loses a row publishes no such marker. It publishes `Absent`,
which is exactly what it publishes for a machine that genuinely has no claim.
The invariant was therefore satisfiable by an index that dropped rows, which is
the failure it exists to catch. `BrokenDroppedRow.cfg` is the regression test.

`cause` is independent of the published `kind`, and publication behavior does
not share the expected-kind helper used by the C2 oracle. That separation makes
both directions of C2's cause-to-kind mapping checkable:
`BrokenWrongFailureKind.cfg` misreports malformed input, while
`BrokenWrongBudgetFailureKind.cfg` independently reaches budget exhaustion and
misreports it. Both remain typed, so membership in the set of failure kinds is
not enough to pass.

**The publication is the unit, not the machine.** One publication may carry
several machines, and a machine is only one of several identities through which
publications can connect.

A publication can connect through four **tagged** domains — kickoff MethodDef,
state-machine TypeDef, implementation MethodDef, and claimed type name admitted
for reuse. A claimed name carried only as evidence is not automatically a link.
Equal numeric tokens from different domains do not collide. `links` abstracts
those tagged identities into one finite set because their connectivity
semantics are identical.

`payload` is deliberately separate from `links`. The identities that explain
why two rejection publications merge are not the same thing as the diagnostic
evidence they contribute. Conflating the two made an earlier `covers` model
unable to state evidence completeness without also changing the graph.
Payload values are likewise domain-tagged, standing for kickoff candidates,
state-machine candidates, and claimed types without making their unrelated
identities collide.

Kind and detail are separate values in the model so a mutation can combine
fields from different publications. C5 requires the intact pair to come from
the component and to agree across its projection; it deliberately does not
specify which contributor wins.

Freeze has no "everything is merged" precondition. Adding one would invent a
contract obligation and make `C5_ComponentsEqualGraphClosure` vacuous: a broken
merge could never reach the state in which it is checked.

## Checked properties

| Name | Design invariant | Statement |
| --- | --- | --- |
| `C1_Totality` | C1 | Once `Built`, every machine's result matches an independent recount of `truth` |
| `C2_FailureIsTyped` | C2 (structural-async `GetByStateMachine` fragment) | `Failed` implies the cause-specific kind and non-`Absent` results |
| `C3_FailureRejectsAll` | C3 | `Failed` implies *every* machine reports `Rejected` |
| `C5_ComponentsEqualGraphClosure` | C5 | Component equality is exactly connectivity through tagged merge keys |
| `C5_ComponentProjectionAgrees` | C5 | One component has one frozen evidence set and reason |
| `C5_EvidenceMembershipIsComplete` | C5 | Frozen evidence is the union of every component publication's payload |
| `C5_ReasonComesFromComponent` | C5 | The selected `(Kind, Detail)` pair belongs intact to one component publication |
| `FailureIsAbsorbing` | C2, C3 | The published classification and reason cannot change after failure |
| `EventuallyTerminal` | — | Construction always reaches `Built` or `Failed` |
| `EventuallyFrozen` | — | A non-empty rejection publication set eventually freezes |

`FailureIsAbsorbing`, `EventuallyTerminal`, and `EventuallyFrozen` are
temporal; the rest are invariants.

## Scenarios

| Configuration | Bounds | Purpose | Result |
| --- | --- | --- | --- |
| `StateMachineCompleteness.cfg` | `MachineCount = 3`, `MaxBudget = 3` | Success and malformed-input failure reachable | 729 states, 351 distinct, depth 5 |
| `BudgetExhaustion.cfg` | `MachineCount = 3`, `MaxBudget = 2` | Budget too small to classify every machine | 648 states, 297 distinct, depth 4 |
| `RejectionComponentMerge.cfg` | three keys, two evidence values, three publications | Exact graph partition, projection, and evidence | 4,899,805 states, 1,157,521 distinct, depth 5 |

`BudgetExhaustion.cfg` intentionally omits C1: `Built` is unreachable when the
budget is smaller than the machine population, so C1 would be vacuous there.
`StateMachineCompleteness.cfg` checks C1 with reachable successful
construction; the budget scenario checks the typed global-failure path.

Counts were recorded with TLC v1.8.0 build `2026.08.21.155922`
(`9787e65`). They describe state-graph size, not elapsed performance.

Three publications are the smallest bound that distinguishes joining a
representative from joining its whole component. Three merge keys permit both
direct overlap and a transitive chain. Two evidence values and two kinds are
enough to expose a split frozen projection, a hybrid reason, and a dropped
union member.

## Demo — the round-19 bug, in three states

This is what the model is for. `BrokenPartialFailure.cfg` encodes the defect that
took nineteen review rounds to find by hand in #4835: whole-module failure that
leaves already-recorded results standing, producing a *partial* rejection where
the contract requires a total one.

TLC prints every variable and each action's source range. The states below are
**abridged to the three variables that carry the argument**, and action labels
omit source ranges. Run the command to see them in full.

```console
$ TLA_METADIR=$(mktemp -d \
    "${TMPDIR:-/tmp}/dotnet-inspect-state-machine-demo.XXXXXX")
$ trap 'rm -rf "$TLA_METADIR"' EXIT
$ java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
    -workers 1 -metadir "$TLA_METADIR" -cleanup -noGenerateSpecTE \
    -config BrokenPartialFailure.cfg StateMachineCompleteness.tla

Error: Invariant C3_FailureRejectsAll is violated.
Error: The behavior up to this point is:

State 1: <Initial predicate>
  /\ truth  = <<"Resolvable", "Resolvable", "Resolvable">>
  /\ result = <<"Unclassified", "Unclassified", "Unclassified">>
  /\ phase  = "Building"

State 2: <ClassifyOne(1,"Resolvable","Resolved")>
  /\ result = <<"Resolved", "Unclassified", "Unclassified">>
  /\ phase  = "Building"

State 3: <FailModule("MalformedInput")>
  /\ result = <<"Resolved", "Rejected", "Rejected">>
  /\ phase  = "Failed"
  /\ kind   = "Malformed"

Finished in 00s
```

Read state 3. The module failed, but machine 1 still answers `Resolved`. A
consumer asking "did this module fail?" sees a mixture, which C3 reserves for
per-claim refusal — the two failures have become indistinguishable.

Three states, 0.32 seconds, no fixture and no compiler. The equivalent hand-built
gate needs a fixture that converts *every* machine, and the reason nineteen
rounds missed it is that a fixture converting only one machine looks correct and
passes.

That is the argument for specifying this property here rather than in a comment
beside the test. The counterexample is cheap, exact, and re-runnable by anyone.

## Deliberate counterexamples

Each counterexample is a **committed configuration**, not a described edit.
The classification model carries `FailureMode`, `FailureMutationMode`, and
`FinishMode`; the merge
model carries `MergeMode` and `FreezeMode`. Their correct settings drive the
three scenarios above, and each configuration below selects one broken
setting.

That is deliberate. A numbered safety claim or absorption property no mutation
can falsify is not checking anything, so non-vacuity evidence has to exist. But
recording it as a README table saying "this mutation was violated" would be a
normative claim in prose with nothing holding it true — the exact defect shape
this design document was written to eliminate. Committed configurations are
re-runnable by anyone, and they fail loudly if a later change makes an invariant
weaker than it looks.

| Configuration | Broken behavior | Expected violation |
| --- | --- | --- |
| `BrokenPartialFailure.cfg` | Module failure preserves results already recorded, rejecting only unclassified machines | `C3_FailureRejectsAll` |
| `BrokenAbsentOnFailure.cfg` | Module failure reports `Absent` rather than `Rejected` | `C2_FailureIsTyped` |
| `BrokenUntypedFailure.cfg` | Module failure publishes no typed failure kind | `C2_FailureIsTyped` |
| `BrokenWrongFailureKind.cfg` | Malformed-input failure publishes the budget-exhaustion kind | `C2_FailureIsTyped` |
| `BrokenWrongBudgetFailureKind.cfg` | Budget exhaustion publishes the malformed-input kind | `C2_FailureIsTyped` |
| `BrokenMutatingFailure.cfg` | A published whole-module failure changes its detail | `FailureIsAbsorbing` |
| `BrokenPartialMerge.cfg` | A publication joins current representatives but not their whole components | `C5_ComponentsEqualGraphClosure` |
| `BrokenUnvisitedPublish.cfg` | Construction publishes before classifying every machine | `C1_Totality` |
| `BrokenUnmergedPublish.cfg` | A publication records links without joining the components they reach | `C5_ComponentsEqualGraphClosure` |
| `BrokenOvermerge.cfg` | A publication joins disconnected prior publications | `C5_ComponentsEqualGraphClosure` |
| `BrokenDroppedRow.cfg` | A row never reached is published as `Absent` | `C1_Totality` |
| `BrokenDroppedEvidence.cfg` | Freeze retains only each publication's local diagnostic payload | `C5_EvidenceMembershipIsComplete` |
| `BrokenLocalReason.cfg` | Publications in one component retain different local reasons | `C5_ComponentProjectionAgrees` |
| `BrokenHybridReason.cfg` | Freeze combines kind and detail from different publications | `C5_ReasonComesFromComponent` |

`BrokenPartialFailure.cfg` is the one worth dwelling on. It is the bug a reviewer
found by accident in the nineteenth review round of the accompanying test change,
after a prose comment describing the very property had been read and cleared by
two other reviewers. TLC finds it in under a second.

Several configurations record holes that earlier revisions of this model did not
detect. They are kept because each is the regression test for a way the model
was checking less than it appeared to.

- `BrokenUnmergedPublish.cfg` — the first revision had no notion of which
  machines *ought* to share a component, so an index that merged nothing
  satisfied component agreement vacuously.
- `BrokenDroppedRow.cfg` — the first revision modeled a lost row as a
  distinguished `Unclassified` marker. Real indexes publish `Absent`. Changing
  the mutation to match reality made `C1_Totality` pass, which is how the
  vacuity was found.
- `BrokenDroppedEvidence.cfg` — a publication's merge links and diagnostic
  payload were one `covers` set. That abstraction could test connectivity or
  evidence accumulation, but not distinguish them.
- `BrokenOvermerge.cfg` — requiring every graph edge to merge checked only one
  direction. Unrelated publications could still merge without violating any
  invariant.
- `BrokenLocalReason.cfg` replaces an earlier first-publication-wins mutation.
  No consumer needs that selection rule and no gate enforces it. The contract
  is that one intact component reason is shared, not which reason wins.
- `BrokenHybridReason.cfg` replaces an atomic reason value with independently
  modeled kind and detail, so a hybrid pair can now falsify the intact-pair
  statement.
- `BrokenMutatingFailure.cfg` rewrites only the abstract failure detail while
  phase, kind, classifications, and budget stay fixed. It makes detail
  absorption load-bearing without also producing a successful index that
  violates C1.

The last two are worth generalizing from. A model can fail by idealizing the
system, but it can also fail by promoting an incidental mechanism into a
contract. Correcting the property is not enough if the abstraction still
conflates state domains or specifies behavior no consumer needs.

## Running it

Follow [`docs/runbooks/tla-plus-setup.md`](../../../runbooks/tla-plus-setup.md)
to obtain the pinned tools. From this directory, with `tla2tools.jar` in
`$TLA_TOOLS`, run the configurations sequentially: concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
set -euo pipefail

TLA_METADIR=$(mktemp -d \
  "${TMPDIR:-/tmp}/dotnet-inspect-state-machine-model.XXXXXX")
trap 'rm -rf "$TLA_METADIR"' EXIT

run_clean() {
  local config=$1 module=$2 metadir="$TLA_METADIR/$1"
  mkdir -p "$metadir"
  java -XX:+UseParallelGC -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
    -workers auto -metadir "$metadir" -cleanup -noGenerateSpecTE \
    -config "$config.cfg" "$module.tla"
}

run_clean StateMachineCompleteness StateMachineCompleteness
run_clean BudgetExhaustion StateMachineCompleteness
run_clean RejectionComponentMerge RejectionComponentMerge

rm -rf "$TLA_METADIR"
trap - EXIT
```

All three report `Model checking completed. No error has been found.`

The counterexamples are run the same way and each must report its expected
violation:

```bash
set -euo pipefail

TLA_METADIR=$(mktemp -d \
  "${TMPDIR:-/tmp}/dotnet-inspect-state-machine-mutations.XXXXXX")
trap 'rm -rf "$TLA_METADIR"' EXIT

expect_violation() {
  local config=$1 module=$2 expected=$3 output
  local metadir="$TLA_METADIR/$config"
  mkdir -p "$metadir"
  if output=$(java -XX:+UseParallelGC \
      -cp "$TLA_TOOLS/tla2tools.jar" tlc2.TLC \
      -workers 1 -metadir "$metadir" -cleanup -noGenerateSpecTE \
      -config "$config.cfg" "$module.tla" 2>&1); then
    echo "$config unexpectedly passed" >&2
    return 1
  fi
  grep -Fq "$expected is violated" <<<"$output"
}

expect_violation BrokenPartialFailure StateMachineCompleteness \
  "Invariant C3_FailureRejectsAll"
expect_violation BrokenAbsentOnFailure StateMachineCompleteness \
  "Invariant C2_FailureIsTyped"
expect_violation BrokenUntypedFailure StateMachineCompleteness \
  "Invariant C2_FailureIsTyped"
expect_violation BrokenWrongFailureKind StateMachineCompleteness \
  "Invariant C2_FailureIsTyped"
expect_violation BrokenWrongBudgetFailureKind StateMachineCompleteness \
  "Invariant C2_FailureIsTyped"
expect_violation BrokenMutatingFailure StateMachineCompleteness \
  "Action property FailureIsAbsorbing"
expect_violation BrokenUnvisitedPublish StateMachineCompleteness \
  "Invariant C1_Totality"
expect_violation BrokenDroppedRow StateMachineCompleteness \
  "Invariant C1_Totality"
expect_violation BrokenPartialMerge RejectionComponentMerge \
  "Invariant C5_ComponentsEqualGraphClosure"
expect_violation BrokenUnmergedPublish RejectionComponentMerge \
  "Invariant C5_ComponentsEqualGraphClosure"
expect_violation BrokenOvermerge RejectionComponentMerge \
  "Invariant C5_ComponentsEqualGraphClosure"
expect_violation BrokenDroppedEvidence RejectionComponentMerge \
  "Invariant C5_EvidenceMembershipIsComplete"
expect_violation BrokenLocalReason RejectionComponentMerge \
  "Invariant C5_ComponentProjectionAgrees"
expect_violation BrokenHybridReason RejectionComponentMerge \
  "Invariant C5_ReasonComesFromComponent"

rm -rf "$TLA_METADIR"
trap - EXIT
```
