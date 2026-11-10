# Classic async splice Lean model

This Lean 4 model is proof evidence for the
[general inverse](../../classic-async-reconstruction.md#general-inverse) of
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472), stage 2 of
[#10190](https://github.com/richlander/dotnet-inspect/issues/10190) slice 2.
The normative owner remains
[`classic-async-reconstruction.md`](../../classic-async-reconstruction.md).
The model changes no product contract or runtime path. It pairs with the
[MoveNext lifecycle TLA+ model](../classic-async-movenext-lifecycle/), which
owns the re-entry lifecycle over time; this model owns the laws over every
input.

## Why Lean here

The inverse's central claim is an equivalence that must hold for every body
and every schedule of suspension choices: the lowered machine, re-entered
however many times, produces the user-effect trace of the sequential body it
was lowered from. `docs/proof-methodology.md` assigns a law over every input to
Lean. The housekeeping-clear role rests on two further laws about removing a
store from a path, and on their composition.

## Proven claims

| Owner claim | Theorem |
| --- | --- |
| The lowered machine and the spliced body yield the same user-effect sequence under every schedule of suspension choices | `ClassicAsyncSplice.runMachine_eq_runSeq` |
| Resuming at a stored state runs exactly the continuation after that await, re-dispatching into each enclosing region | `ClassicAsyncSplice.resume_eq` |
| A suspended `MoveNext` produced the sequential prefix before its await; a completed one produced the whole trace | `ClassicAsyncSplice.runStmts_suspended`, `ClassicAsyncSplice.runStmts_completed` |
| Removing a store from which no read of its location is reachable before another store changes no observed value (compiler-temporary arm) | `StoreRemoval.removeDeadStore_observe` |
| A store of the value the location already holds changes nothing; the second of two adjacent stores is such a store (adjacent-clear arm) | `StoreRemoval.removeRedundantStore_observe`, `StoreRemoval.removeAdjacentStore_observe` |
| A sequence of removals, each sound against the path the earlier removals produced, is sound as a whole | `StoreRemoval.removals_compose` |

## The machine and the splice

The body grammar is a list of statements: a user effect (an observation), an
await carrying its state constant, or a `tryFinally` whose body may await and
whose finally block is a list of effects (an await inside a finally is outside
the stage 2 domain and declines). The schedule is a list of booleans consumed
one per await: `true` means the awaiter had completed, `false` means the
machine stores the state and returns. The fold under the running state is built
into the machine model rather than proven separately: the only state tests the
machine performs are the dispatch that skips to a landing (`resumeStmts`) and
the finally guard, which the model encodes by returning through enclosing
finally regions without running their effects when suspending (`runStmts`).
Everything the splice keeps is on a path where that guard was true.

`runMachine` drives the machine to completion from the kickoff's `-1`,
re-entering at the stored state after each suspension. `runSeq` is the spliced
body. The headline theorem equates them for every body whose state constants
are distinct, which Roslyn guarantees by numbering every suspension site once.

## Correspondence boundary

| Model | Role table row |
| --- | --- |
| `runStmts` entry with the schedule | the kickoff's `Start` with state `-1` |
| `await` with `true` | a suspension site whose `IsCompleted` test was true |
| `await` with `false` | the suspension site storing `<>1__state = N`, `<>u__k`, and returning |
| return without running `fin` | the finally guard `state < 0` being false on the suspension return |
| `resumeStmts` skipping to the await | the dispatch chain or `switch`, re-dispatching at each region entry |
| `fin` after the body completes or after a resume inside it | the finally body under a true guard (`-1`) |
| `after` | the statements the structurer receives for the continuation |

The C# inverse must separately gate that every removed node carries one role
of the table, that the state constants are distinct (the certificate and the
Roslyn lowering give this), that the `catch (object)` form of await inside a
finally declines, and that the store removals are decided one at a time against
the reduced body (`StoreRemoval.removals_compose` is the law; the pass's
sequential decision is its instance). The store-removal module abstracts
control flow to one path: its premise is stated per path, which is exactly how
the inverse's reachability check consumes it.

## Build

From this directory:

```sh
rm -rf .lake && lake build
```

The build has no `sorry`, no `axiom`, and no warnings. Headline theorems depend
only on the standard axioms (`propext`, `Classical.choice`, `Quot.sound`), as
`eng/run-lean-checks.sh` verifies.
