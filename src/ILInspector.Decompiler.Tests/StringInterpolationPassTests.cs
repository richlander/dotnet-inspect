using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StringInterpolationPassTests
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
    public void HandlerAppendSequence_RaisesToInterpolatedString()
    {
        var function = Raised(nameof(CfgSampleClass.StringInterpolation));

        var interpolation = Assert.Single(function.Descendants.OfType<InterpolatedStringExpression>());
        Assert.Equal(5, interpolation.Parts.Length);
        Assert.Equal(2, interpolation.FormattedValues.Count);
        Assert.DoesNotContain(function.Descendants.OfType<NewObject>(),
            n => n.Constructor.DeclaringType.Name == "DefaultInterpolatedStringHandler");
    }

    [Fact]
    public void PrintRaised_RendersInterpolatedString()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.StringInterpolation))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"Hello, {name}! You are {age} years old.\";", output);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", output);
    }

    [Fact]
    public void RepeatedFormattedValues_RendersEachHole()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolatedStruct))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"value={value} again={value}\";", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }
}
