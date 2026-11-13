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

| Stage | Content | State |
| --- | --- | --- |
| 1 | General inverse for iterators with no yield inside a `try` block, consulted after every shape matcher declines | This change |
| 2 | Finally frames: a yield inside `try`/`finally`, `using`, or `foreach` over a disposable enumerator | Planned |
| 3 | Run the inverse first; retire each shape matcher that keeps no accepted population of its own | Planned |

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
- the shape matchers' own acceptance, which is unchanged in stage 1; or
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
   `yield return` statements as proven seams.

The decline boundary: a machine with a fault region (finally frames) declines
before any transform; a value-type receiver read as a whole value declines;
an unoptimized (`Debug`) `MoveNext` passes the transform but the structurer
leaves a transfer in the result, so validation declines it; the kickoff with
user-visible work before the handoff never reaches the inverse. A generic
iterator method's machine is not imported through the kickoff seam today, so
neither the matchers nor the inverse see it.

A decline is visible: the iterator marker that replaces the kickoff carries
the inverse's reason (`... not reconstructed: a yield inside a try block
lowers to finally frames`), under the existing `DEC0004` iterator cause.

Falsifier: a kickoff the inverse reconstructs whose emitted body, compiled
with the same compiler, produces a `MoveNext` that differs from the original
in more than state numbering, hoisted-field naming, or a carried scope-end
clear.

## Evidence

| Measure on the pinned real-world corpus | `bf89e224b` | Stage 1 |
| --- | ---: | ---: |
| State-machine kickoffs fully raised (of 540) | 278 | 336 |
| Branching methods fully raised (of 27,753) | 23,143 | 23,143 |
| Public API bodies fully raised (of 20,273) | 19,915 | 19,924 |
| All methods fully raised (of 89,065) | 79,426 | 79,484 |
| Methods that lost `Full` or fully raised | — | 0 |

Stage 1 declines on the same corpus: 61 finally frames, 5 where a transfer
survives the pipeline, 2 with a return that is neither seam nor completion,
and 1 with a surviving value return.

Tests: `IteratorGeneralInverseTests` pins hoisted-parameter and value-receiver
reconstruction, opcode-exact compile-back of the reconstructed ladder rows,
and the finally-frame decline with its reason. `IteratorReconstructionPassTests`
pins the Release reconstruction and the Debug decline of the post-yield
side-effect loops, and `LadderIteratorGateTests` moves the two
captured-parameter rows from owned residuals to recovered rows.
