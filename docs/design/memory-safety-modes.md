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
principled and self-gating: new-rules behavior keys off the module-level
`MemorySafetyRulesAttribute` (`IrImporter.ModuleUsesUpdatedMemorySafetyRules`),
so a legacy module's output is byte-identical to what it was before the feature
existed, and a new-rules module synthesizes only the `unsafe` contexts justified
by recoverable contracts and reconstructed operations.

Optimistic ("simulate") mode is selected explicitly
(`MetadataSource.SimulateNewRules`; the decompiler harness exposes it as
`--simulate-new-rules`). It forces new-rules rendering for *any* input, so a
legacy module is shown as the new rules *would* require — a migration preview
that deliberately overlaps a source fixer. It must stay opt-in and clearly
labeled, because it can invent contexts the original binary never had to satisfy.

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
forces `IrFunction.UsesUpdatedMemorySafetyRules` true regardless of the module
attribute, so the printer applies `unsafe` contexts to legacy code wherever the
new rules *would* require them. What it can recover is bounded by recoverability
(above): a context is added only where the binary still carries a trace.

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
repository. Emission remains gated by the compile-back rail's pinned
`Microsoft.CodeAnalysis.CSharp`, which does not yet parse the form. Until that
package advances, the printer uses the same minimal-region policy with
`unsafe { }` blocks. Once the rail can validate expressions, any legal
expression position may use the tighter form; it is not limited to return
statements. Tracked: #2021.

Still future (not built): emit `// SAFETY-TODO` audit comments at introduced
contexts. Expression emission remains tracked by #2021 until the pinned
compile-back compiler can validate it.
