using ILInspector.Decompiler.Pipeline;

using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pointer additive arithmetic must not double-scale by <c>sizeof(element)</c>.
/// An IL pointer <c>add</c>/<c>sub</c> is raw byte-address math and already
/// carries the explicit <c>* sizeof(T)</c> the source compiled to (a pointer
/// difference divides by <c>sizeof(T)</c>); a C# pointer <c>+</c>/<c>-</c> scales
/// the integer operand by <c>sizeof(element)</c> again, so spelling it directly
/// silently computes the wrong value. The printer routes the IL byte offset
/// through <c>byte*</c> so the math is reproduced verbatim. See issue #1593.
/// </summary>
public class PointerArithmeticScalingTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef NInt = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef IntPointer = TypeRef.Pointer(Int32);
    static readonly TypeRef BytePointer = TypeRef.Pointer(Byte);
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "PointerArithmeticSamples");

    // `p + (nint)i * 4`: the IL byte offset for `p[i]` / `p + i` on an `int*`.
    static Binary ScaledIntPointerOffset()
        => new(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "p", IntPointer),
            new Binary(
                BinaryKind.Multiply,
                isChecked: false,
                isUnsigned: false,
                new Convert(NInt, isChecked: false, isUnsigned: false, new LoadArgument(1, "i", Int32)),
                new Constant(4, Int32)));

    [Fact]
    public void PointerIndex_RoutesByteOffsetThroughBytePointer()
    {
        // `*(p[i])`: a LoadIndirect over `p + (nint)i * 4`. The result is cast back
        // to `int*` so the deref still reads an `int`, not a single byte.
        var output = Print(ReturnFunction(
            Int32,
            new LoadIndirect(Int32, ScaledIntPointerOffset()),
            new Parameter("p", IntPointer),
            new Parameter("i", Int32)));

        Assert.Contains("*((int*)((byte*)p + ", output);
        // The bare C# pointer deref would scale `* 4` a second time (element i*4).
        Assert.DoesNotContain("*(p + ", output);
    }

    [Fact]
    public void PointerAdd_CastsResultBackToPointerType()
    {
        var output = Print(ReturnFunction(
            IntPointer,
            ScaledIntPointerOffset(),
            new Parameter("p", IntPointer),
            new Parameter("i", Int32)));

        Assert.Contains("return (int*)((byte*)p + ", output);
        Assert.DoesNotContain("return p + ", output);
    }

    [Fact]
    public void PointerDifference_DifferencesBytePointers()
    {
        // `(long)((a - b) / 4)`: C# `int* - int*` already yields an element count,
        // so dividing by 4 again is wrong; differencing `byte*` yields the byte
        // count the IL `sub` computes, and `/ 4` recovers the element count.
        var difference = new Binary(
            BinaryKind.Subtract,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "a", IntPointer),
            new LoadArgument(1, "b", IntPointer));
        var scaled = new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: false, difference, new Constant(4, Int32));
        var output = Print(ReturnFunction(
            Int64,
            new Convert(Int64, isChecked: false, isUnsigned: false, scaled),
            new Parameter("a", IntPointer),
            new Parameter("b", IntPointer)));

        Assert.Contains("((byte*)a - (byte*)b) / 4", output);
        Assert.DoesNotContain("(a - b) / 4", output);
    }

    [Fact]
    public void IntegerArithmetic_IsUnchanged()
    {
        // Negative canary: non-pointer integer arithmetic gets no `byte*` cast.
        var sum = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "a", Int32),
            new LoadArgument(1, "b", Int32));
        var output = Print(ReturnFunction(
            Int32,
            sum,
            new Parameter("a", Int32),
            new Parameter("b", Int32)));

        Assert.Contains("return a + b;", output);
        Assert.DoesNotContain("byte*", output);
    }

    [Fact]
    public void BytePointerArithmetic_IsUnchanged()
    {
        // A one-byte element is not scaled by the IL, so C#'s pointer `+` already
        // matches; no `byte*` rewrite (and no `(byte*)(byte*)` double cast).
        var add = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "p", BytePointer),
            new LoadArgument(1, "i", Int32));
        var output = Print(ReturnFunction(
            BytePointer,
            add,
            new Parameter("p", BytePointer),
            new Parameter("i", Int32)));

        Assert.Contains("return p + i;", output);
        Assert.DoesNotContain("byte*", output);
    }

    static string Print(IrFunction function)
    {
        function.CheckInvariant();
        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
    }

    static IrFunction ReturnFunction(TypeRef returnType, IrExpression value, params Parameter[] parameters)
    {
        var block = new Block();
        block.Add(new Return(value));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
