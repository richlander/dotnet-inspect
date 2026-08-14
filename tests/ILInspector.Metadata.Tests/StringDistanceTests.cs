using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public class StringDistanceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("package", "packag", 1)]
    public void EditDistance_ReturnsLevenshteinDistance(
        string source,
        string target,
        int expected)
        => Assert.Equal(expected, StringDistance.EditDistance(source, target));

    [Theory]
    [InlineData("", "", 1.0)]
    [InlineData("same", "same", 1.0)]
    [InlineData("abcd", "abc", 0.75)]
    [InlineData("", "abc", 0.0)]
    public void Similarity_NormalizesByLongerInput(
        string source,
        string target,
        double expected)
        => Assert.Equal(expected, StringDistance.Similarity(source, target), precision: 10);
}
