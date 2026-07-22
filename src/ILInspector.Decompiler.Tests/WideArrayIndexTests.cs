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

    [Fact]
    public void CheckedULongSumIndexAsSigned_CastsWholeExpressionToLong()
    {
        // `checked` never changes an expression's C# type: a `checked` `ulong` sum
        // used as a signed index is still `ulong`, so the recovery keeps the
        // `(long)` cast. Stripping bare would re-insert `conv.ovf.i.un`.
        Assert.Equal("return a[(long)checked(v[j] + x)];", Print(nameof(CfgSampleClass.CheckedULongSumIndexAsSigned)));
    }

    [Fact]
    public void ULongShrIndexAsSigned_KeepsLongCast()
    {
        // An unsigned shift-right (`shr.un`) result takes the shifted operand's
        // `ulong` type; used as a signed index it keeps the `(long)` cast.
        Assert.Equal("return a[(long)((ulong)v[j] >> 1)];", Print(nameof(CfgSampleClass.ULongShrIndexAsSigned)));
    }

    [Fact]
    public void ULongDivIndexAsSigned_KeepsLongCast()
    {
        // An unsigned divide (`div.un`) carries its signedness in the opcode
        // variant; a `ulong` quotient used as a signed index keeps the `(long)`.
        Assert.Equal("return a[(long)((ulong)v[j] / d)];", Print(nameof(CfgSampleClass.ULongDivIndexAsSigned)));
    }

    [Fact]
    public void ULongRemIndexAsSigned_KeepsLongCast()
    {
        // An unsigned remainder (`rem.un`) analog of the divide case.
        Assert.Equal("return a[(long)((ulong)v[j] % d)];", Print(nameof(CfgSampleClass.ULongRemIndexAsSigned)));
    }

    [Fact]
    public void LongShlIndexBare_StripsBare()
    {
        // A genuinely-signed `long` shift-left index recovers as `long`, matching
        // the signed `conv.ovf.i`, so it strips bare with no spurious `(long)`.
        Assert.Equal("return a[i << 1];", Print(nameof(CfgSampleClass.LongShlIndexBare)));
    }

    [Fact]
    public void LongCheckedSumIndexBare_StripsBare()
    {
        // A `checked` signed `long` add index likewise recovers as `long` and
        // strips bare — checkedness does not force a cast.
        Assert.Equal("return a[checked(i + j)];", Print(nameof(CfgSampleClass.LongCheckedSumIndexBare)));
    }

    [Fact]
    public void NotULongElementIndexAsSigned_KeepsLongCast()
    {
        // A unary reports its operand's masked stack type; recovering through it
        // types `~v[j]` (over a `ulong` element) as `ulong`, so a signed index
        // keeps the `(long)` cast (bare would flip to `conv.ovf.i.un`).
        Assert.Equal("return a[(long)(~v[j])];", Print(nameof(CfgSampleClass.NotULongElementIndexAsSigned)));
    }

    [Fact]
    public void NegLongIndexBare_StripsBare()
    {
        // A genuinely-signed `long` negate index recovers as `long` and strips
        // bare with no spurious `(long)` cast.
        Assert.Equal("return a[-i];", Print(nameof(CfgSampleClass.NegLongIndexBare)));
    }

    [Fact]
    public void NotLongIndexBare_StripsBare()
    {
        // The bitwise-not analog of the signed-negate strip.
        Assert.Equal("return a[~i];", Print(nameof(CfgSampleClass.NotLongIndexBare)));
    }

    [Fact]
    public void NegLongIndexInChecked_WrapsNegateInUnchecked()
    {
        // The stripped bare `-i` index would recompile as an overflow-checked
        // negate inside the enclosing `checked`, so the negate is wrapped in
        // `unchecked(...)` to keep the original unchecked `neg`.
        Assert.Equal("return checked(a[unchecked(-i)] + 1);", Print(nameof(CfgSampleClass.NegLongIndexInChecked)));
    }

    [Fact]
    public void NegULongElementIndexAsSigned_CastsNegateOperand()
    {
        // C# cannot negate a `ulong`, so the dropped signed reinterpret is
        // re-inserted on the OPERAND (`-(long)v[j]`), not around the negate —
        // `(long)(-v[j])` would be CS0023. Recompiles to `ldelem.i8; neg`.
        Assert.Equal("return a[-(long)v[j]];", Print(nameof(CfgSampleClass.NegULongElementIndexAsSigned)));
    }

    [Fact]
    public void NegULongElementToLong_CastsNegateOperand_OutsideIndex()
    {
        // The fix is in the general printer, not array indexing: a masked `ulong`
        // element negated outside any index still gets the operand cast.
        Assert.Equal("return -(long)v[j];", Print(nameof(CfgSampleClass.NegULongElementToLong)));
    }

    [Fact]
    public void NegNuintElementToNint_CastsNegateOperand()
    {
        // The `nuint` mirror: unary minus is illegal on `nuint`, so the operand is
        // reinterpreted `(nint)` before the negate.
        Assert.Equal("return -(nint)v[j];", Print(nameof(CfgSampleClass.NegNuintElementToNint)));
    }

    [Fact]
    public void NegEnumULongElementIndex_CastsOperandToLong()
    {
        // Unary minus is illegal on EVERY enum, not just `ulong`/`nuint`. An
        // 8-byte-backed enum negate re-inserts `(long)` on the operand and, used as
        // a signed index, strips clean. `(long)(-v[j])` would be CS0023.
        Assert.Equal("return a[-(long)v[j]];", Print(nameof(CfgSampleClass.NegEnumULongElementIndexAsSigned)));
    }

    [Fact]
    public void NegEnumLongElementToLong_CastsOperandToLong()
    {
        // A `long`-backed enum negated outside any index still needs the operand
        // cast — the enum has no unary minus. General printer path, not indexing.
        Assert.Equal("return -(long)v[j];", Print(nameof(CfgSampleClass.NegEnumLongElementToLong)));
    }

    [Fact]
    public void NegEnumUIntElementToInt_CastsOperandToInt()
    {
        // A 4-byte (`uint`-backed) enum reinterprets the operand as `(int)`, the
        // value-preserving `I4` view the `neg` ran on (the outer `(int)` is the
        // enum-to-underlying conversion the source spelled, unrelated to the fix).
        Assert.Equal("return (int)(-(int)v[j]);", Print(nameof(CfgSampleClass.NegEnumUIntElementToInt)));
    }

    [Fact]
    public void NegCrossAssemblyEnumElementIndex_CastsOperandToInt()
    {
        // A cross-assembly (CoreLib) enum resolves to an Unknown shape, so its
        // underlying width is unavailable via the enum map. The enum still has no
        // unary minus, but the masked stack width is in the `ldelem.i4` opcode, so
        // the reinterpret is recovered from it: `a[-(int)v[j]]`. The width fallback
        // covers unresolved enums the underlying-based path cannot see, and used as
        // a signed index it strips clean. Recompiles to `ldelem.i4; neg; conv.i`.
        Assert.Equal("return a[-(int)v[j]];", Print(nameof(CfgSampleClass.NegCrossAssemblyEnumElementIndex)));
    }
}
