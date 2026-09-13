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
        Assert.True(CSharpConversionRules.ConstantFits(int.MaxValue, Core("IntPtr")));
        Assert.True(CSharpConversionRules.ConstantFits(-1, Core("IntPtr")));
        Assert.False(CSharpConversionRules.ConstantFits((long)int.MaxValue + 1, Core("IntPtr")));
        Assert.True(CSharpConversionRules.ConstantFits(uint.MaxValue, Core("UIntPtr")));
        Assert.False(CSharpConversionRules.ConstantFits(-1, Core("UIntPtr")));
        Assert.False(CSharpConversionRules.ConstantFits((long)uint.MaxValue + 1, Core("UIntPtr")));
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
    public void IsImplicitNumericAssignment_IncludesFloatWideningsAndRejectsExplicitPairs()
    {
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("Int32"), Core("Single")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("Int64"), Core("Single")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("UInt64"), Core("Single")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("UInt64"), Core("Double")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("Int32"), Core("IntPtr")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("UInt32"), Core("UIntPtr")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("IntPtr"), Core("Double")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("UIntPtr"), Core("UInt64")));
        Assert.True(CSharpConversionRules.IsImplicitNumericAssignment(Core("Char"), Core("UInt16")));
        Assert.False(CSharpConversionRules.IsImplicitNumericAssignment(Core("Double"), Core("Single")));
        Assert.False(CSharpConversionRules.IsImplicitNumericAssignment(Core("Int32"), Core("UInt32")));
        Assert.False(CSharpConversionRules.IsImplicitNumericAssignment(Core("Int64"), Core("IntPtr")));
        Assert.False(CSharpConversionRules.IsImplicitNumericAssignment(Core("UIntPtr"), Core("Int64")));
        Assert.False(CSharpConversionRules.IsImplicitNumericAssignment(Core("Boolean"), Core("Int32")));
    }

    [Fact]
    public void CheckedConversionCanThrow_ModelsCheckedExplicitCastHazards()
    {
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("Int32"), Core("Int64")));
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("UInt32"), Core("Int64")));
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("IntPtr"), Core("Int64")));
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("UIntPtr"), Core("UInt64")));
        Assert.False(CSharpConversionRules.CheckedConversionCanThrow(Core("Byte"), Core("Char")));
        Assert.True(CSharpConversionRules.CheckedConversionCanThrow(Core("Int32"), Core("UInt32")));
        Assert.True(CSharpConversionRules.CheckedConversionCanThrow(Core("UInt32"), Core("Int32")));
        Assert.True(CSharpConversionRules.CheckedConversionCanThrow(Core("UIntPtr"), Core("Int64")));
    }
}
