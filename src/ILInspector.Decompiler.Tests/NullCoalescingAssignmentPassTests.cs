using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class NullCoalescingAssignmentPassTests
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
    public void LocalNullAssignmentDiamond_RaisesToNullCoalescingAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignLocal));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>());
        Assert.Equal("string", assignment.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(assignment.Value);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignLocal))).Output;

        Assert.NotNull(output);
        Assert.Contains("value ??= fallback;", output);
        Assert.Contains("return value;", output);
    }

    [Fact]
    public void NullCoalescingOperator_RemainsExpression()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalesce));

        Assert.Single(function.Descendants.OfType<Coalesce>());
        Assert.DoesNotContain(function.Descendants.OfType<NullCoalescingAssignment>(), _ => true);
    }
}
