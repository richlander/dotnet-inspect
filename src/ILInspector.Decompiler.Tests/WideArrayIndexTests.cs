using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Printer")]
public class WideArrayIndexTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName)
        => CSharpPrinter.Print(Raised(methodName)).Output!.ReplaceLineEndings("\n").Trim();

    [Fact]
    public void LongArrayIndexLoad_RendersBareIndex()
    {
        // The compiler-inserted conv.ovf.i (checked (nint) range conversion) is
        // implicit for a long index, so it must not be spelled explicitly.
        Assert.Equal("return a[i];", Print(nameof(CfgSampleClass.LongArrayIndex)));
    }

    [Fact]
    public void ULongArrayIndexLoad_RendersBareIndex()
    {
        // conv.ovf.i.un for a ulong index is likewise implicit.
        Assert.Equal("return a[i];", Print(nameof(CfgSampleClass.ULongArrayIndex)));
    }

    [Fact]
    public void LongArrayIndexStore_RendersBareIndex()
    {
        Assert.Equal("a[i] = v;", Print(nameof(CfgSampleClass.LongArrayIndexStore)));
    }

    [Fact]
    public void LongArrayIndexRef_RendersBareIndex()
    {
        Assert.Equal("return ref a[i];", Print(nameof(CfgSampleClass.LongArrayIndexRef)));
    }

    [Fact]
    public void LongArrayIndexExpression_RendersBareIndex()
    {
        // Only the outer index range conversion is stripped; the long operand
        // expression is spelled as-is (its own add opcode is unaffected).
        Assert.Equal("return a[i + j];", Print(nameof(CfgSampleClass.LongArrayIndexExpr)));
    }

    [Fact]
    public void IntArrayIndexLoad_IsUnchanged()
    {
        // An int index needs no range conversion, so there is nothing to strip.
        Assert.Equal("return a[0];", Print(nameof(CfgSampleClass.FirstElement)));
    }

    [Fact]
    public void LongEnumArrayElementIndex_CastsToUnderlyingPrimitive()
    {
        // A long-backed enum array element (`ldelem.i8`) reports Int64 storage,
        // but the rendered `values[j]` is enum-typed; a bare `a[values[j]]` is
        // CS0266. The printer casts to the underlying wide primitive, which is
        // idiomatic and opcode-exact.
        Assert.Equal("return a[(long)values[j]];", Print(nameof(CfgSampleClass.LongEnumArrayIndex)));
    }

    [Fact]
    public void LongEnumRefIndex_CastsToUnderlyingPrimitive()
    {
        // The ref/pointee analog (`ldind.i8`): the enum pointee is cast to its
        // underlying wide primitive, not stripped bare (CS0266).
        Assert.Equal("return a[(long)(r)];", Print(nameof(CfgSampleClass.LongEnumRefArrayIndex)));
    }

    [Fact]
    public void ULongArrayElementAsSigned_KeepsExplicitLongCast()
    {
        // `ldelem.i8` masks the `ulong` element as Int64, but the conversion is
        // signed (`conv.ovf.i`). Stripping bare would spell a `ulong` index and
        // re-insert `conv.ovf.i.un`, so the `(long)` cast must be kept.
        Assert.Equal("return a[(long)v[j]];", Print(nameof(CfgSampleClass.ULongArrayIndexAsSigned)));
    }

    [Fact]
    public void ULongRefAsSigned_KeepsExplicitLongCast()
    {
        // The ref analog (`ldind.i8`) of the masked-`ulong`-as-signed index.
        Assert.Equal("return a[(long)(r)];", Print(nameof(CfgSampleClass.ULongRefIndexAsSigned)));
    }

    [Fact]
    public void LongArrayElementAsUnsigned_KeepsExplicitULongCast()
    {
        // The mirror: a `long` element used as an unsigned index (`conv.ovf.i.un`)
        // keeps a `(ulong)` cast, not stripped bare (which would be signed).
        Assert.Equal("return a[(ulong)v[j]];", Print(nameof(CfgSampleClass.LongArrayIndexAsUnsigned)));
    }

    [Fact]
    public void ULongArrayElementIndex_StripsBareLikeLong()
    {
        // A plain `ulong` element index: the recovered `ulong` element type
        // matches the unsigned conversion, so it strips to the bare index.
        Assert.Equal("return a[v[j]];", Print(nameof(CfgSampleClass.ULongArrayElementIndex)));
    }

    [Fact]
    public void ULongIndexAsSignedChecked_WrapsCastInUnchecked()
    {
        // Inside a `checked` region the sign-changing `(long)` reinterpret would
        // recompile to a `conv.ovf.i8.un` the original never had, so it is wrapped
        // in `unchecked(...)`; the always-checked index conv stays outside.
        Assert.Equal("return checked(a[unchecked((long)v[j])] + 1);", Print(nameof(CfgSampleClass.ULongIndexAsSignedChecked)));
    }

    [Fact]
    public void LongIndexAsUnsignedChecked_WrapsCastInUnchecked()
    {
        // The mirror: a `long` element used as an unsigned index inside `checked`
        // wraps its sign-changing `(ulong)` cast in `unchecked(...)`.
        Assert.Equal("return checked(a[unchecked((ulong)v[j])] + 1);", Print(nameof(CfgSampleClass.LongIndexAsUnsignedChecked)));
    }

    [Fact]
    public void LongEnumIndexChecked_KeepsBareCast()
    {
        // A same-sign cast (a long-backed enum's `(long)`) emits no conv even
        // inside `checked`, so it stays bare with no spurious `unchecked(...)`.
        Assert.Equal("return checked(a[(long)values[j]] + 1);", Print(nameof(CfgSampleClass.LongEnumIndexChecked)));
    }

    [Fact]
    public void ULongSumIndexAsSigned_CastsWholeExpressionToLong()
    {
        // A `ulong` sum used as a signed index reports Int64 stack storage; the
        // operand type is recovered through the binary as `ulong`, so it does not
        // strip bare (which would re-insert `conv.ovf.i.un`) but casts the whole
        // expression to `(long)`.
        Assert.Equal("return a[(long)((ulong)v[j] + x)];", Print(nameof(CfgSampleClass.ULongSumIndexAsSigned)));
    }

    [Fact]
    public void ULongElementSumIndexAsSigned_CastsWholeExpressionToLong()
    {
        // Both operands are masked `ulong` elements: the recovery sees through the
        // `ldelem.i8` masking on each side, still typing the sum `ulong` and
        // keeping the `(long)` cast.
        Assert.Equal("return a[(long)(v[j] + w[k])];", Print(nameof(CfgSampleClass.ULongElementSumIndexAsSigned)));
    }

    [Fact]
    public void ULongSumIndexBare_StripsBare()
    {
        // A bare `ulong` compound index matches the unsigned conversion, so it
        // strips to the bare sum with no redundant `(ulong)` cast.
        Assert.Equal("return a[v[j] + w[k]];", Print(nameof(CfgSampleClass.ULongSumIndexBare)));
    }
}
