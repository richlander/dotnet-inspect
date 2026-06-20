using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RangeFromGetSubArrayPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void GetSubArray_BothBounds_RaisesToRangeSlice()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeBoth));

        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.True(slice.Range.HasStart);
        Assert.True(slice.Range.HasEnd);
        Assert.Empty(function.Descendants.OfType<Call>());
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void GetSubArray_BothBounds_RendersIndexer()
        => Assert.Contains("return a[i..j];", Print(nameof(CfgSampleClass.ArrayRangeBoth)));

    [Fact]
    public void GetSubArray_ExplicitFromStartIndexCtor_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeExplicitFromStartIndex));

        Assert.Empty(function.Descendants.OfType<SliceExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "GetSubArray");
        Assert.Contains(function.Descendants.OfType<NewObject>(),
            n => n.Constructor.DeclaringType is { Namespace: "System", Name: "Index" });

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("new Index(i, false)", output);
        Assert.DoesNotContain("return a[i..j];", output);
    }

    [Fact]
    public void GetSubArray_FromOnly_RendersOpenEnd()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeFrom));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.True(slice.Range.HasStart);
        Assert.False(slice.Range.HasEnd);
        Assert.Contains("return a[i..];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_ToOnly_RendersOpenStart()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeTo));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.False(slice.Range.HasStart);
        Assert.True(slice.Range.HasEnd);
        Assert.Contains("return a[..j];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_All_RendersBareRange()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeAll));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.False(slice.Range.HasStart);
        Assert.False(slice.Range.HasEnd);
        Assert.Contains("return a[..];", CSharpPrinter.Print(function).Output);
    }
}
