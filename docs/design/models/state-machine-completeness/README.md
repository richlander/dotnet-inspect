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
| `phase` | `"Building"`, `"Built"`, or `"Failed"` |
| `kind` | the typed failure kind once `phase = "Failed"` |
| `result` | per machine: `Unclassified`, `Resolved`, `Absent`, `Rejected` |
| `component` | per machine: the rejection component it belongs to |
| `evidence` | per machine: which rejection kind the component settled on |
| `visited` | machines construction has reached |
| `budget` | construction steps remaining |

`evidence` exists because without it the confluence invariant was **vacuous**.
Merging only fires on two already-rejected machines, so "members of a component
agree on their result" was satisfied by construction: every member was
`"Rejected"` already. Carrying a value that can genuinely differ is what gives
the invariant something to be false about. Mutation M2 is the regression test
for that.

`SharedPairs` is derived rather than configured: consecutive machines share a
contributor, forming one chain. A chain is the interesting topology because
merging must be transitive — machines 1 and 3 must end up together even though
no single claim names both.

## Checked properties

| Name | Design invariant | Statement |
| --- | --- | --- |
| `C1_Totality` | C1 | In `Built` or `Failed`, every machine holds exactly one classified result |
| `C2_FailureIsTyped` | C2 | `Failed` implies a typed kind, and never a success-shaped state |
| `C3_FailureRejectsAll` | C3 | `Failed` implies *every* machine reports `Rejected` |
| `C5_ComponentsAgree` | C5 | Machines in one component share a result and evidence |
| `C5_SharedRejectionsMerge` | C5 | Once `Built`, rejections that share a contributor share a component |
| `FailureIsAbsorbing` | C2, C3 | `Failed` is never left, and results never change after it |
| `EventuallyTerminal` | — | Construction always reaches `Built` or `Failed` |

`FailureIsAbsorbing` and `EventuallyTerminal` are temporal; the rest are
invariants.

## Scenarios

| Configuration | Bounds | Purpose | Result |
| --- | --- | --- | --- |
| `StateMachineCompleteness.cfg` | `MachineCount = 3`, `MaxBudget = 3` | Both success and malformed-input failure reachable | 1,241 states, 341 distinct, depth 7 |
| `BudgetExhaustion.cfg` | `MachineCount = 3`, `MaxBudget = 2` | Budget too small to classify every machine, forcing `BudgetExceeded` | 388 states, 105 distinct, depth 5 |

`MachineCount = 3` is the smallest bound that makes transitive merging real: two
machines can only merge directly, so a chain of three is required to distinguish
"merge the named pair" from "merge the component". Mutation M2 depends on it.

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
| `BrokenPartialMerge.cfg` | Merge unifies evidence for only the two named machines, not the component they join | `C5_ComponentsAgree` |
| `BrokenUnvisitedPublish.cfg` | Construction publishes before classifying every machine | `C1_Totality` |
| `BrokenUnmergedPublish.cfg` | Construction publishes without merging rejections that share a contributor | `C5_SharedRejectionsMerge` |

`BrokenPartialFailure.cfg` is the one worth dwelling on. It is the bug a reviewer
found by accident in the nineteenth review round of the accompanying test change,
after a prose comment describing the very property had been read and cleared by
two other reviewers. TLC finds it in under a second.

`BrokenUnmergedPublish.cfg` records a hole in an earlier revision of this model.
That revision had no `SharedPairs` and no `C5_SharedRejectionsMerge`, and the
mutation **survived**: with no notion of which machines ought to share a
component, an index that merged nothing satisfied component agreement vacuously.
The surviving mutation was the signal that the invariant was weaker than the
prose it was meant to capture.

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
