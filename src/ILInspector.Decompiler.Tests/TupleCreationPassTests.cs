using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TupleCreationPassTests
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

    [Fact]
    public void ValueTupleConstructor_RaisesToTupleExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TuplePair));

        var tuple = Assert.Single(function.Descendants.OfType<TupleExpression>());
        Assert.Equal(2, tuple.Elements.Count);
        Assert.StartsWith("ValueTuple<", tuple.TupleType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void PrintRaised_RendersTupleLiteral()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.TuplePair))).Output;

        Assert.NotNull(output);
        Assert.Contains("return (a + b, a * b);", output);
        Assert.DoesNotContain("new ValueTuple", output);
    }
}
