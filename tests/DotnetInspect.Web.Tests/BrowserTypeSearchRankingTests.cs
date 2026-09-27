using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Spotlight's loaded-Type ranking binds the shared Type-name tiers rather
/// than owning its own predicates. Names come from System.Text.Json and the
/// .NET Platform.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserTypeSearchRankingTests
{
    private static readonly (string Key, string Full)[] Candidates =
    [
        ("options", "System.Text.Json.JsonSerializerOptions"),
        ("context", "System.Text.Json.Serialization.JsonSerializerContext"),
        ("serializer", "System.Text.Json.JsonSerializer"),
        ("node", "System.Text.Json.Nodes.JsonNode"),
        ("parser", "System.Net.ServerSentEvents.SseParser`1"),
    ];

    [Theory]
    [InlineData("JsonSer", "serializer:prefix,context:prefix,options:prefix")]
    [InlineData("Parse", "parser:substring")]
    [InlineData("Nodes", "node:path")]
    [InlineData(
        "System.Text.Json.JsonSer",
        "serializer:prefix,options:prefix")]
    [InlineData(
        "JsonSerializer*",
        "serializer:exact,context:exact,options:exact")]
    public void SearchTypes_UsesSharedTiersAndWithinTierOrder(
        string query,
        string expected)
    {
        string candidatesJson = JsonSerializer.Serialize(
            Candidates.Select(static candidate => new
            {
                key = candidate.Key,
                name = candidate.Full[(candidate.Full.LastIndexOf('.') + 1)..],
                full = candidate.Full,
            }));

        using JsonDocument hits = JsonDocument.Parse(
            PackageExports.SearchTypes(query, candidatesJson));

        string actual = string.Join(
            ',',
            hits.RootElement.EnumerateArray()
                .Where(static hit => hit.GetProperty("kind").GetString() != "fuzzy")
                .Select(static hit =>
                    $"{hit.GetProperty("key").GetString()}:"
                    + hit.GetProperty("kind").GetString()));
        Assert.Equal(expected, actual);
    }
}
