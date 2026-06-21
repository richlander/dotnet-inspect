using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IsPatternPassTests
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
    public void StatementGuard_RaisesAsNullTestToIsPattern()
    {
        // `if (o is string s)` lowers to `string s = o as string; if (s != null)`.
        // The pass folds the as-store and null test into one `is` pattern.
        var function = Raised(nameof(CfgSampleClass.IsPatternGuard));

        var pattern = Assert.Single(function.Descendants.OfType<IsPattern>());
        Assert.Equal("string", pattern.Type.ToDisplayString());
        Assert.IsType<LoadArgument>(pattern.Value);
        Assert.Empty(function.Descendants.OfType<IsInstance>());
    }

    [Fact]
    public void StatementGuard_RendersIsPatternHeader()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.IsPatternGuard))).Output;

        Assert.NotNull(output);
        Assert.Contains("if (o is string s)", output);
        Assert.DoesNotContain("as string", output);
        Assert.DoesNotContain("is not null", output);
    }

    [Fact]
    public void Conjunction_RaisesLeftOperandToIsPattern()
    {
        // `o is string s && s.Length > 0` — the pattern binds in the left
        // conjunct and is read in the right.
        var function = Raised(nameof(CfgSampleClass.IsPatternConjunction));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string s && s.Length > 0", output);
    }

    [Fact]
    public void PropertyPattern_RendersPropertyPatternClause()
    {
        // `o is string { Length: 5 }` lowers to the same as-store plus
        // `s != null && s.Length == 5`; the printer folds the internal type
        // pattern + equality back to the property-pattern altitude.
        var function = Raised(nameof(CfgSampleClass.IsPatternProperty));

        Assert.Single(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("o is string { Length: 5 }", output);
        Assert.DoesNotContain("&&", output);
        Assert.DoesNotContain(".Length == 5", output);
    }

    [Fact]
    public void PropertyPattern_WhenPatternLocalIsUsedInBody_StaysFlat()
    {
        var function = Raised(nameof(CfgSampleClass.IsPatternPropertyWithBindingUse));

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("{ Length: 5 }", output);
        Assert.Contains(".Length", output);
    }

    [Fact]
    public void AsLocalReadOnFallThroughPath_StaysFlat()
    {
        // The `as` local is read on both the matched and fall-through paths, so
        // binding it inside the pattern would leave it not definitely assigned
        // on the false path. The pass must leave the flat `as` + null test.
        var function = Raised(nameof(CfgSampleClass.AsWithoutPattern));

        Assert.Empty(function.Descendants.OfType<IsPattern>());
        Assert.Single(function.Descendants.OfType<IsInstance>());
    }
}
