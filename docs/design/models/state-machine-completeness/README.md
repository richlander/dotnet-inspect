# State-machine index completeness

A TLA+ model of the stateful fragment of
[`state-machine-relationship-index.md#completeness`](../../state-machine-relationship-index.md#completeness).

## What this model is for

Most of the completeness property is a sequential partition: every structural
state machine gets exactly one of three results. A model that restated it would
add ceremony without adding information, and a reader would learn less from the
model than from the sentence.

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

## Structure

There are two models because the invariants have different units.
`StateMachineCompleteness.tla` ranges over structural machines:

| Variable | Meaning |
| --- | --- |
| `truth` | per machine: what it **actually** is, independent of the index |
| `phase` | `"Building"`, `"Built"`, or `"Failed"` |
| `kind` | the typed failure kind once `phase = "Failed"` |
| `result` | per machine: `Unclassified`, `Resolved`, `Absent`, `Rejected` |
| `visited` | machines construction has reached |
| `budget` | construction steps remaining |

`RejectionComponentMerge.tla` ranges over published rejections:

| Variable | Meaning |
| --- | --- |
| `published` | number of rejection publications |
| `links` | per publication: tagged keys that connect components |
| `payload` | per publication: contributed diagnostic evidence |
| `claimKind`, `claimDetail` | per publication: its own failure reason |
| `component` | per publication: union-find component identity |
| `owner` | per merge key: the current publication representative |
| `frozenEvidence` | per publication: frozen evidence membership |
| `frozenKind`, `frozenDetail` | per publication: frozen component reason |

`truth` is the most important variable, and the first draft did not have it.
Without it, C1 said only that no machine retained an `Unclassified` marker — and
a real index that loses a row publishes no such marker. It publishes `Absent`,
which is exactly what it publishes for a machine that genuinely has no claim.
The invariant was therefore satisfiable by an index that dropped rows, which is
the failure it exists to catch. `BrokenDroppedRow.cfg` is the regression test.

**The publication is the unit, not the machine.** This is the correction that
took two review rounds to reach. The implementation creates one union-find node
per call to `PublishRejection`; one node may carry several machines, and a
machine is only one of several identities through which nodes can connect.

The next read found the deeper error: a publication does not merge merely
through machines. It connects through four **tagged** domains — kickoff
MethodDef, state-machine TypeDef, implementation MethodDef, and claimed type
name when `RejectKickoffCandidates` registers that name for reuse. A claimed
name carried only as evidence is not automatically a link. Equal numeric tokens
from different domains do not collide. `links` abstracts those tagged
identities into one finite set because their connectivity semantics are
identical.

`payload` is deliberately separate from `links`. The identities that explain
why two rejection publications merge are not the same thing as the diagnostic
evidence they contribute. Conflating the two made an earlier `covers` model
unable to state evidence completeness without also changing the graph.
Payload values are likewise domain-tagged, standing for kickoff candidates,
state-machine candidates, and claimed types without making their unrelated
identities collide.

Kind and detail are separate values in the model so a mutation can combine
fields from different publications. Production captures both from one
publication in `new(component.Kind, component.Detail)`. C5 requires that
intact pair to come from the component and to agree across its projection; it
deliberately does not specify which contributor wins.

Freeze has no "everything is merged" precondition. Production merges eagerly
when publishing and performs no such check before freezing. Adding the guard
would both invent an obligation and make `C5_ComponentsEqualGraphClosure`
vacuous: a broken merge could never reach the state in which it is checked.

## Checked properties

| Name | Design invariant | Statement |
| --- | --- | --- |
| `C1_Totality` | C1 | Once `Built`, every machine's result matches an independent recount of `truth` |
| `C2_FailureIsTyped` | C2 | `Failed` implies a typed kind, and never a success-shaped state |
| `C3_FailureRejectsAll` | C3 | `Failed` implies *every* machine reports `Rejected` |
| `C5_ComponentsEqualGraphClosure` | C5 | Component equality is exactly connectivity through tagged merge keys |
| `C5_ComponentProjectionAgrees` | C5 | One component has one frozen evidence set and reason |
| `C5_EvidenceMembershipIsComplete` | C5 | Frozen evidence is the union of every component publication's payload |
| `C5_ReasonComesFromComponent` | C5 | The selected `(Kind, Detail)` pair belongs intact to one component publication |
| `FailureIsAbsorbing` | C2, C3 | `Failed` is never left, and results never change after it |
| `EventuallyTerminal` | — | Construction always reaches `Built` or `Failed` |

`FailureIsAbsorbing` and `EventuallyTerminal` are temporal; the rest are
invariants.

## Scenarios

| Configuration | Bounds | Purpose | Result |
| --- | --- | --- | --- |
| `StateMachineCompleteness.cfg` | `MachineCount = 3`, `MaxBudget = 3` | Success and malformed-input failure reachable | 729 states, 351 distinct, depth 5 |
| `BudgetExhaustion.cfg` | `MachineCount = 3`, `MaxBudget = 2` | Budget too small to classify every machine | 648 states, 297 distinct, depth 4 |
| `RejectionComponentMerge.cfg` | three keys, two evidence values, three publications | Exact graph partition, projection, and evidence | 4,899,805 states, 1,157,521 distinct, depth 5 |

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

TLC prints every variable in each state; the states below are **abridged to the
three that carry the argument**. Run the command to see them in full.

```console
$ java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
    -config BrokenPartialFailure.cfg StateMachineCompleteness.tla

Error: Invariant C3_FailureRejectsAll is violated.
Error: The behavior up to this point is:

State 1: <Initial predicate>
  /\ truth  = <<"Resolvable", "Resolvable", "Resolvable">>
  /\ result = <<"Unclassified", "Unclassified", "Unclassified">>
  /\ phase  = "Building"

State 2: <ResolveOne(1)>
  /\ result = <<"Resolved", "Unclassified", "Unclassified">>
  /\ phase  = "Building"

State 3: <FailModule("Malformed")>
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
The classification model carries `FailureMode`, `FailureExitMode`, and
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
| `BrokenRecoveringFailure.cfg` | Whole-module failure resumes construction | `FailureIsAbsorbing` |
| `BrokenPartialMerge.cfg` | A publication joins current representatives but not their whole components | `C5_ComponentsEqualGraphClosure` |
| `BrokenUnvisitedPublish.cfg` | Construction publishes before classifying every machine | `C1_Totality` |
| `BrokenUnmergedPublish.cfg` | A publication records links without joining the components they reach | `C5_ComponentsEqualGraphClosure` |
| `BrokenOvermerge.cfg` | A publication joins disconnected prior publications | `C5_ComponentsEqualGraphClosure` |
| `BrokenDroppedRow.cfg` | A row never reached is published as `Absent` | `C1_Totality` |
| `BrokenDroppedEvidence.cfg` | Freeze retains only each publication's local diagnostic payload | `C5_EvidenceMembershipIsComplete` |
| `BrokenSplitReason.cfg` | Publications in one component retain different local reasons | `C5_ComponentProjectionAgrees` |
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
- `BrokenSplitReason.cfg` replaces an earlier first-publication-wins mutation.
  The current implementation does select the first appended publication, but
  no consumer needs that selection rule and no gate enforces it. The contract
  is that one intact component reason is shared, not which reason wins.
- `BrokenHybridReason.cfg` replaces an atomic reason value with independently
  modeled kind and detail, so a hybrid pair can now falsify the intact-pair
  statement.
- `BrokenRecoveringFailure.cfg` makes non-vacuity evidence explicit for the
  temporal absorption property rather than relying on the transition shape.

The last two are worth generalizing from. A model can fail by idealizing the
implementation, but it can also fail by promoting an incidental implementation
choice into a contract. Correcting the property is not enough if the
abstraction still conflates state domains or specifies behavior no consumer
needs.

## Running it

Follow [`docs/runbooks/tla-plus-setup.md`](../../../runbooks/tla-plus-setup.md)
to obtain the pinned `tla2tools.jar`, then:

```bash
for cfg in StateMachineCompleteness BudgetExhaustion; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
    -config "$cfg.cfg" StateMachineCompleteness.tla
done

java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
  -config RejectionComponentMerge.cfg RejectionComponentMerge.tla
```

All three report `Model checking completed. No error has been found.`

The counterexamples are run the same way and each must report its expected
violation:

```bash
for cfg in BrokenPartialFailure BrokenAbsentOnFailure \
  BrokenRecoveringFailure BrokenUnvisitedPublish BrokenDroppedRow; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
    -config "$cfg.cfg" StateMachineCompleteness.tla
done

for cfg in BrokenPartialMerge BrokenUnmergedPublish BrokenOvermerge \
  BrokenDroppedEvidence BrokenSplitReason BrokenHybridReason; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
    -config "$cfg.cfg" RejectionComponentMerge.tla
done
```
