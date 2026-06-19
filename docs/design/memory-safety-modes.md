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

**Conservative is the default and, for now, the only mode.** It is principled and
self-gating: new-rules behavior keys off the module-level `MemorySafetyRulesAttribute`
(`IrImporter.ModuleUsesUpdatedMemorySafetyRules`), so a legacy module's output is
byte-identical to what it was before the feature existed, and a new-rules module
replays the `unsafe` contexts the compiler demanded. No new surface is needed; it is
the path Features C (body `unsafe` blocks) and D (signature `unsafe`) already follow.

Optimistic is recorded here as a **future opt-in mode**, not built.

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

## What an optimistic mode would add (future)

A future opt-in mode (e.g. a `--simulate-new-rules` switch) would intentionally
fabricate new-rules-conformant source for *any* input, mirroring a source fixer
(cf. the ILLink `unsafe` evolution codefix, diagnostics IL5005/IL5006):

- synthesize `scoped` on stack-bound ref-struct declarations to silence CS9081;
- apply `unsafe` contexts to legacy code where the new rules *would* require them;
- optionally emit `// SAFETY-TODO` audit comments at introduced blocks;
- (later) emit the tighter `unsafe(expr)` expression form once it lands in a usable
  compiler (tracked: roslyn #84012 / csharplang #10196).

This mode must stay opt-in and clearly labeled, because it invents source the original
author may never have written.
