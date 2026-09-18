# Pointer-variable compound updates

Owner: Decompiler raising. Implements
[#7499](https://github.com/richlander/dotnet-inspect/issues/7499) within the
[thin-printer plan](https://github.com/richlander/dotnet-inspect/issues/2095).

## Claim

An admitted pointer-variable update writes the same pointer storage using the
same element displacement, evaluation order, and checked behavior as its
read/arithmetic/write lowering. The typed update owns element scaling and the
choice of `+=`, `-=`, `++`, or `--`; the printer only spells that choice.

[Value-typed emission](value-typed-emission.md) supplies the thin-writer
constraint. [Raise discipline](../decompiler-raise-discipline.md) supplies the
evidence obligations. Neither pointer-element recovery nor value-producing
increment recovery is broadened by this contract.

## Real witness

Microsoft.CodeAnalysis.Common 5.0.0, `lib/net9.0/Microsoft.CodeAnalysis.dll`,
contains `System.IO.Hashing.XxHashShared.Accumulate512Inlined`, MethodDef
`0x060000A1`, MVID `bc961ed5-3be6-478b-adaf-34d3f75b70ec`. Its vector paths
contain these updates:

```csharp
accumulators += Vector256<ulong>.Count;
secret += Vector256<byte>.Count;
source += Vector256<byte>.Count;
```

These statements already have compound spelling. This slice retires the
printer's decision, rather than claiming improved similarity for those lines.
[The authored source](https://github.com/dotnet/roslyn/blob/6c4a46a31302167b425d5e0a31ea83c9a9aa1d09/src/Compilers/Core/Portable/Hashing/XxHashShared.cs#L629-L669)
is a separate comparison, not evidence of decompiled-source equality.

## Admission and replacement

The lowering is a statement store whose addition or subtraction reads that
same pointer place before its right operand. Its destination type, rather
than an indirect opcode's native-integer storage type, determines the pointer
element. Arguments, locals, testified residual slots, fields, properties and
indexers, and indirect pointer storage share this contract.

Receiver and address matching consumes the existing bounded, side-effect-free
place grammar. Property getter/setter identity, dispatch, receiver, and index
arguments must agree. Both accessor references remain product evidence.
Volatile reads or writes decline.

The initial displacement domain is a signed `int` in the compiler's known
primitive-element scaling shell: an unchecked native-integer conversion,
followed by multiplication by the element size. Multiplication and pointer
arithmetic must agree on checkedness; checked pointer arithmetic is unsigned,
while the signed index multiplication is signed. Byte-sized elements use the
corresponding unscaled `int` form. A folded `int` byte displacement is admitted
when divisible by the known element size. An element displacement of one
selects increment/decrement. Other integer domains, unknown sizes,
noncanonical conversions, and mismatched overflow contexts stay explicit.

The replacement retains the original target read and index expression, in
that order, with an implicit write to the captured target after the arithmetic.
The right operand can mutate the pointer, its receiver, or aliased storage:
the captured old value and destination still govern the update. The original
statement position and source offset remain; no control-flow edge, region,
label, or neighboring statement is consumed or moved. Nested bodies finalize
through their own pipeline and are not rewritten by an outer scan.

An unsupported store does not fall back to printer-owned compound inference.
It remains an explicit assignment using the existing general expression
renderer. This contract does not certify that renderer's residual pointer
arithmetic; newly demonstrated gaps require issue tracking.

## Basis and adoption

C# compound assignment captures the left-hand location and its value before
the right operand. Roslyn Release lowering is the executable witness.
[ILSpy's assignment transform](https://github.com/icsharpcode/ILSpy/blob/master/ICSharpCode.Decompiler/IL/Transforms/TransformAssignment.cs)
likewise recovers assignment operators in its IL transformation layer; that
is architectural comparison, not authority or transferred code.

The default host-neutral pipeline decides updates after slot materialization
and before coercion insertion. CLI and Browser/Wasm adopt the same node and
renderer without a flag. Lowered mode leaves explicit stores. Checked blocks,
loop increment positions, unsafe operation classification, accessor evidence,
and annotated-source kinds are part of adoption, not later follow-ups.
In `for` headers, checked updates use explicit assignments with checked
arithmetic on the right: a checked expression or block is not itself a legal
statement expression there. The admitted re-evaluable place grammar permits
that spelling without changing target capture or right-operand evaluation.
The existing [operation-context contract](memory-safety-modes.md) remains
authoritative: pointer-value arithmetic has a legacy lexical-unsafe
requirement, but is not intrinsically unsafe under updated rules. Target
dereferences and consumed member contracts retain their own requirements.

The pointer branch in `CSharpPrinter.CompoundStatement` retires in this slice.
The subsequent [scalar self-update decision](scalar-self-updates.md) owns
ordinary same-place selection. Scalar/enum numeric binding and general
pointer-expression rendering remain separate work under #2095.

## Gates

`PointerCompoundAssignmentTests` is the focused Release gate for both
compiler-produced memory-safety modes, the real Roslyn witness, and
identity/scaling/scope/entry boundaries. Its native ProductArtifact gate runs
without the compile-back floor. These gates must pass before publication;
this design alone is not validation evidence.
`CheckedLoopHeadersRecompileExactlyWithoutTheFloor` covers both header
positions, forward and reverse updates, and mixed overflow contexts.
It compiles the unmodified product artifact
in both memory-safety modes rather than accepting plausible-looking text.

Publication retains exact-base/head same-input Render A/B with all changed
methods classified, paired actual CLI and annotated-source documents, native
compile-back outcomes, and actual printer reduction. Native fidelity,
bare-render validity, structural correspondence, and source similarity are
reported separately. Existing and newly discovered residuals remain visible
in their owning issues.
