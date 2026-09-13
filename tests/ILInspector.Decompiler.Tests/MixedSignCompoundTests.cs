using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A compound assignment whose binary mixes the target's signedness — e.g.
// `ulong V_0 -= (long)1` in System.Math.BitIncrement — has no C# common type, so
// `target op= rhs` is CS0034 at Full. The rhs must bind to the target lvalue type
// (#1476). csc emits this from its own lowerings, not directly spellable source,
// so it is constructed at the IR level.
public class MixedSignCompoundTests
{
    static readonly TypeRef ULong = TypeRef.CoreLib("System", "UInt64");
    static readonly TypeRef Long = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef NUInt = TypeRef.CoreLib("System", "UIntPtr");
    static readonly TypeRef NInt = TypeRef.CoreLib("System", "IntPtr");

    // ulong V_0 = arg; V_0 = V_0 - (long)1; return V_0;  →  the second store folds
    // to a compound assignment whose right operand is a signed (long) 1.
    static string Render(BinaryKind kind)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var rhs = new ILInspector.Decompiler.Pipeline.Convert(Long, isChecked: false, isUnsigned: false, new Constant(1, Int));
        var binary = new Binary(kind, isChecked: false, isUnsigned: false, new LoadLocal(0, ULong), rhs);

        var block = new Block(0);
        block.Add(new StoreLocal(0, ULong, new LoadArgument(0, "arg", ULong)));
        block.Add(new StoreLocal(0, ULong, binary));
        block.Add(new Return(new LoadLocal(0, ULong)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(ULong, [new Parameter("arg", ULong)], HasThis: false, GenericParameterCount: 0),
            [ULong],
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void SubtractCompound_MixedSign_BindsToTargetType()
    {
        var output = Render(BinaryKind.Subtract);

        Assert.Contains("V_0 -= ", output);
        // The right operand must not stay signed (`(long)1`), which is CS0034
        // against the unsigned target.
        Assert.DoesNotContain("(long)", output);
    }

    [Fact]
    public void AddCompound_MixedSign_BindsToTargetType()
    {
        var output = Render(BinaryKind.Add);

        Assert.Contains("V_0 += ", output);
        Assert.DoesNotContain("(long)", output);
    }

    [Fact]
    public void DivideCompound_MixedSign_SignedOpUnsignedTarget_StaysUnreconciled()
    {
        // Divide is sign-sensitive (`/` vs `div.un`). This synthetic shape is a
        // SIGNED divide (isUnsigned: false) over an UNSIGNED target — the opcode
        // signedness does not match the lvalue, so casting the operand to the
        // unsigned target type would flip the opcode. The reconciliation gate
        // (NeedsCompoundSignCast) therefore declines it and the operand keeps its
        // signed `(long)` form. (A faithful `div.un` over an unsigned target IS
        // reconciled — see NuintDivideUnsigned_Faithful_BindsToTargetType.)
        var output = Render(BinaryKind.Divide);

        Assert.Contains("(long)", output);
    }

    // ── Native-int (#1459): nuint/nint are wide same-width like ulong/long, so the
    // same CS0034 mix arises; the indirect (`ref nuint`) form additionally loses
    // the unsigned lvalue type through ldind.i. ──────────────────────────────────

    // arg/local nuint compound with a signed (nint) right operand.
    static string RenderLocal(BinaryKind kind, TypeRef target, TypeRef rhsType, bool isUnsigned)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var binary = new Binary(kind, isChecked: false, isUnsigned, new LoadLocal(0, target), new LoadArgument(1, "b", rhsType));
        var block = new Block(0);
        block.Add(new StoreLocal(0, target, new LoadArgument(0, "arg", target)));
        block.Add(new StoreLocal(0, target, binary));
        block.Add(new Return(new LoadLocal(0, target)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(target, [new Parameter("arg", target), new Parameter("b", rhsType)], HasThis: false, GenericParameterCount: 0),
            [target],
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    static string RenderLocalConstant(BinaryKind kind, TypeRef target, TypeRef rhsType, bool isUnsigned, int value)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var rhs = new ILInspector.Decompiler.Pipeline.Convert(rhsType, isChecked: false, isUnsigned: false, new Constant(value, Int));
        var binary = new Binary(kind, isChecked: false, isUnsigned, new LoadLocal(0, target), rhs);
        var block = new Block(0);
        block.Add(new StoreLocal(0, target, new LoadArgument(0, "arg", target)));
        block.Add(new StoreLocal(0, target, binary));
        block.Add(new Return(new LoadLocal(0, target)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(target, [new Parameter("arg", target)], HasThis: false, GenericParameterCount: 0),
            [target],
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    // `ref nuint x; x op= b` — the target is read through a LoadIndirect the
    // importer types as the SIGNED native `IntPtr` (ldind.i), even though the
    // StoreIndirect's resolved pointee type is `nuint`. The compound cast must use
    // the resolved pointee type, not the bare target-read type.
    static string RenderIndirect(BinaryKind kind, bool isUnsigned)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var refParam = TypeRef.ByRef(NUInt);
        var read = new LoadIndirect(NInt, new LoadArgument(0, "x", refParam));
        var binary = new Binary(kind, isChecked: false, isUnsigned, read, new LoadArgument(1, "b", NInt));
        var block = new Block(0);
        block.Add(new StoreIndirect(NInt, new LoadArgument(0, "x", refParam), binary));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M", owner,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("x", refParam), new Parameter("b", NInt)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void NuintSubtractNint_BindsToTargetType()
    {
        var output = RenderLocal(BinaryKind.Subtract, NUInt, NInt, isUnsigned: false);

        Assert.Contains("V_0 -= (nuint)", output);
        // `nuint -= nint` is CS0034; the right operand must not stay bare nint.
        Assert.DoesNotContain("-= b", output);
    }

    [Fact]
    public void NuintSubtractNintConstant_DoesNotCastToSignedNativeInt()
    {
        var output = RenderLocalConstant(BinaryKind.Subtract, NUInt, NInt, isUnsigned: false, 16384);

        Assert.Contains("V_0 -= ", output);
        Assert.DoesNotContain("(nint)", output);
    }

    [Fact]
    public void NuintDivideUnsigned_Faithful_BindsToTargetType()
    {
        // A `div.un` (isUnsigned: true) over an unsigned target: the opcode
        // signedness matches the lvalue, so casting the right operand to `nuint`
        // does not flip the opcode and removes the CS0034 — unlike the signed
        // divide over an unsigned target, which stays unreconciled.
        var output = RenderLocal(BinaryKind.Divide, NUInt, NInt, isUnsigned: true);

        Assert.Contains("V_0 /= (nuint)", output);
        Assert.DoesNotContain("/= b", output);
    }

    [Fact]
    public void IndirectNuintSubtract_UsesResolvedPointeeType()
    {
        // The ldind.i target read is typed `nint`; without using the resolved
        // `nuint` pointee type the reconciliation would see nint/nint (not mixed)
        // and emit the CS0034 `x -= b`.
        var output = RenderIndirect(BinaryKind.Subtract, isUnsigned: false);

        Assert.Contains("x -= (nuint)", output);
        Assert.DoesNotContain("-= b", output);
    }

    [Fact]
    public void NintTarget_SameSign_NotReconciled()
    {
        // A signed nint target with a signed nint operand is not a mixed-sign pair,
        // so no cast is inserted — the canary that the reconciliation is gated on
        // an actual signedness mismatch.
        var output = RenderLocal(BinaryKind.Subtract, NInt, NInt, isUnsigned: false);

        Assert.Contains("V_0 -= b", output);
        Assert.DoesNotContain("(nuint)", output);
        Assert.DoesNotContain("(nint)", output);
    }
}
