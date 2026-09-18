# Checked integer operand binding

## Owner and claim

Decompiler numeric binding owns this contract: a checked primitive integer
operation binds its operands to the opcode's signedness at the original stack
width before emission. Reinterpreting an operand's bits is separate from
checking the arithmetic result. An absent IL conversion must not become a
checked range conversion in C#.

This is a focused adoption of [value-typed emission](value-typed-emission.md),
using its existing `Coerce` node. It covers `add.ovf`, `sub.ovf`, `mul.ovf`,
and their unsigned forms when both operands have the same full-width primitive
integer family: `int`/`uint`, `long`/`ulong`, or `nint`/`nuint`. Native width
remains symbolic; the decision does not depend on the inspecting machine.
Small integers, mixed stack widths, pointers, enums, unresolved types, and
unchecked operators retain their existing owners.

The opcode, not the destination's declared signedness, selects the arithmetic
domain. The destination may require a separate coercion of the result. A scalar
compound is available only when its target read has that same domain; otherwise
the ordinary assignment remains. Identity coercions do not hide a same-place
read or the literal unit step from the scalar decision.

## Basis and representation

ECMA-335's checked arithmetic opcodes distinguish signed and unsigned
addition, multiplication, and subtraction. C# binary numeric promotion selects
operators from operand types; mixing signed and unsigned values can either fail
to bind or select a different width or overflow domain. A same-width
reinterpretation needs `unchecked` when it could otherwise acquire a range
check from its lexical context.

The existing `Convert` node retains actual IL conversion history. `Coerce`
expresses the required C# operand type without inventing another IL operation.
Declared or converted operands already in the selected domain retain their
type evidence. Other operands receive explicit coercion boundaries, including
expressions whose storage type appears to match: a storage type alone does not
establish the type of every expression the printer spells. The existing coercion
renderer owns spelling and lexical overflow context, as it does at other typed
boundaries. For a wide unary operand it consumes the existing unary rendered-type
recovery: an unsigned storage type does not mean a negation renders unsigned.

Operand expressions must also retain their original integer width before the
checked operator binds. In particular, C# negation of `uint` promotes to `long`,
whereas an IL I4 `neg` wraps at 32 bits. Its signed I4 interpretation must be
retained even when the checked result is widened or boxed; narrowing a promoted
C# negation afterward does not recover the original opcode contract. This
applies within the admitted checked operands, not as a general unary rewrite.

Binding precedes ordinary sink coercion and scalar self-update decisions.
Therefore destination coercion sees the decided result type. This does not
change scalar place identity, accessor dispatch, evaluation order, or loop
recovery. [Scalar update overflow context](scalar-update-overflow-context.md)
continues to own checked blocks, legal header syntax, and context restoration.

## Evidence and adoption

Issue #7610 supplies compiler-produced .NET 11 RC1 Release cases with checked
scalar updates and `ulong`/`long` or `nuint`/`nint` operands in both `for` header
positions. Their unchecked reinterpret casts disappear from IL; the original
rendering omits the binding and fails to compile.

The real motivating asset is .NET 11 RC1 `Microsoft.VisualBasic.Core.dll`,
`Microsoft.VisualBasic.CompilerServices.Operators.NegateUInt32`, MethodDef
`0x06000237`, MVID `7e3b5f2a-92bc-49ed-981f-5d1ee9d254cd`. Its checked signed
subtraction currently renders as `checked((long)0 - (ulong)operand)`.
Related `AddUInt32`, `SubtractUInt32`, and `MultiplyUInt16` methods expose the
same operand-domain boundary. Runtime-library discovery is supporting evidence,
not a claim that every reported candidate is defective.

The lowering shell is a checked `Binary` over the accepted primitive family.
Coercions retain the operand subtree and its IL conversions; no storage,
temporary, block, or control-flow edge is consumed. Consequently the
control-flow ownership obligation is unchanged. A changed arithmetic width,
changed checked signedness, newly checked reinterpretation, or changed
evaluation order falsifies the replacement.

`CheckedIntegerOperandTests` supplies Release compiler-fixture and typed-IR
gates for statements, headers, opposite-sign destinations, nested overflow
contexts, lambdas, unary operands, and native integers. I4-negation cases cover
scalar stores, direct/wide/boxed returns, nested operators, and explicit widening.
`CompilerProducedBindingsRecompileExactlyWithoutTheFloor` is the native
product-artifact fidelity gate in both memory-safety modes. Its batch is
`Speed=Slow` and runs as a focused pre-merge gate and in daily Deep Inspect;
the remaining cases are PR-fast. The real VB witness also has a metadata-backed regression; its native
compile-back currently cannot prepare the `System.Security.Permissions`
reference (#7565), so real-witness native fidelity is unverified until that independent
acquisition boundary is resolved. Render A/B and product-issued structural
comparisons report spelling changes separately from validity and fidelity.
An independently measured static-local-function probe remains
`RecompileFail` before and after because fallback call and declaration names
disagree (#7633); it is not part of the passing fidelity gate.

CLI and Browser/Wasm adopt the same final raising pipeline. No host-specific
binder or printer-side checked operand classifier is introduced. Existing
unchecked mixed-sign inference remains a separate retirement; this slice
prevents new checked-binding inference from entering the printer.
