# Value-typed emission: the coercion choke point

This document scopes a major investment in the decompiler's **value-flow** layer,
the sibling of [control-flow-structuring.md](control-flow-structuring.md). Where
that doc governs how blocks become nested `if`/`else`, this one governs how a
*value* is rendered into a *typed position* — and argues that the decompiler is
missing a first-class abstraction the compiler family it belongs to has always
had.

Read [decompiler.md](../decompiler.md) first for the pipeline shape and the
recognizability goal.

## The missing member of the type system

The decompiler has a rich vocabulary for **what a value is**: `TypeRef`
(structural semantic identity), the per-node `ResultType`, and the join-merged
`Conditional.MergedType`. That is the *natural type*, in Roslyn's terms.

It has **no representation of two things**:

1. **What a *position* requires** — the *target type* of a value-consuming sink:
   a `return`, a call argument, a field/array-element store, a `box`, a
   conditional arm, a `switch` label. Every sink has an expected type, but that
   type is implicit and re-derived at each print site.
2. **The coercion that bridges them** — "render a value of natural type `N` into a
   context that requires `T`." This is not a *thing* in the IR. It is *emergent
   behavior*: a string synthesized ad hoc inside the printer — `(T)x`,
   `unchecked((T)x)`, or nothing — at roughly a dozen independent render branches.

So the decompiler built **half of target-typing and stopped**. It has the
natural-type half; it never built the target-type half or the conversion node that
consumes both. "Target-typing without the target or the conversion" necessarily
degrades into *the printer guessing* — which is the single root cause behind a long
tail of invalid-C# defects, none of them a bug in any one pass.

The missing member is a first-class **coercion**: a node, inserted during raising,
that already knows "I am the conversion of this value to type `T`," so the printer
renders a node instead of *deciding* to be a cast. Roslyn calls it
`BoundConversion`; ILSpy calls its result `.ConvertTo(T)`.

### Why a node, not a helper

The decompiler already has coercion *helpers* — `CastValue`
(`CSharpPrinter.Numerics.cs:925`) and `EnumIntegerCast`
(`CSharpPrinter.Numerics.cs:178`). They are not enough, because a helper the
printer *may* call is not an invariant. Because the coercion is a transient string
and not a node, it is invisible to the rest of the architecture:

- `CheckInvariant()` cannot assert "no value reaches a sink un-coerced."
- Fidelity cannot treat an unresolved coercion as an honest `Partial` signal, the
  way an unknown join type already is.
- No other pass can see, move, or reason about it.

Making the coercion a node — computed once, rendered by one rule, checked by one
invariant — is what converts *"discipline in every printer branch"* into *"one node
type."* The alternative is what we have: the same missing rule rediscovered one
render context at a time.

## Where we are — the leak surface, measured

The cast/coercion decision is spread across the printer, each site re-deriving
"an integer flowing into an enum position needs an explicit cast, and an
out-of-range constant needs `unchecked`":

| Site | File | What it decides locally |
| --- | --- | --- |
| enum-typed conditional arm | `CSharpPrinter.Numerics.cs` `ConditionalArm` | cast each integer arm to the enum target |
| retyped enum constant | `CSharpPrinter.cs:1745` | `(Enum)value`, **`int`-only** |
| `switch` case label | `CSharpPrinter.cs:3144` | `SwitchLabelText`, **`int`-only** |
| array-element store | `CSharpPrinter.cs:1665` | `CastValue(value, ElementType)` — uses the `stelem` storage type |
| `box` operand | `CSharpPrinter.cs:1811` | drops the boxed type entirely |
| enum bitwise / comparison | `CSharpPrinter.Numerics.cs` | cast the integer operand to the enum |
| overflow / `unchecked` | `CSharpPrinter.Numerics.cs:194` | `MayOverflow…`, **`Unknown`-shape only**, raw `Constant` only |
| constant typing | `TypedConstantsPass.cs:110` | retypes **`int`-only**, does not pierce `Convert` |

The italicised limits are the leak: each is a place the missing rule is
*partially* implemented. The consequence is a recurring, pre-existing defect class.
Six consecutive adversarial-review rounds on one PR (a fix originally scoped to a
single conditional-arm shape) each surfaced the *same* class in a *new* sink —
none a regression, all latent:

1. narrow-enum out-of-range constant → `(Tiny)300` (CS0221)
2. unsigned-enum negative high-bit constant → `... : -2147483648` (CS0029)
3. retyped enum constant in comparison / bitwise / `??` → `(E)(-1)` (CS0221)
4. `long` enum `switch` label → `case 1311768467463790320:` (CS0266)
5. `long`-backed enum in array element / `box` → bare `long` (CS0266 / CS0029)
6. unsigned `long`-backed enum via `conv.i8(ldc.i4.m1)` → `(UE)((long)-1)` (CS0221)

Every fix was correct and local; every next round found the next sink. That slope
— *N* near-identical fixes converging on nothing — is the value-flow analogue of
the control-flow "normalizer treadmill" that
[control-flow-structuring.md](control-flow-structuring.md) diagnoses. The two are
**orthogonal axes**: the adversarial review of that redesign
([gist](https://gist.github.com/richlander/a6f12e0ca8c426ee034be29a01b3f7a2))
explicitly found that the value-flow diamonds "move *data* across a join, not
control flow … those passes survive any structuring rewrite." A post-dominator
structurer does not touch this. This is the second lane, and it has not been
written down until now.

## Prior art — every compiler in the family solves this

The .NET compiler ecosystem resolves conversions **once, into typed IR, via a
single classifier**, and treats the back-end as a *total function* of typed input.
The decompiler is the only member that pushed the decision into its back-end.

### Roslyn — the forward direction, and the exact precedent

Roslyn faces the forward problem (source → IL): where does a conversion go, and is
it implicit, explicit, or an error?

- **Conversions are nodes, inserted at bind time.** One classifier
  (`Conversions.ClassifyConversionFromExpression`) decides; `Binder.CreateConversion`
  materialises a `BoundConversion` into the tree. Lowering and emit never *decide* a
  cast — they render already-typed nodes.
- **Constant conversions and `checked`/`unchecked` legality are folded in one
  place** (the binder's constant-conversion folding), which is exactly what the
  scattered `MayOverflow…` heuristic re-implements.
- **Target-typed conditional / `switch` expressions (C# 9) are our bug, verbatim.**
  Roslyn used to give `a ? b : c` a *best common type* and error (CS0173) when none
  existed. It was reworked so a `BoundUnconvertedConditionalOperator` stays
  **untyped** until a target type is applied, then one conversion resolves it. The
  decompiler's `Conditional.MergedType`-is-null-so-fall-back-to-the-first-arm
  failure is *precisely* the "best common type is insufficient; carry it unconverted
  and target-type it" lesson.

The decompiler's job is Roslyn's binder **in reverse**: given a typed value and a
target type, spell the *minimal* C# conversion that `ClassifyConversion` maps back to
the original IL. "Invert the conversion classifier" is a real north star.

### RyuJIT — the type-propagation prerequisite

The JIT (IL → machine code) never spells C#, but it *does* reconstruct types from
IL's loosely-typed stack, which is our prerequisite:

- The importer normalises the `int32`/`int64`/`native int` stack into a typed
  `GenTree`, inserting explicit `GT_CAST` nodes; conversions become IR at import,
  never re-decided at codegen.
- At join points it **merges stack types to a common supertype and spills to typed
  temps** — the same operation as our slot-merge-at-join, which today drops to
  `Unknown` and taints fidelity. The JIT's spill-temp typing is the model for doing
  it completely.

### ILSpy — the same-domain existence proof

ILSpy raises IL → C# and has exactly the abstraction we lack: a
**`TranslatedExpression`** that carries the expression *and* its resolved type, with
a single `.ConvertTo(targetType, …)` method that is *the* coercion site. Its
`ExpressionBuilder` never open-codes a cast; it produces a translated expression and
converts it. This is the closest working reference and the most convincing proof the
choke point is achievable under the IL→C# constraint.

### Ghidra — the non-.NET cross-check

Ghidra's decompiler propagates data types through P-code as a dataflow lattice and
inserts casts through a single `CastStrategy`. Different language, same shape:
propagation feeds one cast-insertion policy.

## The capability — two halves of one type

### 1. Target type on every sink

Each value-consuming position declares the type it expects. Most IR nodes already
carry it structurally (a `StoreElement` knows its array's element type; a `Box`
knows the boxed type; a `Return` has the method's return type) — the gap is that
the printer sometimes reads the *storage-opcode* type instead of the *semantic*
type (the `stelem.i8` → `long` defect). Where a sink's target is genuinely
implicit, it is derived once and attached, not re-derived per print.

### 2. `Coerce(value, targetType)` — the node

A single C#-surface coercion node, distinct from the existing `Convert` node.
This distinction is load-bearing: `Convert` models the value's **IL history**
(`conv.i8`); `Coerce` models the **C# rendering conversion** needed at a target. The
new node is inserted during raising (or synthesised at the emission boundary) and
owns, in one place, the rules the printer currently spreads around:

- implicit vs. explicit conversion (only literal `0` converts to an enum bare);
- `unchecked(...)` for a constant that overflows the *backing* width, whether the
  backing is known, cross-assembly-`Unknown` (conservative), or an
  `Enum`-shape-without-`value__` (assume C#'s default `int`);
- enum ↔ underlying, numeric widening, `null`-literal typing;
- seeing through a widening `conv.i8`/`conv.u8` over an integer constant (small and
  unsigned `long`-backed enum members lower this way).

The rendering rule is one total function of `(value, targetType)`; the printer
calls it and never open-codes `(T)x` again.

### The invariant

With the node in place, well-formedness becomes checkable: **no value may occupy a
typed sink except through a `Coerce`** (or be provably already at the target type).
`CheckInvariant()` asserts it; a violation fails at the pass level, in a unit test,
instead of being discovered by recompiling corpus output. The compile-back oracle
stops being the *only* place this class is caught.

## Scope and constraints

This stays inside the decompiler's deliberate ceilings:

- **SRM-only, no assembly loading.** A cross-assembly enum resolves to
  `TypeShape.Unknown` and its backing width is not available. `Coerce` must render a
  faithful, conservative cast for that case (it cannot become a member name), never
  load the defining assembly. This is a permanent input, not a gap to close.
- **Not SSA, not Hindley-Milner.** The prerequisite is *propagation* over the
  existing shallow-stack IR — retype constants (including `long` and
  `Convert`-wrapped) into every typed sink, complete the join-merge typing — not a
  general inference engine. The IR stays the readable-output-first model ILSpy uses.
- **Faithfulness over prettiness.** A valid cast that recompiles to the same IL is
  the floor; a member name is a bonus the node can add when the value is a named,
  non-`Convert`-wrapped constant.

## Migration — incremental, with coverage proven

The redesign is landable in slices *because* the invariant makes coverage
measurable, unlike the control-flow rewrite's all-or-nothing invariant relaxation.

1. **Introduce `Coerce` and the one rendering function.** Fold `CastValue`,
   `EnumIntegerCast`, `SwitchLabelText`, the retyped-constant branch, `Box`, and
   `StoreElement` into it. No behavior change intended; the corpus fidelity card is
   the guard.
2. **Complete constant typing.** Extend `TypedConstantsPass` to retype `int`,
   `long`, and `Convert`-wrapped integer constants into every enum-typed sink, so
   the printer sees enum-typed values uniformly.
3. **Turn the invariant on.** Assert every typed sink routes through `Coerce`;
   burn down the violations it flags (these are the remaining leak sites, now
   enumerated by the checker rather than by adversarial review).
4. **Complete join typing** where the importer drops to `Unknown` but a sound
   common type exists (the JIT spill-temp model), reducing `Partial`-by-unknown-join.

Each slice reports the standard decompiler-affecting-PR evidence: focused tests,
the corpus quality-diff card, and improved/still-flat examples.

## Acceptance and start-trigger

Consistent with [control-flow-structuring.md](control-flow-structuring.md)'s
insistence on a falsifiable trigger rather than a standing intention:

- **Start** slice 1 when the value-flow treadmill is confirmed — which the six-round
  history above already demonstrates. This lane is *ready to start*, not deferred.
- **Done** when the invariant (step 3) is enforced in `CheckInvariant()` and green
  across the corpus: at that point the class cannot recur silently, because a new
  un-coerced sink fails a unit test, not a recompile.
- **Explicitly out of scope** and tracked separately: member-naming of
  `Convert`-wrapped constants (a naming nicety, not a validity gap) and any
  cross-assembly enum backing that would require loading the defining assembly.

The measure of success is not a fully-raised delta — it is that "run adversarial
review until clean" would converge in **one** round, because there is one place to
get right instead of a dozen.
