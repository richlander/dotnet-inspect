using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class CompilerGeneratedNamesTests
{
    [Theory]
    [InlineData("<Owner>b__0_0", true)]
    [InlineData("<Owner>b__0", true)]
    [InlineData("<Owner>b__0_0`1", true)]
    [InlineData("<Owner>g__Local|0_0", true)]
    [InlineData("<Owner>g__Local|0_0`1", true)]
    [InlineData("<<Owner>b__0_0>b__0_1", true)]
    [InlineData("Noise>b__0_0", false)]
    [InlineData(">g__Local|0_0", false)]
    [InlineData("<Owner>b__", false)]
    [InlineData("<Owner>b__x", false)]
    [InlineData("<Owner>b__0_x", false)]
    [InlineData("<Owner>b__00_0", false)]
    [InlineData("<Owner>b__2147483648_0", false)]
    [InlineData("<Owner>g__Local|x_0", false)]
    [InlineData("<Owner>g__Local|0_0_0", false)]
    [InlineData("<Owner>g__|0_0", false)]
    [InlineData("<>b__0_0", false)]
    public void IsLocalFunctionOrLambda_RequiresCanonicalShape(
        string name,
        bool expected)
        => Assert.Equal(
            expected,
            CompilerGeneratedNames.IsLocalFunctionOrLambda(name));

    [Fact]
    public void ContainingTypeDisplayName_UsesExactSegmentsAndConservativeFlatFallback()
    {
        Assert.Equal(
            "Ns.GeneratedOuter",
            CompilerGeneratedNames.ContainingTypeDisplayName(
                TypeRef.Definition(
                    "Asm",
                    "Ns",
                    "GeneratedOuter+<>c__DisplayClass0_0")));
        Assert.Equal(
            "Ns.Outer.GeneratedOuter",
            CompilerGeneratedNames.ContainingTypeDisplayName(
                ResolvedDefinition(
                    "Outer`1+GeneratedOuter`1+<>c__DisplayClass0_0",
                    "Outer`1",
                    "GeneratedOuter`1",
                    "<>c__DisplayClass0_0")));
        Assert.Equal(
            @"Ns.A\+B",
            CompilerGeneratedNames.ContainingTypeDisplayName(
                ResolvedDefinition(
                    "A+B+<>c__DisplayClass0_0",
                    "A+B",
                    "<>c__DisplayClass0_0")));
    }

    [Theory]
    [InlineData("GeneratedOuter+Worker")]
    [InlineData("Outer+GeneratedOuter+<>c__DisplayClass0_0")]
    public void ContainingTypeDisplayName_RejectsOrdinaryOrAmbiguousFlatNames(
        string name)
        => Assert.Null(
            CompilerGeneratedNames.ContainingTypeDisplayName(
                TypeRef.Definition("Asm", "Ns", name)));

    static TypeRef ResolvedDefinition(
        string flattenedName,
        params string[] segments)
    {
        var result = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Ns", [.. segments]));
        return TypeRef.Definition(
            "Asm",
            "Ns",
            flattenedName,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                result.Name));
    }
}
