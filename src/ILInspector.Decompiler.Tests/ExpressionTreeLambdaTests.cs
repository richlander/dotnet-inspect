using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExpressionTreeLambdaTests
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
    public void SimpleExpressionTreeLambda_RecoversLambda()
    {
        var function = Raised(nameof(CfgSampleClass.SimpleExpressionTreeLambda));

        var lambda = Assert.Single(function.Descendants.OfType<Lambda>());
        Assert.Single(lambda.Parameters);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("return x => x + 1;", output);
        Assert.DoesNotContain("Expression.Lambda", output);
        Assert.DoesNotContain("Expression.Add", output);
        Assert.DoesNotContain("Expression.Parameter", output);
    }

    [Fact]
    public void ManualExpressionTreeFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualSimpleExpressionTreeFactory));

        // The manual alias reads the parameter through two independent value
        // sources (a stack slot in the body, a local in the array), so the
        // single-source identity guard rejects it: no fabricated lambda.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.Contains("Expression.Add", output);
        Assert.Contains("Expression.Parameter(typeof(int), \"x\")", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualReusedParameterFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualReusedParameterFactory));

        // One ParameterExpression backs both array entries; recovering `(x, x)`
        // would be invalid C#. The distinct-owning-local guard declines it.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualDuplicateNameFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualDuplicateNameFactory));

        // Two distinct ParameterExpressions share the name "x"; recovering would
        // emit duplicate declarations. The distinct-name guard declines it.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }

    [Fact]
    public void ManualUnspellableNameFactory_StaysFactoryCalls()
    {
        var function = Raised(nameof(CfgSampleClass.ManualUnspellableNameFactory));

        // The name "bad-name" is not a C#-spellable identifier; sanitizing it would
        // change ParameterExpression.Name. The escapable-name guard declines it.
        Assert.Empty(function.Descendants.OfType<Lambda>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("Expression.Lambda<Func<int, int>>", output);
        Assert.DoesNotContain("=>", output);
    }
}
