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

### Reference-coalesce assignment testimony

Reference-coalesce assignment is a pre-print decision (#8105). Its evidence
is an assignment type, not the left operand's IL result type and not a promise
that every expression has a known C# natural type. The bounded relation admits
null with a proven reference, equal proven reference types, and a proven
reference paired with `object`. Unknown reference shapes, hierarchy
conversions, variance, boxing, and user-defined conversions do not acquire
proof from this rule. Nullable/value coalesces retain their existing contract.

The expression carries the decision into residual-slot assignment checks.
Binding runs at the final coercion boundary, after expression reconstruction
and existing slot materialization.
Both raised and lowered pipelines, including nested bodies, use that shared
path. Printing consumes the issued type rather than inspecting coalesce arms.
An object-typed call or construction argument whose proven coalesce assignment
type is narrower retains an explicit reference-conversion witness; allowing
assignment is not permission to rebind an overload (#3135).

This bounded C# assignment relation does not replace the existing exact-storage
admission contract or broaden its producer-result testimony. For example,
Roslyn's `IEqualityComparer<T>` producer `comparer ?? EqualityComparer<T>.Default`
continues to materialize under that existing contract; this slice does not
push it back into residual-slot inference merely because the new bounded
relation does not model its hierarchy conversion.
`CompilerProducedComparerKeepsExistingStorageAdmission` gates this non-action
with the same interface/default-comparer relation in a cached-result scenario.
Hierarchy-aware binding and broader storage-proof work remain on #2095.

The motivating published input is dotnet-inspect.any 0.14.0,
`ApiOutputFormatter.FormatCallGraphAnnotation` (`0x06000ED9`). Its coalesce
producer and object-typed null share a string-observed slot. Deciding the
coalesce does not, by itself, authorize materializing the separate null
producer. Printer decision retirement and slot-count reduction are distinct
measurements.

The focused Release gate is `ReferenceCoalesceBindingTests`, covering
compiler-produced reference/null and reference-to-object cases, the pinned
producer shape, overload binding, nested bodies, lowered output, and unknown
non-actions, plus Roslyn's real `AnalyzerImageReference.Display` coalesce.
Native product-artifact compile-back checks the direct binding outcomes;
nested lambda and local-function cases retain their measured generated-identity
limits rather than claiming exact fidelity. Fixed-input residual/unifier
censuses and Render A/B measure the population effect separately.

### Reference-conditional assignment testimony

Reference/null conditional arm compatibility is decided before printing
(#8181). This is a set of accepted reference targets, not one C# natural type:
all-null arms admit any proven reference, and a nested conditional can retain
both its arm evidence and its existing merged-result fallback. Binding
intersects the arms' accepted targets without inferring class hierarchies.
Unknown targets do not acquire reference proof.

The final emission boundary binds after reconstruction and materialization,
with coalesce assignment testimony already available. Raised, lowered, and
nested bodies share that path; detached reconstruction bodies wait for their
host's final binding. Cloning retains issued testimony, and final binding
refreshes it after operand rewrites. Printing queries the issued evidence
rather than walking conditional arms to recover the reference decision.

This retirement preserves existing numeric, char, and enum rendering,
merged-result fallback, and exact-storage admission. It neither changes
`Conditional.ResultType` nor substitutes a narrower assignment type for it.
General reference conversions and conditional overload-binding repair remain
separate work; target compatibility alone does not authorize either.

Newtonsoft.Json 13.0.4's `IsoDateTimeConverter.set_DateTimeFormat` is the
published witness (#1767): the null/string producer and string field consumer
must continue to share one assigned local. `ReferenceConditionalBindingTests`
pins that assembly and gates raised/lowered binding, nested alternatives,
null/unknown/value boundaries, clone/refresh, and storage non-actions in
Release. Its Slow native family belongs to Deep Inspect and the focused
pre-merge gate; the setter retains its measured temporary-induced `OpcodeDiff`
rather than claiming exact fidelity. Fixed-input censuses and Render A/B
measure population effects; the retirement does not promise fewer residual
slots.

### Primitive-join target testimony

Primitive integer-family join compatibility is decided before printing
(#2095). One bounded relation serves `Conditional`, `SwitchExpression`, and
`Coalesce`. It records whole-join compatibility and, for each target whose
rendered arms are accepted, the effective source type that licenses
target-aware arm rendering. Rendered-arm testimony is issued only for an actual
retarget; when source and target are equal, arm spelling continues to use each
arm's own effective type and cannot acquire a narrowing cast from the join
source. Conditional and switch expressions evaluate the same arm set for both
facts. A coalesce's whole-join check observes both operands, while its
rendered-arm testimony observes only the right operand; keeping both facts
preserves that existing asymmetry instead of broadening coalesce targetability.
This relation is neither a replacement for the join's result type nor a general
C# conversion classifier.

The relation admits an integer-like source and target only when the existing
slot-coercion contract can spell that source at the target and every arm is one
of the following:

- an `int` or `long` constant, whose eventual spelling remains value-aware;
- an existing `Coerce`, whose operand can be retargeted by the established
  join-arm rule; or
- a value with an effective C# type that is already implicit at the target,
  has the existing boolean-to-integer spelling, or has a spellable numeric
  coercion at the same source/target slot width.

Missing type evidence, a differing-width reinterpretation, a floating-point or
reference target, and any conversion outside the existing coercion contract
decline. Enum and `char` rendering retain their dedicated earlier routes; this
testimony must not preempt member naming, character literals, or their existing
declines. Constant fit, the join-level bare-arm/natural-type policy,
checked-context wrapping, precedence, and layout remain spelling decisions.
The relation does not authorize storage materialization or alter overload
binding.

Binding runs at the final emission boundary after reconstruction and
materialization. Raised, lowered, and nested bodies share that path; detached
reconstruction bodies wait for their host's final binding. Cloning retains
issued testimony, and final binding refreshes it after operand rewrites.
Printing queries the issued target/source pair rather than walking the join
arms to recover compatibility, including the separately issued coalesce
right-arm fact. The printer-owned
`CanRenderPrimitiveJoinForTarget` and its arm-list capability checks are deleted;
actual arm and literal spelling stays in `CSharpPrinter`.

The pinned real witness is Microsoft.CodeAnalysis 5.0.0
`Microsoft.CodeAnalysis.BitVector.get_Item` (`0x060009AF`):
an `Int64`-merged conditional flows to an unsigned 64-bit call parameter and
must remain `i < 0 ? _bits0 : (ulong)_bits[i]`. On the fixed 14-assembly,
89,065-method corpus, the pre-change relation accepts 17 retargets with zero
measurement failures; 8 are already owned by the earlier `char` route, leaving
9 live primitive-gate witnesses. No switch-expression or coalesce retarget is
live in that corpus, so their coverage is a shared-contract guard, not a
population claim. Focused Release tests cover all three consumers,
same-width signedness, constant behavior, boolean arms, differing-width and
missing-type declines, enum/`char` non-preemption, clone/refresh, raised/lowered
output, and the pinned witness. Fixed-input censuses and Render A/B measure
population effects separately.

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
The two nodes represent different facts: `Convert` models the value's **IL
history** (`conv.i8`); `Coerce` models the **C# rendering conversion** needed at
a target. The new node is inserted during raising (or synthesised at the
emission boundary) and owns, in one place, the rules the printer currently
spreads around:

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

With the node in place, well-formedness is checkable: **no value may occupy a
typed sink except through a `Coerce`** (or be provably already at the target
type). `CoercionInvariant.Check` asserts it; a violation fails at the pass
level, in a unit test, instead of being discovered by recompiling corpus
output. Three structural properties fix what the assertion covers:

- **One sink model.** `CoercionSinks.Enumerate` is the single enumeration of
  typed sinks and their semantic targets; the insertion pass wraps through it
  and the checker asserts through it, so the two cannot disagree about what a
  sink is. The sink set grows in one reviewed place.
- **One exemption predicate.** "Provably already at the target type" is
  `CoercionDomain.IsAtTarget` — never a per-sink judgment, or the
  scattered-partial-rule problem reappears one level up as identity checks
  with per-sink blind spots exempting exactly the sinks that need coercion.
- **A declared domain.** The invariant owns the value-flow class this lane
  treats — integer-family primitives, `bool`/`char`, and resolved enums
  (`CoercionDomain.InDomain`). Reference and struct conversions join when an
  inverse conversion-classifier exists; a cross-assembly `Unknown` definition
  cannot be *proven* an enum, so it stays render-time-handled and outside the
  checkable domain.

The assertion's reach is bounded on purpose, and the bound is **measured, not
silent** (#2145). `CoercionInvariant.Audit` returns two things: **violations**
(in-domain, wrappable sinks without a `Coerce` — gates assert zero) and
**counted residuals** — in-domain mismatches the scope deliberately leaves to
later or targeted deciders, keyed by value kind. Merge-node arms at non-enum
in-domain joins are enumerated as `PrinterOwned` sinks: the pass does not wrap
them, but the checker sees and counts them, so "0 violations" can never be
misread as covering them. The remaining residuals — slot loads (instance 2's
lane), `Box` operands, `StoreIndirect` targets, `switch` labels, lambda
returns — stay outside the enumeration with their reasons documented at
`CoercionSinks`. What the checker guarantees is **routing agreement** for
wrappable sinks plus a visible residual ledger for the rest; each residual is
printer-owned — rendered by its own `CoerceText` branch — until it graduates
into the enumeration and its count goes to zero.

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

### Full-expression contexts

Conditional arms and compound-assignment right-hand sides accept a complete
expression, not an atomic operand. Their fallback spelling therefore does not
add an outer pair of parentheses. Required grouping inside that expression,
type coercions, checked/unchecked contexts, and evaluation order remain owned
by their existing renderers and decided IR. This is a spelling adoption under
issue #2376, not a new raising rule or a claim to recover redundant authored syntax.

The motivating real witness is `System.Data.SqlTypes.SqlBytes.MaxLength`:
its conditional cast arms need their cast syntax, but not another enclosing
pair. `OrderedBinarySpillSamples.SnapshotAcrossMutation` supplies the
compiler-produced mutation boundary. `ExpressionContextParenthesesTests`
gates these full-expression contexts and retains required nested grouping;
the existing ordered-spill native gate covers compound-update fidelity.
Both CLI and Browser/Wasm consume the shared printer in this same adoption
step. Other operand contexts retain their existing precedence policy.

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
3. **Turn the invariant on.** Landed: the `Coerce` node,
   `CoercionInsertionPass` (pipeline-last), and `CoercionInvariant.Check`, with
   one shared `RequiresCoercion` decision so pass and checker cannot drift.
   Corpus evidence: 15 assemblies, 134,373 methods — 0 violations, 7,954
   `Coerce` nodes routed across 3,225 methods (active, not vacuous), and a
   **render-text A/B against the merge base** in which every one of the 17
   changed methods is a verified fidelity fix (wrong `op_Implicit`
   overloads/values at call arguments, CS1929 extension receivers, CS0266
   iterator yields) — neutral on common sinks, correcting
   previously-unfaithful casts at call-argument sinks. The insertion domain is
   deliberately "sinks whose printer rendering is provably CoerceText with the
   same target"; everything else is an enumerated residual docket:
   slot-carried values (the unifier owns their type until instance 2), lambda
   returns (delegate `Invoke` signature not yet resolved), merge-node values
   (CoerceText's own targeted branches render them, statement-position
   formatting included; primitive same-family `Conditional` values route
   through `CoercionRendering.CanSpellSlotCoercion` and distribute target
   casts into arms), `Box` operands (the unbox-over-box spelling renders
   through `ConvertText`, and a bare constant under `(object)` boxes the
   literal's own type), `StoreIndirect` targets (printer's `IndirectStoreType`
   not yet shared), `switch` labels (outside the rewritable tree), and operand
   positions (`TryCoerceEnumOperand` — reconciliation, not sinks). Slices 1–3
   deliver the coercion choke point; step 4 is independent — and the
   longer-term end state is Roslyn's: establish the invariant **at
   construction** (importer emits coerced trees) rather than by a
   pipeline-last mutating pass.
4. **Complete join typing — the shared type-propagation spine.** Opened: the
   importer's `MergeSlotTypes` types an enum-meets-integer join as the enum
   **only when the integer side is exactly the enum's underlying type** —
   nominal equality, so width and sign exact, same-assembly resolved enums
   only, bool excluded. The merge is then a pure reinterpretation and every
   downstream sink coercion is value-preserving. Two adversarial rounds set
   that bar: a family-level match let a byte-backed enum absorb a full-int
   path (`(BE)x` turned 300 into 44, marked Full — the
   recompiles-to-a-different-program class), and the cross-assembly
   pairing-proves-enum argument was dropped entirely because it proves the
   family but never the width. The printer side gained `TryCoerceJoinArm` —
   the one join-arm rule, both directions, serving conditional, switch-
   expression, and coalesce arms alike, family-guarded so the underlying cast
   can never truncate. Join-census over the 15-assembly corpus:
   `Partial`-by-unknown-join methods 155 → 119; the enum/int bucket for
   same-assembly int-backed enums is zero. Remaining, by census: cross-assembly
   enum-likes (width unprovable — recoverable later only with sink-context
   evidence), the reference-merge lane (cross-assembly base chains and
   constructed-generic interfaces, SRM-boundary work), and the `void*`/`nuint`
   native clique. The full RyuJIT typed-temp model (spill and re-import) stays
   open here for instance 2.
5. **Materialize stack-slot locals (instance 2).** On the step-4 propagation, emit
   each slot's live ranges as typed local IR nodes and **delete
   `TryChooseUnifiedStackSlotType`** — the writer stops inventing and unifying
   variables. Extend the invariant to "every rendered local is a materialized IR
   node." Mostly a deletion once step 4 lands. In progress: 5a landed slot
   reconciliation (`TestifiedSlotTypes` — the one slot-evidence rule: typed
   loads contribute their type, untyped loads their sink target, underivable
   or disagreeing loads veto); 5b-1 landed slot stores as typed sinks with the
   total same-family renderer; 5b-2 lands `SlotMaterializationPass` — decided
   in-domain function-scope slots become typed locals (keeping their `S_n`
   names, so the change is render-neutral up to declaration form) **before**
   coercion insertion, which discharges the minted loads' sink obligations
   like any local's. What stays on the print-time unifier is the counted
   residual: ambiguous testimony, cross-family (true disjoint ranges),
   unproven element-store identity recovery, incomplete
   slot-copy components, and nested `Lambda`/`LocalFunctionStatement` scopes.
   The nested-name prerequisite #2275 landed in #2356. Lambda and local-function
   raising complete the default pipeline, including materialization, on
   imported bodies before embedding them. For a capturing lambda whose captures
   are outer argument/`this` reads and whose imported body contains no further
   lambda or local-function scope, capture substitution precedes the final
   slots-only inlining and storage-finalization tail. This lets the existing
   effect, single-use, type-witness, and evaluation-order rules see the recovered
   argument reads rather than potentially throwing environment-field reads.
   It does not inline user locals, invent a second inliner, or simplify in the
   printer. Outer-local captures and further nested bodies retain their prior
   finalization order so foreign local pools do not enter this opportunity.
   The motivating witness is Newtonsoft.Json 13.0.4,
   `JsonContract.CreateSerializationCallback`: its captured `MethodInfo` receiver
   becomes an argument read, allowing the single-use `object[]` spill to return
   to the invocation argument. `CapturingLambdaStorageFinalizationTests` gates
   the compiler-produced positive and the retained ordering/storage boundaries.
   The same existing live-range rules can remove a pure captured-argument alias
   across an intervening call, as in `GenericContext.ForMethod` from
   dotnet-inspect.any 0.14.0; the captured-reader fixture preserves that neighbor.
   `CapturingLambdaCoupledBodyTests` compiles the unchanged product-issued RTS
   artifact and compares both the factory and its actual `ldftn` target with the
   existing IL contract. The compiler-produced callback's factory remains Exact
   while its generated body improves from OpcodeDiff to Exact; this gate
   therefore detects a body change that a factory-only comparison misses.
   This slow test runs in Deep Inspect and as a focused pre-merge gate.
   Native RTS for the pinned Newtonsoft witness cannot currently produce a donor
   because its reconstructed context is missing `SerializationCallback`; the
   fixture result is not transferred as a real-witness generated-body verdict.
   This host-neutral pass is used by the default pipeline in both CLI and
   Browser/Wasm; no host or rendering changes are required.
   Late re-materialization of residual
   nested webs remains deferred; it is not a missing first materialization
   step. Direct slot-copy components now
   materialize atomically when every member clears the same type, scope, and
   rendering gates; otherwise every member stays printer-owned.
   `MaterializesCompleteDirectCopyComponent` and
   `DefersWholeDirectCopyComponentWhenOneSlotIsUndecided` gate both sides of
   that boundary. `SlotMaterializationPass.Analyze` owns the overlapping veto
   attribution consumed by `--slot-residual-census`; each decision identifies
   its exact body scope and slot number, and the census fails unless those
   identities equal the materialization-entry and retained web sets. Raises
   between late F2 and that entry receive separate census accounting. The former
   conditional-single-load veto is retired: the late expression-inlining pass
   has already consumed every conditional store that it can safely move into
   its sole consumer, while the remaining post-F2 stores render as standalone
   assignments rather than through a printer-owned consumer fold. Decided
   in-domain webs in that residual therefore materialize normally; their
   declaration ordering may change, but no expression moves.

   Multiple store blocks are not themselves a materialization veto for an
   already-decided, multiply observed web. Every occurrence retains its place
   in the ordered IR; the same typed local represents the complete web across
   those blocks. This does not split live ranges, infer a join type, consume a
   control-flow edge, or expand the coercion domain. The independent
   storage-rewrite invariant checks that preservation. Incomplete direct-copy
   components still remain wholly on slots.

   The motivating witness is Microsoft.CodeAnalysis.CSharp 5.0.0,
   `SourcePropertyAccessorSymbol.GetAccessorName`: its string-prefix stores
   and consumers already share one type across the raised branch arms.
   `CrossBlockSlotMaterializationTests` gates compiler-produced branch,
   repeated-condition and throwing-path cases, the real Roslyn witness, and
   complete versus incomplete cross-block copy components. It also preserves
   loop backedges and rejects conflicting or underivable cross-block testimony.
   Its slow compile-back gate, owned by Deep Inspect and the focused pre-merge
   selection, requires Exact for the throwing and repeated-condition fixtures.
   The two branch-prefix fixtures retain their measured OpcodeDiff: earlier
   structuring already duplicates the final return across the branch arms.
   That difference is not introduced or repaired by materialization. The
   ordinary admission gates are PR-fast; existing nested-scope gates remain
   in force.
   Production adoption is the unchanged shared CLI and Browser/Wasm pipeline.

   Multiple stores with only one load are also settled storage, not evidence
   of a pending expression fold. The earlier expression passes own those folds;
   materialization neither folds nor moves their remaining stores. The blanket
   multi-store/single-load veto is retired while the independent type, scope,
   rendering, pending-swap, and atomic-copy boundaries remain.
   The motivating witness is Microsoft.CodeAnalysis.Common 5.0.0,
   `AnalyzerImageReference.Display`: its display/path fallback already renders
   as assignments and an `if`, with an inner `??` expression. The complete
   string-copy component becomes typed locals without changing those decisions.
   `SingleLoadSlotMaterializationTests` gates that real witness, compiler-produced
   fallback storage, and preservation of already-raised conditional, coalesce,
   lazy-cache, and short-circuit consumer expressions. These admission
   and fold gates are PR-fast. The slow native-RTS gate, owned by Deep Inspect
   and the focused pre-merge selection, requires Exact for the four expression
   folds. The fallback fixture retains its measured OpcodeDiff: earlier raising
   already keeps its outer fallback as a branch. Paired product-artifact
   compile-back preserves both original and recompiled streams; materialization
   neither introduces nor repairs that existing difference. The shared default
   pipeline adopts the change for CLI and Browser/Wasm; EH adoption is not part
   of this storage slice.

   At materialization, a slot whose stores are all Boolean-valued may recover
   the Boolean identity of an integer-typed load consumed by a Boolean sink
   or condition. Every load still testifies: a numeric use conflicts, and an
   underivable use vetoes recovery. This is identity recovery, not an
   integer-to-Boolean conversion; mixed Boolean/integer stores retain the
   existing `BooleanSinkIdentityRecovery` boundary. Earlier raising passes
   retain their original testimony so materialization does not preempt their
   constant or control-flow decisions. The printer uses the same Boolean-sink
   rule for lowered and still-deferred slots instead of maintaining a second
   sink vocabulary.

   A Boolean `box` operand is also a semantic Boolean observer: its metadata
   token names the boxed value type, not the evaluation-stack storage width.
   Recovery still requires every producer to be Boolean-valued and every load
   to agree. Numeric observers conflict; mixed Boolean/integer producers retain
   the existing decline boundary. This does not add general boxing conversions
   or make boxing an insertion/invariant sink. Microsoft.CodeAnalysis.CSharp
   5.0.0 `Binder.FoldNeverOverflowBinaryOperators` motivates this boundary:
   unifying its disconnected carriers must not make an incorrect Int32-boxing
   body compilable when both original boxing tokens require Boolean.
   `RealRoslynBooleanBoxesMaterializeBooleanStorage` gates the actual
   compiler-produced two-store/two-box web and its typed Boolean operands
   through the default tail. `BoxObserversParticipateInCrossBlockIdentity`
   gates shared printer/materialization testimony and numeric/mixed-producer
   neighbors. Both are PR-fast. The real method's native whole-module fidelity
   remains unavailable because of its reconstructed private Roslyn context;
   the shape gate is not a transferred compile-back or whole-method verdict.

   The motivating real witness is Newtonsoft.Json 13.0.4,
   `DefaultContractResolver.InitializeContract`: the Boolean conditional
   assigned to `DefaultCreatorNonPublic` retains an integer-typed stack load.
   `CompilerProducedPropertyConditionalMaterializesBooleanIdentity` preserves
   that compiler-produced shape, and
   `CompilerProducedPropertyConditionalRecompilesWithRetainedTemporary`
   gates binding and the existing retained-temporary compile-back difference;
   it does not claim exact IL fidelity.
   `ConflictingBooleanAndNumericUsesRemainPrinterOwned`,
   `BooleanSinkDoesNotRetypeMixedBooleanAndIntegerStores`, and
   `IntegerConditionKeepsItsNumericIdentity` gate the nearby decline and
   non-action boundaries. Direct-copy components remain atomic:
   `MaterializesBooleanSinkIdentityAcrossDirectCopyComponent` gates the
   decided case; an undecided member still retains the entire component.
   The C2 deletion and invariant extension follow once the residual census
   reaches the printer-owned floor.

   Element-store identity also recovers late: integer-typed loads used as the
   value of a char or metadata-resolved enum array store may testify to that
   element type when every producer is a conditional with two representable
   constant arms. Char recovery shares
   `CoercionRendering.TryCharConstantValue` with the printer; enum recovery
   requires resolved backing data and constants within its signed/unsigned
   range. Stack-family compatibility alone is not a value-preservation proof.
   The existing slot-coercion gate still rejects unsupported widths. Every
   observer contributes testimony, and all existing scope, control-flow, and
   atomic copy-component gates remain. No expression or condition moves.
   Nonconditional producers, nonconstant arms, out-of-range values, and missing
   enum backing remain printer-owned. Earlier testimony is unchanged; the
   general element-target printer fallback remains necessary for lowered and
   deferred trees.

   Real witnesses are Newtonsoft.Json 13.0.4
   `DateTimeUtils.WriteDateTimeOffset` (`'+'`/`'-'`),
   Microsoft.CodeAnalysis 5.0.0 `BitVector.GetDebuggerDisplay` (`'1'`/`'0'`),
   and the two `ReadParameterRefKinds` implementations in
   dotnet-inspect.any 0.14.0 (`ArgumentRefKind.Ref`/`Value`).
   `ElementSlotIdentityTests` gates compiler-produced activation, constant
   boundaries, competing and underivable observations, all-store agreement,
   and incomplete copies. Its compile-back gate preserves the existing
   retained-temporary `OpcodeDiff`, not an exact-IL claim. The existing
   `CharElementStorePrinterTests` binding gate remains in force.

   Slot identity is the owning body plus its number, not the number alone.
   A nested web therefore cannot veto an otherwise decided outer web merely
   by reusing its number. Outer materialization preserves nested node identity,
   local tables, and residual slots. The shared-versus-isolated local-scope
   discriminator is an IR fact consumed by local-reference tracking and the
   printer; its existing behavior is unchanged.

   The measured witnesses are Microsoft.CodeAnalysis 5.0.0
   `SyntaxDiffer.RecordChange` (`int S_256`) and
   Microsoft.CodeAnalysis.CSharp 5.0.0
   `OverloadResolution.BetterConversionTargetCore` (`bool S_256`).
   `NestedSlotMaterializationTests` preserves the corresponding overloads from
   the repository's compiler dependency and gates outer activation, retained
   nested nodes, coercion obligations, and local-slot invariants.
   `NestedScopeNameCollisionTests` gates binding with and without outer
   materialization for lambda, local-function, already-materialized nested,
   and deeply nested naming cases. That scope-identity change did not expand
   the coercion domain or allocate more nested locals: all 140 nested residual
   webs in the 14-assembly investigation still failed the existing type-domain
   gate.

   Exact core-library string and object webs also materialize when every
   producer already has the testified type. This does not expand the coercion
   domain or infer reference conversions: an object-typed null cannot testify
   to string storage, and a string-typed producer cannot testify to object
   storage. Exact arrays use the same admission across element families when
   their complete array type passes the shared explicit-type spelling gate.
   This covers single-dimensional zero-based arrays and C#-spellable
   multidimensional arrays of rank 2 through 32. The array itself, including
   rank, not merely its element representation, must already have the testified
   type. Jagged arrays, generic elements, and in-scope generic parameters use
   that same gate; unsupported constituents, unspellable names, unbound
   generic shapes, non-SZ rank-one arrays, and explicit bounds or sizes that
   C# would erase remain deferred. The spelling gate is not a
   universal binding or generic-constraint proof. Existing explicit casts are
   preserved; array conversions are not inferred. In particular, covariance
   does not make `string[]` and `object[]` the same storage type.
   A covariant consumer may receive an explicitly
   string-array-typed load without widening its storage identity.
   Microsoft.CodeAnalysis.CSharp 5.0.0
   `ConversionsBase.ConversionEasyOut`'s static `byte[,]` table motivates
   multidimensional storage. `ExactMdArraySlotMaterializationTests` gates
   that real witness, compiler-produced reads and allocations, rank and
   constituent boundaries, exact producer identity, mutation/copy ordering,
   complete copy components, and the retained swap. Admission cases are
   PR-fast; its slow native-RTS fixture gate is owned by Deep Inspect and
   the focused pre-merge selection. The same shared pipeline adopts this
   storage in CLI and Browser/Wasm; array conversions are not added.
   Named reference storage also materializes when the imported type-shape map
   confirms its named definition is a reference type and its complete type
   passes that same explicit-type spelling gate. This includes classes,
   interfaces, delegates, and their spellable constructed generic forms.
   Unresolved definitions, non-exact producers, and
   unspellable or out-of-scope constructions remain deferred. Admission
   consumes existing metadata facts; it does not acquire dependencies or
   infer assignability, boxing, covariance, or generic constraints.
   Named value storage follows the same exact-type rule when the imported
   definition is a known value type, the complete type is spellable, and the
   type is not byref-like. This includes ordinary structs and their
   constructed generic forms, including nullable values. No struct conversion,
   nullable lifting, boxing, width/sign conversion, or storage alias is
   inferred: every producer must already have the testified nominal type.
   The rewrite preserves each value-copy occurrence and each existing boxed
   operand rather than moving a read across a mutation. Unknown definitions,
   managed references and ref-like lifetime cases
   remain outside this admission; the existing numeric domain is unchanged.
   Microsoft.CodeAnalysis 5.0.0
   `Collections.RoslynImmutableInterlocked.VolatileRead` motivates this
   category: its `ImmutableArray<T>` read must remain before the memory barrier.
   `ValueSlotMaterializationTests` gates the real read, compiler-produced
   ordinary/nullable/generic value storage, copy-before-mutation, typed boxing,
   exact width/sign boundaries, incomplete copies, and the retained swap.
   Its slow compile-back gate exercises the compiler-produced family in
   Release; the existing independent storage invariant checks ordered
   producer and occurrence preservation, not whole-method equivalence.
   Adoption is through the unchanged shared CLI and Browser/Wasm pipeline.
   Bare type and method generic parameters also materialize as their exact
   in-scope type when imported constraint flags establish that they do not
   permit byref-like arguments. Every producer must already have that same
   parameter identity; equal names do not identify type and method parameters.
   Missing or ambiguous constraint facts, shadowed or unspellable parameters,
   and `allows ref struct` remain deferred. This consumes existing constraint
   facts without inferring conversions, assignability, constraint satisfaction,
   or a value/reference classification for an unconstrained parameter.
   `Newtonsoft.Json.JsonConverter<T>.ReadJson` in Newtonsoft.Json 13.0.4
   motivates this boundary alongside Roslyn's `ArrayBuilder<T>.Pop`.
   `GenericSlotMaterializationTests` gates the real Roslyn witness,
   compiler-produced type/method parameters, class/struct constraints,
   copy-before-replacement, typed boxing, incomplete copy components, and
   ref-like decline. Admission cases are PR-fast; its slow native-RTS fixture
   gate is owned by Deep Inspect and the focused pre-merge selection.
   Existing rewrite conservation, pending-swap and scope boundaries apply;
   the same shared pipeline serves CLI and Browser/Wasm.
   Exact pointer storage also materializes when its complete pointer type
   passes the shared explicit-type spelling gate and every producer already
   has that same type. Pointee identity and pointer depth remain part of
   storage identity; no pointer conversion, native-integer conversion,
   allocation movement, or lifetime proof is inferred. Existing explicit
   conversions are preserved. Managed references, pinned storage, and
   function-pointer storage remain outside this admission, as do unspellable
   or out-of-scope pointer shapes and incomplete copy components.
   Microsoft.CodeAnalysis 5.0.0
   `System.IO.Hashing.XxHash128.HashLengthOver240` motivates the
   stack-allocation boundary, alongside the retained pointer arithmetic in
   `XxHashShared.Accumulate512Inlined`. `ExactPointerSlotMaterializationTests`
   gates the real hash witness, exact pointer identity, producer preservation,
   atomic copies, and compiler-produced reads, mutation, and stack allocation.
   Its slow native-RTS gate requires seven Exact fixture outcomes with the
   compile-back floor disabled, including the indirect compound assignment
   enabled by the existing raiser consuming the typed pointer local.
   Admission cases are PR-fast; Deep Inspect
   and the focused pre-merge selection own the slow gate.
   The separate [pointer-element compound-update raise](pointer-element-compound-updates.md)
   consumes its exclusive address-only spills before materialization. Its
   ownership and evaluation-order gate is independent of this unchanged
   exact-storage admission; retained pointer carriers still follow this rule.
   The [pointer-variable compound-update raise](pointer-variable-compound-updates.md)
   separately decides same-pointer storage updates after materialization,
   retiring the printer's pointer-compound branch without broadening storage
   admission.
   [Scalar self-update decisions](scalar-self-updates.md) separately annotate
   ordinary stores after coercion, retiring their same-place/unit-step discovery
   while retaining existing numeric binding and residual-slot reconciliation.
   The unchanged shared pipeline serves CLI and Browser/Wasm.
   Every observer still supplies
   testimony, and the existing structural-fold, nested-scope, and atomic-copy
   boundaries remain in force. No value or control-flow edge moves.
   An exact-storage carrier already recognized by the later swap raiser stays on slots
   until that raiser consumes it; materialization must not turn an existing
   tuple swap back into assignments. This reuses the swap owner's matcher and
   preserves its existing named-local boundary.

   Return-accumulator recovery uses the same IR-owned scope identity: a local
   in an independent nested pool cannot become an observation of an outer
   accumulator merely by sharing its number. A nested function's capture of
   the outer local still prevents that accumulator's elimination.

   Real witnesses include Newtonsoft.Json 13.0.4
   `JsonValidatingReader.ReadAsString` and Microsoft.CodeAnalysis 5.0.0
   `PathUtilities.NormalizePathPrefix` and
   `StringExtensions.GetWithSingleAttributeSuffix`.
   `StringSlotMaterializationTests` gates exact and sink-derived testimony,
   typed-null versus object-null producers, nominal string identity,
   conflicting and underivable observations, atomic copies, structural folds,
   and corresponding real Roslyn methods in the repository compiler dependency.
   `CompilerProducedReadAndObserveMaterializesRetainedString` preserves a
   retained call result across a state-changing call;
   `CompilerProducedStringFixturesRecompileExactly` gates exact compile-back for
   the retained result and compiler-produced swap.
   `CompilerProducedStringSwapRetainsItsPendingCarrier` gates the pending-swap
   boundary exposed by dotnet-inspect.any 0.14.0
   `LevenshteinDistance.Compute`. The scope regression witness is
   System.CommandLine 3.0.0-preview.5.26302.115
   `HelpBuilder.Default.GetArgumentUsageLabel`;
   `NestedLocalOwnershipSeparatesIndependentPoolsAndOuterCaptures` gates
   independent nested pools and retained outer captures.

   Object materialization preserves an already explicit `Box` rather than
   inventing boxing from an assignable producer. Real witnesses include
   Newtonsoft.Json 13.0.4 `JsonReader.get_ValueType` and
   `JsonSerializer.DeserializeInternal`, and Microsoft.CodeAnalysis 5.0.0
   `ExceptionUtilities.UnexpectedValue`.
   `ObjectSlotMaterializationTests` gates exact and sink-derived object
   testimony, preserved null and boxing nodes, every-producer agreement,
   nominal identity, observer disagreement, atomic copies, pending swaps, and
   the real Roslyn witness in the repository compiler dependency.
   `CompilerProducedObjectFixturesRecompileExactly` gates retained call results,
   retained boxing, and object swaps with independent compile-back.

   Byte-array witnesses include Newtonsoft.Json 13.0.4
   `JsonValidatingReader.ReadAsBytes` and `TraceJsonReader.ReadAsBytes`, plus
   Microsoft.CodeAnalysis 5.0.0 `LittleEndianReader.ReadReversed` and
   `CryptoBlobParser.ReadReversed`.
   `ByteArraySlotMaterializationTests` gates exact array identity, preserved
   allocation/initializer/cast nodes, sink-derived testimony, nominal element
   identity and rank, competing producers and observers, atomic copies, and
   pending swaps. Its compiler-produced fixtures retain reads and allocations
   across state changes and preserve initialized/aliased arrays;
   `CompilerProducedByteArrayFixturesRecompileExactly` checks those cases and
   array swaps with independent compile-back. Materialization does not change
   allocation timing, element writes, or the identity shared by array aliases.

   String-array witnesses include Newtonsoft.Json 13.0.4
   `JsonSchemaBuilder.ResolveReferences` and
   `BinaryConverter.EnsureReflectionObject`, and Microsoft.CodeAnalysis 5.0.0
   `PathUtilities.ExpandAbsolutePathWithRelativeParts`.
   `StringArraySlotMaterializationTests` gates exact producers, preserved
   allocation/initializer/cast nodes, nominal element identity and rank,
   unanimous observers, covariant sinks versus non-exact producers, atomic
   copies, pending swaps, and the corresponding real Roslyn method.
   `CompilerProducedStringArrayFixturesRecompileExactly` checks retained reads,
   allocations, initializers, mutation through a covariant alias, covariant
   returns, and swaps. Array aliases and runtime element-store checks remain
   unchanged; storage materialization neither moves writes nor narrows values.

   The element-family admission replaces the former Byte/String allow-list,
   not the exact producer requirement. It consumes
   `CSharpSpellability.CanSpellSzArrayStorageType`, which shares the existing
   by-value explicit-parameter type grammar and host-name checks rather than
   introducing a second type walker. Motivating pinned witnesses include
   Newtonsoft.Json 13.0.4 `StringUtils.FormatWith`,
   `DynamicUtils.BinderWrapper.Init`, and `JsonTextReader.ReadStringIntoBuffer`,
   plus dotnet-inspect.any 0.14.0 `TypeViewContext.get_TypeShapeViewSchema`.
   `ExactSzArraySlotMaterializationTests` gates element families, whole-array
   identity, generic scope and constituent-shape boundaries, atomic copies,
   retained producers, and pending swaps. Compiler-produced retained reads,
   allocations, covariant aliases, and generic/jagged arrays supply the
   compile-back fixtures. `SlotMaterializationInvariant` checks each completed
   rewrite under the existing validation policy; admission and later rendering
   still need their separate gates. The mixed SZ/rectangular-element fixture
   has IR coverage but not exact compile-back coverage: unchanged base tools
   also reverse its array ranks during C# composition
   ([#7103](https://github.com/richlander/dotnet-inspect/issues/7103)).
   The neighboring scalar, generic, jagged, pointer, and function-pointer
   fixtures require exact compile-back.

   Named-reference admission consumes
   `CSharpSpellability.CanSpellNamedReferenceStorageType`, reusing the existing
   named-definition projection and explicit-parameter spelling rules. A
   motivating real witness is Microsoft.CodeAnalysis 5.0.0
   `ChildSyntaxList.GetHashCode`, whose retained `SyntaxNode` reference already
   has exact type testimony. `NamedReferenceSlotMaterializationTests` preserves
   that witness through the repository compiler dependency and gates known
   versus unresolved shapes, generic spelling, exact producers, atomic copies,
   and pending swaps. Compiler-produced class, interface, delegate, generic,
   allocation, and covariant-return fixtures require exact compile-back.
   Missing dependency evidence deliberately keeps the same compiler-produced
   reference on slots rather than guessing its shape. These ordinary gates
   are PR-fast; the compile-back family is slow and owned by daily Deep Inspect
   and the focused pre-merge gate.

   `MaterializesSingleStoreConditionalWithSingleRead` and
   `MaterializesBooleanIdentityWhenConditionalFeedsBooleanLocal` gate
   conditional materialization;
   `MaterializesBooleanIdentityWhenConditionalFeedsBooleanProperty` gates the
   property setter case. Production adoption is through the shared default
   raising pipeline used by CLI and Browser/Wasm consumers; no host-specific
   conversion or naming path is introduced.

### Storage-rewrite validation

Materialization preserves the ordered IR tree while replacing each converted
outer slot web with one fresh local at its pre-rewrite testified type.
Every occurrence of that web uses the same local; distinct webs use distinct
locals, existing local types remain stable, and direct-copy components convert
atomically. Non-slot nodes, including producers and nested function bodies,
retain their identities and ordered child structure.

`SlotMaterializationInvariant` checks that contract around completed rewrites
when the existing `IrInvariants` switch is enabled. It derives correspondence
from the before/after trees rather than trusting the admission plan or its
replacement map. `SlotMaterializationInvariantTests` gates accepted compiler
and real Roslyn cases plus faulty rewrites; its slow CoreLib sweep supplies
broad activation evidence. Like `ControlFlowModelDifferentialTests`, this is a
bounded comparison with explicit coverage, not a second optimizer. Tree arity
and identity suffice here because materialization preserves shape; they would
not suffice for a restructuring pass.

The check does not prove eligibility, inspect every non-child metadata field,
certify later inlining or printing, or establish whole-program equivalence.
Existing admission, coercion, compile-back, and output gates retain those
separate roles. Production adoption is inside the shared materialization pass,
including direct, staged, and stepped callers in CLI and Browser/Wasm hosts.
It inherits the existing validation switch and host policy, adds no new knob,
and does not check a deliberately interrupted step as a completed rewrite.
The next admission slices consume this gate before expanding semantic type
categories; this slice itself changes neither admission nor printer output.

Each slice reports the standard decompiler-affecting-PR evidence: focused tests,
the corpus quality-diff card, and improved/still-flat examples. As ReturnToSender
coverage grows, compile-back-affecting slices add the A/B evidence described in
[docs/templates/decompiler-compile-back-harness-pr.md](../templates/decompiler-compile-back-harness-pr.md)
while retaining `decompiler-pr.md` as the PR body whenever product output
changes.

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
