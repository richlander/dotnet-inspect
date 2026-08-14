using System.Runtime.Versioning;
using System.Text.Json;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserStyleOptionsTests
{
    [Fact]
    public void ListStyleOptions_ProjectsProductOwnedChoices()
    {
        BrowserStyleOption[] actual = JsonSerializer.Deserialize(
            BrowserInspectionEngine.ListStyleOptions(),
            BrowserJsonContext.Default.BrowserStyleOptionArray) ?? [];

        Assert.Equal(Pipeline.StyleOptionCatalog.Choices.Count, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            Pipeline.StyleOptionChoice expected = Pipeline.StyleOptionCatalog.Choices[i];
            Assert.Equal(expected.Id, actual[i].Id);
            Assert.Equal(expected.Title, actual[i].Title);
            Assert.Equal(expected.Summary, actual[i].Summary);
            Assert.Equal(expected.Tier.ToString(), actual[i].Tier);
            Assert.Equal(expected.ByteDivergent, actual[i].ByteDivergent);
            Assert.Equal(expected.OracleEndorsed, actual[i].OracleEndorsed);
            Assert.Equal(expected.ConflictGroup, actual[i].ConflictGroup);
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
            BrowserJsonContext.Default.StringArray);

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
            BrowserJsonContext.Default.StringArray);
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => BrowserStyleOptions.Resolve(conflict));
        Assert.Contains("conflict", failure.Message, StringComparison.Ordinal);
    }
}
