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

    [Fact]
    public void ManualHandlerSourceLocal_IsNotRaised()
    {
        // This is a source-level handler local, not the compiler's hidden temp
        // for `$"..."`. Raising it would erase the user's chosen lower-level
        // spelling and, for richer overloads, can drop semantics.
        var function = Raised(nameof(CfgSampleClass.ManualInterpolatedStringHandler));

        Assert.DoesNotContain(function.Descendants.OfType<InterpolatedStringExpression>(), _ => true);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("DefaultInterpolatedStringHandler handler", output);
        Assert.Contains("handler.AppendLiteral", output);
        Assert.DoesNotContain("$\"Hello", output);
    }

    [Fact]
    public void ManualHandlerProviderCtor_IsNotRaised()
    {
        // The provider overload carries formatting semantics not represented by
        // a plain interpolated string in this IR slice.
        var function = Raised(nameof(CfgSampleClass.ManualInterpolatedStringHandlerWithProvider));

        Assert.DoesNotContain(function.Descendants.OfType<InterpolatedStringExpression>(), _ => true);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("CultureInfo.InvariantCulture", output);
        Assert.DoesNotContain("$\"value=", output);
    }
}
