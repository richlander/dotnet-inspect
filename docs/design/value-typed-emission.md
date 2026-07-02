# The thin writer: value-typed emission

This document scopes a major investment in how the decompiler's **writer** — the
`CSharpPrinter` that turns raised IR into C# text — earns its keep. The thesis: the
writer is **too thick**. It does not merely *spell* a decided tree; it makes
semantic decisions the IR never recorded, and rediscovers each one — wrongly — one
render context at a time. This doc defines the end state (a **thin writer**: a total
function of a fully-typed IR), the invariant that enforces it, and the instances of
thickness to remove under it, largest first.

It is the value-flow sibling of
[control-flow-structuring.md](control-flow-structuring.md). Where that doc governs
how blocks become nested `if`/`else`, this one governs how a *decided* value reaches
the page.

Read [decompiler.md](../decompiler.md) first for the pipeline shape and the
recognizability goal.

## The thin-writer invariant

A thin writer makes **zero semantic decisions**. It renders a tree that has already
been decided — every value carries its resolved type, every conversion is a node,
every local is materialized — and its only job is **surface spelling**: precedence
and parenthesization, identifier escaping, layout, and the C# text of an
already-decided node. Those stay; *thin is not logicless*.

What must leave the writer is every place it *decides* rather than *spells*. Each is
the same illness — the writer inferring, at print time, something the typed IR
should already carry — and each has leaked the same way: correct in the context it
was written for, wrong in the next one.

| Instance | The writer currently decides… | It should be… | Field evidence |
| --- | --- | --- | --- |
| **1. Coercion** (flagship) | whether/how to cast a value to a target type, and when to wrap `unchecked` | a `Coerce` node + one renderer | the six-round enum-cast history below |
| **2. Stack-slot materialization** | which locals exist, their types, and when to split one slot into two | typed local IR nodes from type propagation | #2075 — the `S_0`/`S_256` collapse |
| **3. Definite assignment** (later) | which locals need `= default` | a pre-print flow pass handing over a decided tree | #631 (partial) |

Precedence, escaping, layout, node-spelling are deliberately *not* on this list —
they are the writer's real job.

The invariant that makes "thin" checkable: **no value reaches the writer un-decided**
— every typed sink routes through a `Coerce` (instance 1) and every rendered local is
a materialized, typed IR node (instance 2) — asserted by `CheckInvariant()`. A
violation fails a unit test, not a recompile.

The rest of this document details **instance 1 (coercion)** in full — it is the
largest, most bug-dense, and the template for the others — then **instance 2 (slot
typing)**, which the same type-propagation prerequisite removes almost for free.
Instance 3 is noted where it sits and deferred. A second, orthogonal axis of
thinness — the writer's *output* being structure rather than strings — is scoped
in [the output half](#the-output-half--structure-not-strings) below.

## Instance 1 — coercion: the missing member of the type system

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

The decompiler already has coercion *helpers* — `Coerce` (slice 1's rename of
`CastValue`, `CSharpPrinter.Numerics.cs`) with its family (`EnumConstantText`,
`TryCoerceEnumOperand`, `EnumIntegerCast`). They are not enough, because a helper the
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

The cast/coercion decision lives in one family in `CSharpPrinter.Numerics.cs`
(slice 1, #2114): `Coerce(value, target)` for typed sinks,
`TryCoerceEnumOperand` for operands meeting an enum sibling or arm target, and
`EnumConstantText` (the name-or-cast rule) with the `EnumIntegerCast`/
`MayOverflowEnumBackingType` internals — which handle known backing widths,
cross-assembly `Unknown` (conservative sbyte), missing-`value__` shapes (assume
`int`), and see through widening `Convert`s via `TryEnumCastLiteral`. Those
rules are the hard-won product of the six-round history below (#2080); the
guard drift that consolidation surfaced (the binary/comparison sites admitted
`bool` where the arm sites excluded it — an invalid-C# class) is fixed inside
the one operand rule. What stays scattered is the **routing**: each sink still
independently decides *to* call the family, and *which target type* to hand it:

| Site | What it still decides locally |
| --- | --- |
| enum-typed conditional / switch arm | route the arm through `TryCoerceEnumOperand` |
| `??` coalesce right side | route through `TryCoerceEnumOperand` |
| enum bitwise / comparison operand | pick the enum side, route the other through `TryCoerceEnumOperand` |
| compound assign right side | route through `TryCoerceEnumOperand` with the lvalue type |
| retyped enum constant | route through `EnumConstantText` |
| `switch` case label | route through `EnumConstantText` |
| array-element store | derive the semantic element type (`StoreElementTargetType`), route through `Coerce` |
| `box` / `return` / call args / stores | route through `Coerce` with the sink's declared type |
| constant typing | `TypedConstantsPass` retypes **`int`-only**, does not pierce `Convert` |

Every row is a call site that must *remember* to route, with the right target
type — a sink added or reshaped without the call silently bypasses the rules,
and nothing checks. The residual partial rule (`TypedConstantsPass` is
`int`-only) is migration step 2; the missed-call class is what the invariant
exists to catch. This is how the class recurred: six consecutive
adversarial-review rounds on one PR (#2080, a fix originally scoped to a single
conditional-arm shape) each surfaced the *same* class in a *new* sink — none a
regression, all latent:

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
  scattered `MayOverflowEnumBackingType` heuristic re-implements.
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
  `GenTree` — retagging integer constants in place and inserting `GT_CAST` for other
  narrowing/sign-changing inputs; conversions become IR at import, never re-decided
  at codegen.
- At join points it **spills non-empty stack slots to shared typed temps**, and for
  a few supported conflicts (`int`/native-int/byref, `float`/`double`) it upgrades
  the temp and reimports the clique or inserts casts on the narrower inputs. This is
  *targeted* conflict repair, not a general supertype algorithm — but it is the same
  operation as our slot-merge-at-join, which today drops to `Unknown` and taints
  fidelity, and the typed-temp model is how to do it more completely.

### ILSpy — the same-domain existence proof

ILSpy raises IL → C# and has exactly the abstraction we lack: a
**`TranslatedExpression`** that carries the expression *and* its resolved type, with
`.ConvertTo(targetType, …)` as its **primary, de-facto coercion choke point**.
`ExpressionBuilder` defers casts to it pervasively and only open-codes a
`CastExpression` in a few genuinely semantic cases (delegate/lambda, some
unbox-related paths), and there is a sibling `ConvertToBoolean` for the truthiness
case. It is not literally one method — but the discipline (a typed expression with a
single dominant conversion entry point) is exactly the shape we want, and it is the
most convincing proof the choke point is achievable under the IL→C# constraint.

### Ghidra — the non-.NET cross-check

Ghidra's decompiler runs **monotone type propagation over its data-type ordering**
(`ActionInferTypes`, not a formal meet/join lattice) and inserts casts through a
**language-selected `CastStrategy` implementation** (`CastStrategyC`,
`CastStrategyJava`). Different language, same shape: propagation feeds one
cast-insertion policy per target language.

## The coercion capability — two halves of one type

### 1. Target type on every sink

Each value-consuming position declares the type it expects. Some sinks carry it
directly (a `Box` knows the boxed type; a `Return` has the method's return type),
but others **do not, and this is part of the work**: `StoreElement.ElementType` is
often the `stelem.*` *storage* type (`Int64` for `stelem.i8` into an enum array,
because the importer only prefers the array element when widths match), so the
semantic target must be *derived* from `array.ResultType`'s element type, distinct
from the opcode storage type. The rule is: every sink resolves a *semantic* target
type once — from the array/field/parameter/return type, not the storage opcode —
and attaches it, rather than the printer re-deriving (and sometimes mis-reading) it
per print.

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
  unsigned `long`-backed enum members lower this way);
- **lexical `checked`/`unchecked` context.** The same value and target need a bare
  `(T)x` outside a `checked` region but `unchecked((T)x)` inside one — otherwise a
  narrowing/sign-changing constant cast silently recompiles to `conv.ovf.*` the IL
  never had (today handled by `CheckedSafeCast` / `_checkedContext`). So the render
  is not total on `(value, targetType)` alone.

The rendering rule is one function of `(value, targetType, checkedContext)` — the
checked context either threaded in or captured on the node — and the printer calls
it and never open-codes `(T)x` again.

**`Convert` and `Coerce` compose; they do not merge.** At a sink where the value
already carries a `Convert` (an IL `conv.i8`) *and* the target type mismatches,
raising **wraps** the `Convert` in a `Coerce` — it does not rewrite one into the
other. Leak case #6 is exactly this: `Coerce(Convert(long, ldc.i4.m1), UE)` renders
`unchecked((UE)((long)-1))`, where the inner `Convert` keeps the value's IL history
(`(long)-1`) and the outer `Coerce` owns the surface conversion to the enum and the
overflow decision. Keeping them as separate, nesting nodes — rather than folding the
IL conversion into the rendering one — is what lets the overflow rule see through the
`Convert` to the literal without losing the faithful IL spelling.

### The invariant

With the node in place, well-formedness becomes checkable: **no value may occupy a
typed sink except through a `Coerce`** (or be provably already at the target type).
`CheckInvariant()` asserts it; a violation fails at the pass level, in a unit test,
instead of being discovered by recompiling corpus output.

The exemption is itself a choke point. "Provably already at the target type"
must be **one shared type-identity predicate**, not a per-sink judgment —
otherwise the scattered-partial-rule problem reappears one level up, as
identity checks with per-sink blind spots (`int`-only here, `Unknown`-blind
there) exempting exactly the sinks that need coercion. One `Coerce` renderer,
one identity predicate that gates skipping it.

This proves **routing, not rendering**: the invariant guarantees every sink *reaches*
the one coercion function, collapsing the leak surface from ~12 sites to one — but it
does not prove that function's *output* is correct. That output is still validated by
the coercion function's own unit tests and the compile-back oracle — the
**ReturnToSender** harness (`tools/DecompilerHarness/ReturnToSender.cs`), which
recompiles decompiled output and A/B-compares against the current pipeline. The win
is that the oracle stops being the *only* place an un-coerced sink is caught, and a
new sink can no longer silently bypass the rule.

## Instance 2 — stack-slot materialization and typing

The same illness, a different organ. The writer does not only decide *conversions*;
it decides *which locals exist and what type they are*. `TryChooseUnifiedStackSlotType`
and `StackSlotName` invent the `S_0`/`S_256` variables from IL stack-slot positions
and, at print time, **unify or split** their types — picking one C# type for a slot
reused across live ranges, or splitting it into two variables when the types
conflict. That is a semantic decision (SSA-value identity and typing) made in the
writer, from opcode-stack bookkeeping the IR never resolved into locals.

It fails the same way coercion does. **#2075** was exactly this: a stack slot reused
for an `int` value and a `BindValueKind` (enum) value; the writer's unifier collapsed
both onto one `int` local, and the enum use then rendered without a cast — invalid
C#. The shape that produced the enum-cast leaks produced a *variable-identity* leak,
because the same component was guessing.

The fix is the coercion redesign's own prerequisite, reused: once **type propagation**
(instance-1 migration step 4 — the RyuJIT typed-temp model) materializes each slot's
live ranges as **typed local IR nodes** before printing, there is nothing left to
unify. The writer stops inventing variables; `TryChooseUnifiedStackSlotType` is
deleted, and the thin-writer invariant extends to "every rendered local is a
materialized IR node," checkable the same way. This is why instance 2 rides on
instance 1: they share the type-propagation spine, so instance 2 is *mostly the
deletion* of print-time typing once propagation exists — not a second engine.

## Instance 3 — definite assignment (noted, deferred)

The writer also decides which locals need `= default` to satisfy C# definite
assignment (the `#631` flow walk over the printer's `_facts`). This is a third
flow analysis — the sibling of control-flow structuring and type flow — and it is
*thin-writer-adjacent*: defensible where it is, but strictly it could run as a
pre-print pass that hands the writer a decided tree. It is the lowest-priority
instance and is called out here only so the umbrella is complete; it is not
sequenced below.

## The output half — structure, not strings

Thinness has a second axis. The instances above remove *decisions* from the
writer; this section names where its *output representation* is headed. Today the
printer synthesizes strings all the way down, and so does everything downstream
that needs C# structure: precedence and parenthesization are per-call-site
judgments (the `Operand()` vs `Expression()` distinction), and ReturnToSender's
source composition must re-parse and patch rendered text
(`CompileBackCSharpNames.Clean` string-strips `modreq(...)` and re-spells
`System.Int32` as `int`) because a string is the only seam the writer offers.

The direction is already in motion on the declaration side (#2057): the
structured signature model (`ApiSignature`, surfaced as `ApiMember.SignatureModel`)
is built during extraction and queried for summaries and identity instead of
reparsing rendered signatures, and RTS compile-back relies on product-owned
declaration composition. The body side should meet it. The end-state writer
splits into a **composer** (decided IR → a structured C# surface model; a
`Coerce` becomes a cast *node*, not cast *text*) and a **renderer** (surface
model → text, total and mechanical). Declarations from metadata and bodies from
the decompiler then meet in one structured surface, which RTS composes without
string surgery.

This is, again, the family pattern. Roslyn keeps syntax a model until the very
end. ILSpy prints C# from an AST via `CSharpOutputVisitor`, with parenthesization
inserted by a dedicated `InsertParenthesesVisitor` pass over the tree — even the
"writer's real job" (precedence, parens) becomes a checkable pass rather than
per-site string logic.

Sequencing: this axis does not change the migration order. A structured output
layer without decided semantics merely relocates the guessing — the same sinks
would decide whether to *build* a cast node instead of whether to *emit* `(T)x`.
Value-typed emission is the prerequisite. It does change the shape of the seams:
the one `Coerce` rendering function must be the only place cast text is born, so
that when the surface model arrives it becomes a node factory without
re-scattering the decision. The surface-model migration is its own future design
note; this doc pins only the constraint that nothing in the slices below may
widen the printer's string seam — new emission logic funnels through the
choke-point functions that a composer can later replace node-for-node.

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

1. **Promote `CastValue` into `Coerce`, one rendering function.** Landed
   (#2114): `Coerce` is the typed-sink renderer, `EnumConstantText` the one
   name-or-cast rule (switch labels, retyped constants, enum sinks), and
   `TryCoerceEnumOperand` the one enum-operand decision — with
   `EnumIntegerCast`/`MayOverflowEnumBackingType` as private internals. The
   consolidation was measured, not behavior-neutral, exactly as predicted: it
   surfaced and fixed a guard-drift invalid-C# class (bool operands at enum
   positions) and extended member naming to un-retyped constants, with the
   corpus card flat against a same-corpus base A/B.
2. **Complete constant typing.** Landed (#2120): `TypedConstantsPass` retypes
   `int`, `long`, and unchecked 8-byte-widening `Convert`-wrapped constants into
   enum-typed sinks (semantic array-element targets, either-side comparisons,
   `??=`/property stores, enum-merged conditional arms), with a third pipeline
   run after reconstruction for late-created sinks. The `ldc.i4; conv.i8`
   lowering of long/ulong-backed enum constants now names its member —
   `CfgULong.All`, not `unchecked((CfgULong)((long)(-1)))`. Switch labels stay
   printer-spelled (`EnumConstantText`): `SwitchSection` holds them outside the
   rewritable tree.
3. **Turn the invariant on.** Assert every typed sink routes through `Coerce`;
   burn down the violations it flags (these are the remaining leak sites, now
   enumerated by the checker rather than by adversarial review). Slices 1–3 deliver
   the coercion choke point and can land without step 4.
4. **Complete join typing — the shared type-propagation spine** (worth its own
   slice/issue). Where the importer drops to `Unknown` but a sound common type
   exists, propagate types at joins (the RyuJIT typed-temp model), reducing
   `Partial`-by-unknown-join. This is the prerequisite *both* instances lean on:
   it feeds instance 1's constant/`Convert` typing and is what instance 2 needs to
   materialize locals.
5. **Materialize stack-slot locals (instance 2).** On the step-4 propagation, emit
   each slot's live ranges as typed local IR nodes and **delete
   `TryChooseUnifiedStackSlotType`** — the writer stops inventing and unifying
   variables. Extend the invariant to "every rendered local is a materialized IR
   node." Mostly a deletion once step 4 lands.

Each slice reports the standard decompiler-affecting-PR evidence: focused tests,
the corpus quality-diff card, and improved/still-flat examples. As ReturnToSender
coverage grows, compile-back-affecting slices add its A/B evidence per
[docs/templates/decompiler-compile-back-harness-pr.md](../templates/decompiler-compile-back-harness-pr.md).

## Acceptance and start-trigger

Consistent with [control-flow-structuring.md](control-flow-structuring.md)'s
insistence on a falsifiable trigger rather than a standing intention:

- **Start** slice 1 when the value-flow treadmill is confirmed — which the six-round
  history above already demonstrates. This lane is *ready to start*, not deferred.
- **Instance 1 done** when the coercion invariant (step 3) is enforced in
  `CheckInvariant()` and green across the corpus: at that point the cast class cannot
  recur silently, because a new un-coerced sink fails a unit test, not a recompile.
- **Thin writer done** when the invariant covers both instances — every typed sink
  through a `Coerce`, every rendered local a materialized IR node (steps 4–5) — so the
  writer is a total function of a decided, fully-typed IR. Instance 3 remains an
  optional later slice.
- **Explicitly out of scope** and tracked separately: member-naming of
  `Convert`-wrapped constants (a naming nicety, not a validity gap) and any
  cross-assembly enum backing that would require loading the defining assembly.

The measure of success is not a fully-raised delta — it is that the writer stops
being a place bugs can hide: "run adversarial review until clean" converges in **one**
round, because there is one place to get each decision right instead of a dozen, and
`CheckInvariant()` — not a recompile of corpus output — is where a regression is
caught.
