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
- The `scoped` modifier on a **local** is *not* recoverable: it is compile-time-only
  escape analysis and emits no IL or metadata (only `scoped` *parameters* get
  `ScopedRefAttribute`). A decompiler reading IL has zero signal that the source said
  `scoped`.

Therefore synthesizing `scoped` cannot be part of conservative replay — there is no
fact to replay. Omitting it on a hoisted stack-bound span declaration produces at most
a **warning** (CS9081, "result of a stackalloc expression … may be exposed outside of
the containing method"); the output still compiles. Silencing that warning by adding
`scoped` is a judgment the source author made, not a fact in the binary, so it belongs
to the optimistic mode.

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

Still future (not built): synthesize `scoped` on stack-bound ref-struct
declarations to silence CS9081; emit `// SAFETY-TODO` audit comments at introduced
blocks; emit the tighter `unsafe(expr)` expression form once it lands in a usable
compiler (tracked: roslyn #84012 / csharplang #10196).
