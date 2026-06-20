# Memory-Safety Rendering Modes (Conservative vs Optimistic)

This note records a decision about how the decompiler renders `unsafe` and related
constructs in light of .NET's updated memory-safety rules (the redefined `unsafe`
context, the `MemorySafetyRulesAttribute`, and the per-member `RequiresUnsafeAttribute`).
See [decompiler.md](../decompiler.md) — "Unsafe contexts under the updated
memory-safety rules" — for the mechanics this note frames.

## The two takes

There are two coherent ways for the decompiler to treat the new rules:

- **Conservative ("replay").** Reproduce only what the compiler *forced* the source
  to do. If a binary exists, it already satisfied whatever rules it was compiled
  under, and the metadata records the proof (a module-level `MemorySafetyRulesAttribute`,
  a member's `RequiresUnsafeAttribute`). We replay those facts and nothing more.
- **Optimistic ("simulate").** Show the code as the new rules *would* require, even
  for input that never had to satisfy them — a migration preview that deliberately
  overlaps a source fixer.

## Decision

**Conservative is the default; optimistic is an opt-in mode.** Conservative is
principled and self-gating: new-rules behavior keys off the module-level
`MemorySafetyRulesAttribute` (`IrImporter.ModuleUsesUpdatedMemorySafetyRules`),
so a legacy module's output is byte-identical to what it was before the feature
existed, and a new-rules module replays the `unsafe` contexts the compiler
demanded. It is the path Features C (body `unsafe` blocks) and D (signature
`unsafe`) already follow.

Optimistic ("simulate") mode is selected explicitly
(`MetadataSource.SimulateNewRules`; the decompiler harness exposes it as
`--simulate-new-rules`). It forces new-rules rendering for *any* input, so a
legacy module is shown as the new rules *would* require — a migration preview
that deliberately overlaps a source fixer. It must stay opt-in and clearly
labeled, because it can invent contexts the original binary never had to satisfy.

## What forces the split: recoverability

The deciding factor is whether a construct leaves a trace in the binary.

- The `unsafe` *context* is recoverable: the compiler stamps `MemorySafetyRulesAttribute`
  / `RequiresUnsafeAttribute`, and pointer/`calli`/stackalloc operations are visible in
  IL. So replaying `unsafe` blocks is faithful.
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

Still future (not built): emit `// SAFETY-TODO` audit comments at introduced
blocks; emit the tighter `unsafe(expr)` expression form once it lands in a usable
compiler (tracked: roslyn #84012 / csharplang #10196).

## Opaque-contract classification (analysis surface)

Separate from rendering, the analysis layer flags **opaque-contract** methods:
a requires-unsafe method (`CallerUnsafeMode != None`) whose signature carries no
pointer (`OpaqueUnsafe.IsOpaque` / `LibraryBodyIndex.OpaqueUnsafeMethods`). The
requires-unsafe obligation is then visible only via `RequiresUnsafeAttribute` or
the `unsafe` modifier — a caller reading the parameter and return types alone sees
nothing unsafe. This is the new-rules analogue of the pointerless `unsafe` method
discussed above: under the updated rules the attribute survives, so the fact is
*recoverable as an annotation* even though the signature hides it.

The classification is **positive and sound** — it states that the contract is
invisible in the signature, never that the body is safe (a `mode != None`
pointerless method may still do real unsafe work, e.g. `ContractUnsafe(int[])`
which hardcodes an element index as an unenforced caller precondition). It joins
no body evidence; it reads `CallerUnsafeMode` alone. Specimens in
`UnsafeChainA.LibraryA`: positives `M1`, `HollowUnsafe`, `ContractUnsafe`;
negatives are the pointer-signature methods (`RealUnsafePointer`,
`SignatureOnlyUnsafe`, `DelegatedUnsafe`, `EscapingStackPointer`) and the safe
control (`Safe`).

## Hollow-unsafe classification (analysis surface)

The dual axis to opaque-contract is **hollow-unsafe**: a requires-unsafe method
(`CallerUnsafeMode != None`) whose body shows no directly-visible unsafe operation
(`HollowUnsafe.IsHollow` / `LibraryBodyIndex.HollowUnsafeMethods`). A *realized*
unsafe operation (a pointer dereference, `calli`, `localloc`/`cpblk`/`initblk`, or
a call into the unsafe surface) is anchored to an IL offset in `UnsafeEvidence`;
structural evidence (a pointer in the signature, a pointer/pinned local) is not.
The IL offset is the discriminator — a method is hollow when none of its evidence
carries one.

Unlike opaque-contract, this is an **absence** claim, so it is deliberately
caveated: it states only that no unsafe operation is *directly visible* in the
scanned body — never that the method is safe or that its `unsafe` is removable.
A pointer local can be optimized away in Release, erasing the IL trace of a real
dereference: the `M1` specimen derefs in source yet records no body op, so it is
reported hollow *despite being genuinely unsafe*. That false hollow is the
standing proof that "no visible op" must never be read as "safe". Specimens in
`UnsafeChainA.LibraryA`: positives `M1` (false hollow), `HollowUnsafe` and
`SignatureOnlyUnsafe` (genuinely hollow); negatives are the methods with a
realized op (`RealUnsafePointer`, `ContractUnsafe`, `EscapingStackPointer`),
`DelegatedUnsafe` (forwards the pointer — a realized unsafe call), and the safe
control (`Safe`).

The two classifications are orthogonal: opaque-contract is about the *signature*
(is the obligation visible to a caller?), hollow-unsafe about the *body* (does the
IL realize an unsafe op?). Their intersection — pointerless-signature **and** no
body op (`HollowUnsafe`, plus the `M1` trap) — is the strongest "this `unsafe`
might be reducible" signal, but the `M1` recall gap keeps even that advisory.
