# ILInspector.Instructions (prototype)

A **prototype** of the gated Rung 5 typed-stack substrate from
[issue #1908](https://github.com/richlander/dotnet-inspect/issues/1908): one
`decode → typed instructions → typed-stack → EH-aware blocks` pipeline, intended
to subsume the decode + block-building currently duplicated in
`ReachingDefinitions.BuildBlocks` and the decompiler importer, and to add the one
witness those copies cannot: **stack element types, receiver types, and stack
value provenance**.

This library is a **named, gated placeholder** per the
[`ILInspector.Instructions` decision protocol](https://github.com/richlander/dotnet-inspect/issues/1908#issuecomment-4837663012):
it is built as a candidate prototype and is **not wired into `Analysis` or the
decompiler**. Convergence happens only after owner sign-off with measured
evidence, exactly as the `ILInspector.Research` edge decision did.

## Boundary (typed-stack exit only)

SRM-only, NativeAOT-friendly, Roslyn-free. No `IrNode`, expression tree,
structuring, C#, or inspected-assembly loading. The library reads the single
`MetadataReader` it is handed and never loads referenced assemblies; cross-assembly
type references coarsen to object-reference and generic parameters to `Unknown`.

## Pieces

| Type | Role |
| --- | --- |
| `InstructionDecoder` | The single IL decode → `DecodedInstruction` stream. |
| `BlockGraph` | EH-aware basic blocks emitting `ControlFlow.BlockEdges` (ECMA-335 I §12.4). |
| `StackType` / `StackValue` | The ECMA-335 III stack-type lattice and a slot = (type, producer offset). |
| `StackTypeInterpreter` | Abstract typed-stack with merge-at-joins and seeded EH handler entries. |
| `IStackTypeResolver` / `MetadataStackTypeResolver` | Metadata-light token resolution for the few effects that need it. |
| `MethodInstructions` | Layer 0 façade: decode + blocks + offset→instruction/block lookup; typed stack is opt-in via `InterpretStack`. |

The typed stack is **fail-closed**: any unresolved stack effect, height
disagreement at a join, or underflow degrades the whole method result to
`IsComplete == false` with a reason, rather than guessing — preserving the
two-tier model (fast scan stays the recall net; typed-stack is the escalation
path for candidates whose gate needs typed evidence).

## Evidence (real run)

Dogfooded read-only over `System.Private.CoreLib` (`net11.0`): **40,956 / 41,012**
method bodies (99.9%) produced a complete typed stack, with 48,876 object-reference
call operands tracked; the remaining bodies fail closed with an explicit reason.
This is evidence the decode/blocks/typed-stack are robust on real-world IL — not a
product wiring.

## Tests

```bash
dotnet run --project src/ILInspector.Instructions.Tests -c Release
```

(xUnit v3 executable runner — use `dotnet run`, not `dotnet test`.)
