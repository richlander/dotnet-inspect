# Control-flow structuring: the common-exit redesign

This document scopes the next major investment in the decompiler's control-flow
recovery. It is the design artifact behind the "non-tree forward-conditional"
work: the diagnosis that the remaining candidate-worse gap is dominated by one
structural shape the current `StructuringPass` cannot express, and a plan to
close it without destabilizing the pass.

Read [decompiler-pipeline.md](../decompiler-pipeline.md) first for the pipeline
shape and the recognizability goal. This doc governs one pass.

## Where we are

`StructuringPass` raises flat goto-based IR into nested `if`/`else`, loops are
raised by `DoWhileLoopPass`/`ForLoopPass`, EH by `EhStructuringPass`, and switch
jump-tables by `SwitchRaisingPass`. Two recent passes closed bounded gaps:

- **#631** — the printer's definite-assignment walk gained a CFG-based pass so a
  surviving goto no longer floods every local to `= default`.
- **#640** — `ReturnMergePass` + a guard-leaf inlining generalization raise the
  comparison tree csc emits for a sparse `switch`.

What remains is not bounded. Measured on `System.Private.CoreLib` (preview.5),
the 1,293 candidate-worse methods bucket by the reason `StructuringPass` left
them flat:

| Bail reason | Count | Shape |
| --- | ---: | --- |
| `cond-target-past-region` | 680 | a conditional branch whose target is past the region |
| `forward-branch-not-region-exit` | 403 | an unconditional forward goto that is not the region exit |
| `unconsumed-regions` (EH) | 79 | a try/catch/finally the EH pass left flat |
| `backward-branch` / `cond-backward-loop` | 111 | an unraised loop |
| `leave-target-container` / structured-ok | 20 | EH leave survivor, or worse for a non-structuring reason |

The top two — **1,083 of 1,293** — are the same shape: **a forward branch to a
common merge/exit that lies past the region**. Representative methods:
`System.Array::InternalSetValue`, `System.Array::LastIndexOf`,
`System.Array::Reverse`, `Interop.OSReleaseFile::GetPrettyName`,
`System.Array::CopyImpl`. This is the gap this doc addresses.

Two shapes that are **already handled** and are out of scope: short-circuit
`&&`/`||` guard chains (they nest cleanly today — the `TripleAnd`/`IfAnd`/
`OrChainGuardPass` fixtures round-trip), and the comparison tree (#640).

## The blocker is the region model, not a missing case

`StructuringPass` is a two-phase, all-or-nothing pass over **index ranges**.
`Validate(ctx, start, stop, joinIndex, breakTarget)` walks `blocks[start..stop)`;
a branch is in the slice only if its target lands inside that range or is the
range's single exit. A forward branch past `stop` bails
(`StructuringPass.cs`, the `cond-target-past-region` and
`forward-branch-not-region-exit` returns). If the whole function validates, a
mirror `BuildRegion` walk materializes the tree; otherwise the container stays
flat. The all-or-nothing rule is the pass's safety story: it structures
completely or keeps the always-correct flat form, so a half-understood shape can
never mis-structure.

A **common exit** breaks the index-range model. Trace `InternalSetValue`:

```csharp
if (value is not null) goto IL_009E;     // outer diamond
  if (!V_3.IsValueType) goto IL_008B;    // nested in the value-is-null arm
    ...; if (!ContainsGCPointers) goto IL_0074;
    ClearWithReferences(...); goto IL_01C0;   // arm ends at the common exit
  IL_0074: ClearWithoutReferences(...); goto IL_01C0;
  IL_008B: ...; goto IL_01C0;
IL_009E: ...; goto IL_01C0;
IL_01C0: <tail>                          // the common exit / post-dominator
```

The outer diamond's join is `IL_01C0`, which `FindDiamondJoin` can find. But the
recursion then validates the value-is-null arm with `stop = IL_009E`, and inside
it the arms branch to `IL_01C0`, which is **past `IL_009E`**. `IL_01C0` is the
post-dominator of the whole method body; a `goto IL_01C0` from any depth is past
*every* enclosing `stop`. No amount of threading the existing model fixes this —
an index range cannot name a merge point that lives outside it. This is the
recognizable limitation: the current pass recovers **reducible regions whose
single exit is the range boundary**, which is a strict subset of structured
control flow.

The fix is to make the join a **graph property (the post-dominator), not a range
boundary** — the same move ILSpy's `ControlFlow`/`ConditionDetection` and every
dominator-driven structurer make. The original (retired) emitter's structuring
layer was already dominator-driven for exactly this reason; the replacement
pipeline has not yet reached that point.

## Two targets, and the all-or-nothing question

A subtlety shapes the design: **even the baseline emitter keeps the goto here.**
For `InternalSetValue` it renders nested `if`/`else` but retains
`goto IL_01C0;` and the `IL_01C0:` label. So there are two possible targets,
with different costs:

### Target A — partial structuring with a retained exit label

Nest the `if`/`else` and **keep** the common-exit `goto`/label, matching the
oracle. This is the smaller behavioral change, but it requires relaxing the
all-or-nothing invariant: the pass must be able to structure *most* of a
container while leaving one labelled merge block standing. That invariant is the
current safety story, so relaxing it is the real cost — a partially-structured
tree has more shapes to get wrong, and the "stays flat or fully structures"
guarantee no longer backstops a bug.

### Target B — full goto elimination

Eliminate the common-exit goto by arranging the nesting so every arm falls
through to the exit. This is always possible for a reducible CFG, but in general
requires **code duplication** (the tail after the merge is duplicated into each
arm) or an introduced boolean flag — the classic structured-programming cost.
For a merge that is a short `return` tail this is cheap and clean (it is exactly
what `ReturnMergePass` already does for comparison trees); for a merge with a
substantial shared tail it bloats the output and diverges further from the
oracle.

### Recommendation: post-dominator joins, exit kept as a goto (A), return-tail merges eliminated (B)

- Compute post-dominators for the container and let the diamond/guard recursion
  use the **immediate post-dominator** as the join, lifting the index-range
  restriction. This is the core change and is shared by both targets.
- When the post-dominator merge is a **short return tail**, eliminate it by
  inlining (generalize `ReturnMergePass` beyond comparison trees, reusing its
  gating discipline). This is full structuring for the cheap case.
- Otherwise, retain the merge as a labelled block and let the arms `goto` it —
  matching the oracle. This is target A, and it is where the all-on-nothing
  relaxation is required.

Rendering a retained label/goto is already supported (the flat path prints them
today); the new work is letting a *structured* tree contain a labelled merge
block as a first-class node rather than only as the flat fallback.

## Soundness strategy

The lesson from #640 is that `StructuringPass` changes are regression-prone: the
first comparison-tree cut mangled ~15 ternary/boolean methods (`Pick`,
`Ternary`, `IfAnd`, …) by turning clean selections into guard clauses, and only
a scale gate (`ComparisonTrees.IsLikely`, ≥4 constant comparisons) separated the
genuine switch trees from small selections. A post-dominator rewrite touches far
more methods (`--pass-impact structuring` already reports ~3,400 of 12,000), so
every increment must be measured, not reasoned about.

The rails, in order of authority:

1. **`--compile-check` A/B** — a mis-structure usually produces invalid C#
   (CS0165 from a broken declaration, CS0161/unreachable from a dropped path).
   The gate is a byte-for-byte defect-set diff against `main`; zero new invalid
   methods is the bar (#631 and #640 both cleared it).
2. **`CompileBackGateTests` / `LoweredCompileBackGateTests`** — a structurally
   wrong-but-valid raise is caught by recompiling the fixture and diffing IL
   opcodes. Every selection fixture must stay on its current clean path.
3. **`--candidate next`** — the candidate-worse count is the scoreboard; it must
   drop, and the recovered methods must move to agree / baseline-worse, never to
   a new worse bucket.
4. **`--pass-impact <pass>`** — the blast-radius view (#641): before shipping,
   list every method the change touches and confirm the set is the intended
   shape, not collateral on methods no fixture covers.

A post-dominator structuring that is unsound on an irreducible or multi-entry
region must **fall back to flat**, never guess — the all-or-nothing safety is
relaxed only for the *recognized* partial shape (a single post-dominator merge),
not abandoned.

## Incremental plan

Each step is its own PR, measured against the rails above. Steps are ordered so
the cheap, fully-eliminable cases land first and the invariant relaxation comes
only when the post-dominator machinery is proven.

1. **Post-dominator computation.** Add a tested `PostDominators.Of(container)`
   (forward must-analysis over the block CFG — the dual of the dominator pass the
   old emitter had, and shaped like the CFG dataflow #631 already introduced).
   No behavior change; pure analysis with unit tests on synthetic CFGs.
2. **Return-tail merges, non-tree.** Generalize `ReturnMergePass` to fold a
   post-dominator merge that is a short `return` tail, dropping the
   comparison-tree gate but keeping the scale/duplication guards. Fully
   structures the cheap case; no invariant change. Measure the candidate-worse
   drop and the blast radius.
3. **Post-dominator joins in the diamond/guard recursion.** Let `Validate`/
   `BuildRegion` accept a forward branch to the region's immediate
   post-dominator as the join, lifting the `target > stop` bail for that one
   target. Still all-or-nothing; recovers the cases where the merge is reachable
   by fallthrough after nesting.
4. **Partial structuring with a retained merge label (the invariant relaxation).**
   Allow a structured container to keep one labelled post-dominator block that
   the arms `goto`, matching the oracle for tails that cannot be eliminated.
   This is the largest and riskiest step; it lands last, behind the proven
   machinery and the full rail suite.

Steps 1–3 are sound extensions that keep the safety guarantee; step 4 is the one
that changes it, and is gated on the prior steps demonstrating the
post-dominator model is correct in practice.

## Out of scope

- Loops (`backward-branch`/`cond-backward-loop`, 111) — a separate loop-raising
  effort, not control-flow merging.
- EH (`unconsumed-regions`, 79) — the EH pass leaving regions flat is a distinct
  gap; the CFG-DA's `Leave` bail (#631) is the related printer-side residue.
- Switch jump tables (`SwitchRaisingPass`) and comparison trees (#640) — done.

## Open questions

- **How much of the 1,083 is fully eliminable (return-tail) vs retained-label?**
  Step 2 answers it empirically: the return-tail subset's recovery count tells
  us how much value lands before the invariant relaxation is needed.
- **Does the oracle's retained-goto shape match what step 4 would emit?** If the
  baseline's label placement differs, recovered methods land in
  `baseline-worse`/`uncertain` rather than `agree` — still a real quality win,
  but the scoreboard reads differently. Worth confirming against a sample before
  step 4.
- **Is partial structuring worth it, or should retained-goto methods stay flat?**
  If step 2 (return-tail) plus steps 1/3 recover most of the value, step 4's
  invariant relaxation may not pay for its risk. Decide after measuring 1–3.
