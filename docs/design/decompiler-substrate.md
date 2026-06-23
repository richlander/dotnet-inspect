# Decompiler Substrate Layers

How shared rewrite-gate predicates are factored out from the raising passes, and
how we notice when several passes have independently grown the same need. This
is a design note about a recurring shape, not a tour of every helper. Use
**decompiler substrate** for the broad shared layer and **identity predicates**
for the exact gates that decide whether a rewrite may fire; avoid **fact
substrate**, because "facts" already names hidden-fact annotations and sidecar
ledger metadata. See
[decompiler.md](../decompiler.md) for the IR pipeline the passes run over,
[decompiler-quality.md](../decompiler-quality.md) for the correctness oracle the
passes are held to, and [hidden-fact-annotations.md](hidden-fact-annotations.md)
for the read-only fact layer modelled on the same instinct.

## The shape

A raising pass turns an IL idiom back into its C# spelling. To do that safely it
must keep asking the same small questions: *is this the BCL member I think it
is?* *is this type compiler-generated?* *are these two expressions the same
re-evaluable place?* Each question is a predicate over IR evidence —
structural, side-effect-free, answerable without rewriting anything.

When a predicate is answered inline inside one pass, the next pass that needs it
copies the code. The copies drift: one matches a member on namespace and name,
another on assembly identity and exact signature; one admits a local read where
another must not. Drift in a rewrite gate is a soundness bug waiting to happen,
because the whole point of the check is to gate a rewrite.

The fix is a **substrate layer**: a thin `public static class` in `Pipeline/`
that owns one predicate category, exposed as small composable atoms the passes
call. The live substrate layers are:

| Layer | Predicate category | Example question |
| --- | --- | --- |
| `MemberIdentity` | Exact BCL member / type identity | Is this `RuntimeHelpers.GetSubArray`? |
| `GeneratedCodeIdentity` | Compiler-generated shape (attribute-gated) | Is this a non-capturing lambda holder? |
| `PlaceIdentity` | Intra-method re-evaluable place identity | Are these two reads the same local? |
| `ReferenceOwnership` | Reference-scope ownership for consumed scaffolds | Is every use of this synthetic local inside the nodes this rewrite consumes? |

They are siblings by construction — thin, allocation-light, no pass state, named
for the evidence category not the caller — but each owns a different category:
metadata identity, compiler-shape evidence, intra-method place identity, and
reference-scope ownership.

## Boundaries and roadmap

The substrate is not a new whole type system. The product decompiler path stays
SRM-only, NativeAOT-friendly, Roslyn-free, and does not load inspected
assemblies. Runtime type-system code (`Internal.TypeSystem`,
`System.Private.TypeLoader`) and `MetadataLoadContext` are useful prior art for
vocabulary and boundary checks, not product dependencies. For a broad new
substrate layer, start with a short mapping note: which concepts are copied in
smaller form, which are avoided, and which dependency boundaries remain
non-goals.

The roadmap is consumer-driven:

- Exact member/type identity, generated-code identity, and place identity are
  live because real raises already consume them.
- PDB/state-machine helpers are a good foundation for classic async/iterator
  recovery, but should start with a concrete raise or adversarial gap.
- Tuple/nullability metadata decoding belongs in shared services when it feeds
  tuple/deconstruction, signature display, or readable-name work.
- Deterministic display and name-collision services should remain substrate
  first; a readable-name mode can come later without changing the inspection
  default.
- Ledger sidecars should keep naming required predicate primitives, positive and
  adversarial coverage, and the current missing discriminator for `Partial`
  rows.

Treat old roadmap bullets as candidates, not automatic priorities. A slice
becomes current work when it has a concrete pass customer, an adversarial bug, a
ledger/scorecard movement, or a corpus/fidelity signal.

## Atoms, not one maximal predicate

The load-bearing design choice is that a substrate layer exposes **atoms the
caller composes**, never a single "does everything" predicate. The equality
logic is shared; *which node kinds a pass admits* is not — that admissibility is
a deliberate soundness discriminator the pass still owns.

`PlaceIdentity` is the clearest illustration. Several passes — `??=`, `?.`, switch
dispatch, boolean folding, `^n` from-end, and indexer fold targets — all need
"same re-evaluable place", and several had byte-identical `Same*` helpers. But
they do **not** all admit the same nodes:

- Most fold a bare variable read (`SameVariable` — local, argument, or `this`).
- Boolean folding also accepts a spilled stack slot (`SameVariable ||
  SameStackSlot`).
- `IndexFromEndPass` accepts **only** a stack slot (`SameStackSlot`). Broadening
  it to a direct local read would rewrite a faithful `a[a.Length - n]` into
  `a[^n]`, whose recompiled IL differs — an opcode-exactness break. The
  stack-slot restriction is what proves the compiler actually spilled the
  receiver, i.e. that the source really was `^n`.
- Indexer folds (`d[k] ??= v`, `d[k] += v`) reduce to "same re-evaluable place"
  on the *index arguments* too: `SameOperand` (a variable read or an identical
  literal) and its pairwise list form `SameOperands`. This is a live instance of
  the convergence rule below — two unrelated consumers, the
  `NullCoalescingAssignmentPass` and the printer's compound-assignment fold,
  independently needed "are these two index-argument lists the same re-evaluable
  place", so the check became one atom both compose rather than two hand-rolled
  loops.

Stepper audit (#1011, stack-slot reuse) confirmed the adjacent live-range
contract around these atoms: `SameStackSlot` proves only one compiler spill
within a pass-owned lowering shape; it is not a whole-method identity claim for a
slot number. Reused evaluation-stack positions are allowed to carry unrelated C#
types across disjoint straight-line live ranges. Earlier passes should consume
their owned ranges before `StackSlotLiveRangePass`; that pass may split only the
loads reached before the next write to the same slot, and it deliberately skips
structured EH regions where later control-flow rewrites can reshape the range.
`StackSlotReuseRenderingTests` pins the positive and near-miss cases: bool
materialization and object/list/count reuse split when needed, while subtype
stores loaded through a common supertype remain one variable.

A single maximal `SamePlace(a, b)` predicate would have silently handed
`IndexFromEndPass` the broadening that breaks it. Exposing `SameVariable` and
`SameStackSlot` as separate atoms keeps the equality honest in one place while
each pass keeps its discriminator explicit at the call site. The same principle
governs `MemberIdentity`: it offers `IsCoreLibraryType`,
`IsStaticCoreLibraryMethod`, and named member checks, not one fuzzy "is this the
method I mean".

The testing dividend is concrete: adversarial lookalikes (a property getter or a
method call is *not* a place; a same-named method on a different assembly is
*not* the member) live once as unit tests on the substrate atom. Consuming
passes then need only an integration fixture per idiom, not a re-derivation of
the full adversarial matrix.

`ReferenceOwnership` applies the same rule to compiler-scaffold ownership. Passes
such as `LockSugarPass`, `UsingStatementPass`, `StringInterpolationPass`, list
patterns, and is-pattern raises all had the same proof obligation: a synthetic
local or stack slot may be consumed only if every load/store/address reference is
inside the subtree the rewrite owns. The shared atoms answer that location
question (`LocalReferencesOnlyWithin`, `StackSlotReferencesOnlyWithin`,
`SubtreeReferencesLocal`, `SubtreeStoresLocal`, `IsInside`); the pass still owns
the discriminator that says *which* roots are consumed and *why* the lowering
shell is a lock, using, interpolation, or pattern.

Keep this substrate intentionally small. Its value is that repeated ownership
proofs are auditable in one place; it should not grow into a general-purpose
ownership framework or decide whether a source construct has been recognized.

That is the practical form of **shape + proof + decline**. `LockSugarPass` names
the `Monitor.Enter`/`Monitor.Exit` lowering shell, composes
`ReferenceOwnership.LocalReferencesOnlyWithin` to prove the copied receiver and
`lockTaken` locals do not escape the consumed stores/enter/finally guard, and
declines if the proof fails. Do not replace this with a generic "recognize any
lock-like pattern" matcher; the substrate owns reusable proof atoms, not the
source construct.

## Noticing convergence

The hard part is not building a layer — it is noticing that three discrete
implementations should have been one. Two mechanisms, one proactive and one
reactive:

### Proactive — the ledger names predicate dependencies

`LoweringCoverage` is the compiled completeness ledger: one row per Roslyn
`LocalRewriter` lowering, recording the mechanism (which pass) and completeness.
It already makes the *dedicated-vs-shared* gradient derivable — a pass type used
by one row is dedicated, by several is shared. The substrate extension is for
sidecar providers to name the **predicate primitives** their rows depend on,
keyed by the stable ledger row name. When three or more rows cite the same
primitive, that is the signal to promote it to a substrate layer before the
fourth copy is written — the ledger turns "we keep needing this" from tribal
memory into queryable metadata.

The sidecar metadata deliberately stays out of the central ledger rows. This keeps
the conflict-reduction shape from PRs such as the stable ledger and printer
splits: the central ledger remains the stable denominator, while additive
per-pass sidecars carry the changing work-queue metadata.

### Reactive — a duplication census

The cheaper guard is a census test that flags un-migrated copies: a
`static bool Same*(…)` / `static bool Is*(…)` predicate helper living inside a pass
rather than in a substrate layer is a smell the test can surface. It does not
forbid a pass-local helper — sometimes a predicate genuinely belongs to one pass
— but it forces the question into review: *is this the third copy of a predicate
that should be shared?* The answer is recorded, not assumed.

Both mechanisms encode the same rule of thumb: **the third occurrence builds the
layer.** One use is local; two is a coincidence; three is a category.

The "two or more consumers" bar applies to a *composable atom* like
`SameVariable` — there, an atom with a single caller is just a renamed helper,
and building it early risks a speculative shape with no second consumer (a real
cost). It does **not** apply rigidly to a *named member predicate* like
`MemberIdentity.IsRuntimeHelpersGetSubArray`: the shared thing there is the
*category* (exact BCL-member identity, matched on assembly + signature, never
namespace/name), and a one-consumer member check still belongs in the layer so
it cannot drift back to fuzzy matching. So: shared *category* admits a
single-consumer named predicate; a shared *atom* wants two.

## Adding or extending a layer

A change that touches this substrate states, in its PR:

1. **The predicate category and its evidence.** Metadata identity, compiler shape,
   intra-method place — which existing layer, or why a new sibling.
2. **The atoms, and why they are atoms.** What each admits, and the
   discriminator each leaves to the caller. If a proposed atom would let any
   current caller broaden unsafely, it is the wrong shape.
3. **The right granularity.** A composable *atom* (`SameVariable`) needs two or
   more behavior-preserving consumers — no speculative primitives. A named
   *member predicate* for a shared category (`IsRuntimeHelpersGetSubArray`) may
   have one consumer: it belongs in the layer so it cannot drift back to fuzzy
   matching, even if only one pass needs it today.
4. **Behavior preservation for migrations.** Each migrated call site reproduces
   its old predicate exactly (the composition, not just the name), verified by
   the unchanged fidelity gates plus the existing pass fixtures.

Adversarial coverage lives on the atom; the consuming pass keeps its integration
fixture. The full decompiler suite plus the product build remain the backstop —
because the substrate exists to make the passes *safer*, the passes' own gates
are what prove a migration changed nothing.
