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
- rejection components **merge** as claims are discovered, in an order chosen by
  the compiler that emitted the assembly;
- a whole-module failure must produce a **total** rejection, not a partial one
  that is textually indistinguishable from a per-claim refusal.

The third is exactly the invariant that a reviewer found by accident, after a
prose comment describing it had been read and cleared by two other reviewers.
Mutation M1 below shows TLC finding the same bug in under a second.

## What it does not establish

The model establishes evidence **about the model**. It is not evidence about
`StateMachineRelationshipIndex`. The gates named in each invariant of the design
document are that evidence.

C4 and C6 are deliberately out of scope. C4 is a statement about what a consumer
may infer from a value, not about system state. C6 is a statement about an
external observer's ability to recompute a population, which is not something a
state machine of the construction can express.

## Structure

| Variable | Meaning |
| --- | --- |
| `truth` | per machine: what it **actually** is, independent of the index |
| `published` | how many rejections have been published so far |
| `covers` | per publication: the machines its claim names |
| `claimKind` | per publication: its own kind, never rewritten by a merge |
| `phase` | `"Building"`, `"Built"`, or `"Failed"` |
| `kind` | the typed failure kind once `phase = "Failed"` |
| `result` | per machine: `Unclassified`, `Resolved`, `Absent`, `Rejected` |
| `component` | per machine: the rejection component it belongs to |
| `evidence` | per machine: which rejection kind the component settled on |
| `visited` | machines construction has reached |
| `budget` | construction steps remaining |

`truth` is the most important variable, and the first draft did not have it.
Without it, C1 said only that no machine retained an `Unclassified` marker — and
a real index that loses a row publishes no such marker. It publishes `Absent`,
which is exactly what it publishes for a machine that genuinely has no claim.
The invariant was therefore satisfiable by an index that dropped rows, which is
the failure it exists to catch. `BrokenDroppedRow.cfg` is the regression test.

**The publication is the unit, not the machine.** This is the correction that
took two review rounds to reach. `RejectClaims` builds one `RejectionComponent`
from a *list* of claims and appends it to `_rejectionComponents`, so a single
publication names several machines and carries one kind for all of them; a
machine may also appear in several publications, which is what `MergeExisting`
unions. A previous draft gave each machine its own kind and its own discovery
position, which cannot express "the first appended component wins" — in that
model no component spans machines, so the rule had nothing to range over.
`covers` and `claimKind` are indexed by publication for this reason, and
`PublishRejection` mirrors the real `PublishRejection` + `MergeExisting` pair.

`MustMerge` is **derived** from `covers` rather than configured, so the merging
obligation is exactly the one the input creates, and transitivity falls out of
applying it across overlapping publications. An earlier draft configured a fixed
chain of pairs, which stated the obligation independently of the claims that
produce it.

`evidence` carries one value standing for the `(Kind, Detail)` pair. Production
captures both together from a single component — `new(component.Kind,
component.Detail)` — so no reachable behavior takes them from different
components, and splitting them would add states without adding a checkable
claim.

`Finish` deliberately has **no** "everything is merged" precondition. Production
merges eagerly when publishing and performs no such check before freezing, so
requiring one would be an obligation the model invented — and it would also mask
`BrokenUnmergedPublish`, since an unmerged state could then never reach `Built`.

## Checked properties

| Name | Design invariant | Statement |
| --- | --- | --- |
| `C1_Totality` | C1 | Once `Built`, every machine's result matches an independent recount of `truth` |
| `C2_FailureIsTyped` | C2 | `Failed` implies a typed kind, and never a success-shaped state |
| `C3_FailureRejectsAll` | C3 | `Failed` implies *every* machine reports `Rejected` |
| `C5_ComponentsAgree` | C5 | Machines in one component share a result and evidence |
| `C5_EvidenceIsFirstDiscovered` | C5 | A component's evidence is its earliest-**published** contributing claim's |
| `C5_SharedRejectionsMerge` | C5 | Once `Built`, machines named by one publication share a component, transitively |
| `FailureIsAbsorbing` | C2, C3 | `Failed` is never left, and results never change after it |
| `EventuallyTerminal` | — | Construction always reaches `Built` or `Failed` |

`FailureIsAbsorbing` and `EventuallyTerminal` are temporal; the rest are
invariants.

## Scenarios

| Configuration | Bounds | Purpose | Result |
| --- | --- | --- | --- |
| `StateMachineCompleteness.cfg` | `MachineCount = 3`, `MaxRejections = 3`, `MaxBudget = 4` | Both success and malformed-input failure reachable | 13,825 states, 8,080 distinct, depth 6 |
| `BudgetExhaustion.cfg` | `MachineCount = 3`, `MaxRejections = 3`, `MaxBudget = 2` | Budget too small to classify every machine, forcing `BudgetExceeded` | 2,641 states, 1,500 distinct, depth 4 |

`MachineCount = 3` is the smallest bound that makes transitive merging real: two
machines can only merge directly, so a chain of three is required to distinguish
"merge the named pair" from "merge the component". Mutation M2 depends on it.

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

Each counterexample is a **committed configuration**, not a described edit. The
model carries three mode constants — `FailureMode`, `MergeMode`, `FinishMode` —
whose correct settings the two scenarios above use, and whose broken settings
each configuration below selects.

That is deliberate. A property no mutation can falsify is not checking anything,
so non-vacuity evidence has to exist. But recording it as a README table saying
"this mutation was violated" would be a normative claim in prose with nothing
holding it true — the exact defect shape this design document was written to
eliminate. Committed configurations are re-runnable by anyone, and they fail
loudly if a later change makes an invariant weaker than it looks.

| Configuration | Broken behavior | Expected violation |
| --- | --- | --- |
| `BrokenPartialFailure.cfg` | Module failure preserves results already recorded, rejecting only unclassified machines | `C3_FailureRejectsAll` |
| `BrokenAbsentOnFailure.cfg` | Module failure reports `Absent` rather than `Rejected` | `C2_FailureIsTyped` |
| `BrokenPartialMerge.cfg` | Publishing sets evidence only on the machines the new claim names | `C5_ComponentsAgree` |
| `BrokenUnvisitedPublish.cfg` | Construction publishes before classifying every machine | `C1_Totality` |
| `BrokenUnmergedPublish.cfg` | Publishing skips the components the named machines already belong to (`MergeExisting` omitted) | `C5_SharedRejectionsMerge` |
| `BrokenDroppedRow.cfg` | A row never reached is published as `Absent` | `C1_Totality` |
| `BrokenOrderDependentMerge.cfg` | Merged kind comes from the newest contributing claim rather than the earliest published | `C5_EvidenceIsFirstDiscovered` |

`BrokenPartialFailure.cfg` is the one worth dwelling on. It is the bug a reviewer
found by accident in the nineteenth review round of the accompanying test change,
after a prose comment describing the very property had been read and cleared by
two other reviewers. TLC finds it in under a second.

Three of these configurations record holes that earlier revisions of this model
did not detect. They are kept because each one is the regression test for a way
this model was, at some point, checking less than it appeared to.

- `BrokenUnmergedPublish.cfg` — the first revision had no notion of which
  machines *ought* to share a component, so an index that merged nothing
  satisfied component agreement vacuously.
- `BrokenDroppedRow.cfg` — the first revision modeled a lost row as a
  distinguished `Unclassified` marker. Real indexes publish `Absent`. Changing
  the mutation to match reality made `C1_Totality` pass, which is how the
  vacuity was found.
- `BrokenOrderDependentMerge.cfg` — the first revision combined merged evidence
  with a minimum, which is commutative. That made order independence true of
  the model and impossible to falsify, while the design document asserted an
  order independence the implementation does not have. The second revision
  fixed the invariant but kept the wrong *unit*, modeling per-machine kinds
  when production carries one kind per published component.

The last of those is the one worth generalizing from, and note that it took two
attempts. A model that idealizes the system will happily prove properties the
system lacks; correcting the property is not enough if the abstraction still
does not match the operation the code performs. The modeler is the least likely
person to notice either.

## Running it

Follow [`docs/runbooks/tla-plus-setup.md`](../../../runbooks/tla-plus-setup.md)
to obtain the pinned `tla2tools.jar`, then:

```bash
for cfg in StateMachineCompleteness BudgetExhaustion; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
    -config "$cfg.cfg" StateMachineCompleteness.tla
done
```

Both report `Model checking completed. No error has been found.`

The counterexamples are run the same way and each must report its expected
violation:

```bash
for cfg in Broken*.cfg; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS" tlc2.TLC \
    -config "$cfg" StateMachineCompleteness.tla
done
```
