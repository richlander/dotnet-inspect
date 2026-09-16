using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class EnumUnderlyingCastTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void EnumReturnedAsUnderlyingInt_RendersExplicitCast()
    {
        var output = Render(nameof(CfgSampleClass.PriorityToInt));

        Assert.Contains("return (int)priority;", output);
        Assert.DoesNotContain("return priority;", output);
    }

    [Fact]
    public void EnumArithmetic_RendersOperandCasts()
    {
        var output = Render(nameof(CfgSampleClass.PriorityPlusOne));

        Assert.Contains("return (int)priority + 1;", output);
        Assert.DoesNotContain("return priority + 1;", output);
    }

    [Fact]
    public void EnumEnumArithmetic_RendersBothOperandCasts()
    {
        var output = Render(nameof(CfgSampleClass.PrioritySum));

        Assert.Contains("return (int)left + (int)right;", output);
        Assert.DoesNotContain("return left + right;", output);
    }

    [Fact]
    public void EnumCallArgumentToUnderlyingInt_RendersExplicitCast()
    {
        var output = Render(nameof(CfgSampleClass.CallWithPriorityInt));

        Assert.Contains("TakesPriorityInt((int)priority);", output);
        Assert.DoesNotContain("TakesPriorityInt(priority);", output);
    }

    [Fact]
    public void LongEnumReturnedAsUnderlyingLong_RendersExplicitCast()
    {
        var output = Render(nameof(CfgSampleClass.LongPriorityToLong));

        Assert.Contains("return (long)priority;", output);
        Assert.DoesNotContain("return priority;", output);
    }

    [Fact]
    public void EnumToWiderPrimitive_KeepsExistingConvert()
    {
        var output = Render(nameof(CfgSampleClass.PriorityToLong));

        Assert.Contains("return (long)priority;", output);
    }

    [Fact]
    public void EnumPlusIntReturnedAsEnum_StaysEnumTyped()
    {
        var output = Render(nameof(CfgSampleClass.NextPriority));

        Assert.Contains("return priority + 1;", output);
        Assert.DoesNotContain("return (int)priority + 1;", output);
    }

    [Fact]
    public void EnumMinusIntReturnedAsEnum_StaysEnumTyped()
    {
        var output = Render(nameof(CfgSampleClass.PreviousPriority));

        Assert.Contains("return priority - 1;", output);
        Assert.DoesNotContain("return (int)priority - 1;", output);
    }

    [Fact]
    public void CheckedEnumArithmeticToUnderlyingInt_PreservesCheckedContext()
    {
        var output = Render(nameof(CfgSampleClass.CheckedPriorityPlusOne));

        Assert.Contains("return checked((int)priority + 1);", output);
    }
}
