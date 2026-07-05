# Allocation Triage Pre-Filters

Which static allocation candidates Performance Triage surfaces, why the
pre-filters are shaped the way they are, and what the static side can and cannot
predict about realized cost. Grounded in active corpus use of the static analysis
joined against real allocation traces.

Related docs:

- [Graph signal annotations](graph-signal-annotations.md) — the signal/leverage
  triage axes allocation rows plug into
- [Hidden-fact annotations](hidden-fact-annotations.md) — the per-method
  allocation-occurrence evidence model

## Two halves of an allocation verdict

An allocation row has two independent components:

1. **Cost-shape** — the objective, per-invocation nature of the allocation: its
   kind (box/array/closure/delegate/object), whether it sits on a loop back-edge,
   where the value escapes, how large it is. This is static and knowable from IL.
2. **Realized frequency** — how many times the containing method actually runs in
   a given workload. This is dynamic and *not* knowable from IL: the same shape is
   cold in a rarely-called method and dominant in a hot one.

The static analysis owns the first. A trace join (the `runfaster` prototype, or a
profiler) owns the second. The pre-filters below follow from keeping that split
honest: **the static side prunes shapes that are cold by construction and
describes shape; it does not rank shapes by heat, because heat is frequency and
frequency is not static.**

## Evidence

Three assemblies, static candidates joined to a real GC-allocation-tick trace,
spanning the optimization spectrum:

| Target | Static candidates | Trace tick-match | Top realized allocation |
| --- | ---: | ---: | --- |
| An un-optimized app path | ~8.5k | ~100% | a capturing `Where` closure, per element |
| A heavily-optimized generic library | ~0.9k | ~0% (IL-offset) | a constructor-argument state object |
| The decompiler itself | ~5.2k | ~83% | a per-call iterator state machine + stack |

Two facts recur across all three:

- The IL-offset join is precise on non-inlined code and near-blind on
  generic/inlined code; type-level confirmation is the necessary complement there.
- The dominant realized volume is **call-frequency-driven**, not loop-driven.

## What the static side can prune (cold by construction)

These shapes are cold because of what they *are*, independent of workload, so
pruning or demoting them loses no realized paydirt. Measured hit-rate against the
join is near zero for each.

| Shape | Rationale | Status |
| --- | --- | --- |
| Throw-path / error-path allocation | Exception setup, not steady state | Filtered |
| Exception-object construction | Same | Filtered |
| Constructor / type-initializer setup | One execution per instance/type | Demoted (amortized caveat) |

The amortized-setup demotion carries a caveat rather than a hard drop, because a
constructor invoked from a loop is a genuine transient signal; that case is
preserved by checking for loop invocation before demoting.

## What the static side must not do

**Rank by shape.** The realized top-volume sites have weak shape signal. In the
decompiler corpus the highest-volume allocation sits well below the top of any
static shape ranking (near the top third, with other top-volume sites past the
median), and the top group is dominated by `Once`/`Conditional` (once-per-call)
allocations with no escape refinement and no loop membership. Their cost is pure
call frequency. A ranker that promotes loop or escape shape buries them.

The existing loop gate on the *aggregate* per-method density row is therefore
scoped deliberately: it applies only to the vague catch-all row, never to
specific-shape rows. Specific-shape rows stay visible regardless of loop
membership, so a hot once-per-call closure or delegate is not hidden.

## Negative results worth keeping

Active use ruled out two plausible-looking filters:

- **Static-field escape is not an amortization filter.** Allocations that escape
  into a static field score near-zero realized heat in the corpus, which suggests
  demoting them as one-time cache/singleton initialization. That correlation is
  already explained by two existing behaviors — the compiler's
  `ldsfld`/`dup`/`brtrue`/`stsfld` cached-delegate pattern is suppressed at
  detection, and plain static-field object initializers never reach a
  specific-shape or loop-density row — so no additional filter changes curated
  output. It would also be unsound as a rule: an unconditional per-call
  `field = expr` assignment escapes to a static field yet allocates every call.
  The coldness is a property of existing suppression, not a semantic guarantee of
  the escape kind.

- **Collection-element escape is workload-correlated, not universal.** It is cold
  in a decompiler sweep but would be hot in a collection-building workload, so it
  is a deprioritization signal at most, never a prune.

## Principle

Every pre-filter must be justifiable by *why the shape is cold by construction*
(throw path, one-time initialization), not by *where it happened to be cold in one
trace*. Ranking the survivors by heat requires realized frequency, which only a
trace join supplies. The static side's contribution to precision is a clean,
semantically-grounded pruning of the un-actionable, plus the shape vocabulary that
lets a join explain *why* a confirmed-hot site is hot (loop-intrinsic versus
call-frequency-driven) and therefore *how* to fix it.
