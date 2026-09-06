# Control-flow structuring: the common-exit redesign

This document scopes the next major investment in the decompiler's control-flow
recovery. It is the design artifact behind the "non-tree forward-conditional"
work: the diagnosis that the remaining real-gap docket (`--gaps`) is dominated by
one structural shape the current `StructuringPass` cannot express, and a plan to
close it without destabilizing the pass.

Read [decompiler.md](../decompiler.md) first for the pipeline
shape and the recognizability goal. This doc governs one pass.

## Where we are

`StructuringPass` raises flat goto-based IR into nested `if`/`else`, loops are
raised by `DoWhileLoopPass`/`ForLoopPass`, EH by `EhStructuringPass`, and switch
jump-tables by `SwitchRaisingPass`. Two recent passes closed bounded gaps:

- **#631** — the printer's definite-assignment walk gained a CFG-based pass so a
  surviving goto no longer floods every local to `= default`.
- **#640** — `ReturnMergePass` + a guard-leaf inlining generalization raise the
  comparison tree csc emits for a sparse `switch`.
- **switch terminator sections** — `SwitchRaisingPass` folds the `default:` label
  onto a shared case section when the default jumps into a case body that
  returns/throws (`case N: default: throw;`). Recovers the `Enum::GetNamesNoCopy`
  family — every out-of-range table index and the default share one terminator
  block. +16 fully-raised; 11 `System.Enum` methods went malformed → valid.

What remains is not bounded. The stop-reason histogram below is produced by
`decompiler-harness --structuring-stops` (landed as the first PR of this track),
which attaches a `StructuringDiagnostics` sink to the pipeline and tallies, per
block container, why `StructuringPass` left it flat. Measured on the running
runtime's `System.Private.CoreLib` (net11 preview.5), across 41,012 methods —
**10,166 containers structured, 1,672 left flat across 1,645 methods**:

| Stop reason | Containers | Shape |
| --- | ---: | --- |
| `cond-target-past-region` | 876 | a conditional branch whose target is past the region |
| `forward-branch-not-region-exit` | 599 | an unconditional forward goto that is not the region exit |
| `unconsumed-regions` (EH) | 108 | a try/catch/finally the EH pass left flat |
| `cond-backward-branch` | 61 | an unraised loop |
| `leave-target-in-container` | 14 | an EH leave survivor keeps its container flat |
| `eh-terminator-survivor` | 14 | a `Leave`/`EndFinally`/`EndFilter` terminator |

> **Denominator warning (2026-08-12).** `cond-backward-branch` is *not* the
> loop population — it is a first-bail-only lower bound, off by **6.4×
> (CoreLib) to 14× (corpus)**. `--structuring-stops` records one reason per
> container, and loop-carrying containers overwhelmingly bail earlier on
> `cond-target-past-region` / `forward-branch-not-region-exit` (the rotated
> entry or the loop body's merge is hit before the back-edge). Size loop work
> from `--gaps --by-shape` (`loop-residue`) or the postdom probe's `Loop`
> bucket; see the measured sizing in
> [#4063](https://github.com/richlander/dotnet-inspect/issues/4063).

The top two — **1,475 of 1,672** — are the same shape: **a forward branch to a
common merge/exit that lies past the region**. Representative methods:
`System.Array::InternalSetValue`, `System.Array::LastIndexOf`,
`System.Array::Reverse`, `Interop.OSReleaseFile::GetPrettyName`,
`System.Array::CopyImpl`. This is the gap this doc addresses.

> **Status.** `--gaps` reads ~96% fully raised over CoreLib; the residual is
> dominated by `structuring: conditional-branch` — this gap.
> (2026-08-12: `System.Array::InternalSetValue` and `CopyImpl` now fully
> structure on `main` — the shared-terminator slices consumed this doc's
> canonical trace. Judge the shape against the current single-merge exemplars
> listed in the trigger outcome below.) The forward-branch-to-
> common-exit shape is sized directly from `--gaps`'s residual-kind docket and,
> per container, from `StructuringPass`'s own stop reasons (see *Reproducing the
> measurement*) — not from raw text diffing, which conflates structuring with
> definite-assignment cosmetics (`int V_0;` vs `int V_0 = default;`) and pinned/
> `ref` residue.

### Reproducing the measurement

The numbers above come from two harness lenses; both must agree before and
after every step of this plan.

- **The completeness view** — `decompiler-harness --gaps` inspects the raised tree
  alone and reports "fully raised" plus a residual-kind docket. A method with a
  surviving `structuring: conditional-branch` residual is one this plan targets.
  This is the residual-completeness number (~96% fully raised), and because it reads only the
  residual control flow it isolates the structuring gap from the `= default` /
  `pinned` cosmetics that text diffing would conflate.
- **The per-method view** — `decompiler-harness --dump 'Ns.Type::Method'` prints
  every stage; the final stage is `PrintRaised`. Use it to confirm a method
  exhibits (or, after a fix, no longer exhibits) the common-exit `goto`.
- **The stop-reason histogram** — `decompiler-harness --structuring-stops`
  attaches a `StructuringDiagnostics` sink to the Default pipeline and tallies,
  per block container, the `StructuringPass` stop reason (or that it structured).
  This is the merge-docket lens: the `cond-target-past-region` /
  `forward-branch-not-region-exit` table above is reproducible on demand, so
  every subsequent step of this plan can show the bucket shrinking. The counts
  are **per container** (a method may bail in more than one container), which is
  why the totals differ from `--gaps`'s per-method residual count. Honors
  `--cap` to bound a partial sweep.
- **The bucket shape histogram** — `decompiler-harness --gaps --by-shape`
  sub-classifies the `structuring: conditional-branch` bucket itself
  (`ConditionalBranchShapeClassifier`) by the shape around each method's first
  residual guard: `loop-residue` (an unraised back-edge), `eh-entangled` (a
  surviving EH terminator), `comparison-tree` (a sparse-switch if-tree),
  `shared-forward-merge` (a join reached by two or more branches), and
  `nonnested-forward-guards` (interleaved single-predecessor guards — the
  is-pattern / type-test dispatch). Unlike `--structuring-stops` this counts
  **per method in the bucket**, so it scopes which slice clears the most methods.

Two shapes that are **already handled** and are out of scope: ordinary
short-circuit `&&`/`||` guard chains (they nest cleanly today — the
`TripleAnd`/`IfAnd`/`OrChainGuardPass` fixtures round-trip), and the comparison
tree (#640). Temp-backed shared-return chains are the caveat: when multiple
guards branch to one shared return while another guard/default return remains
distinct, duplicating the shared arm can break compile-back opcode fidelity, so
return-dispatch must decline that mixed shared-target shape.

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
dominator-driven structurer make. The structuring pass is range-driven today; the
work this doc plans is to make it dominator-driven.

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
   by our opcode-exact check — csc re-lowers the inlined tail to the same IL.

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
what `ReturnMergePass` does — for any common-exit return tail since step 2, not
only comparison trees); for a merge with a
substantial shared tail it bloats the output and diverges further from the
oracle.

### Recommendation: post-dominator joins, exit kept as a goto (A), return-tail merges eliminated (B)

- Compute post-dominators for the container and let the diamond/guard recursion
  use the **immediate post-dominator** as the join, lifting the index-range
  restriction. This is the core change and is shared by both targets.
- When the post-dominator merge is a **short return tail**, eliminate it by
  inlining (`ReturnMergePass`, generalized beyond comparison trees in step 2,
  reusing its gating discipline). This is full structuring for the cheap case.
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

The checks, in order of authority:

1. **`--validity-check` A/B** — a mis-structure usually produces invalid C#
   (CS0165 from a broken declaration, CS0161/unreachable from a dropped path).
   The gate is a byte-for-byte defect-set diff against `main`; zero new invalid
   methods is the bar (#631 and #640 both cleared it).
2. **`FidelityGateTests` / `LoweredFidelityGateTests`** — a structurally
   wrong-but-valid raise is caught by recompiling the fixture and diffing IL
   opcodes. Every selection fixture must stay on its current clean path.
3. **`--gaps`** — the fully-raised count is the metric; it must rise, and the
   recovered methods must lose their `structuring: conditional-branch` residual
   without any method gaining a new residual.
4. **`--pass-impact <pass>`** — the blast-radius view (#641): before shipping,
   list every method the change touches and confirm the set is the intended
   shape, not collateral on methods no fixture covers.

A post-dominator structuring that is unsound on an irreducible or multi-entry
region must **fall back to flat**, never guess — the all-or-nothing safety is
relaxed only for the *recognized* partial shape (a single post-dominator merge),
not abandoned.

## Pre-structuring normalization layer

Several recent raises are not C# syntax sugar in the usual sense; they are
control-flow normalizers that make Roslyn's flat lowering tree-shaped enough for
`StructuringPass` to consume. Treat them as one explicit layer, not as unrelated
mini dataflow engines.

The **pre-structuring normalization layer** runs after import has produced typed
IR and after the earlier structural passes have exposed their owned scaffolds, but
before the final statement-altitude passes assume `if`/loop/try shapes exist. Its
job is to collapse *provably compiler-owned* flat fragments — guard chains,
slot diamonds, shared return tails, dispatch prologues, and retry/continuation
edges — into shapes `StructuringPass` can validate. It is not a second
structurer. If a pass cannot prove ownership, it leaves the residual visible as
`Partial`/flat C# rather than guessing.

### Boundary and current residents

The layer is the band before `StructuringPass` plus the small entry/leave-target
relaxations inside `StructuringPass` that make those normalized shapes legal.
Current residents:

| Family | Consumes | Proof discriminator |
| --- | --- | --- |
| `OrChainGuardPass`, `OrChainDiamondPass` | short-circuit guard/diamond runs | root setup is straight-line; inner guards are pure condition blocks; target is one shared arm/terminator |
| `ReturnMergePass` | shared short return tails | every consumed predecessor is unconditional and no conditional/switch target label is erased |
| `SlotDiamondPass`, `SlotStoreDiamondPass`, `ComparisonTreeBoolArmPass` | slot-return/store diamonds and bool arms | one exact synthetic slot live range; no unrelated slot reuse; all reads before the next write are owned |
| `ReturnDispatchPass`, `PrologueGuardReturnPass` | whole-method return dispatch and EH-method prologue guards | branch target is a short return/throw/guard tail, not a live shared computation block |
| `NullConditionalCoalescePass`, `LambdaCachePass` | compiler scaffolds that must be normalized before inlining/structuring | exact member/generated-code/place identity proves the scaffold and preserves receiver evaluation |
| `StructuringPass` leave/entry relaxations | EH normal continuations and safe retry-loop leaves | region remains intact; no filter/finally relocation; labels survive unless the leave is represented by structured C# |

Passes outside this layer can still run near it in the list, but they should not
invent new branch ownership, liveness, or EH legality models. If a later raise
needs those facts, promote the fact to shared substrate first.

### Allowed rewrites

A pre-structuring normalizer may:

- remove a branch, store, or block only when every predecessor/use it consumes is
  inside the recognized shape;
- replace a flat branch run with a tree node (`IfStatement`, `WhileLoop`, `Break`,
  `Continue`) only when the resulting C# preserves the original control transfer
  and the fidelity level remains honest;
- duplicate only short, self-contained terminators (`return`/`throw`) whose
  duplication cannot reorder side effects or erase a required label;
- fold a synthetic slot only across the live range the pass proves, not across a
  same-numbered slot elsewhere in the method;
- consume `Leave` only when the C# construct still executes the same `finally`
  path, or when the leave exits to the recovered region's normal continuation.

It must not:

- move a block into or out of a `try`, `catch`, `filter`, or `finally`;
- delete a label targeted by an unconsumed `Branch`, `SwitchBranch`, or `Leave`;
- infer source syntax from a name, slot number, or member name without an exact
  identity predicate;
- introduce a hidden boolean or temp to make output prettier unless a later
  fidelity check proves it is opcode-exact or the output is explicitly degraded.

### Proof obligations

Every pass in this layer carries these proof obligations in code review:

1. **Ownership.** Name the complete shape: entry block, consumed blocks/stores,
   target blocks, and the single point where control resumes. No external entry
   may enter a consumed block.
2. **Label survival.** Before deleting a block or target, prove no surviving
   branch/switch/leave still needs its label. Otherwise leave the block flat or
   clone a short terminator instead of moving it.
3. **EH legality.** Prove the rewrite does not change protected-region
   membership, handler scope, filter execution, or `finally` count. When in
   doubt, decline and keep the `leave` visible.
4. **Slot/place identity.** Use `PlaceIdentity`, `SameStackSlot`, or a shared
   liveness helper. A slot number alone is not a method-wide identity claim.
5. **Evaluation order.** Root setup may stay before a folded guard only when it
   already ran unconditionally. Inner guards must not hoist side effects across
   short-circuit boundaries.
6. **Honest fallback.** A rejected shape must remain visible: residual control
   flow, `UnsupportedNode`, or lowered fidelity. No success-shaped no-op fallback.

`StructuringPass` checks loop-transfer ownership against the candidate tree it
actually built, not against a separate simulation of that build. It builds from
cloned blocks, verifies that existing `break`/`continue`/retry-`leave` transfers
retain their enclosing owner and that every internal surviving `Leave` retains
a label owner. It also rejects an actual retained-region build that places
another arm after a terminal retained-merge branch. It then installs that
candidate transactionally or declines. Structuring rewrite steps are recorded
only after every candidate check that can decline, and success counters
increment only after the replacement is installed. Decline diagnostics remain
before the rewrite step and installation.
`StructuringAuditCommitPointTests.StructuringAuditCommitsAfterEveryDeclineAndInstallation`
is the non-vacuity gate for that call ordering;
`RetainedAuditRecordsOnlyInstalledReplacement` covers the successful and
declined outcomes. The compiler-backed and synthetic owner/decline boundaries
are gated by `InfiniteLoopStructuringTests` and
`StructuringGotoScopeTests`.

Cloned statements retain `SourceOffset` as provenance, but semantic clones do
not own that offset's printable label. Only the canonical surviving statement
may render the label; `StructuringGotoScopeTests` gates the distinction so a
clone cannot strand a later `goto` outside the clone's C# scope.
When a later raise replaces a statement, the replacement inherits both its
canonical or suppressed label-ownership state; `NullCoalescingAssignmentPassTests`
gates both boundaries.

### Shared helpers and facts

Prefer shared helpers over pass-local folklore:

- `Cfg.BlockEdges` and `PostDominators` for executable per-container successors
  and dominance; `StructuringFlowFacts` for the structurer's region-aware label
  preservation and clone ownership, including nested `leave` targets; and
  `StructuringDiagnostics` for measured stop reasons.
- `PlaceIdentity` for re-evaluable local/argument/slot/operand identity.
- `MemberIdentity` and `GeneratedCodeIdentity` for compiler/BCL scaffold proof.
- `StackSlotLiveRangePass` and any future liveness helper for reused synthetic
  stack slots.
- Sidecar ledger facts for pass coverage, missing discriminators, and adversarial
  coverage once a row becomes a recurring frontier.

The convergence rule from the decompiler substrate doc applies here: the third
copy of a predicate builds the shared layer. A new pass may carry a local helper
only when the proof is genuinely pass-local; otherwise reviewers should ask for a
substrate atom or a control-flow helper first.

`ControlFlowModelDifferentialTests.ControlFlowViews_AgreeOverCoreLib` gates the
overlap between those models. It runs the real pipeline to the pre-switch
boundary, compares `StructuringFlowFacts` explicit transfers with `Cfg.Build`,
and compares switch raising's internal successor view with `Cfg.Build` wherever
switch raising accepts the block. An independent terminator projection checks
`Cfg.Build`'s implicit fall-through multiplicity and method/region-exit
classification, including switch default edges, `EndFinally`, and successor
range validity. CoreLib contains no `EndFilter` at this boundary, so a synthetic
case owns that classification instead of letting a combined EH counter hide the
empty corpus domain. The gate separately requires terminal `Leave` coverage,
pinning the intentional distinction: `Cfg.Build` marks a terminal `Leave` as a
region exit with no successor, switch raising declines it, and structuring does
not classify it as a jump predecessor. Nested `Leave` clone ownership has no
executable-edge overlap; `StructuringFlowFactsTests` owns that separate contract.

The pre-switch corpus also contains 12 blocks whose direct `Break` leaves the
current block container and zero direct `Continue` blocks. Neither transfer has
a lexical fall-through successor in that container, and `Cfg.Build` represents
both as edge-free structured exits. Switch raising still declines `Break`
because wrapping it in a switch would capture its enclosing-loop owner. A
terminal `Continue` cannot be captured by the new switch, so switch raising
accepts it as a terminating section and now agrees with `Cfg.Build`'s empty
successor set. Another 45 blocks
contain a conditional arm ending in `Break`; their other path has a real
in-container fall-through edge, so the successor view continues to model that
edge. A synthetic case where such an arm precedes `EndFinally` pins that the
independent unsupported terminator declines the block instead.

`ControlFlowViews_AgreeOnSyntheticBoundaryTerminators` owns these boundaries in
the fast `Area=Pass` lane, including a terminal `Continue`, a non-final transfer,
an explicitly branched lexical-next edge, and a transfer owned by a nested loop.
It also asserts that the default pipeline still contains exactly one
`SwitchRaisingPass` anchor before constructing the pre-switch slice. This is the
measured prerequisite for consolidation: `Cfg.Build` must first represent or
reject direct structured transfers before switch raising can consume it
wholesale.

The compiler-produced loop/switch/`continue` witness is an output canary, not a
corpus instance of that pre-switch boundary. Both before and after the original
differential change, the default pipeline raises the switch, moves the
post-switch statement into `default`, and renders the terminating cases with
`break`; no `Continue` exists before switch raising because structuring runs
later. The terminal-`Continue` acceptance is therefore unreachable in today's
single default-pipeline pass and has zero corpus coverage; it is a supported
boundary for an already-structured input, pinned directly rather than presented
as measured default behavior. `SwitchRaisingTerminalContinuationTests` pins the
compiler-produced no-movement result, an already-structured loop where terminal
`Continue` nodes remain `continue` after switch wrapping, a mixed
continue/joining switch, and the close negative where a nested loop-owned
`Break` still forces the wrapping attempt to decline.

On the .NET 11 Preview 7 CoreLib, the gate covers 42,640 methods, 45,505
containers, 48,559 resolved explicit edges, zero external explicit edges,
53,367 implicit fall-through edges (including 342 switch default edges),
122,969 switch-modeled blocks, 113 terminal `Leave`s, 10 `EndFinally`
terminators, zero `EndFilter` terminators, 12 direct `Break` blocks, zero direct
`Continue` blocks, and 45 nested structured-transfer blocks with zero
differences over the supported overlap. That evidence makes `Cfg.Build` the
owner of flat structural edge semantics and direct structured-transfer
fall-through semantics. Switch raising has a narrower acceptance domain rather
than different edges: it declines a direct `Break` that a new switch would
capture and accepts terminal `Continue` with the same empty successor set.
`StructuringFlowFacts` remains a separate region-aware projection because its
label and clone-ownership facts are not executable edges.

### Review triggers and canaries

Run a **Stepper Semantic Auditor** pass when a change:

- removes or clones blocks, stores, return tails, or branch targets;
- consumes `Leave`, `EndFilter`, or `EndFinally`, or touches a recovered EH body;
- changes slot/stack-slot ownership or broadens `SameStackSlot`/place identity;
- rewrites byref, pinned, unsafe, volatile, constrained-call, or ref-return
  shapes;
- changes a pass's position relative to `StructuringPass`, `ExpressionInlining`,
  or `StackSlotLiveRangePass`.

Run `--pass-impact <pass>` for any matcher broadening, and compare
`--validity-check` / `--fidelity-check` (or the fixture fidelity gates) whenever
the change can make `Full` output more structured. Always re-run
`--gaps --by-shape` for row-reduction claims.

Known canaries:

- short-circuit `&&`/`||` chains (`IfAnd`, `TripleAnd`, `MixedAndOr`);
- shared return tails (`GotoCommonExit`, `ByteRangeSearchTree`,
  `SlotDiamondDispatch`);
- EH leave/retry and filter shapes (`GetCwd`, `TrySteal`, filter fixtures);
- bool slot materialization and slot reuse (`SelectBoolReturn`, stack-slot reuse
  tests);
- byref/pinned/unsafe shapes (`SpanElementCompoundAdd`, fixed/pinned fixtures);
- lambda-cache and generated-code identity fixtures.

### New pass checklist

Before adding another pre-structuring pass, answer this checklist in the PR:

- Which compiler lowering shape does it own, and what exact discriminator proves
  that shape?
- Which blocks/stores/targets can it delete, and how are external entries ruled
  out?
- Which shared helper supplies CFG, liveness, place identity, member identity, or
  generated-code identity?
- What happens at EH boundaries, and which illegal transform fixture would fail
  if the guard were too broad?
- Which existing canary could regress, and which pass-impact / fidelity /
  validity command was run after the upstream sync?
- How does a declined shape remain visible and measurable?

## Incremental plan

Each step is its own PR, measured against the checks above. Steps are ordered so
the cheap, fully-eliminable cases land first and the invariant relaxation comes
only when the post-dominator machinery is proven.

1. **Post-dominator computation.** Add a tested `PostDominators.Of(container)`
   (forward must-analysis over the block CFG — shaped like the CFG dataflow #631
   already introduced). No behavior change; pure analysis with unit tests on
   synthetic CFGs.

   *Status: landed.* `Pipeline.PostDominators` (`PostDominators.Of`) computes
   immediate post-dominators over `Cfg.BlockEdges` via the Cooper–Harvey–Kennedy
   fixpoint on the reverse CFG, with a single virtual exit that every method exit,
   external target, and EH survivor flows to. `ImmediatePostDominator` returns a
   block index, `VirtualExit`, or `None` (a block that cannot reach the exit —
   never throws); `PostDominates(p, b)` walks the ipostdom chain. Gated by
   `PostDominatorsTests` on the four synthetic shapes below; nothing consumes it
   yet.

   *Concrete first-PR shape.* The infrastructure to build on already exists, and
   the new code is its dual:
   `ILInspector.Decompiler.Pipeline.Cfg.Build(IReadOnlyList<Block>)` returns
   per-block `BlockEdges(Successors, ExternalTargets, ExitsMethod, LeavesRegion)`
   over the pipeline container. `PostDominators`, `Dominators`, the printer's
   definite-assignment walk, and other structural-CFG consumers share that
   executable edge model. `StructuringPass` does not currently consume it: the
   pass uses `StructuringFlowFacts`, a separate region-aware projection that
   also preserves labels and clone ownership for transfers nested inside
   already-raised regions. Any future post-dominator-driven structurer must
   reconcile those additional ownership requirements explicitly.

   The first PR adds `Pipeline.PostDominators` over `Cfg.BlockEdges`: reverse
   the edge set, add a single virtual exit that all method-exit blocks (and
   `ExternalTargets`/`LeavesRegion` survivors, treated as exits) flow to, and
   run the Cooper–Harvey–Kennedy iterative fixpoint to get each block's immediate
   post-dominator. It is
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
   structures the cheap case; no invariant change. Measure the `--gaps` drop
   and the blast radius.
   *Status: landed.* The `ComparisonTrees.IsLikely` gate is gone; the fold now
   fires on any short return tail reached by two or more unconditional
   predecessors (the two guards make the tail the immediate post-dominator of
   its arms, so duplicating it reorders nothing). `--gaps` fully-raised rose
   +48 (39,347 → 39,395); the validity check defect set is byte-for-byte
   unchanged vs main (0 regressed, 0 new invalid); `--pass-impact return-merge`
   covers 261 methods with 0 pass bugs. The plain forward common-exit
   (`GotoCommonExit`) now folds to nested guards.
3. **Post-dominator joins in the diamond/guard recursion.** Let `Validate`/
   `BuildRegion` accept a forward branch to the region's immediate
   post-dominator as the join, lifting the `target > stop` bail for that one
   target. Still all-or-nothing; recovers the cases where the merge is reachable
   by fallthrough after nesting.

   *Status: merge-exit slice landed.* `Validate`/`BuildRegion` now accept a
   conditional branch whose target is the region's tracked join (`joinIndex`)
   even when `joinIndex > stop` — a diamond false arm that early-exits straight
   to the merge. Because `joinIndex >= stop` always holds, this can never
   intercept a `target < stop` case, so it cannot reshape any container that
   already structured (zero blast radius). Result: `--gaps` fully raised
   39,395 → 39,418 (+23); `cond-target-past-region` 879 → 851; defect diff vs
   the step-2 baseline 0 regressed, 2 methods improved (malformed → valid);
   `--pass-impact structuring` 0 pass bugs; 317 decompiler tests green
   (`DiamondArmEarlyExitGuardedMerge` fixture pins the recovery — it bails
   without the slice, structures with it).

   *Finding — the residual is two distinct mechanisms, not more post-dominator
   joins.* The remaining `cond-target-past-region` (851) splits by block count:
   198 ≤6 blocks, 224 of 7–12, 423 of >12. The small/medium bulk is the
   `||`/`&&`-guard-chain-ending-in-throw shape (`if (A) goto THROW; if (B) goto
   MERGE; THROW: throw; MERGE: return`, e.g. `System.Range::GetOffsetAndLength`,
   `System.Enum::ValidateRuntimeType`). Post-dominators are **degenerate** here:
   because `throw` is a parallel method exit, every block's immediate
   post-dominator collapses to the virtual exit, so the CFG cannot name the
   success-MERGE as a join. These need a distinct boolean-combining mechanism
   (combine guards across a shared throw terminator into `if (A || !B) throw;`),
   closer to the existing `&&`/`||` guard handling than to post-dominator joins.
   The >12-block tail is the large shared-merge DAG that step 4 (retained merge
   label) targets. So the "full post-dominator join" idea recovers exactly the
   merge-exit slice above; the bulk is deferred to those two follow-ups.

   *Status: throw-guard combine landed.* The `||`/`&&`-guard-to-throw bulk is
   recovered by extending `OrChainGuardPass` rather than the structurer:
   `csc` lowers `if (A || B) Throw();` to a short-circuit guard run where the
   first guard block also carries the method's unconditional prologue (the code
   computing the operands) ahead of its branch. The original pass required
   *every* guard, including the root, to be a pure single-condition block, so
   any method whose prologue shared the first guard's block (e.g.
   `System.Range::GetOffsetAndLength`) was rejected and stayed flat. The pass
   now accepts a root that ends in its guard conditional but carries leading
   straight-line statements (only the root — inner guards run conditionally, so
   their statements cannot be hoisted out of short-circuit order), keeping that
   prologue in the folded block. Result: `--gaps` fully raised 39,418 → 39,772
   (+354); `structuring: conditional-branch` 1,545 → 1,191; defect diff vs the
   step-3 baseline 0 regressed, 5 methods improved (malformed → valid);
   `--pass-impact or-chain-guard` 1,177 methods, 0 pass bugs; 318 decompiler
   tests green (`OrChainGuard_RootCarriesSetupStatements` pins it — fails
   without the change). `Range::GetOffsetAndLength` now raises to the exact
   source `if ((uint)end > (uint)length || (uint)start > (uint)end)
   ThrowArgumentOutOfRangeException();`.
4. **Partial structuring with a retained merge label (the invariant relaxation).**
   Allow a structured container to keep one labelled post-dominator block that
   the arms `goto`, matching the oracle for tails that cannot be eliminated.
   This is the largest and riskiest step; it lands last, behind the proven
   machinery and the full check suite.

   *Scope addition (2026-08-12, from
   [#4063](https://github.com/richlander/dotnet-inspect/issues/4063)):* the
   join plan represents and classifies back-edge regions from day one. The
   first retained-label production slice consumes only proven acyclic forward
   regions; raising a non-crossing region whose back-edges target its head as
   `while`/`while (true)` remains a follow-on, together with continue placement,
   condition hoisting for effectful latches, labeled break, and conditional
   rotated entries.

   *Status: shared-terminator merge slice landed (partial).* The first slice of
   step 4 relaxes the all-or-nothing invariant for one safe case: a shared
   **terminator** (a short `throw`/`return` block) reached past its region can be
   duplicated into the guards that branch to it, since `IsTerminatorBlock`
   guarantees no control flow to preserve, so duplication is always
   semantics-preserving. `IsSharedTerminator`'s `throw` arm now also fires when a
   throw block has **one** conditional predecessor *and is fallen into* — the
   `if (A || B) throw;` shape whose inner guard carries a prologue, which
   `OrChainGuardPass` (needs pure inner guards) cannot flatten (e.g.
   `DateTime`'s tick-range checks). Result: `--gaps` fully raised 39,772 →
   39,775 (+3); defect diff vs the step-3 baseline 0 regressed, 2 methods
   improved (`ArgumentOutOfRangeException::ThrowEqual`/`ThrowNotEqual`);
   `--pass-impact structuring` 0 pass bugs; 319 decompiler tests green
   (`SharedThrow_SingleGuardFallenInto_InlinesAsGuardClauses` pins it — fails
   without the change). `IsSharedTerminator` also now excludes any terminator
   carrying a base/this constructor-chain call (it must stay the body's first
   statement for the `: base(...)` lift; duplicating it would strand a bare chain
   call, CS0175).

   *Status: first retained-label production slice implemented.* The existing
   whole-container all-or-nothing path still runs first. Only after it declines,
   the product-owned join plan selects the furthest valid merge for the current
   range, proves single entry and a merge reached by at least two explicit
   transfers, and plans sequential tail ranges before mutation. The slice
   retains unconditional and conditional gotos to the merge, preserves each
   merge label when the tail is structured, and declines crossing, unrooted,
   EH/switch, external-entry, and back-edge-entangled regions. Unrelated
   back-edge regions in untouched tail ranges do not veto an eligible forward
   region; unresolved loop regions remain represented but flat.

   *Status: first canonical retained-loop slice implemented (2026-08-17).*
   After whole-container structuring declines, the retained planner may widen
   one range to a rotated-entry `while` only when the existing loop recognizer's
   proof is complete: one preheader branch to one conditional latch, one
   back-edge source and head, a non-crossing region, and the real
   post-dominator equal to the block immediately after the latch. The range
   rejects overlapping loops, external entries, EH, switches, and nested
   transfers. Every forward merge carried by the loop must independently prove
   that ordinary validation fails and retained-merge validation succeeds; all
   such merges are then validated and built transactionally with the loop.
   Retained gotos remain gotos inside the new loop. A retained conditional
   transfer with imported control-flow provenance remains a
   `ConditionalBranch` rather than becoming an `IfStatement` with a synthesized
   `Branch`; a synthesized conditional transfer has no imported identity to
   preserve and declines the loop. This prevents partial structuring from
   creating a new output-only transfer identity and is enforced by
   `RetainedLoopConditionalMergePreservesTransferKind` and
   `RetainedLoopSynthesizedConditionalMergeStaysFlat`;
   `BranchingMethod_MarksConditionalBranchesImported` gates the importer
   provenance that distinguishes the two. When that conditional is also the
   first statement of a targeted block, a separate `LabelAnchor` owns the block
   label rather than replacing the conditional's instruction provenance;
   `RetainedLoopImportedConditionalTargetKeepsInstructionProvenanceAndBlockLabel`
   gates both identities. The existing
   generated-transfer ownership check guards `break`/`continue` binding. A
   post-build proof also requires each retained target label to remain in the
   same lexical C# block as its goto or an enclosing block; a label nested below
   or beside its goto declines the whole transaction. Accepted retained merges
   receive a dedicated empty label anchor, so later expression/sugar passes
   cannot consume the owner and printer-synthesized `unsafe` scopes begin after
   the target. Downstream `fixed` recovery also declines before moving an
   externally targeted anchor into the fixed body or removing a targeted pin,
   guard, unpin, or folded-derive label. The array, string, and
   managed-reference fixed forms share that scope proof.

   Nested diamond recovery additionally proves that no surviving transfer from
   either arm enters the sibling arm before treating them as `if`/`else`.
   Direct conditional transfers whose terminator or short past-region target is
   provably cloned into the first arm are exempt because they dissolve rather
   than enter the sibling, but only when the clone has no surviving nested
   transfer back into that sibling range. A clone whose target is the sibling
   head is accepted only when build consumes that proof at the exact recursive
   stop; otherwise the transfer stays explicit. The same proof gates ordinary
   and region-exit diamonds. This preserves the non-low-surrogate fallback in
   `OrdinalCasing.ToUpperOrdinal` and `ToLowerOrdinal`, whose third predecessor
   is valid C# fallthrough rather than an `else` arm, without flattening
   compiler-lowered two-case return switches. Retained loops reject
   already-raised `Switch` nodes and nested `return`/`throw` exits as well as raw
   switch/EH transfers, because the block-level post-dominator model does not
   represent those descendant exits.

   Leave-retry loops cache their proven head classification and past-region
   terminator clone before building mutates the source blocks. This prevents a
   detached guard from making a validated return edge disappear while the loop
   body is materialized.

   `CanonicalWhileWithRetainedBodyMergeRaises` and
   `CoreLibUrlDecodeWithRetainedBodyMergeRaises` gate the accepted synthetic
   multi-merge and compiler-produced paths.
   `RetainedBodyMergeWithoutRotatedEntryStaysFlat`,
   `RetainedBodyMergeWithMultipleLatchesStaysFlat`,
   `RetainedBodyMergeAtLatchStaysFlat`,
   `RetainedBodyMergeWithNoncanonicalExitStaysFlat`,
   `RetainedBodyMergeWithExternalInteriorEntryStaysFlat`, and
   `RetainedBodyMergeWithSwitchStaysFlat` gate the declined boundary;
   `RetainedBodyMergeWithStructuredSwitchStaysFlat` and
   `RetainedBodyMergeWithStructuredMethodExitStaysFlat` gate the descendant
   switch/exit boundary;
   `RetainedBodyMergeWithCrossArmPredecessorPreservesJoin` and
   `CoreLibOrdinalCasingCrossArmPredecessorPreservesFallbackPath` gate the
   cross-arm predecessor proof;
   `RegionExitDiamondWithCrossArmTransferStaysFlat` gates its region-exit
   sibling, while
   `ClonedSiblingTargetWithNestedSiblingTransferStaysOutOfDiamond` proves a
   cloned target cannot carry a nested goto back into the sibling scope, and
   `ReverseNestedSiblingTransferStaysOutOfDiamond` proves the reverse sibling
   direction stays flat as well;
   `SiblingHeadPastRegionCloneIsEmittedAtConsumingDepth` proves the sibling-head
   exemption emits the clone instead of dropping that path;
   `CompilerTwoCaseSwitchReturnKeepsDissolvingCrossArmStructured` proves a
   cloned transfer is not over-rejected;
   `LeaveRetryPastRegionExitUsesValidatedCloneAfterHeadDetaches` proves a
   validated leave-retry exit survives build-time detachment;
   `RetainedBodyMergeWithEmptyLandingPadStaysFlat`,
   `RetainedBodyMergeNestedBelowItsGotoStaysFlat`, and
   `RetainedBodyMergeNestedExpressionCannotVouchForHiddenLabel` prove every
   surviving goto still has a printable, lexically visible target label and
   that nested expression provenance cannot substitute for statement-level
   label ownership;
   `RetainedBodyMergeLabelStaysOutsideSynthesizedUnsafeScope` and
   `RetainedBodyMergeLabelSurvivesDownstreamInlining` prove that owner survives
   final emission. `ArrayPin_ExternallyTargetedBodyLabel_StaysLowered`,
   `ManagedReferencePin_ExternallyTargetedBodyLabel_StaysLowered`, and
   `Rung6StringPinningExternallyTargetedBodyLabelStaysLowered` gate that all
   fixed-statement forms preserve the same scope;
   `ArrayPin_TargetedUnpinLabel_StaysLowered`,
   `ManagedReferencePin_TargetedUnpinLabel_StaysLowered`, and
   `Rung6StringPinningTargetedUnpinLabelStaysLowered` gate removed scaffold
   labels. Against
   exact base `6f4d8f73f`, the slice moved CoreLib from 11,786 to 11,793
   structured containers (752 to 745 flat) and the pinned corpus from 30,046
   to 30,068 (2,677 to 2,655 flat), with zero pass bugs in both
   `--structuring-stops` sweeps.

   *Finding — the acyclic residual splits three ways, and the return-tail merge
   is NOT a terminator-duplication win.* Of the ~1,208 `conditional-branch`
   residuals, **691 contain loop back-edges** (recorded here as out of scope
   under the pre-#4063 framing; superseded — non-crossing back-edge regions
   whose exits reach one post-dominator are now day-one step-4 scope, see the
   corrected loop entry under *Out of scope*); the
   ~517 acyclic split into (a) shared **terminator** merges — `throw` (recovered
   above) and `return`; and (b) shared **non-terminator** merges (e.g.
   `HashCode::Combine`, where the merge is a computation block with a successor)
   that genuinely need the retained-merge-label relaxation and cannot be
   recovered by duplication. The shared **return**-tail merge looked like a quick
   +67, but a `return` block with ≥2 conditional predecessors is *also exactly*
   the false-exit of an `&&`/`||` short-circuit guard chain (`if (a > 0 && b > 0)
   return X; return Y;` lowers to two conditionals both targeting `return Y`).
   Duplicating that shared return into each guard splits the chain before it can
   combine into a single `if (a && b)`, changing the recompiled opcodes — the
   #640 fidelity canary (`IfAnd`/`MixedAndOr`/`TripleAnd`). So the genuine
   return-tail merge (e.g. `String::Trim`) is deferred to a follow-up that first
   teaches the guard combiner to defer to it; it is **not** a terminator-rule
   win.

   *Finding — the `range-search-tree` slice (#921) is blocked by this same
   deferred return-tail merge.* The `--gaps --by-shape` `comparison-tree` bucket
   splits (per a corpus sweep of the 8 product libraries) into 28
   `switch-comparison-hybrid` (a residual jump-table `SwitchBranch` alongside the
   comparisons — switch raising, not this track), 21 `range-search-tree` (a
   relational `if (x > c)` binary search over clustered cases, e.g.
   `HttpClientFactory::IsNonPublic`), and ~0 genuine flat equality cascades (those
   already raise). The #1081 audit pinned `HttpClientFactory::IsNonPublic` on
   `origin/main` (`0d14d8e`) as Full fidelity with one shared false return block
   reached by six guarded range-test predecessors; `CfgSampleClass.ByteRangeSearchTree`
   is the local fixture for that exact residual. A clustered switch whose every arm
   is a straight-line `return <const>` **already raises today** (the dispatch is a
   clean nested diamond once the leaves inline); the same dispatch with guarded
   range arms — `100 => b[1] is >= 64 and <= 127, …` — stays flat. csc lowers each
   range arm to conditionals that converge with the other arms on one shared
   `return false` tail. That shared return tail is exactly the deferred
   return-tail merge above. The deadlock is structural: the boolean/range fold
   that would turn each arm into a straight-line terminator runs **after**
   structuring and matches tree nodes, while structuring is all-or-nothing and
   bails on the unfolded arms — so neither fires. Teaching the guard combiner to
   defer to a genuine shared return-tail merge (the `String::Trim` follow-up) is
   therefore the same unlock for the `range-search-tree` slice; it is not separate
   work.

   *Stepper audit — branch-target and switch-dispatch invariants.* The #1011
   branch-target/switch audit stepped `Interop::GetExceptionForIoErrno` and
   `IlProjection::OperandLength` with `--steps --diff --cfg --facts --remarks`.
   No illegal intermediate rewrite was found. The pass contract is: a rewrite may
   erase a branch target only when the target is either inside the same validated
   reducible region or copied as a self-contained terminator; otherwise the target
   label/tail must survive to the final render. `ReturnMergePass` satisfies this
   by consuming only unconditional predecessors of a short return tail and leaving
   conditional/switch-target labels intact. `StructuringPass` keeps containers
   flat unless validation proves every reached target can be represented, and
   residual `SwitchBranch` rendering must use a single-evaluated temp with
   `if`/`goto` arms so targets outside a C# switch section remain legal labels.
   This invariant is pinned by `SwitchBranchRenderingTests` (including
   `Structuring_PreservesFlattenedSwitchTargetLabels`) and the
   `StructuringDiagnosticsTests` region-exit / past-region case-body fixtures.

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

- Loops — **corrected by measurement
  ([#4063](https://github.com/richlander/dotnet-inspect/issues/4063), 2026-08-12)**:
  the loop residual is not a separate effort. 85% of corpus loop-residue
  containers also hold a shared forward merge; the modal case is a
  `FindWhileShape`-recognizable while killed by all-or-nothing on its body's
  merge. Step 4 without a back-edge rule recovers zero loops; a loop track
  without step 4 recovers only ~15% of the bucket — the tracks multiply. Step
  4's join primitive now serves the first canonical retained-loop subset:
  rotated-entry, single-latch `while` regions with one real immediate exit.
  Retained `while (true)`, continue placement, multi-latch loops, condition
  hoisting for effectful latches, labeled break, and conditional rotated
  entries remain follow-on scope. Loops whose bodies return, throw, or
  otherwise reach the virtual exit, and loops whose exit is the container end,
  also remain flat because this slice requires a real immediate exit
  post-dominator. (The old "`cond-backward-branch`, 61" framing understated
  the population — see the denominator warning above.)
- EH (`unconsumed-regions`, 108) — the EH pass leaving regions flat is a distinct
  gap; the CFG-DA's `Leave` bail (#631) is the related printer-side residue.
- Switch jump tables (`SwitchRaisingPass`) and comparison trees (#640) — done.

## Open questions

- **How much of the 1,475 is fully eliminable (return-tail) vs retained-label?**
  Step 2 answers it empirically: the return-tail subset's recovery count tells
  us how much value lands before the invariant relaxation is needed. The
  `--structuring-stops` diagnostic (landed) makes this a number, not a guess.
- **What is the true structuring-only reduction, separated from the residue?**
  `--gaps`'s residual-kind docket isolates `structuring: conditional-branch`
  from the `= default` / `pinned` cosmetics, and `--structuring-stops` sizes the
  merge docket directly (1,475 containers), so this track is measured against its
  own number, not a conflated text-diff tally.
- **Does step 4's retained-goto shape read cleanly?** When a region cannot fully
  structure and a label survives, the placement of that label and its `goto`
  should still read as deliberate C#, not noise. Confirm against a `--dump`
  sample before step 4 — a recovered method that merely trades one ugly shape for
  another is not a win.
- **Is partial structuring worth it, or should retained-goto methods stay flat?**
  If step 2 (return-tail) plus steps 1/3 recover most of the value, step 4's
  invariant relaxation may not pay for its risk. Decide after measuring 1–3.

## Design spike (#1148): sizing the dominator/retained-label rewrite before starting it

[Issue #1148](https://github.com/richlander/dotnet-inspect/issues/1148) asks for a
sizing-and-evidence spike before promoting step 4 (the retained-merge-label
invariant relaxation) to a major architecture lane. It is the considered response
to recommendation #1 of the
[adversarial architecture review](https://gist.github.com/richlander/f3ed4a639670133b9105b245081b3604),
which argues that finishing the dominator-driven structurer should be the *top
engineering priority* — not chiefly to raise the fully-raised number, but because
"it *shrinks the bespoke diamond/guard pass surface that hurts maintainability and
onboarding*," and that "the longer this migration stays half-done, the more the
normalization layer accretes, and the more each tiny raise PR pays interest on a
debt the redesign would clear." That recommendation is informed by the *pattern
across many reduction phases*, not a single baseline.

The [review of this spike](https://gist.github.com/richlander/a6f12e0ca8c426ee034be29a01b3f7a2)
then made the decisive correction: an earlier draft sized the prize on
`System.Private.CoreLib` alone — the one corpus the review (rec #2) and the issue
both flag as *unrepresentative* (Microsoft-idiomatic Release code, not the
hand-rolled control flow users feed an SDK tool). That draft's "leverage is
shrinking" conclusion outran its evidence. This version re-baselines on a
**real-world corpus** and keeps every leverage claim scoped to its corpus.

Corpora, all on current `main` (`dd510c87`):

| Corpus | Methods | Fully raised | `conditional-branch` | Fwd-merge candidates¹ |
| --- | --- | --- | --- | --- |
| CoreLib (preview.5) | 41,012 | 96.46% | 693 (1.69%) | 618 (1.51%) |
| NuGet² | 67,448 | 89.71% | 1,837 (2.72%) | 1,848 (2.74%) |
| dotnet-inspect itself | 20,083 | 82.68% | 445 (2.22%) | 424 (2.11%) |

¹ `cond-target-past-region` + `forward-branch-not-region-exit` from
`--structuring-stops` (see #1 on the per-method/per-container denominators).
² Real-world NuGet corpus, exact package versions for reproducibility:
Newtonsoft.Json 13.0.4, Microsoft.CodeAnalysis.CSharp 5.0.0,
Microsoft.CodeAnalysis(.Common) 5.0.0, System.CommandLine
3.0.0-preview.5.26302.115, NuGet.Versioning 7.3.0,
Microsoft.ApplicationInsights 2.23.0 (the `lib/` assembly of each).

1. **Residual shape ownership — and the real-world finding.** Two denominators
   are in play and must not be summed: `--gaps` counts **per method** (its
   most-actionable bucket, one per method), while `--structuring-stops` counts
   **per flat container** (a method can hold several). On CoreLib, `--gaps` reads
   96.46% fully raised with **693** methods in `structuring: conditional-branch`;
   `--structuring-stops` separately reports 618 forward-merge *containers*
   (`cond-target-past-region` 317 + `forward-branch-not-region-exit` 301), 77 loop
   containers, 12 EH, 13 switch. These are different tallies of overlapping
   populations, not a partition of 693. The step-4-addressable population was
   defined here as the *acyclic* forward-merge containers; extended 2026-08-12
   by [#4063](https://github.com/richlander/dotnet-inspect/issues/4063) to
   include non-crossing back-edge regions whose exits reach one post-dominator
   (see the corrected loop entry under *Out of scope*).

   The corpus table is the headline. The forward-branch-to-common-exit shape —
   exactly what step 4 targets — is **not** a shrinking CoreLib remainder: it runs
   **1.4–1.8× denser in real-world code** (2.74% on the NuGet corpus, 2.11% on our
   own assemblies, vs 1.51% on CoreLib), while loops stay rare everywhere
   (`cond-backward-branch` 0.01–0.19%). The review's point stands: CoreLib
   *under*-represents the hand-rolled control flow that proliferates in user code,
   so any "the prize is small" claim measured only there is unsound. Sizing the
   shape honestly, it is the dominant structural gap across every corpus measured,
   and more so off CoreLib.

2. **Pass-overlap map (the review's "absorb for free" claim, sized).**
   `--pass-impact` shows the normalizers that compensate for the range-boundary
   model: `or-chain-guard` (330), `slot-diamond` (249), `slot-store-diamond`
   (236), `return-merge` (87), `or-chain-diamond` (45), `return-sinking` (29),
   `comparison-tree-bool-arm` (5), `return-dispatch` (3), `prologue-guard-return`
   (1). The review's strongest argument is that a dominator core would "absorb
   these for free," collapsing the pass surface. Sized concretely, that is only
   partly true. The *value-flow* diamonds (`slot-diamond`, `slot-store-diamond` —
   the two **highest-impact** passes in the list, 485 methods combined) and
   `boolean-folding` move *data* across a join, not control flow; they are
   orthogonal to how the join is *named* and survive a dominator rewrite
   unchanged. Only the **merge-shaped** passes (`return-merge`, `or-chain-guard`,
   `return-dispatch`, `prologue-guard-return`, `or-chain-diamond`,
   `comparison-tree-bool-arm`) are in step 4's territory — and steps 2–3's
   findings already show the two largest of those **cannot** be absorbed by
   post-dominators at all: a degenerate `throw` exit collapses every block's
   ipostdom to the virtual exit (so `or-chain-guard`'s shape has no nameable
   join), and the return-tail merge collides with `&&`/`||` combine (#640). So the
   realistic pass-surface dividend from finishing step 4 is the *small* tail of
   merge passes, not the diamond bulk the maintenance burden actually lives in.
   The review's premise — that the normalization layer is interest paid on a debt
   the redesign clears — over-estimates how much of that layer the redesign can
   retire. Two honest caveats, though: (a) `--pass-impact` measures *methods
   touched*, which is **not** the review's actual concern — pass *fragility and
   churn* ("Relax OR-chain guard entry checks," "Fold split stack-slot store
   diamonds") is a different axis this metric does not capture; and (b) even a
   thin merge-pass dividend is real onboarding surface. So this evidence narrows
   *which* passes a rewrite could retire; it does not refute the maintainability
   motive, and the docs-only recs (#4/#6) make that surface easier to *read*, not
   smaller — symptom relief, not a cure.

3. **Canary risk.** The endangered fidelity canary is **#640**
   (`IfAnd`/`MixedAndOr`/`TripleAnd`): a shared `return` tail with ≥2 conditional
   predecessors is *also* the false-exit of an `&&`/`||` short-circuit chain, so a
   naive retained-merge/duplication splits the chain and changes recompiled
   opcodes. Short-circuit guards, shared return tails, EH leaves, and switch
   targets are the structures a partial-structuring relaxation must not disturb.

4. **Prototype result.** The minimal retained-label path *already shipped* as the
   step-4 shared-**terminator** slice, and it recovered only **+3** fully-raised
   methods on CoreLib — the cheap terminator case is nearly exhausted. The large
   wins came from the *normalizer* route (throw-guard combine +354, step-3
   merge-exit +23, step-2 return-tail +48), all with 0 validity regressions and
   **no invariant change**. Read straight, this is not "step 4 is unneeded": the
   normalizers have cleared the shapes *around* the structural merge, so what
   remains — the genuine non-terminator retained-merge-label case (a merge block
   with a live successor) — *is* the shape only step 4 addresses, and it is still
   **unprototyped**. The cheap cases being exhausted is evidence step 4 is
   *approaching* the only remaining lever, not evidence it is unjustified.

5. **Corpus blast radius.** On CoreLib the normalizer route drove
   `conditional-branch` from the doc's 1,191 → 693 with 0 regressed validity
   defects per slice — genuine, safe progress. But that −498 is a CoreLib number;
   the real-world corpora above show the same shape is denser there, so the
   normalizer treadmill has *not* drained the prize the way the CoreLib trend
   alone implied. "Shrinking" was the wrong word; "displaced onto the structural
   core that step 4 owns" is the accurate one.

6. **Human readability.** Forced retained-goto *can* read as `goto IL_xxxx;` /
   `IL_xxxx:` label soup — but the earlier draft over-stated this by sampling a
   worst-case multi-merge/switch body (`SafeFileHandle::PreOpenConfigurationFromOptions`)
   and comparing it against best-case normalizer output. That is not the redesign
   on trial. The proven reference design (ILSpy `ConditionDetection`, partial
   structuring) emits nested `if`/`else` with a **single** retained merge `goto` —
   the doc's own `InternalSetValue` shape — which reads as deliberate C#, not soup.
   So readability is a genuine open risk for *multi-merge* residuals, but it is
   **not** a proven defect of single-label partial structuring, and must be judged
   against a `--dump` of step 4's real output on single-merge diamonds, not against
   today's flat fallback.

**Recommendation.** Sequence, don't shelve. The corrected evidence does **not**
support "step 4 isn't worth it" — the real-world corpora show the forward-merge
shape is the dominant structural gap and *denser* off CoreLib, and the cheap
normalizer cases are nearly exhausted, so step 4 is approaching the only remaining
lever. What the evidence *does* support is a near-term ordering and a start-trigger:

- **Land the bounded return-tail-aware guard combiner first.** Teaching the
  `&&`/`||` combiner to defer to a genuine shared return-tail merge protects the
  #640 canary and unblocks `range-search-tree` (#921) *without* relaxing the
  all-or-nothing invariant. Low risk, clear value, independent of the rewrite.
- **Then start step 4 — with the corpus baseline in place, not deferred
  indefinitely.** Review rec #2 (a fixed real-world NuGet corpus measured as a CI
  baseline-diff) should be wired up *before* the rewrite lands so its
  fidelity/validity blast radius is caught against the code users actually
  decompile, not CoreLib — it is the regression sensor for the rewrite, not an
  authorization gate on starting it. The NuGet numbers above are the seed of that
  baseline.

**Start-trigger for step 4.** The go/no-go is already decided: the range model
provably cannot name a post-dominator outside its range, the cheap normalizer
route is exhausted (+3 from the last terminator slice), and the shape is the
dominant structural gap on every corpus measured — denser off CoreLib, not
shrinking. So this trigger governs *when to start*, not *whether*; and it is
written to make "last, gated step" a measured event rather than a standing excuse
to re-measure. The corpus baseline (review rec #2) exists to catch regressions
*during* the rewrite, not to re-authorize starting it.

Begin the non-terminator retained-label prototype when both of these hold:

1. **The treadmill has visibly stalled against the structural core** — a normalizer
   PR in the diamond/guard family moves the real-world forward-merge residual by
   **< ~0.1 percentage point**. This is read off the PR slope (accumulated
   experience), not a fresh sweep. *Amended 2026-08-12 (ratified with this
   revision):* the condition is equally met when **no qualifying diamond/guard
   family normalizer PR has merged for three or more weeks** — an order of
   magnitude beyond the family's historical cadence (seven merges in eight
   days in June 2026; see the git log of the guard/diamond/merge passes) —
   while the forward-merge shape remains a dominant residual. The original
   wording could only read deltas from attempts, so a lane no one attempts
   could never fire its own gate: an abandoned treadmill is a stalled
   treadmill, and the gate must be able to say so.
2. **Readability is confirmed on the shape step 4 actually owns** — a `--dump` of a
   single-merge diamond under a throwaway retained-label prototype reads as nested
   `if`/`else` + one labelled merge (the `InternalSetValue` shape), not the
   worst-case multi-merge soup.

Magnitude is *watched, not gated*: the rec-#2 corpus baseline runs continuously, so
the forward-merge density is always current (today ~2.74% on the NuGet corpus, well
above CoreLib's 1.51%). Only a collapse toward zero would reopen the go/no-go — and
mechanism rules that out, so it is a sensor backstop, not a precondition to wait on.

If condition 1 holds but condition 2 fails, the shape stays flat **by policy** (a
stated decision, not an accident) and the lane is closed with that finding recorded.
This keeps step 4 the last and riskiest step while ensuring its start cannot quietly
become "never."

### Outcome (2026-08-12): both conditions met under the amended trigger — the lane opens on merge

The trigger has been evaluated; the full evidence is on
[#1175](https://github.com/richlander/dotnet-inspect/issues/1175).

- **Condition 1 (treadmill stalled): met under the amended trigger.** The
  original per-PR-delta wording cannot be evaluated — no qualifying
  diamond/guard-family normalizer PR exists in the window to measure (the
  last family merge is #3003, 2026-07-22; the normalizer work since is
  switch-family #3986 and value-flow #3949), so the trigger was amended above
  to cover the no-attempt case, calibrated against the family's own
  historical cadence. As of 2026-08-12 the no-attempt window stands at three
  weeks versus June's seven-merges-in-eight-days, and the forward-merge shape
  remains the dominant residual. Supporting observations: the last cheap
  terminator slice yielded **+3** methods (see the step-4 status above), and
  the canonical specimen has been consumed (`InternalSetValue`/`CopyImpl` now
  structure on `main`). Merging this revision ratifies the amendment and the
  outcome together; under the original wording alone, condition 1 would
  instead resolve with the next qualifying PR's measured delta, and condition
  2's evidence stands independently either way. Judge single-merge
  readability against the probe's exemplars
  (`StateMachineBox<T>::RentFromCache`, `Number::DiyFp128RoundToUInt128`,
  `NumberFormatInfo::ValidateParseStyleFloatingPoint`), not the stale list.
- **Condition 2 (readability): met** by a throwaway retained-label probe
  (branch `probe/issue-1175-condition2` @ `8c611b226`, 175 LOC, evidence-only):
  acyclic single-merge diamonds render as nested `if`/`else` + one labelled
  merge; goto/label counts drop 2–4×; twice the structured tree unlocked a
  downstream `&&` fold. Soundness: 0 pass bugs over 42,502 CoreLib methods on
  both sweeps, 0 UNSOUND assertions on fired methods, #640 canaries
  byte-identical, zero printer changes needed.

Design inputs the probe and [#4063](https://github.com/richlander/dotnet-inspect/issues/4063)
add to the step-4 plan:

1. **Merge selection**: deepest-valid post-dominator plus recursive
   re-application to the tail — nearest-first structures only a prefix of
   multi-merge containers (readable but visibly half-done).
2. **Back-edge regions are represented and classified from day one**; the
   canonical rotated-entry/single-latch subset is consumed transactionally,
   while unsupported loop regions remain flat and an unrelated loop in an
   untouched tail does not veto an eligible forward region (see the corrected
   loop entry under *Out of scope*).
3. **Definite assignment**: the production slice extends the #631 analysis with
   a forward structured-goto walk, so assignments reaching an acyclic retained
   merge are intersected with lexical fallthrough instead of flooding locals to
   `= default` (`Matrix4x4::Decompose` was 1→7 in the probe). A retained goto
   nested inside the first loop slice still exceeds that top-level model and
   conservatively falls back to default initialization;
   `RetainedLoopGotoBailsDefiniteAssignmentConservatively` gates that disclosed
   boundary until a loop-aware dataflow extension replaces it.
4. **Sequencing** (unchanged from the spike): wire the rec-#2 real-world
   corpus baseline as the regression sensor before the rewrite lands.

5. **Regression sensor implementation in #4094.** Snapshot schema v6 records
   every imported branch, switch, and EH-transfer slot by stable method
   identity, control-flow kind, IL offset, and same-offset ordinal. Its outcome
   is output-visible: matching uses source provenance, transfer kind, and
   targets, with the owning IL block as the fallback for provenance-less
   synthesized transfers and the recovered source name when that block belongs
   to a raised local function. The name remains stable when sibling raise
   coverage changes. Unmatched residuals receive stable output-site identities.
   Reusing or rebuilding an equivalent retained goto is neutral; reparenting is
   neutral when source provenance remains available. A
   provenance-less transfer is intentionally keyed to its owning IL block and
   nested owner, so cross-block and cross-local-function movement remain visible.
   Adding a printable residual is a loss. On fixed NuGet artifacts, every loss
   fails independently of unrelated gains, and pinned method, imported-site, or
   empty-domain drift fails closed. The named gates are
   `ControlFlowSiteLedger_ObservesCompilerProducedSwitchRaise`,
   `ControlFlowSiteLedger_TreatsRebuiltEquivalentTransferAsResidual`,
   `ControlFlowSiteLedger_TreatsReparentedEquivalentTransferAsResidual`,
   `ControlFlowSiteLedger_DistinguishesNestedFunctionOwners`,
   `ControlFlowSiteLedger_NestedOwnerIdentitySurvivesSiblingCoverageChange`,
   `Compare_ControlFlowLossCannotBeOffsetByUnrelatedGain`, and
   `Compare_NewOutputResidualIsLossAndRemovedOutputResidualIsGain`. Repo-built
   assemblies remain advisory because their IL population churns. This is the
   required non-offsettable prerequisite for the retained-label rewrite; it
   detects output loss that CFG-model agreement cannot, but does not replace
   focused compiler-produced boundary fixtures, render A/B, or fidelity
   evidence. The migration also makes pinned `valid` → invalid, fully-raised
   → residual, and `Full` → non-`Full` fidelity method transitions
   non-offsettable
   (`Compare_PinnedValidityLossCannotBeOffsetByGain` and
   `Compare_PinnedFullyRaisedLossCannotBeOffsetByGain`, plus
   `Compare_PinnedFullFidelityLossCannotBeOffsetByGainOrValidityCoverageChange`).
   #4238/#4255 restored the 13 validity and fully-raised regressions exposed by
   the first attempted migration, and #4281/#4301 restored its hidden six-site
   region-exit loss before the schema-v6 baselines were activated.

   The final activation partitioned every intervening pinned transition by
   owning product change rather than netting directions together: #4314
   contributed 179 losses and 9 gains from its sparse-switch soundness decline,
   #4154 contributed 8 losses and 100 gains while rejecting EH entries into
   recovered do-while bodies, and #4301 contributed 10 losses and 6 gains while
   restoring the target region-exit raise and preserving sibling transfers.
   Other intervening changes contributed no site movement. The exact generated
   baselines then compared to themselves with zero losses, zero gains, and no
   changed methods. These measurements classify reviewed product transitions;
   they do not weaken the non-offsettable comparison contract above.

   Round-1 review then found that equal block offsets in two raised local
   functions could collapse to one fallback identity. Adding the nested-owner
   ordinal reclassified 30 residual identities in two Roslyn methods as 30
   losses and 30 gains; every pair differed only by the new `@local_0` owner
   component, so this was measurement migration rather than product movement.
   The baseline was regenerated only after that partition. The same review also
   closed a persisted-data false green by requiring branch, conditional, and
   leave output identities to carry exactly one target; the gates are
   `ControlFlowSiteLedger_DistinguishesNestedFunctionOwners` and
   `ControlFlowSites_RejectMultipleTargetsForSingleTargetTransfer`.

   Follow-up review showed that the first owner ordinal was fail-closed but
   coverage-dependent: adding an earlier raised sibling renumbered an unchanged
   later owner. Replacing it with the recovered source name reclassified the
   same 30 Roslyn identities as another 30 losses and 30 gains, again with no
   product outcome movement. The compiler-backed
   `ControlFlowSiteLedger_NestedOwnerIdentitySurvivesSiblingCoverageChange`
   gate keeps the later owner's key fixed when its sibling coverage changes.
