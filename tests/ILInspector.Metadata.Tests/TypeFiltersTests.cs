using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class TypeFiltersTests
{
    [Theory]
    [InlineData("<>c__DisplayClass6_2", true)]
    [InlineData("<GetRegisteredTypes>d__10", true)]
    [InlineData("__StaticArrayInitTypeSize=16", true)]
    [InlineData("Outer", false)]
    [InlineData("ApiOutputFormatter", false)]
    public void IsCompilerGenerated_matches_simple_names(string typeName, bool expected)
        => Assert.Equal(expected, TypeFilters.IsCompilerGenerated(typeName));

    [Theory]
    // Nested-qualified names (metadata form, Outer+Inner) must detect the
    // synthesized inner segment even though the leading segment is user code.
    [InlineData("Outer+<GetRegisteredTypes>d__10", true)]
    [InlineData("DotnetInspector.Commands.TypeCommand+<>c__DisplayClass21_0", true)]
    [InlineData("Outer+Middle+<Run>d__0", true)]
    // Non-generated nested types must not be flagged.
    [InlineData("Outer+Inner", false)]
    [InlineData("ApiOutputFormatter", false)]
    public void IsCompilerGeneratedNested_inspects_all_segments(string typeName, bool expected)
        => Assert.Equal(expected, TypeFilters.IsCompilerGeneratedNested(typeName));
}
