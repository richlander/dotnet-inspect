using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class CSharpConversionRulesTests
{
    static TypeRef Core(string name) => TypeRef.CoreLib("System", name);

    [Fact]
    public void NeedsNumericCast_IsLimitedToSameStackFamilyPrimitiveReinterpretation()
    {
        Assert.True(CSharpConversionRules.NeedsNumericCast(Core("Int32"), Core("UInt32")));
        Assert.False(CSharpConversionRules.NeedsNumericCast(Core("Int32"), Core("Int64")));
        Assert.False(CSharpConversionRules.NeedsNumericCast(Core("Boolean"), Core("Int32")));
        Assert.False(CSharpConversionRules.NeedsNumericCast(Core("Int16"), Core("Int32")));
    }

    [Fact]
    public void ConstantFits_ModelsCSharpConstantExpressionConversionRanges()
    {
        Assert.True(CSharpConversionRules.ConstantFits(0, Core("UInt32")));
        Assert.False(CSharpConversionRules.ConstantFits(-1, Core("UInt32")));
        Assert.True(CSharpConversionRules.ConstantFits(-1, Core("IntPtr")));
        Assert.False(CSharpConversionRules.ConstantFits(-1, Core("UIntPtr")));
    }

    [Fact]
    public void IsImplicitIntegerWidening_ModelsValueRangeContainment()
    {
        Assert.True(CSharpConversionRules.IsImplicitIntegerWidening(Core("Int32"), Core("Int64")));
        Assert.True(CSharpConversionRules.IsImplicitIntegerWidening(Core("UInt32"), Core("Int64")));
        Assert.True(CSharpConversionRules.IsImplicitIntegerWidening(Core("IntPtr"), Core("Int64")));
        Assert.True(CSharpConversionRules.IsImplicitIntegerWidening(Core("UIntPtr"), Core("UInt64")));
        Assert.False(CSharpConversionRules.IsImplicitIntegerWidening(Core("Int32"), Core("UInt32")));
    }

    [Fact]
    public void CheckedConversionCanThrow_ModelsCheckedExplicitCastHazards()
    {
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("Int32"), Core("Int64")));
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("UInt32"), Core("Int64")));
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("Byte"), Core("Char")));
        Assert.True(CSharpConversionRules.CheckedConversionCanThrow(Core("Int32"), Core("UInt32")));
        Assert.True(CSharpConversionRules.CheckedConversionCanThrow(Core("UInt32"), Core("Int32")));
        Assert.True(CSharpConversionRules.CheckedConversionCanThrow(Core("IntPtr"), Core("Int64")));
    }
}
