namespace DotnetInspector.Queries.Tests;

public sealed class LoadedTypeSearchRankingTests
{
    private static readonly LoadedTypeSearchCandidate[] Candidates =
    [
        new("options", "JsonSerializerOptions",
            "System.Text.Json.JsonSerializerOptions"),
        new("context", "JsonSerializerContext",
            "System.Text.Json.Serialization.JsonSerializerContext"),
        new("serializer", "JsonSerializer",
            "System.Text.Json.JsonSerializer"),
        new("node", "JsonNode",
            "System.Text.Json.Nodes.JsonNode"),
        new("parser", "SseParser`1",
            "System.Net.ServerSentEvents.SseParser`1"),
    ];

    [Theory]
    [InlineData(
        "JsonSer",
        "serializer:Prefix,context:Prefix,options:Prefix")]
    [InlineData("Parse", "parser:Substring")]
    [InlineData("Nodes", "node:Path")]
    [InlineData(
        "System.Text.Json.JsonSer",
        "serializer:Prefix,options:Prefix")]
    [InlineData(
        "JsonSerializer*",
        "serializer:Exact,context:Exact,options:Exact")]
    [InlineData("Json*Opt", "options:Prefix")]
    [InlineData(
        "*Serializer",
        "serializer:Exact,context:Prefix,options:Prefix")]
    public void Rank_UsesSharedTiersAndWithinTierOrder(
        string query,
        string expected)
    {
        string actual = string.Join(
            ',',
            LoadedTypeSearchRanking.Rank(
                    query,
                    Candidates,
                    static (key, kind) => new Hit(key, kind))
                .Where(hit => hit.Kind != LoadedTypeSearchMatchKind.Fuzzy)
                .Select(hit => $"{hit.Key}:{hit.Kind}"));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rank_EmptyQueryUsesShortNameOrderAndLimit()
    {
        LoadedTypeSearchCandidate[] candidates =
        [
            .. Enumerable.Range(0, 35)
                .Select(index =>
                    new LoadedTypeSearchCandidate(
                        index.ToString(),
                        new string('A', 35 - index),
                        $"Example.Type{index}")),
        ];

        Hit[] hits = LoadedTypeSearchRanking.Rank(
            " ",
            candidates,
            static (key, kind) => new Hit(key, kind));

        Assert.Equal(30, hits.Length);
        Assert.All(
            hits,
            hit => Assert.Equal(
                LoadedTypeSearchMatchKind.All,
                hit.Kind));
        Assert.Equal("34", hits[0].Key);
    }

    [Fact]
    public void Rank_DuplicateKeysPublishOnce()
    {
        Hit[] hits = LoadedTypeSearchRanking.Rank(
            "Json",
            [
                new("shared", "JsonDocument", "System.Text.Json.JsonDocument"),
                new("shared", "JsonNode", "System.Text.Json.Nodes.JsonNode"),
            ],
            static (key, kind) => new Hit(key, kind));

        Assert.Single(hits);
        Assert.Equal("shared", hits[0].Key);
    }

    private sealed record Hit(
        string Key,
        LoadedTypeSearchMatchKind Kind);
}
