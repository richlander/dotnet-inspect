# Pointer-element compound updates

Owner: Decompiler raising. Advances [#7382](https://github.com/richlander/dotnet-inspect/issues/7382)
within the [printer-inference retirement plan](https://github.com/richlander/dotnet-inspect/issues/2095).

## Claim

An admitted pointer-element compound update evaluates its pointer and index
once, reads the selected location before evaluating its right operand, and
writes the arithmetic result back to that same location. Removing its
address-only spill must preserve that sequence, including exceptions and
right-operand mutations of the pointer, index, or selected storage.

The replacement is a typed statement, not a printer-recognized pair of
assignments. The printer spells the already-decided element and operator.
[Value-typed emission](value-typed-emission.md) supplies the thin-writer
constraint; its storage admission remains unchanged.
[Raise discipline](../decompiler-raise-discipline.md) supplies the proof and
evidence requirements, not an additional behavior contract.

## Motivating asset and endpoint

Microsoft.CodeAnalysis.Common 5.0.0, `lib/net9.0/Microsoft.CodeAnalysis.dll`,
contains `System.IO.Hashing.XxHashShared.Accumulate512Inlined`. Its scalar
fallback retains two address spills after exact pointer storage materializes:

```csharp
S_256 = accumulators + (i ^ 1);
*S_256 += sourceVal;
S_257 = accumulators + i;
*S_257 += Multiply32To64((uint)sourceKey, (uint)(sourceKey >> 32));
```

The intended endpoint for those statements is:

```csharp
accumulators[i ^ 1] += sourceVal;
accumulators[i] += Multiply32To64((uint)sourceKey, (uint)(sourceKey >> 32));
```

The pinned MethodDef is `0x060000A1`, MVID
`bc961ed5-3be6-478b-adaf-34d3f75b70ec`.
[Roslyn's MIT-licensed source](https://github.com/dotnet/roslyn/blob/6c4a46a31302167b425d5e0a31ea83c9a9aa1d09/src/Compilers/Core/Portable/Hashing/XxHashShared.cs#L629-L669)
provides the authored comparison. Agreement with a PDB checksum establishes
agreement with that declaration, not independent build provenance.

## Admitted lowering

The compiler's Release lowering captures an element address with `dup`,
reads through the captured address, evaluates the right operand, performs the
integer operation, and stores through the same captured address.

The initial domain is unchecked `+`, `-`, `*`, `&`, `|`, and `^` on `int`,
`uint`, `long`, or `ulong` elements, with a right operand of that exact type.
Indirect opcode storage width must match the element width. A signed
`ldind.i4`/`ldind.i8` opcode does not change the signedness of the pointer's
declared element.

An index is a signed `int`, scaled by the known element size through the
compiler's unchecked native-integer conversion and multiplication. A folded
constant byte offset is also admitted when it is an exact multiple and yields
an `int` index. The pointer precedes the index in evaluation order. Checked
address arithmetic, other index domains, commuted pointer operands, and
unknown scaling retain their existing lowered representation.

## Ownership and ordering

The consumed address carrier is a synthetic stack slot with exactly one
store and exactly two loads in its owning function scope. Both loads belong
to the same immediately following indirect update: one is its destination,
the other is the address of the binary operation's left-hand read. Any other
observation, replacement, intervening statement, or different address
declines the raise. Named local captures are not this lowering shell.

The replacement consumes the capture, two slot loads, indirect read/write,
byte-scaling shell, and binary operation together. Pointer, index, and right
operand expressions retain their evaluation count and order. A compound
statement owns the implicit read between its index and right operand; a
later transform must not treat its children as freely reorderable values.

Both consumed statements are adjacent in one block, and neither may own a
referenced statement label. An external entry into either statement declines
the raise. No block, edge, exit,
exception region, or structured transfer is consumed or moved. Nested
functions own separate slot pools and are finalized by their existing
pipeline; an outer scan does not rewrite their bodies.

Volatile reads or writes, checked arithmetic, narrow integer truncation,
floating-point operations, managed references, pinned storage, and
function-pointer elements remain outside this initial domain.

## Basis and analogous implementation

C# compound assignment evaluates its left operand once and captures the
location for the later write. Roslyn's forward lowering is the executable
witness for that language rule.
[ILSpy's assignment transform](https://github.com/icsharpcode/ILSpy/blob/master/ICSharpCode.Decompiler/IL/Transforms/TransformAssignment.cs)
likewise treats assignment recovery as an IL transform, checking carrier
usage, memory-access compatibility, truncation, and evaluation effects.
That is architectural evidence, not authority for this admission; no code is
transferred.

## Adoption and evidence

The default host-neutral raising pipeline owns admission before final slot
materialization and coercion insertion. Both CLI and Browser/Wasm consume that
same pipeline. No host flag, additional acquisition, or source lookup is
required. Existing printer pointer-scaling helpers move to shared code without
broadening their old callers; the new statement does not ask the printer to
recover its index or compound operator.

`PointerElementCompoundAssignmentTests` is the PR-fast gate for
compiler-produced positive cases, real Roslyn structure, and retained
capture/scaling/typing/scope/entry boundaries. The fixture family is compiled
in Release with the repository .NET 11 RC1 SDK in both legacy and updated
memory-safety modes. Its focused native ProductArtifact RTS gate requires
Exact with `compile-back-floor=false` for the admitted family and the retained
named-address capture; that slow gate belongs to Deep Inspect and focused
pre-merge evidence.

Four declined neighbors already have native `OpcodeDiff` at the unchanged
base: checked scaling, byte-element narrowing, long indices, and raw byte
offsets. [#7471](https://github.com/richlander/dotnet-inspect/issues/7471)
owns those gaps. Their PR-fast gates prove that this raise leaves them alone,
not that their existing output is Exact or correct.

Before publication, retain paired actual CLI documents, structural review,
and exact-base semantic Render A/B with the complete changed-method
population classified. Native artifact fidelity, bare-render validity,
structural correspondence, and authored-source similarity are separate
verdicts. Publication records the exact heads and independent verdicts;
passing a fixture gate does not certify an unmeasured corpus.

This slice does not claim that the whole hash method is fully raised.
Control-flow layout, vector-call spelling, generic argument elision, names,
and source-only information remain tracked by #7382 and its linked issues.
Other compound-assignment families remain on the retirement plan rather than
being silently declared migrated.
