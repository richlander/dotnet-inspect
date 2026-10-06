namespace DotnetInspector.Queries.Tests;

public sealed class TypeFindPopulationSelectionTests
{
    [Fact]
    public void Select_PrefixWinsAndOrdersShortestCompletionFirst()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "JsonSer",
                    Candidates(
                        "options",
                        "System.Text.Json.JsonSerializerOptions",
                        "serializer",
                        "System.Text.Json.JsonSerializer",
                        "substring",
                        "Example.JsonSerializerFactory")));

        Assert.Equal(TypeFindPopulationTier.Prefix, selection.Tier);
        Assert.Equal("JsonSer", selection.EffectivePattern);
        Assert.Equal(
            ["serializer", "substring", "options"],
            selection.Matches.Select(
                static match => match.Candidate.Association));
        Assert.All(
            selection.Matches,
            static match => Assert.Equal(1.0, match.Similarity));
    }

    [Fact]
    public void Select_DottedPrefixReturnsEffectiveWildcard()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "System.Text.Json.JsonSer",
                    Candidates(
                        "serializer",
                        "System.Text.Json.JsonSerializer")));

        Assert.Equal(TypeFindPopulationTier.Prefix, selection.Tier);
        Assert.Equal(
            "System.Text.Json.JsonSer*",
            selection.EffectivePattern);
    }

    [Fact]
    public void Select_SubstringFollowsEmptyPrefix()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "Serializer",
                    Candidates(
                        "json",
                        "System.Text.Json.JsonSerializer",
                        "xml",
                        "System.Xml.Serialization.XmlSerializer")));

        Assert.Equal(TypeFindPopulationTier.Substring, selection.Tier);
        Assert.Equal(
            ["xml", "json"],
            selection.Matches.Select(
                static match => match.Candidate.Association));
    }

    [Fact]
    public void Select_ExplicitGenericNotationSkipsNameTiers()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "JsonSerializer<T>",
                    Candidates(
                        "serializer",
                        "System.Text.Json.JsonSerialiser`1")));

        Assert.Equal(TypeFindPopulationTier.Partial, selection.Tier);
        Assert.Equal(
            "serializer",
            Assert.Single(selection.Matches).Candidate.Association);
    }

    [Fact]
    public void Select_PartialOrdersBySimilarity()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "JsonSerialiser",
                    Candidates(
                        "node",
                        "System.Text.Json.Nodes.JsonNode",
                        "serializer",
                        "System.Text.Json.JsonSerializer")));

        Assert.Equal(TypeFindPopulationTier.Partial, selection.Tier);
        Assert.Equal(
            "serializer",
            selection.Matches[0].Candidate.Association);
        Assert.Equal(
            selection.Matches
                .Select(static match => match.Similarity)
                .OrderByDescending(static similarity => similarity),
            selection.Matches.Select(
                static match => match.Similarity));
    }

    [Fact]
    public void Select_DuplicateFullNameRetainsFirstAssociation()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "JsonSer",
                    Candidates(
                        "first",
                        "System.Text.Json.JsonSerializer",
                        "second",
                        "System.Text.Json.JsonSerializer")));

        TypeFindPopulationMatch<string> match =
            Assert.Single(selection.Matches);
        Assert.Equal("first", match.Candidate.Association);
    }

    [Fact]
    public void Select_LimitAppliesToSuccessfulNonSimilarityTier()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "Json",
                    Candidates(
                        "document",
                        "System.Text.Json.JsonDocument",
                        "element",
                        "System.Text.Json.JsonElement"),
                    limit: 1));

        Assert.Single(selection.Matches);
        Assert.Equal(
            "element",
            selection.Matches[0].Candidate.Association);
    }

    [Fact]
    public void Select_LimitDoesNotFurtherReducePartialSuggestions()
    {
        TypeFindPopulationSelection<string> selection = Assert.IsType<
            TypeFindPopulationSelection<string>>(
                TypeFindPopulationSelector.Select(
                    "JsonSerialiser",
                    Candidates(
                        "serializer",
                        "System.Text.Json.JsonSerializer",
                        "options",
                        "System.Text.Json.JsonSerializerOptions"),
                    limit: 1));

        Assert.Equal(TypeFindPopulationTier.Partial, selection.Tier);
        Assert.Equal(2, selection.Matches.Length);
    }

    [Fact]
    public void Select_EmptyPopulationReturnsNoSelection()
    {
        Assert.Null(
            TypeFindPopulationSelector.Select(
                "JsonSer",
                Array.Empty<TypeFindPopulationCandidate<string>>()));
    }

    [Theory]
    [InlineData("Json*")]
    [InlineData("Json?")]
    [InlineData("NoSuchTypeNameQzxv")]
    public void Select_UnsupportedOrUnmatchedPatternReturnsNoSelection(
        string pattern)
    {
        Assert.Null(
            TypeFindPopulationSelector.Select(
                pattern,
                Candidates(
                    "serializer",
                    "System.Text.Json.JsonSerializer")));
    }

    private static TypeFindPopulationCandidate<string>[] Candidates(
        params string[] values)
    {
        Assert.Equal(0, values.Length % 2);
        return
        [
            .. values
                .Chunk(2)
                .Select(pair =>
                    new TypeFindPopulationCandidate<string>(
                        pair[0],
                        pair[1])),
        ];
    }
}
