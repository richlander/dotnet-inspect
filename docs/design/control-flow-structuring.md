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

What remains is not bounded. The bail-reason histogram below is produced by
`decompiler-harness --structuring-bails` (landed as the first PR of this track),
which attaches a `StructuringDiagnostics` sink to the pipeline and tallies, per
block container, why `StructuringPass` left it flat. Measured on the running
runtime's `System.Private.CoreLib` (net11 preview.5), across 41,012 methods —
**10,166 containers structured, 1,672 bailed across 1,645 methods**:

| Bail reason | Containers | Shape |
| --- | ---: | --- |
| `cond-target-past-region` | 876 | a conditional branch whose target is past the region |
| `forward-branch-not-region-exit` | 599 | an unconditional forward goto that is not the region exit |
| `unconsumed-regions` (EH) | 108 | a try/catch/finally the EH pass left flat |
| `cond-backward-branch` | 61 | an unraised loop |
| `leave-target-in-container` | 14 | an EH leave survivor keeps its container flat |
| `eh-terminator-survivor` | 14 | a `Leave`/`EndFinally`/`EndFilter` terminator |

The top two — **1,475 of 1,672** — are the same shape: **a forward branch to a
common merge/exit that lies past the region**. Representative methods:
`System.Array::InternalSetValue`, `System.Array::LastIndexOf`,
`System.Array::Reverse`, `Interop.OSReleaseFile::GetPrettyName`,
`System.Array::CopyImpl`. This is the gap this doc addresses.

> **Update — current main (post-#647/#648).** The scoreboard
> (`--candidate next`) now reports **1,217** candidate-worse, down from 1,293:
> the RVA-span (#647) and inline-array (#648) raises trimmed ~76. The
> control-flow merge shape is untouched by those passes —
> `System.Array::InternalSetValue` still renders nested `if … goto IL_01C0;`
> with every arm branching to the common exit `IL_01C0` (verified against
> current main). The diagnosis below stands; only the headline count moved.
>
> One caveat the refreshed measurement surfaced: the `--candidate next`
> candidate-worse **docket is now mixed**. Its top `FirstDiffLine` buckets are
> dominated by non-structuring residue — `int V_0;` vs `int V_0 = default;`
> (definite-assignment cosmetics), `byte[] V_1;` vs `pinned byte[] V_1;`,
> `ref byte V_0;` vs `ref … Unsafe.NullRef…`. The forward-branch-to-common-exit
> shape is **not** legible from DiffNext's line buckets; it must be sized from
> `StructuringPass`'s own bail reasons (see *Reproducing the measurement*).

### Reproducing the measurement

The numbers above come from two harness lenses; both must agree before and
after every step of this plan.

- **The scoreboard** — `decompiler-harness --candidate next` (DiffNext) diffs
  the retired `CSharpEmitter` baseline against the raised pipeline and prints
  the `candidate-worse` count plus `FirstDiffLine` buckets. This is the
  burndown number (currently 1,217), but its buckets conflate structuring with
  the `= default` / `pinned` residue, so it sizes the *whole* gap, not the merge
  shape.
- **The per-method view** — `decompiler-harness --dump 'Ns.Type::Method'` prints
  every stage; the final stage is `PrintRaised`. Use it to confirm a method
  exhibits (or, after a fix, no longer exhibits) the common-exit `goto`.
- **The bail-reason histogram** — `decompiler-harness --structuring-bails`
  attaches a `StructuringDiagnostics` sink to the Default pipeline and tallies,
  per block container, the `StructuringPass` bail reason (or that it structured).
  This is the merge-docket lens: the `cond-target-past-region` /
  `forward-branch-not-region-exit` table above is reproducible on demand, so
  every subsequent step of this plan can show the bucket shrinking. The counts
  are **per container** (a method may bail in more than one container), which is
  why the totals differ from DiffNext's per-method `candidate-worse`. Honors
  `--cap` to bound a partial sweep.

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

## Prior art: the closest existing structurer

This track lands work the .NET tools community has built before, so it is worth
naming whose design we are converging on. We surveyed the production decompilers
and the academic structuring literature against three axes: (1) dominance/
post-dominance to find joins, (2) retain gotos vs. goto-free output, (3)
dominance-driven vs. collapsing-graph pattern matching.

**ILSpy (`icsharpcode/ILSpy`, `ICSharpCode.Decompiler/IL/ControlFlow/`) is the
closest match — and the most relevant reference for dotnet collaborators.** It
shares our philosophy almost exactly:

- **Dominance-driven.** It builds a Cooper–Harvey–Kennedy dominator tree
  (`FlowAnalysis/Dominance.cs`) — the *same* algorithm this repo's
  `DominatorTree.cs` already implements — and processes blocks in dominator-tree
  order.
- **Partial structuring, gotos retained.** `ConditionDetection.cs` leaves a
  `Branch` (goto) in place for any join; it never duplicates code into arms and
  introduces no boolean state flags. This is our Target A.
- **Post-dominance, but only for loops.** `LoopDetection.FindExitPoint` builds a
  reverse CFG and takes the lowest common ancestor in the post-dominator tree to
  pick a loop's single structured exit, falling back to goto for additional
  exits. Conditionals are handled by domination, not explicit post-dominance.

Two mechanism differences are directly useful to our plan:

1. **ILSpy does not compute an immediate post-dominator for conditional joins.**
   `ConditionDetection.CanInline` inlines a successor only when its
   `IncomingEdgeCount == 1` (so it is reached solely from here, i.e. dominated);
   any merge block — which necessarily has `IncomingEdgeCount >= 2` — is left as
   a goto target. The *outcome* is identical to ours (merges become labelled
   gotos), but the test is a predecessor count, not a post-dominator query. Our
   pipeline `Cfg.BlockEdges` already yields successor sets, so predecessor counts
   are a cheap derivation. **This suggests a lower-risk first cut for the
   conditional case: gate on "merge has >1 predecessor" rather than standing up
   the full post-dominator analysis, and reserve post-dominators for when the
   loop subset (out of scope here) is tackled.** Step 1 below should weigh the
   proxy against full post-dominators on this evidence.
2. **ILSpy does not inline return tails by default**
   (`aggressivelyDuplicateReturnBlocks = false`). Our Target-B return-tail
   elimination (step 2) is a deliberate addition beyond ILSpy's default, justified
   by our opcode-exact rail — csc re-lowers the inlined tail to the same IL.

Second-closest is **Ghidra** (`CollapseStructure`): it also retains gotos for
hard cases with no flags or duplication, but via classic collapsing-graph
structural analysis (Sharir/Cifuentes lineage) with no post-dominance. The
opposite pole is **DREAM / "No More Gotos"** (Yakdan et al., NDSS 2015), whose
explicit goal is goto-free output via semantics-preserving condition rewriting —
the inverse of retaining a labelled merge. We are firmly on the ILSpy side of
that line.

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

   *Concrete first-PR shape.* Two pieces of infrastructure already exist to
   build on, and the new code is their dual:

   - `ILInspector.Decompiler.DominatorTree.Build(ControlFlowGraph)` implements
     the Cooper–Harvey–Kennedy iterative dominance algorithm
     (`DominatorTree.cs`) — but over the *legacy* `ControlFlowGraph` the retired
     emitter used, not the pipeline.
   - `ILInspector.Decompiler.Pipeline.Cfg.Build(IReadOnlyList<Block>)` returns
     per-block `BlockEdges(Successors, ExternalTargets, ExitsMethod,
     LeavesRegion)` over the *pipeline* container — the same edge model
     `StructuringPass` and the printer's definite-assignment walk consume, so a
     post-dominator built here can never disagree with them about successors.

   The first PR adds `Pipeline.PostDominators` over `Cfg.BlockEdges`: reverse
   the edge set, add a single virtual exit that all method-exit blocks (and
   `ExternalTargets`/`LeavesRegion` survivors, treated as exits) flow to, and
   run the same CHK fixpoint to get each block's immediate post-dominator. It is
   pure analysis — nothing consumes it yet — gated by unit tests on synthetic
   `Block` containers: a diamond (both arms post-dominated by the merge), a
   nested diamond with a single global merge (the `InternalSetValue` shape, where
   `ipostdom` of every arm is the common exit), an early-return arm (no common
   post-dominator below the split → virtual exit), and an irreducible/multi-exit
   shape (must produce a defined result or signal "no single post-dominator",
   never throw). The acceptance bar is that `ipostdom` of the outer split in the
   `InternalSetValue` fixture is the common-exit block — the join the index-range
   model cannot name today.
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

## Roslyn signals: where the shape comes from, and where C# is going

The merge shape we are recovering is not arbitrary IL — it is **Roslyn's lowering
output**, so the compiler source is the authoritative oracle for what we must
raise. Two signals from `dotnet/roslyn` shape this work.

**The common-exit shape is the decision-DAG lowering.** Pattern `switch`
statements, `switch` expressions, and `is`-patterns lower through a
`BoundDecisionDag` (`src/Compilers/CSharp/Portable/BoundTree/BoundDecisionDag.cs`),
emitted by `LocalRewriter.DecisionDagRewriter`
(`.../Lowering/LocalRewriter/LocalRewriter.DecisionDagRewriter.cs`). Each
`BoundDecisionDagNode` is assigned a `LabelSymbol` (`_dagNodeLabels`) and the
lowered form is a **flat sequence of labelled sections joined by `goto label;`**,
with shared `when` sections that multiple arms jump into and a single converging
result. A decision DAG is acyclic and converges on one node — i.e. it is *by
construction* a forward branch graph to a common post-dominator, exactly the
`cond-target-past-region` / `forward-branch-not-region-exit` docket. Recursive
patterns, list patterns, and extended property patterns are **already merged**,
so the corpus we sweep already contains this shape today; it is not a future
concern. This is strong evidence that the post-dominator/return-tail model is
aimed at the right structure: the DAG's convergence node *is* the merge to retain
(Target A) or inline (Target B).

**In-progress language features change the structured-target landscape.** From
the compiler's `docs/Language Feature Status.md`:

- **Labeled `break`/`continue`** (in progress, `features/labeled-break-and-continue`).
  Today a nested-loop early exit must lower to `goto` (the proposal's own
  motivation). Once shipped and present in shipping assemblies, a class of
  retained-goto methods becomes raisable to structured `break Label;`/
  `continue Label;` — a genuine new structured target for part of the
  retained-goto docket. It is the loop subset (out of scope for this merge-focused
  track) and not yet a raise target until csc emits it, but it is the clearest
  sign the language is growing first-class constructs for exactly the
  "jump to a point past the enclosing region" problem this doc is about.
- **Unions** (in progress, `features/Unions`). Type-union dispatch lowers through
  the same decision DAG, producing more common-exit merges — more inventory for
  this pass, same shape.
- **Chained relational comparison** (`a < b < c`, in progress) lowers to
  short-circuit `&&`, the guard-chain shape we already structure cleanly — a
  reminder to confirm the existing `&&`/`||` path still covers it as it appears.

The actionable takeaway: validate the structuring model against
`BoundDecisionDag` lowering specifically (build small pattern-`switch` fixtures
and confirm their decision-DAG IL raises), and treat labeled `break`/`continue`
as the eventual structured target for the loop-exit cousin of this problem rather
than something to emit speculatively now.

## Out of scope

- Loops (`cond-backward-branch`, 61) — a separate loop-raising effort, not
  control-flow merging.
- EH (`unconsumed-regions`, 108) — the EH pass leaving regions flat is a distinct
  gap; the CFG-DA's `Leave` bail (#631) is the related printer-side residue.
- Switch jump tables (`SwitchRaisingPass`) and comparison trees (#640) — done.

## Open questions

- **How much of the 1,475 is fully eliminable (return-tail) vs retained-label?**
  Step 2 answers it empirically: the return-tail subset's recovery count tells
  us how much value lands before the invariant relaxation is needed. The
  `--structuring-bails` diagnostic (landed) makes this a number, not a guess.
- **What is the true structuring-only burndown, separated from the residue?**
  The refreshed `--candidate next` docket (1,217) is dominated by `= default`
  and `pinned` cosmetics, not the merge shape, so the scoreboard alone
  overstates the structuring gap. `--structuring-bails` now sizes the merge
  docket directly (1,475 containers), so this track is measured against its own
  number rather than the inherited tally.
- **Does the oracle's retained-goto shape match what step 4 would emit?** If the
  baseline's label placement differs, recovered methods land in
  `baseline-worse`/`uncertain` rather than `agree` — still a real quality win,
  but the scoreboard reads differently. Worth confirming against a sample before
  step 4.
- **Is partial structuring worth it, or should retained-goto methods stay flat?**
  If step 2 (return-tail) plus steps 1/3 recover most of the value, step 4's
  invariant relaxation may not pay for its risk. Decide after measuring 1–3.
