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
}
