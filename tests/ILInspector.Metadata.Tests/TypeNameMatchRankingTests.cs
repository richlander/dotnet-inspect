using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Broadened Type-name tiers, grounded in .NET Platform names that previously
/// fell through Find to similarity suggestions.
/// </summary>
public class TypeNameMatchRankingTests
{
    [Theory]
    [InlineData("System.Text.Json.JsonSerializer", "JsonSer", TypeNameMatchTier.Prefix)]
    [InlineData("System.Text.StringBuilder", "stringbuild", TypeNameMatchTier.Prefix)]
    [InlineData("System.Collections.Generic.List`1", "Lis", TypeNameMatchTier.Prefix)]
    [InlineData("System.Net.ServerSentEvents.SseParser`1", "Parse", TypeNameMatchTier.Substring)]
    [InlineData("System.Text.Json.Serialization.JsonSerializerContext", "Serializer", TypeNameMatchTier.Substring)]
    [InlineData("System.Text.Json.Nodes.JsonNode", "Nodes", TypeNameMatchTier.Path)]
    [InlineData("System.Text.Json.JsonSerializer", "Text.Json.JsonSer", TypeNameMatchTier.Prefix)]
    [InlineData("System.Text.Json.JsonSerializer", "System.Text.Json.JsonSer", TypeNameMatchTier.Prefix)]
    [InlineData("System.Text.Json.JsonSerializer", "ext.Json", TypeNameMatchTier.Substring)]
    [InlineData("System.Collections.Generic.Dictionary`2.KeyCollection", "KeyColl", TypeNameMatchTier.Prefix)]
    [InlineData("System.Text.Json.JsonSerializerOptions", "Json*Opt", TypeNameMatchTier.Prefix)]
    [InlineData("System.Text.Json.JsonSerializerOptions", "*Serializer", TypeNameMatchTier.Prefix)]
    [InlineData("System.Text.Json.JsonSerializerOptions", "Ser?al", TypeNameMatchTier.Substring)]
    [InlineData("System.Text.Json.Nodes.JsonNode", "Nod*s", TypeNameMatchTier.Path)]
    public void Classify_ReturnsStrongestTier(
        string fullName,
        string pattern,
        TypeNameMatchTier expected)
        => Assert.Equal(expected, TypeNameMatchRanking.Classify(fullName, pattern));

    [Theory]
    [InlineData("System.Text.Json.JsonSerializer", "Xml")]
    [InlineData("System.Text.Json.JsonSerializer", "Xml*")]
    [InlineData("System.Collections.Generic.List`1", "List<T>")]
    [InlineData("System.Collections.Generic.List`1", "List`1")]
    [InlineData("System.Text.Json.JsonSerializer", " ")]
    public void Classify_ReturnsNullForUnmatchedOrUnbroadenablePatterns(
        string fullName,
        string pattern)
        => Assert.Null(TypeNameMatchRanking.Classify(fullName, pattern));

    [Fact]
    public void CompareWithinTier_PrefersShortestCompletionThenName()
    {
        string[] names =
        [
            "System.Text.Json.JsonSerializerOptions",
            "System.Text.Json.Serialization.JsonSerializerContext",
            "System.Text.Json.JsonSerializer",
            "System.Text.Json.JsonSerializerDefaults",
        ];

        string[] ordered =
        [
            .. names.Order(
                Comparer<string>.Create(
                    TypeNameMatchRanking.CompareWithinTier)),
        ];

        Assert.Equal(
            [
                "System.Text.Json.JsonSerializer",
                "System.Text.Json.Serialization.JsonSerializerContext",
                "System.Text.Json.JsonSerializerOptions",
                "System.Text.Json.JsonSerializerDefaults",
            ],
            ordered);
    }
}
