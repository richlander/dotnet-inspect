using System.Runtime.Versioning;
using System.Text.Json;
using ILInspector.Decompiler;
using Pipeline = ILInspector.Decompiler.Pipeline;

using InspectWeb.Engine.CatalogFacade;
using InspectWeb.Engine.SourceFacade;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserStyleOptionsTests
{
    [Fact]
    public void ListVocabulary_ProjectsProductOwnedStyleChoices()
    {
        using JsonDocument document = JsonDocument.Parse(
            CatalogExports.ListVocabulary());
        Assert.Equal(1, document.RootElement.GetProperty("schema_version").GetInt32());
        JsonElement actual = document.RootElement
            .GetProperty("sections")
            .EnumerateArray()
            .Single(section =>
                section.GetProperty("id").GetString()
                    == "csharp.style-choices")
            .GetProperty("values");

        Assert.Equal(Pipeline.StyleOptionCatalog.Choices.Count, actual.GetArrayLength());
        for (int i = 0; i < actual.GetArrayLength(); i++)
        {
            Pipeline.StyleOptionChoice expected = Pipeline.StyleOptionCatalog.Choices[i];
            Assert.Equal(expected.Id, actual[i].GetProperty("id").GetString());
            Assert.Equal(expected.Title, actual[i].GetProperty("title").GetString());
            Assert.Equal(expected.Summary, actual[i].GetProperty("summary").GetString());
            Assert.Equal(expected.Tier.ToString(), actual[i].GetProperty("tier").GetString());
            Assert.Equal(expected.ByteDivergent, actual[i].GetProperty("byte_divergent").GetBoolean());
            Assert.Equal(expected.OracleEndorsed, actual[i].GetProperty("oracle_endorsed").GetBoolean());
            Assert.Equal(
                expected.ConflictGroup,
                actual[i].TryGetProperty("conflict_group", out JsonElement conflict)
                    ? conflict.GetString()
                    : null);
        }

        Assert.True(
            document.RootElement
                .GetProperty("sections")
                .EnumerateArray()
                .All(section => section.TryGetProperty("accepted_by", out _)));
    }

    [Fact]
    public void ListVocabulary_ProjectsProductOwnedBodyKinds()
    {
        using JsonDocument document = JsonDocument.Parse(
            CatalogExports.ListVocabulary());
        JsonElement actual = document.RootElement
            .GetProperty("sections")
            .EnumerateArray()
            .Single(section =>
                section.GetProperty("id").GetString()
                    == "csharp.body-kinds")
            .GetProperty("values");

        Assert.Equal(BodyShapeSearch.SupportedKinds.Count, actual.GetArrayLength());
        for (int i = 0; i < actual.GetArrayLength(); i++)
        {
            string expected = BodyShapeSearch.SupportedKinds[i];
            Assert.Equal(expected, actual[i].GetProperty("id").GetString());
            Assert.Equal(
                AnnotatedSourceNodeKinds.GetDisplayLabel(expected),
                actual[i].GetProperty("label").GetString());
        }
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
}
