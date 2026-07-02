# The inverse architecture: a confident-inverse ledger

This document orients a reader — human or agent — to the decompiler as a
**deliberate inverse of the forward .NET compilers**. The thesis: the decompiler
is not an ad-hoc pattern matcher that happens to produce C#; it is the **confident
inverse of Roslyn's IL emission**, and a **sibling of RyuJIT's IL importer**. Every
node in the IR, and every stage in the pipeline, exists to undo a specific forward
construct under a **named assumption**. Where an assumption holds, the round trip
is exact; where it fails, we get a measurable fidelity gap. This doc makes that
correspondence explicit so the design is auditable, the naming is anchored, and
every failure is *predictable* rather than surprising.

It is the structural companion to [value-typed-emission.md](value-typed-emission.md)
(the value-flow inverse) and [control-flow-structuring.md](control-flow-structuring.md)
(the control-flow inverse). Read [decompiler.md](../decompiler.md) first for the
pipeline shape, [decompiler-quality.md](../decompiler-quality.md) for the correctness
oracle, and [decompiler-correctness-pipeline.md](../decompiler-correctness-pipeline.md)
for how proofs and evidence are named.

This is a **spec and a discipline**, not a finished tour: some of it describes
infrastructure that is planned (the annotation layer in
[Two levels](#two-levels-the-prose-and-the-code)) rather than built. Sections that
describe intent say so.

## The relationship is a retraction, not a duality

It is tempting to call decompilation the *dual* of compilation. It is not — and the
distinction matters, because it sets the bar for what we can promise.

- A **duality** (in the category-theory sense) reverses every arrow to get the
  opposite category, turning constructions into their co-constructions
  (product ↔ coproduct). It is an **involution** and lossless. Decompilation is
  neither: it is **lossy** (names, trivia, and spelling choices are gone on the way
  down), so it cannot be an involution.
- What we actually have is a **retraction**: decompilation is a one-sided inverse
  (a *section*) of Roslyn's IL emission, exact **up to semantic equivalence** and
  **within a declared domain**. Formally, the property we hold ourselves to is

  ```text
  emit ∘ decompile  ≈_sem  id_IL      (on the declared domain, under the stated assumptions)
  ```

  and this is exactly what the harness measures as **compile-back fidelity** — the
  recompiled IL of our rendered C# matches the IL we started from. The reverse
  composition (`decompile ∘ emit`) is deliberately lossy; we do not try to recover
  identifiers or formatting.

So the honest framing carries three qualifiers everywhere: **semantic equivalence**,
a **declared domain**, and **per-node assumptions**. The rest of this document is
mostly the enumeration of those assumptions.

The one place true mathematical **duality** *does* live is one level down, in the
analyses the passes run: forward and backward dataflow are order-theoretic duals
(the same lattice framework on the reversed CFG), and abstraction ↔ concretization
form a Galois connection. Those are internal tools, not the compile ↔ decompile
relationship itself.

## Two references, used differently

We relate to the two forward compilers in fundamentally different ways. Conflating
them is the most common way to reason about this design incorrectly.

### Roslyn — the thing we invert

Roslyn owns the **IL ↔ C#** boundary: it binds C# to a typed bound tree, lowers
sugar to primitive nodes, and emits IL. The decompiler runs that boundary
**backward**. Each forward Roslyn phase has an inverse decompiler stage, and the
retraction target `emit ∘ decompile ≈ id` is defined against Roslyn's emitter. When
we ask "is this rendering faithful?", *faithful* means "Roslyn would emit the IL we
started from."

### RyuJIT — the sibling, and the soundness oracle

RyuJIT is **not** something we invert — it consumes IL forward, to machine code. But
it solves *our subproblem*: it also reads IL and must **reconstruct the types IL
erased** (for codegen, where we do it for C#). That makes RyuJIT's importer our
**soundness oracle**:

> Any **type or shape we recover from an IL method body** (the erased evaluation
> stack), RyuJIT's importer must be able to assert soundly from the same IL. Where
> that body-derived recovery is *weaker* than RyuJIT's, we are merely incomplete;
> where it is *stronger*, we are **unsound**.

The oracle scopes to *stack- and body-derived* type recovery. It does **not** bound
what we recover from **metadata and structure** — enum member names, sugar shapes,
generic context — which RyuJIT never asserts and which are sound because they come
from metadata, not from the erased stack.

This is what "confident" in *confident inverse* means: our recovered facts are a
subset of what RyuJIT soundly recovers. RyuJIT's rules are therefore load-bearing
spec, not analogy. The clearest example: RyuJIT normalizes `bool`/`byte`/`short` to
`int32` on the evaluation stack and re-narrows on store (the ECMA-335 stack model).
Any decompiler stage that forgets this — that treats a stack `bool` as
distinguishable from a stack `int32` at a sink — is unsound against the oracle, and
will miscompile. (This is not hypothetical: it is the shape of one of the two
value-typed-emission slice-3 findings — the premature sink-type assertion on a value
whose C# type the slot unifier had not yet decided. The other slice-3 finding, a
lambda return mis-attributed to the outer signature, was a scope-attribution bug in
the sink enumerator, not a stack-model violation. Both were caught by the slice's
adversarial render A/B in branch review; neither landed.)

## The stage ledger (the vertical)

The reversed pipeline. `emit ∘ decompile ≈ id` decomposes stage by stage, in reverse
order (`(f ∘ g)⁻¹ = g⁻¹ ∘ f⁻¹`): each decompiler stage re-establishes the invariant
its forward counterpart consumed.

| Forward (Roslyn) | Inverse stage (decompiler) | Invariant re-established | Primary assumption |
| --- | --- | --- | --- |
| IL emission (bound tree → IL) | **raise / import** (IL → IR) | every value carries a recovered type | IL is verifiable and type-safe (shared with RyuJIT's importer) |
| Lowering (sugar → primitive bound nodes) | **structuring** (CFG → nested C#) | control flow is expressible as structured C# | the CFG came from structured C# (reducible; no goto-soup beyond what C# can spell) |
| Conversion classification (`BoundConversion`) | **coercion insertion** (typed-sink wrapping) | every sink value is at, or explicitly coerced to, its target | the sink type is recoverable **and** distinguishable from the stack type |
| Constant handling / typed constants | **typed-constants** | a constant carries its semantic (e.g. enum) type | the sink's semantic type is resolved in the current assembly |

The **structuring** and **coercion insertion** rows are the two pass exemplars that
complete the "full vertical" from IL up to C#. The others are
governed by their own design notes ([control-flow-structuring.md](control-flow-structuring.md),
[value-typed-emission.md](value-typed-emission.md)).

## The node ledger (the horizontal)

The per-construct correspondence. Each row is a **local inverse**: a decompiler node,
the forward construct it undoes, the precondition under which the inverse is exact,
and the **executable witness** that proves it (a fixture or harness mode — the
inverse is only as real as its runnable check). This table *is* the proof: a
collection of small, witnessed inverses.

The seed rows below are the **conversion family**, where the theory is sharpest and
the RyuJIT-sibling point lands hardest. The full ledger is generated from the
in-code annotations (see [Two levels](#two-levels-the-prose-and-the-code)); this is
the worked example the generator will subsume.

| Node | Forward construct (Roslyn / RyuJIT) | Precondition | Witness |
| --- | --- | --- | --- |
| `Convert` | `BoundConversion` (numeric) / `GT_CAST` | none — models the `conv.*` that ran | round-trips by construction; corpus compile-back |
| `Coerce` *(value-typed-emission)* | `BoundConversion` (the implicit, target-driven part) | sink type recoverable and distinguishable from the stack type | `CoerceChokePointTests` / `CoercionInvariantTests` (Roslyn compile-gated), the corpus render-text A/B, and the invariant sweep (landing with slice 3) |
| `Box` | `BoundConversion` (boxing) / `GT_BOX` | target is the boxed value type | `box`/`unbox` fixtures |

`Convert` and `Coerce` are the same forward concept (`BoundConversion`) split into
two nodes — see [Naming provenance](#naming-provenance) for why the inverse
*requires* that split.

## Assumptions and the failure map

The payoff of naming assumptions per node is that **every fidelity or validity gap
maps to a violated assumption**. The harness's `Partial` / `OpcodeDiff` buckets stop
being a mysterious residue and become a labelled index into this ledger:

- A stack-model confusion (`bool` vs `int32` at a sink) → the *coercion insertion*
  row's "distinguishable from the stack type" assumption.
- A cross-assembly enum rendered as a bare integer → the *typed-constants* row's
  "semantic type resolved in the current assembly" assumption (a known best-effort
  boundary; see below).
- An irreducible CFG that will not structure → the *structuring* row's "came from
  structured C#" assumption.

This is the same **boss / evidence** model as
[decompiler-correctness-pipeline.md](../decompiler-correctness-pipeline.md): each
assumption is a proof obligation, each witness is its evidence, and a regression is a
proof that failed under a named precondition — which is far more actionable than a
raw diff.

## Naming provenance

Because every node is paired with its forward construct, a name can be judged instead
of bikeshedded. Each identifier falls into exactly one bucket, and the bucket sets
the review bar:

| Bucket | Meaning | Review rule |
| --- | --- | --- |
| **Inherited** | Deliberately follows a forward name | Keep aligned; renaming *away* is the smell |
| **Inverted** | Deliberate antonym of the forward name | Document the pairing; the divergence is the point |
| **Native** | No forward analog — decompiler-born | Must carry its own rationale; **no anchor = highest drift risk** |

**Native** names are the standing rename-review queue: they are the only bucket where
"does this name earn its keep?" is a live question. Worked examples:

- `Convert` — **Inherited** (Roslyn `BoundConversion` numeric part; RyuJIT `GT_CAST`).
  Alignment is a feature; do not drift.
- `IrImporter` / `Import` — **Inherited**, a deliberate homage: RyuJIT's IL → IR phase
  is *the importer* (`impImportBlock`). Stated so it is not "modernized" away.
- `StackType` — **Inherited** from the RyuJIT / ECMA stack model.
- **raise** / **structuring** — **Inverted**: Roslyn's forward is *lowering*; the
  antonym is *raising*.
- `Coerce` — **Native**, and the centerpiece. Roslyn and RyuJIT **unify** conversion
  (`BoundConversion` / `GT_CAST` cover both our `Convert` and our `Coerce`). The
  inverse *must* split them: `Convert` records **IL history** (the `conv.*` that
  happened), `Coerce` records the **surface obligation** (the cast a sink demands).
  The name earns it — "coercion" idiomatically means *target-driven, implicit*
  conversion, exactly the sink-demand semantics, distinct from an explicit historical
  conversion.
- `fidelity` / `Full` / `Partial` — **Native** by necessity: forward compilers are
  exact by construction, so "fidelity" has no forward analog.

The generator emits two views: the correspondence ledger, and a **"Native names — no
forward anchor, justify or rename"** list, so naming discipline is a generated
checklist rather than a recurring argument.

## Two levels: the prose and the code

> This section describes intended infrastructure, not code that exists today.

A prose doc alone rots in proportion to its distance from the code. So the ledger
lives at two levels, with a single source of truth:

1. **This document** — the thesis, the two-reference framing, the honest boundaries.
   Hand-written prose.
2. **Co-located annotations** — structured attributes on the IR nodes and passes that
   carry the correspondence data. The ledger tables in this doc are **generated** from
   them, so the two levels cannot drift.

The mechanism is **attributes, not interfaces**. Interfaces are the wrong tool here:
a marker interface carries no payload, fork type identity across build configs (a
node that `is IInverse` only in debug renders differently per configuration — the
exact bug class we are eliminating), and cannot express per-node data (which forward
construct, which oracle, which assumption). Attributes carry all of it and are
reflectable.

```csharp
[InverseOf(Forward.RoslynBoundConversion,
           oracle:  Oracle.RyuJitStackNormalization,
           naming:  NameProvenance.Native,     // Inherited | Inverted | Native
           forwardName: "BoundConversion",
           assumes: nameof(InverseAssumptions.SinkDistinguishableFromStack))]
public sealed class Coerce : IrExpression { /* ... */ }
```

Two design rules keep this honest and cheap:

- **Describe vs enforce are separate.** The attribute is cheap, always-on metadata —
  its `Forward` / `Oracle` references are **enums, not `typeof(BoundConversion)`**, so
  the product path stays SRM-only, Roslyn-free, and NativeAOT-friendly. Only the
  *assumption checks* are debug-only: a `Debug.Assert(InverseAssumptions.…(…))` in the
  hot path (compiled out of release), backed by a release-capable `Check()` for the
  corpus gate — the same return-not-throw shape the coercion invariant already uses.
- **The annotation is load-bearing, not decorative.** A coverage test requires every
  in-domain node to carry `[InverseOf(...)]` or an explicit `[NotInverted(reason)]`,
  and CI diffs the generated ledger against the committed copy. If nothing goes red
  when an annotation is wrong or missing, it is just a fancy comment.
- **Every `assumes:` must name an executable predicate.** Presence and ledger↔attribute
  agreement are not enough — nothing above forces the *assumption itself* to stay true
  when a pass rewrite changes a node's real precondition. So the rule is: every
  `assumes:` names a member exposing a release-capable `Check()`, and the coverage test
  **invokes** those predicates over the fixture corpus. An assumption that cannot be
  spelled as a predicate does not go in the attribute — it goes in prose behind a
  `[NotInverted(reason)]`-style honesty marker. This is the residual-drift guard: it
  binds the attribute's claim to a runnable check, the same discipline the node ledger's
  witness column enforces.

The reflector that reads the annotations and emits the ledger lives in tools/tests,
never in the shipped decompiler.

## Where we are deliberately not an inverse

Honesty section. The inverse is confident only inside its declared domain. Outside it,
we are explicitly best-effort, and these are **not** bugs against the retraction:

- **Cross-assembly `Unknown` types.** A type defined in an assembly we do not load
  cannot be *proven* an enum, so it stays render-time-handled rather than in the
  checkable domain. RyuJIT would resolve it via the runtime's type system; we cannot.
- **Irreducible or synthesized control flow** that structured C# cannot spell.
- **Compiler-fingerprint-dependent lowerings** — which SDK lowered a `switch` or an
  `async` state machine. We invert *a* valid forward emission, not necessarily the
  exact one that produced these bytes.
- **Names and trivia.** The lossy direction: identifiers, comments, and formatting are
  gone. `S_2`-style synthesized names are our own, not recovered.

Each of these is a declared boundary of the domain, and belongs in the node ledger as
a `[NotInverted(reason)]` row so the boundary is visible, not implicit.

## Status

- **Framing (this doc):** drafted; under review.
- **Annotation infrastructure** (`[InverseOf]`, enums, reflector, coverage test, the
  `SinkDistinguishableFromStack` assertion): planned. First slice is the conversion
  family (`Convert` / `Coerce` / `Box`).
- **Node annotations applied across the IR:** planned, to follow the infrastructure.
  Because the annotations land in `IrNodes.cs`, which value-typed-emission slices 4–5
  actively edit, the annotation slice is sequenced *after* the slice-3 merge and the
  follow-up infra, to keep the churn on that file serialized.
- **Generated ledger:** planned; will replace the hand-written seed rows above.
