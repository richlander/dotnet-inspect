using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class DeconstructionAssignmentPassTests
{
    static IrFunction Raised(string methodName, Type? type = null)
    {
        type ??= typeof(CfgSampleClass);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ValueTupleFieldStores_RaiseToDeconstruction()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructTuplePair));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.LocalIndices.Length);
        Assert.True(deconstruction.IsDeclaration);
        Assert.IsType<LoadArgument>(deconstruction.Source);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name is "Item1" or "Item2");
    }

    [Fact]
    public void ExistingLocalStores_RaiseToDeconstructionAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructIntoExistingLocals));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.LocalIndices.Length);
        Assert.False(deconstruction.IsDeclaration);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name is "Item1" or "Item2");
    }

    [Fact]
    public void PrintRaised_RendersDeconstructionAssignment_WithoutTypes()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DeconstructIntoExistingLocals))).Output;

        Assert.NotNull(output);
        Assert.Contains("(sum, product) = pair;", output);
        Assert.DoesNotContain("(int sum, int product) = pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void PrintRaised_RendersDeconstructionDeclaration()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DeconstructTuplePair))).Output;

        Assert.NotNull(output);
        Assert.Contains("(int sum, int product) = pair;", output);
        Assert.Contains("return sum + product;", output);
    }

    [Fact]
    public void HandWrittenTupleFieldAccess_IsNotRaised()
    {
        var function = Raised(nameof(DeconstructionAdversarialSamples.ManualTupleFields), typeof(DeconstructionAdversarialSamples));

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Item", output);
        Assert.DoesNotContain("(int sum, int product) = pair;", output);
    }
}

public static class DeconstructionAdversarialSamples
{
    public static int ManualTupleFields((int Sum, int Product) pair)
    {
        int sum = pair.Sum;
        int product = pair.Product;
        return sum + product;
    }
}
