# Memory-Safety Rendering Modes (Conservative vs Optimistic)

This note records how the decompiler places reconstructed body `unsafe`
contexts under .NET's updated memory-safety rules. See
[decompiler.md](../decompiler.md) — "Unsafe contexts under the updated
memory-safety rules" — for the mechanics this note frames. CSharp owns
declaration modifiers.
For the legacy and updated rule models that supply this binary evidence, see
[Memory-safety models and evidence](memory-safety-models.md).

## The two takes

There are two coherent ways for the decompiler to treat the new rules:

- **Conservative ("replay").** Render only the contexts justified by the
  binary's rules/contracts and reconstructed operation semantics. It does not
  claim to recover the original lexical wrapper.
- **Optimistic ("simulate").** Show the code as the new rules *would* require, even
  for input that never had to satisfy them — a migration preview that deliberately
  overlaps a source fixer.

## Decision

**Conservative is the default; optimistic is an opt-in mode.** Conservative is
principled and self-gating. Metadata's normalized `MemorySafetyRulesResult`
feeds one typed language-mode decision shared by rendering and compile-back:

- `Legacy` selects legacy reconstruction and compiler replay.
- `Updated` selects updated-rules reconstruction and compiler replay.
- `Unsupported`, `Malformed`, `Conflicting`, or metadata `Unavailable` selects
  neither language mode. Conservative rendering fails with an explicit
  unavailable-mode diagnostic before mode-sensitive raising, and compile-back
  reports the artifact unavailable without invoking the compiler.

Production hosts preserve that diagnostic at their source boundary. An
explicitly requested CLI source section fails instead of disappearing from a
successful command, and the browser's source-unavailable result retains the
decompiler reason instead of replacing it with only a generic acquisition
failure. Harness reports admit the same decision before mode-sensitive passes
and retain unavailable methods as explicit coverage rather than silently
omitting them.

This distinction is module-wide. An invalid consumed-member contract can keep a
body visible at Partial fidelity when the caller's own language mode is known;
an invalid defining-module mode cannot, because every context-placement decision
would otherwise be made under an invented Legacy or Updated model. A legacy
module's output remains byte-identical to what it was before the feature
existed, and an updated-rules module synthesizes only the `unsafe` contexts
justified by recoverable contracts and reconstructed operations.

Optimistic ("simulate") mode is selected explicitly
(`MetadataSource.SimulateNewRules`; the decompiler harness exposes it as
`--simulate-new-rules`). It forces new-rules rendering for *any* input, so a
legacy module is shown as the new rules *would* require — a migration preview
that deliberately overlaps a source fixer. It must stay opt-in and clearly
labeled, because it can invent contexts the original binary never had to
satisfy. The explicit override also permits a preview for an unsupported,
malformed, conflicting, or unavailable module marker: the result is a simulated
Updated render, never replay or compile-back evidence for the artifact's unknown
compiler mode.

## What forces the split: recoverability

The deciding factor is whether a construct leaves a trace in the binary.

- The need for an `unsafe` context can be recoverable or derivable: the compiler
  stamps `MemorySafetyRulesAttribute` / `RequiresUnsafeAttribute`, and some
  context-requiring operations remain visible in IL. The original lexical form
  is not recoverable. The same IL can result from an `unsafe` block or
  `unsafe(expr)`, so the decompiler synthesizes a valid context rather than
  claiming to replay the source form.
- The `scoped` modifier on a **local** is generally *not* recoverable: it is
  compile-time-only escape analysis and emits no IL or metadata (only `scoped`
  *parameters* get `ScopedRefAttribute`). A decompiler reading IL has zero signal
  that the source said `scoped` on an arbitrary ref-struct local.

The one exception is a local initialized by a `stackalloc`. A `stackalloc` result
is *inherently* scoped — the language guarantees it can never escape its method —
so a `scoped` local holding one is a **derivable fact**, not a source author's
judgment, even though the keyword itself left no trace. It surfaces only because
hoisting splits the declaration from the assignment (`scoped Span<int> s; …; s =
stackalloc int[n];`): the inline form (`Span<int> s = stackalloc int[n]`) infers
`scoped` on its own, and the split would otherwise lose it and warn CS9081
("result of a stackalloc expression … may be exposed outside of the containing
method"). So the printer spells `scoped` on exactly that hoisted-stackalloc case
as plain conservative correctness; recovering `scoped` for any *other* ref-struct
local would be a guess and stays out of scope.

## Off this axis: stackalloc raising is plain correctness

Raising the compiler's lowering of `Span<T> s = stackalloc T[n]` (a `localloc` fed to
the `Span<T>(void*, int)` constructor) back into a source-level `stackalloc T[n]` is
**not** a memory-safety-mode choice. The lowered ctor shape
(`new Span<T>(stackalloc byte[...], n)`) never compiled in any mode — a `stackalloc`
in argument position types as `Span<byte>`, not `void*` — so the raise is mode-independent
fidelity, applied unconditionally by `StackAllocSpanPass`. The unsafe *wrapping* of that
stackalloc (under `[SkipLocalsInit]`) remains gated on the new rules.

## Primary-constructor storage shape

**Owner and claim:** Decompiler whole-type composition retains an explicit
storage declaration and ordinary constructor when hiding that storage behind
a primary-constructor parameter would remove a required declaration site.
This is the source-shape prerequisite
[#6046](https://github.com/richlander/dotnet-inspect/issues/6046), within #5255,
for the [CSharp declaration-spelling consumer](csharp-memory-safety-spelling.md)
in #5257. CSharp owns whether the field and constructor spell `safe`, `unsafe`,
or neither; a parameter is not a substitute declaration site.

The current production `MemberBodyProducer.Project` already chooses the
lowered form: capture fields are explicit fields, and parameter-dependent
stores remain in ordinary constructors. This contract preserves that behavior;
it does not introduce primary-constructor syntax for ordinary inputs or claim
to recover the original source form. The compile-back planner's independent
primary-constructor synthesis does not establish production reconstruction.
No new fallback API or harness-only rewrite is needed.

Preserving storage does not authorize moving it across an observable constructor
chain. The existing constructor-call diagnostics permit parameter stores around
the elided parameterless `System.Object` constructor, but retain a visible
unsupported residual before a nontrivial base-constructor call.

The Release `PrimaryConstructorStorageTests` gate exercises the product
composer with updated-rules explicit-layout safe and unsafe fields and with
ordinary captures. It observes retained declarations, offsets, constructor
parameters, and stores, not completed model-aware spelling.
`ConstructorCallDiagnosticsPassTests` gates the constructor-chain decline.
Extended-layout declaration legality, caller-contract replay, and whole-output
compilation under updated semantics remain unverified here and belong to the
subsequent #5257/#5255 gates; an explicit-layout fixture does not establish them.

This is a prerequisite within stage 2 of #5226's existing three-stage adoption:
Metadata facts, shared CSharp adoption, then CLI and browser/Wasm outcomes.
CLI whole-type decompilation calls `MemberBodyProducer.Project` directly;
the browser reaches the same composer through `AssemblyContextSourceQuery`.
This slice adds neither a host-local policy nor a new adoption stage.

## What the optimistic mode adds

Optimistic mode (`MetadataSource.SimulateNewRules`; harness `--simulate-new-rules`)
forces the shared mode decision to Updated regardless of the normalized module
result, so the printer applies `unsafe` contexts to legacy or otherwise
unreplayable code wherever the new rules *would* require them. What it can
recover is bounded by recoverability (above): a context is added only where the
binary still carries a trace.

Recoverable, so simulate wraps them for legacy input (mirroring a source fixer,
cf. the ILLink `unsafe` evolution codefix, diagnostics IL5005/IL5006):

- a pointer dereference, `calli`, or stackalloc-under-`[SkipLocalsInit]` — the
  operation is visible in IL;
- a call whose callee has a pointer in its signature — visible in the MemberRef;
- a cross-assembly call to a method stamped `RequiresUnsafeAttribute` in its
  (new-rules) defining assembly — the attribute is read cross-assembly via the
  `MetadataContext`, the same path conservative mode uses.

**Not** recoverable, so simulate cannot wrap them: a legacy same-assembly
pointerless `unsafe` method's requires-unsafe-ness. Legacy compilation stamps no
`RequiresUnsafeAttribute` and the call carries no pointer, so the fact was erased
— there is nothing to replay or recover. This is the principled limit of the mode.

## Field caller-contract replay

**Owner and claim:** Decompiler preserves Metadata's normalized FieldDef caller
contract on every exact same-assembly or cross-assembly field reference. Every
lowered or raised field read, write, or address operation consumes that
contract when deciding whether the caller body needs an unsafe context.
Unsupported, malformed, conflicting, or unavailable target evidence lowers
fidelity visibly instead of becoming a negative fact or a pointer-shape guess.
This focused #5255 slice is tracked by
[#6323](https://github.com/richlander/dotnet-inspect/issues/6323).

The target field's model and contract remain separate from the caller's
rendering mode:

- An updated-target explicit contract requires a context only when the caller
  renders under updated rules, including optimistic simulation.
- A legacy-target implicit compatibility contract requires a context under
  either caller model.
- A positive no-contract result requires no context, including for a
  pointer-bearing field in an updated target.
- When the caller model or a legacy pointer-bearing field shape makes the
  target contract relevant, an unavailable contract authorizes no inferred
  context. Independently, a resolved unsupported, malformed, or conflicting
  target model remains visible invalid evidence rather than becoming a
  negative fact. Both cases keep the body visible with Partial fidelity and
  the existing invalid-member-rules diagnostic. A legacy caller consuming a
  non-pointer field does not require unavailable target contract evidence
  because neither a legacy implicit contract nor an updated explicit contract
  can affect that caller.

The field contract is independent of operation shape. It applies to instance
and static loads and stores, field-address operations, and raised nodes that
retain the field through `ConsumedMemberEvidence`. A pointer receiver can
require a context independently; neither fact substitutes for the other.
Same-assembly FieldDefs consume the current module's
`MemorySafetyMetadataIndex`. Same-module MemberRefs and cross-assembly
MemberRefs resolve one exact name-and-signature FieldDef before consuming the
defining module's normalized index. Ambiguous or unreachable definitions do
not permit attribute-presence fallback.

The ordinary constructor store retained by
[#6046](https://github.com/richlander/dotnet-inspect/issues/6046) is a named
consumer: preserving the explicit field and constructor is useful only when
the body renderer also preserves that store's caller obligation. CSharp still
owns the field and constructor declaration modifiers delivered by
[#6297](https://github.com/richlander/dotnet-inspect/pull/6297). This slice does
not change primary-constructor source shape or declaration spelling.

The Release `DecompilerFieldMemorySafetyTests` gate uses compiler-produced
legacy and updated fixtures to cover same- and cross-assembly loads, stores,
and addresses; explicit, implicit, and no-contract results; raised field
carriers; await-boundary agreement; invalid target evidence; and the retained
ordinary-constructor store. Compiler validation consumes the product-rendered
body without repairing it.

## Rendering altitude and the runtime oracle

The target is the smallest valid context, not a reconstruction of the original
lexical wrapper. Prefer `unsafe(expr)` when one expression and its dependencies
can be isolated. Otherwise wrap the smallest statement range that compiles and
preserves scope and data flow. Do not pull semantically safe statements into the
context merely because they are adjacent.

dotnet/runtime is the oracle in two complementary forms. Migrated runtime source
shows the accepted authored form where it exists. Because most source has not
yet migrated, the
[memory-safety fixer](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/tools/illink/src/ILLink.CodeFix/RequiresUnsafeCodeFixProvider.cs)
supplies the placement model: it starts with the triggering statement, uses a
forward declaration when that keeps later safe statements outside, and expands
the block only when ref-local or other dependency semantics require it. The
printer follows that containment policy without copying source-only audit
comments or claiming the original source used the same form.

The `unsafe(expr)` compiler gate is met: roslyn #84012 / csharplang #10196
shipped, and `unsafe(expression)` parses and compiles on the SDK selected by the
repository and with the compile-back rail's pinned
`Microsoft.CodeAnalysis.CSharp` 5.9.0. The printer uses the expression form when
one rendered value or header expression contains every unsafe-required
operation. It retains a block when the obligation belongs to a void invocation,
an implicit statement operation, multiple expressions or statements, or a
scope/data-flow dependency. Roslyn 5.9.0 also still reports CS9362 when the
unsafe expression's direct operand is a requires-unsafe property access or
method-address conversion, and CS8346 when a pointer-targeted stack allocation
loses its target type inside the wrapper. Those direct operands retain blocks;
a larger enclosing expression may still use the expression form when the
compile-back rail demonstrates that exact shape is accepted. Address-form
`fixed` initializers also retain a block because wrapping their `&place`
operand makes Roslyn report CS0212. Legal expression positions are not limited
to returns.

Still future (not built): emit `// SAFETY-TODO` audit comments at introduced
contexts.
