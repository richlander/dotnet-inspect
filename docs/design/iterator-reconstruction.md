# Iterator reconstruction

This document owns how the Decompiler raises a compiler-generated iterator
kickoff back to its `yield` body. It is the iterator counterpart of
[classic async reconstruction](classic-async-reconstruction.md#general-inverse)
and advances slice 2 of
[#10190](https://github.com/richlander/dotnet-inspect/issues/10190), whose
exit bar asks for 80% of state-machine kickoffs fully raised.

## Status and decision

Decision (2026-11-12): iterators get one general inverse, staged in behind the
existing shape matchers the same way the classic async inverse was staged in
behind its recipes.

Decision (2026-11-14): the inverse runs first. Review of stage 1 found shape
matchers accepting ordinary Release iterators and emitting wrong bodies at
`Full`: a dropped early `yield break`, a receiver field read through a
defaulted machine local, and a hoisted parameter stored through `this`. The
inverse reconstructs each of those correctly, so a matcher is now consulted
only for a machine the inverse declines.

| Stage | Content | State |
| --- | --- | --- |
| 1 | General inverse for iterators with no yield inside a `try` block, consulted after every shape matcher declines | Done |
| 2 | Finally frames: a yield inside `try`/`finally`, `using`, or `foreach` over a disposable enumerator | Planned |
| 3a | Run the inverse first; a shape matcher sees only what the inverse declines | This change |
| 3b | Retire each shape matcher that keeps no accepted population of its own | Planned |

## Why the shape matchers cannot grow

Measured at `bf89e224b` over the pinned real-world corpus (14 assemblies,
89,065 methods, 540 state-machine kickoffs, 278 fully raised):

| Unraised kickoffs | Count |
| --- | ---: |
| Iterator, no reconstruction (`DEC0004` iterator marker) | 150 |
| Classic async, general inverse declined | 52 |
| Reconstructed, a closure or local-function name remains | 57 |
| Other | 3 |

The 150 iterators are 128 distinct kickoff names. Read against the raw
`MoveNext` import, 61 have no exception region and 62 carry the fault region
of finally frames; the rest have a user `catch` or `finally` and no fault
region. The matchers decline the 61 on ordinary user code, not on protocol:
40 read the receiver through `<>4__this`, 42 use a `<>7__wrap` temporary, and
23 dispatch through a `switch`, which the reducible matcher does not treat as
dispatch. `SwitchIteratorReconstruction` accepts exactly six switch targets
and five yields.

## Claim and non-claims

The inverse claims: given an iterator kickoff that is only the state-machine
handoff, and the raw `MoveNext` of that machine, it either returns a body in
which every protocol role below is removed and every yield seam is one
`yield return`, or it declines with a reason and changes nothing.

It does not own or claim:

- kickoff recognition, which stays with `IteratorShapes` and is name-based;
  authenticating the relationship from `IteratorStateMachineAttribute` is a
  separate slice;
- finally frames (stage 2);
- async iterators and runtime-async bodies;
- the bodies the shape matchers emit for the machines the inverse declines;
  or
- general CFG or behavioral-equivalence soundness of the passes it re-runs.

## Lowering shell

The domain is Roslyn `IteratorMethodToStateMachineRewriter` output. The roles
are physical facts of the raw import. Witnesses on the pinned corpus:
`Microsoft.CodeAnalysis.BitVector::Words` (value-type receiver, `switch`
dispatch, a yield before a loop and one inside it),
`Newtonsoft.Json.Linq.JToken::AfterSelf` (early `yield break`), and
`System.CommandLine.Help.HelpBuilder::WrapText` (nested loops with two yields).

| Role | Physical form | Inverse |
| --- | --- | --- |
| State copy | `V_state = this.<>1__state` in the entry block | removed; no read or store of `V_state` may survive |
| Dispatch | every `ConditionalBranch` or `SwitchBranch` whose operand depends only on `V_state`, and the transfers between them, reached from the entry | evaluated once per state the machine stores, then removed |
| Yield seam | `this.<>2__current = X; this.<>1__state = k; return true;` ending one of the body's own blocks | `yield return X;` joined to the block dispatch reaches under state `k`; when `X` is not already of the field's type, the field's type travels with it as a `Coerce`, because `yield return` has no typed sink of its own |
| Initial entry | the block dispatch reaches under state 0 | the body's entry; blocks no longer reachable are removed |
| Running marker and advances | `this.<>1__state = c` for a constant `c` | removed |
| Completion | `return false` | `yield break;`; the one that ends the body is implicit when the body yields |
| Receiver | `this.<>4__this`, or its address when the kickoff captures `*this` | the kickoff's `this` |
| Hoisted parameter | a machine field named after a kickoff parameter | that parameter |
| Hoisted local | `<name>5__N`, `<>7__wrapN`, `<>s__N`, `<>8__N` | fresh local; `name` where the field carries one |
| Housekeeping clear | as owned by the [classic design](classic-async-reconstruction.md#lowering-shell) | removed under the same rule; a scope-end clear of a named local is carried |

The storage, dispatch-folding, and housekeeping rows are the same code the
classic inverse runs (`StateMachineStorage`); only the yield seam, the
completion, and the per-state resume join are iterator protocol.

## Proof obligations

1. **Dispatch is total over stored states.** Each state constant a yield seam
   stores, and state 0, must reach a block start through tests that fold
   under that state. A test that does not fold, a walk that does not
   terminate, or one that lands in the middle of a block declines.
2. **A yield seam is exactly three statements** ending its block, in one of
   the body's own blocks. Any other store to `<>2__current`, any `return
   true` a seam does not own, and any return that is not a constant decline.
3. **No protocol storage survives.** After rebinding, a machine field, the
   state local, or the machine receiver outside a field access declines.
4. **Validation follows the pipeline.** The result must be fully structured,
   carry no machine field and no value return, and contain exactly as many
   `yield return` statements as proven seams. A body into which the pipeline
   raised a lambda or local function declines: closure raising can name the
   variable a capture was copied from instead of the capture (#11208), and
   nothing here can check it. A closure the pipeline leaves lowered is
   honest and does not decline.

The decline boundary: a machine with a fault region (finally frames) declines
before any transform; a value-type receiver read as a whole value declines;
an unoptimized (`Debug`) `MoveNext` passes the transform and reconstructs
unless the structurer leaves a transfer in the result, which validation
declines; the kickoff with
user-visible work before the handoff never reaches the inverse. A generic
iterator method's machine is not imported through the kickoff seam today, so
neither the matchers nor the inverse see it.

Removing dispatch leaves blocks nothing reaches, and the inverse prunes them.
That prune never drops an IL successor, and may keep a block nothing
reaches: in particular a `switch`
instruction falls through to the next instruction when no case matches, so a
user `switch` keeps its `default` arm, or the branch around its cases when it
has none. Treating `switch` as a terminator deleted that arm and folded
`default` onto `case 0` at `Full`; stage 1 had this defect for the iterators
it reached, and running the inverse first would have extended it to every
Release iterator with a `switch` outside a yield.

A reconstructed body carries no `MoveNext` offset: those are another method's
coordinates. That includes the origins the structurer retains when it moves
a branch label onto a loop body or a join; left in place they either erase a
statement's provenance in the source document or, when one happens to equal
a kickoff instruction boundary, cite an unrelated kickoff instruction. Each top-level statement is anchored at the handoff's offset
instead, as the classic async reconstruction anchors its statements at the
kickoff. That anchor is what keeps the kickoff's IL and its state-machine
allocation fact in the `Annotated Source` view, and it is the only kickoff
instruction a reconstructed node may cite. This is the stage 1 rule
unchanged; with the inverse first it also covers the iterators the linear
matcher used to take, whose yields carried no offset and whose `Annotated
Source` therefore showed no kickoff IL.

One limit remains, present since stage 1: a transplanted block keeps its
`MoveNext` start offset, so a fidelity cause on a nested node can cite an
offset that is not in the kickoff.

A decline is visible: the iterator marker that replaces the kickoff carries
the inverse's reason (`... not reconstructed: a yield inside a try block
lowers to finally frames`), under the existing `DEC0004` iterator cause.

Falsifier: a kickoff the inverse reconstructs whose emitted body, compiled
with the same compiler, produces a `MoveNext` that differs from the original
in more than state numbering, hoisted-field naming, or a carried scope-end
clear.

## Evidence

| Measure on the pinned real-world corpus | `bf89e224b` | Stage 1 | Stage 3a |
| --- | ---: | ---: | ---: |
| State-machine kickoffs fully raised (of 540) | 278 | 336 | 337 |
| Branching methods fully raised (of 27,753) | 23,143 | 23,143 | 23,143 |
| Public API bodies fully raised (of 20,273) | 19,915 | 19,924 | 19,924 |
| All methods fully raised (of 89,065) | 79,426 | 79,484 | 79,485 |
| Methods that lost `Full` or fully raised | — | 0 | 0 |

Stage 1 declines on the same corpus: 61 finally frames, 5 where a transfer
survives the pipeline, 2 with a return that is neither seam nor completion,
and 1 with a surviving value return.

Stage 3a matcher populations, counted by distinct machine name with the
inverse running first. On the corpus only the foreach family accepts
anything: 18 foreach delegation, 1 nested delegation, and 1 disposal-bearing
iterator, all with finally frames. The linear, counting-loop, multi-yield,
stateless, switch, yield-break-loop, and reducible matchers accept no corpus
machine. In the test fixtures the switch matcher keeps the compiler-produced
`switch` with a yield in each section, which the inverse declines on a
surviving transfer; the yield-break-loop and stateless matchers keep one
fixture each; the linear, counting-loop, multi-yield, and reducible matchers
accept nothing. The stage 3a corpus measurement does not show how many
bodies changed rendering without changing grade: the snapshot records grade,
not text.

Tests: `IteratorGeneralInverseTests` pins hoisted-parameter and value-receiver
reconstruction, opcode-exact compile-back of the reconstructed ladder rows,
and the finally-frame decline with its reason. `IteratorReconstructionPassTests`
pins the Release reconstruction and the Debug decline of the post-yield
side-effect loops, and `LadderIteratorGateTests` moves the two
captured-parameter rows from owned residuals to recovered rows.

The raised-callable decline costs correct bodies as well as wrong ones. An
iterator whose lambda returns nothing and captures nothing
(`Run(() => Log(1)); yield return n;`) reconstructed correctly at `Full`
before the decline and is now not reconstructed. No corpus method is in that
set. A raised lambda or local function that returns a value never
reconstructed: its `return` is a surviving value return, which validation
declines on its own. The decline is not narrowed to capturing callables because nothing on a
raised `Lambda` proves it captures nothing. A matcher that accepts a machine
after this decline still renders its lambdas as the pipeline raised them, as
before stage 3a. Lifting the decline needs #11208, and reconstructing a
value-returning callable also needs validation to tell its return from the
iterator's.
`IteratorThatCapturesIntoALambda_IsNotGradedFull` pins that no such body is
graded `Full`, built Release and Debug. Only the raised-callable decline
stops `VoidReadOfCopy` and `VoidReadOfCopyAfterYield` (a void lambda that
reads a copied parameter): those four rows fail with the decline removed.
Without it `CopyOfParameter` and `PerIteration` still decline, on the value
return. `VoidLambda` and `LocalFunction` keep their closure lowered and
reconstruct below `Full` either way.

`SwitchBeforeAYield_KeepsItsDefaultArm` pins the prune over a `switch` with
and without `default`, before one yield, before two, and inside a loop, built
Release and Debug.

Stage 3a tests: `ReceiverAndBranchShapes_ReconstructThroughTheGeneralInverse`
pins four shapes a matcher used to render wrongly, and the counting-loop and
multi-yield tests now pin the `for` loop of their sources where they pinned a
`while` loop. `ReconstructedBody_CarriesOnlyTheHandoffOffset` and
`ReconstructedIterator_CitesOnlyTheHandoff` (a loop, nested loops, an
if-join, and a linear body) pin the offset rule in the pull-request lane. The two view-level checks are `Speed=Slow` and
run daily: `RealIteratorDocument_DoesNotTrustCompanionMethodOffsets` (over a
linear body, a loop, and an if-join, a C# node cites only the kickoff's
allocation; it asserted no provenance at all on yields while the linear
matcher owned its one fixture) and
`StateMachine_FactLandsOnTheKickoffNewobj` (the allocation fact stays in the
mixed view).
