using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// C#'s binary numeric promotion never yields a sub-int type: a byte/sbyte/short/
// ushort/char arithmetic, bitwise, or shift expression is typed `int`. The IR
// keeps the ECMA stack type (the sub-int left operand), so a `char - n` result
// flowing into a uint slot must still carry an explicit (uint) cast — otherwise
// the printer treats it as the implicit char->uint conversion and drops the cast
// (CS0266). A genuinely sub-int target the source widens to int keeps no cast.
public class SubIntBinaryCastTests
{
    static readonly TypeRef Char = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef UInt32 = TypeRef.CoreLib("System", "UInt32");

    static string RenderStore(TypeRef localType)
    {
        // V_0 = c - 56320; return V_0;  with c a char parameter.
        var subtract = new Binary(BinaryKind.Subtract, isChecked: false, isUnsigned: false,
            new LoadArgument(0, "c", Char), new Constant(56320, Int32));
        var entry = new Block(0);
        entry.Add(new StoreLocal(0, localType, subtract));
        entry.Add(new Return(new LoadLocal(0, localType)));
        var container = new BlockContainer();
        container.Add(entry);

        var signature = new MethodSignature(localType, [new Parameter("c", Char)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("System", "Sample"), signature, [], container);
        return CSharpPrinter.Print(function).Output ?? "";
    }

    [Fact]
    public void CharSubtractIntoUInt_KeepsExplicitCast()
    {
        var output = RenderStore(UInt32);

        // `char - int` is int in C#; assigning int to uint needs an explicit cast.
        Assert.Contains("(uint)(c - 56320)", output);
    }

    [Fact]
    public void CharSubtractIntoInt_NeedsNoCast()
    {
        var output = RenderStore(Int32);

        // `char - int` is already int; no cast, and certainly no `(uint)`.
        Assert.DoesNotContain("(uint)", output);
        Assert.Contains("c - 56320", output);
    }
}
