# Decompiler Substrate Layers

How shared *facts* are factored out from the raising passes, and how we notice
when several passes have independently grown the same need. This is a design
note about a recurring shape, not a tour of every helper. See
[decompiler.md](../decompiler.md) for the IR pipeline the passes run over,
[decompiler-quality.md](../decompiler-quality.md) for the correctness oracle the
passes are held to, and [hidden-fact-annotations.md](hidden-fact-annotations.md)
for the read-only fact layer modelled on the same instinct.

## The shape

A raising pass turns an IL idiom back into its C# spelling. To do that safely it
must keep asking the same small questions: *is this the BCL member I think it
is?* *is this type compiler-generated?* *are these two expressions the same
re-evaluable place?* Each question is a **fact** about the IR — structural,
side-effect-free, answerable without rewriting anything.

When a fact is answered inline inside one pass, the next pass that needs it
copies the code. The copies drift: one matches a member on namespace and name,
another on assembly identity and exact signature; one admits a local read where
another must not. Drift in a fact check is a soundness bug waiting to happen,
because the whole point of the check is to gate a rewrite.

The fix is a **substrate layer**: a thin `public static class` in
`Pipeline/` that owns one fact category, exposed as small composable atoms the
passes call. Three exist today:

| Layer | Fact category | Example question |
| --- | --- | --- |
| `MemberIdentity` | Exact BCL member / type identity | Is this `RuntimeHelpers.GetSubArray`? |
| `GeneratedCodeIdentity` | Compiler-generated shape (attribute-gated) | Is this a non-capturing lambda holder? |
| `PlaceIdentity` | Intra-method re-evaluable place identity | Are these two reads the same local? |

They are siblings by construction — thin, allocation-light, no pass state, named
for the fact not the caller — but each owns a different category of evidence:
metadata identity, compiler-shape evidence, and intra-method place identity.

## Atoms, not one maximal predicate

The load-bearing design choice is that a substrate layer exposes **atoms the
caller composes**, never a single "does everything" predicate. The equality
logic is shared; *which node kinds a pass admits* is not — that admissibility is
a deliberate soundness discriminator the pass still owns.

`PlaceIdentity` is the clearest illustration. Five passes — `??=`, `?.`, switch
dispatch, boolean folding, and `^n` from-end — all need "same re-evaluable
place", and several had byte-identical `Same*` helpers. But they do **not** all
admit the same nodes:

- Most fold a bare variable read (`SameVariable` — local, argument, or `this`).
- Boolean folding also accepts a spilled stack slot (`SameVariable ||
  SameStackSlot`).
- `IndexFromEndPass` accepts **only** a stack slot (`SameStackSlot`). Broadening
  it to a direct local read would rewrite a faithful `a[a.Length - n]` into
  `a[^n]`, whose recompiled IL differs — an opcode-exactness break. The
  stack-slot restriction is what proves the compiler actually spilled the
  receiver, i.e. that the source really was `^n`.

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

## Noticing convergence

The hard part is not building a layer — it is noticing that three discrete
implementations should have been one. Two mechanisms, one proactive and one
reactive:

### Proactive — the ledger names its facts

`LoweringCoverage` is the compiled completeness ledger: one row per Roslyn
`LocalRewriter` lowering, recording the mechanism (which pass) and completeness.
It already makes the *dedicated-vs-shared* gradient derivable — a pass type used
by one row is dedicated, by several is shared. The substrate extension is to let
a row also name the **fact primitives** its pass depends on. When three or more
rows cite the same primitive, that is the signal to promote it to a substrate
layer before the fourth copy is written — the ledger turns "we keep needing this"
from tribal memory into a queryable fact. (This annotation is described here as
the strategy; it is added incrementally as rows are touched, not retrofitted in
one sweep.)

### Reactive — a duplication census

The cheaper guard is a census test that flags un-migrated copies: a
`static bool Same*(…)` / `static bool Is*(…)` fact helper living inside a pass
rather than in a substrate layer is a smell the test can surface. It does not
forbid a pass-local helper — sometimes a fact genuinely belongs to one pass —
but it forces the question into review: *is this the third copy of a fact that
should be shared?* The answer is recorded, not assumed.

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

1. **The fact category and its evidence.** Metadata identity, compiler shape,
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
