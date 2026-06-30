# Instruction Substrate (`ILInspector.Instructions`)

How one shared IL decode + EH-aware basic-block builder is factored out from the
analyzer and the decompiler, why it is shaped the way it is, and how it is proven
to be a regression-free replacement for the decoders it subsumes. This is a design
note about the substrate's boundaries and contracts, not a tour of every method.
See [decompiler-substrate.md](decompiler-substrate.md) for the *predicate* substrate
(a sibling instinct one level up), and issues
[#1908](https://github.com/richlander/dotnet-inspect/issues/1908) (origin, gate),
[#1939](https://github.com/richlander/dotnet-inspect/issues/1939) (tracker), and
[#1941](https://github.com/richlander/dotnet-inspect/issues/1941) (the typed-stack
measurement) for the running history.

## Purpose

Three places independently decode IL into instructions and basic blocks: the
analyzer's reaching-definitions, the analyzer's body index (allocation/triage
producer), and the decompiler importer. Hand-rolled IL decoders are a recurring
source of drift (a mishandled `no.` prefix, a signed short-operand index), and
each copy must be kept correct on its own. The substrate is the one decode +
block builder all three converge onto, so the most-correct decoder is shared and
the offset/block identity is computed once — which also makes it the exact join
key for the [`ILInspector.Research`](../../src/ILInspector.Research) overlay.

## Layering

```text
Research            (joins Analysis facts + Decompiler representations by IL offset)
  |
  +-- Analysis      (reaching-defs, allocation/loop facts, call graph)
  +-- Decompiler    (IR import, structuring, C#)
        |
      Instructions  (decode -> typed instructions -> EH-aware blocks -> [gated] typed stack)
        |
      ControlFlow   (BlockEdges vocabulary + dominance/dataflow kernels; depends on nothing)
```

`Instructions` **depends on** `ControlFlow` (it emits `ControlFlow.BlockEdges` and
feeds the shared dominance/dataflow kernels), exactly as Analysis does — so in
dependency terms it sits *on top of* `ControlFlow`, which stays representation-
agnostic at the bottom. Analysis and the decompiler depend on `Instructions`;
neither owns it. The product path stays SRM-only, NativeAOT-friendly, Roslyn-free,
and never loads inspected assemblies.

## Layer 0 / Layer 1

- **Layer 0 — identity (metadata-free, the only shared currency).** The decoded
  instruction stream (offset-keyed), the EH-aware `BlockGraph`, and offset→
  instruction/block lookup. This is the de-dup target and the Research join key.
  `MethodInstructions` is the Layer 0 façade; `InstructionDecoder.Decode` +
  `BlockGraph.Build` are the throwing primitives.
- **Layer 1 — interpretation (per-consumer, opt-in, not shared).** Reaching-defs
  (Analysis), allocation/loop facts (Analysis), the decompiler's symbolic IR
  stack, and the substrate's own typed evaluation stack are each *consumers* of
  Layer 0 that build their own model on top. Prior art is emphatic that unlike
  consumers do not share the abstract interpretation: `dotnet/runtime` runs three
  separate typed stacks (ILVerify's `StackValue`, RyuJIT's `GenTree*`+`typeInfo`,
  `ILStackHelper`'s bare heights) over one shared `ILReader`/`FlowGraph`.

The typed evaluation stack is opt-in via `MethodInstructions.InterpretStack(...)`,
so broad scans and the offset join never pay for it.

## Runtime-shape alignment (legibility) and vendoring

The tool is moving to the `dotnet` org and will be reviewed by runtime/Roslyn
engineers, so the substrate deliberately mirrors the shapes they already maintain
in `src/coreclr/tools/Common/TypeSystem/IL` — recognition lowers review cost.

- The byte reader is the runtime `ILReader` + `ILOpcodeHelper` opcode-size table,
  **ported** (not vendored wholesale) into the substrate with the MIT
  "ported from dotnet/runtime" headers, retargeted to SRM's `ILOpCode`. The
  table-driven sizing is what makes prefixes like `no.`/`unaligned.` and short
  operand widths correct by construction. A one-time port, not a tracked vendor
  branch (IL opcodes are ECMA-frozen).
- Recognition is the default, not a straitjacket. Deliberate, **documented**
  divergence is fine where our use case differs: SRM handles instead of
  `Internal.TypeSystem`'s `TypeDesc`/`MethodDesc`; a visitor/callback shape instead
  of partial-class composition (we are a reusable cross-project library); `record`
  / `ImmutableArray` / fail-closed idioms over older mutable-struct style. Vendor
  the byte cursor; never the runtime type system — staying SRM-only is itself a
  boundary reviewers expect.
- Shapes to keep recognizable: `StackValueKind` naming, `FlowGraph.LookupIndex`
  (the offset→block lookup), the `ILImporter`-style hook shell, and
  `ComputeMaxStack` if a height tier is ever built.

## Fidelity contract

De-dup carries no weight if the unified decoder is *worse* than what it replaces,
so the cutover bar is **≥ every implementation replaced** — and **zero-diff parity
is the wrong gate**: the ported reader is strictly better (fixes `no.`, short-var
widths, malformed handling), so it *will* differ from the old decoders exactly on
the bugs it fixes. Two gate kinds instead:

- **Absolute correctness gates** (adjudicate "is the new one right," independent of
  any old impl): the decoded stream exactly tiles `[0, ilLength)` and branch
  targets land on instruction boundaries; a re-encode round-trip reproduces the
  original bytes from the decoder's semantic fields; and, for the typed stack, the
  computed evaluation-stack depth never exceeds the body's declared `MaxStack`.
  All hold over `System.Private.CoreLib`.
- **Directional differential** vs each replaced decoder over the corpus: classify
  every diff as improvement / neutral / regression; the gate is **zero
  regressions** (not zero diffs), with improvements characterized by an absolute
  oracle. This is the same strict-improvement discipline as the decompiler corpus
  gate.

Correctness-direction consumers (e.g. a stackalloc/`InlineArray` rewrite, or an
ArrayPool use-after-return lint) must **hard-gate on substrate `IsComplete`**,
never best-effort — and they fail in opposite directions: a rewrite must
fail-closed (don't transform), a lint must fail-quiet (don't accuse). Both are
licensed only because the substrate is fail-closed at every layer.

## The Analysis cutover (done)

`ReachingDefinitions` was the first de-dup. It now decodes + builds EH-aware blocks
via the substrate (`InstructionDecoder.Decode` + `BlockGraph.Build`, the *throwing*
Layer 0 primitives, preserving RD's malformed-IL `BadImageFormatException`
contract — the fail-closed façade is for consumers that want it) and keeps only its
reaching-defs dataflow. About 500 lines of duplicate decoder + block-builder +
region modeling were deleted.

Proven safe by: full block **and edge** parity vs the old builder over all of
CoreLib (edges matter because the dataflow depends on them), and the entire
`ILInspector.Analysis.Tests` (261) plus `dotnet-inspect.Tests` (1,415) suites
passing unchanged.

## The decompiler granularity finding

The three IL-level block finders do **not** agree on granularity.
`ReachingDefinitions` (and the substrate, which matches it) make *every instruction
inside an EH region a leader* — needed for per-instruction exception-edge dataflow.
`IrImporter.FindLeaders` adds only region boundaries. Measured over CoreLib, the
substrate's leaders are a strict superset of `FindLeaders`' (zero under-splits, so
the substrate never merges a block the decompiler needs separate), with **29,853
extra (substrate-only) leaders** — the per-EH-instruction granularity.

So the decompiler cutover needs a **minimal-CFG refactor**: Layer 0 emits the
minimal/boundary CFG (the `FindLeaders` shape), and `ReachingDefinitions` adds its
EH-instruction splitting as a Layer-1 step. That refactor lands *with* the
decompiler cutover, where the benefit is realized — doing it earlier would only add
RD-specific code without a consumer.

## The gated typed stack

The typed evaluation stack (decode → typed instructions → typed-stack → blocks) is
the [#1908](https://github.com/richlander/dotnet-inspect/issues/1908) Rung-5
placeholder. It is built **only on a measured trigger**: R1 (raw IL + metadata +
reaching-defs) cannot satisfy a gate and the missing witness is specifically stack
element/receiver types or stack value provenance. The
[#1941](https://github.com/richlander/dotnet-inspect/issues/1941) probe measured
the allocation/escape shapes and found **zero** such cases, so the gate stays
closed on numbers. Its credible future home is **memory-safety lifetime**
(ref-struct / `scoped` / ArrayPool aliasing — runtime `StackValue.ByRef` + flags
territory), to be measured the same way. Until then the typed stack stays a
minimal, opt-in Layer-1 model with its own fidelity gate, not a product dependency.

## Status

- Layer 0 substrate: decoder on the ported `ILReader`; tiling, round-trip, and
  MaxStack fidelity gates over CoreLib; Layer 0/1 split with offset→block lookup.
- De-dup: `ReachingDefinitions` cut over (done, validated). Remaining:
  `LibraryBodyIndex` (the allocation/triage producer — convert its decode loop with
  the full corpus baseline as guard) and the decompiler importer (with the
  minimal-CFG refactor).
- Typed stack: gated; no measured trigger; re-probe under memory-safety lifetime.
