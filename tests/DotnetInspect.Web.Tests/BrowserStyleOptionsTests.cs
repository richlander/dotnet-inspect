using System.Runtime.Versioning;
using System.Text.Json;
using ILInspector.Decompiler;
using Pipeline = ILInspector.Decompiler.Pipeline;

using DotnetInspect.Web.Interop.Catalog;
using DotnetInspect.Web.Interop.Source;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserStyleOptionsTests
{
    [Fact]
    public void InspectVocabulary_ExportsProductOwnedStyleChoicesAndTierMap()
    {
        using JsonDocument document = JsonDocument.Parse(
            DotnetInspect.Web.Interop.Catalog.CatalogExports.InspectVocabulary());
        JsonElement content = document.RootElement.GetProperty("content");
        Assert.Equal(1, content.GetProperty("formatVersion").GetInt32());
        Assert.StartsWith(
            "sha256:",
            content.GetProperty("identity").GetProperty("value").GetString(),
            StringComparison.Ordinal);
        JsonElement choices = content
            .GetProperty("vocabularies")
            .EnumerateArray()
            .Single(vocabulary =>
                vocabulary.GetProperty("identity").GetProperty("value").GetString()
                    == "csharp.style-choices")
            ;
        JsonElement tierMap = choices
            .GetProperty("maps")
            .EnumerateArray()
            .Single(map =>
                map.GetProperty("identity").GetProperty("value").GetString()
                    == "tier");
        Assert.Equal("ExactlyOne", tierMap.GetProperty("cardinality").GetString());
        Assert.Equal("Complete", tierMap.GetProperty("coverage").GetString());
        Assert.Equal("terms", tierMap.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(
            "csharp.style-tiers",
            tierMap
                .GetProperty("target")
                .GetProperty("reference")
                .GetProperty("vocabulary")
                .GetProperty("value")
                .GetString());

        JsonElement actual = choices.GetProperty("terms");
        Assert.Equal(Pipeline.StyleOptionCatalog.Choices.Count, actual.GetArrayLength());
        for (int i = 0; i < actual.GetArrayLength(); i++)
        {
            Pipeline.StyleOptionChoice expected = Pipeline.StyleOptionCatalog.Choices[i];
            Assert.Equal(
                expected.Id,
                actual[i].GetProperty("identity").GetProperty("value").GetString());
            Assert.Equal(expected.Title, actual[i].GetProperty("displayLabel").GetString());
            Assert.Equal(expected.Summary, actual[i].GetProperty("summary").GetString());
            JsonElement tier = MapValue(actual[i], "tier");
            Assert.Equal("term", tier.GetProperty("kind").GetString());
            Assert.Equal(
                expected.Tier.ToString(),
                tier.GetProperty("identity").GetProperty("value").GetString());
            Assert.Equal(
                expected.OracleEndorsed,
                MapValue(actual[i], "oracle_endorsed").GetProperty("value").GetBoolean());
            Assert.Equal(
                expected.ConflictGroup,
                TryMapValue(actual[i], "conflict_group", out JsonElement conflict)
                    ? conflict.GetProperty("value").GetString()
                    : null);
        }

        Assert.Equal(
            "nonProjectable",
            document.RootElement.GetProperty("share").GetProperty("kind").GetString());
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void InspectVocabulary_ExportsProductOwnedBodyKinds()
    {
        using JsonDocument document = JsonDocument.Parse(
            DotnetInspect.Web.Interop.Catalog.CatalogExports.InspectVocabulary());
        JsonElement actual = document.RootElement
            .GetProperty("content")
            .GetProperty("vocabularies")
            .EnumerateArray()
            .Single(vocabulary =>
                vocabulary.GetProperty("identity").GetProperty("value").GetString()
                    == "csharp.body-kinds")
            .GetProperty("terms");

        Assert.Equal(BodyShapeSearch.SupportedKinds.Count, actual.GetArrayLength());
        for (int i = 0; i < actual.GetArrayLength(); i++)
        {
            string expected = BodyShapeSearch.SupportedKinds[i];
            Assert.Equal(
                expected,
                actual[i].GetProperty("identity").GetProperty("value").GetString());
            Assert.Equal(
                AnnotatedSourceNodeKinds.GetDisplayLabel(expected),
                actual[i].GetProperty("displayLabel").GetString());
        }
    }

    [Fact]
    public void InspectVocabulary_ReusesOneStableManagedSnapshot()
    {
        string first = CatalogExports.InspectVocabulary();
        string second = CatalogExports.InspectVocabulary();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_UsesProductOwnedSelectionAndConflictSemantics()
    {
        string[] selected =
        [
            "qualify-field-access",
            "guarded-boolean-return-style:branchless",
        ];
        string json = JsonSerializer.Serialize(
            selected,
            BrowserCatalogJsonContext.Default.StringArray);

        Assert.Equal(
            Pipeline.StyleOptionCatalog.ResolveChoices(selected),
            BrowserStyleOptions.Resolve(json));
        Assert.Equal(
            Pipeline.StyleOptionCatalog.DefaultOptions,
            BrowserStyleOptions.Resolve(null));
        Assert.True(BrowserStyleOptions.Resolve(null).PreferLongLiteralSuffix);
        Assert.False(
            BrowserStyleOptions.Resolve(
                JsonSerializer.Serialize(
                    new[] { "explicit-long-literal-cast" },
                    BrowserCatalogJsonContext.Default.StringArray))
                .PreferLongLiteralSuffix);
        Assert.True(
            BrowserStyleOptions.Resolve(
                JsonSerializer.Serialize(
                    new[] { "prefer-long-literal-suffix" },
                    BrowserCatalogJsonContext.Default.StringArray))
                .PreferLongLiteralSuffix);

        string conflict = JsonSerializer.Serialize(
            new[]
            {
                "guarded-boolean-return-style:conditional-expression",
                "guarded-boolean-return-style:branchless",
            },
            BrowserCatalogJsonContext.Default.StringArray);
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => BrowserStyleOptions.Resolve(conflict));
        Assert.Contains("conflict", failure.Message, StringComparison.Ordinal);
    }

    private static JsonElement MapValue(JsonElement term, string map) =>
        TryMapValue(term, map, out JsonElement value)
            ? value
            : throw new InvalidOperationException(
                $"Vocabulary term has no value for map '{map}'.");

    private static bool TryMapValue(
        JsonElement term,
        string map,
        out JsonElement value)
    {
        foreach (JsonElement entry in term.GetProperty("mapEntries").EnumerateArray())
        {
            if (entry.GetProperty("map").GetProperty("value").GetString() != map)
                continue;

            JsonElement.ArrayEnumerator values =
                entry.GetProperty("values").EnumerateArray();
            if (!values.MoveNext())
            {
                value = default;
                return false;
            }
            value = values.Current;
            Assert.False(values.MoveNext());
            return true;
        }

        value = default;
        return false;
    }
}
