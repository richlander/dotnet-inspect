using System.Text.Json;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Vocabulary;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Tests;

public sealed class VocabularyCommandTests
{
    [Fact]
    public void Catalog_ProjectsOwnerValuesWithoutChangingIdentityOrOrder()
    {
        VocabularySection accessibility =
            VocabularyCatalog.GetById("api.accessibility");
        Assert.Equal(
            ApiAccessibility.Values.Select(value => value.Id),
            accessibility.Values.Select(ValueId));

        VocabularySection tiers =
            VocabularyCatalog.GetById("csharp.style-tiers");
        Assert.Equal(
            StyleOptionCatalog.Tiers.Select(value => value.Id.ToString()),
            tiers.Values.Select(ValueId));

        VocabularySection choices =
            VocabularyCatalog.GetById("csharp.style-choices");
        Assert.Equal(
            StyleOptionCatalog.Choices.Select(value => value.Id),
            choices.Values.Select(ValueId));
    }

    [Fact]
    public async Task Command_DefaultRendersTheSelfDescribingSectionIndex()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions())));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("# Vocabulary", result.Output);
        Assert.Contains("## Vocabulary Sections", result.Output);
        Assert.Contains("csharp.style-choices", result.Output);
        Assert.DoesNotContain("## C# Style Choices", result.Output);
    }

    [Fact]
    public async Task Command_DiscoveryListsSectionsAndCategories()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Discover = [],
            })));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("| Accessibility | section |", result.Output);
        Assert.Contains("| @Decompiler | category |", result.Output);
        Assert.Contains("| @Vocabulary | category |", result.Output);
    }

    [Fact]
    public async Task Command_JsonCarriesTypedSchemaAndValues()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = [VocabularyCatalog.AccessibilitySection],
                JsonOutput = true,
            })));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            document.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal("api.accessibility", section.GetProperty("id").GetString());
        JsonElement values = section.GetProperty("values");
        Assert.Equal(4, values.GetArrayLength());
        Assert.True(values[0].GetProperty("default").GetBoolean());
        Assert.Equal(JsonValueKind.Number, values[0].GetProperty("order").ValueKind);

        JsonElement defaultField = section.GetProperty("fields")
            .EnumerateArray()
            .Single(field => field.GetProperty("id").GetString() == "default");
        Assert.Equal("boolean", defaultField.GetProperty("type").GetString());
        Assert.Contains(
            defaultField.GetProperty("operators").EnumerateArray(),
            value => value.GetString() == "equals");
    }

    [Fact]
    public async Task Command_TsvProjectionAndCountUseSectionRows()
    {
        var projected = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = [VocabularyCatalog.StyleChoicesSection],
                Tabular = true,
                Tsv = true,
                Columns = ["ID", "Tier"],
                Rows = RowWindow.Range(1, 2),
            })));
        var counted = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = [VocabularyCatalog.AccessibilitySection],
                Count = true,
            })));

        Assert.Equal(0, projected.ExitCode);
        Assert.Empty(projected.Error);
        Assert.Equal(
            3,
            projected.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.StartsWith("id\ttier", projected.Output);

        Assert.Equal(0, counted.ExitCode);
        Assert.Empty(counted.Error);
        Assert.Equal("4", counted.Output.Trim());
    }

    [Fact]
    public async Task Command_FieldsProjectsTableColumns()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = [VocabularyCatalog.StyleChoicesSection],
                Tabular = true,
                Tsv = true,
                Fields = ["ID", "Tier"],
                Rows = RowWindow.Range(1, 1),
            })));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        string[] lines = result.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("id\ttier", lines[0]);
        Assert.Equal(2, lines[1].Split('\t').Length);
    }

    [Fact]
    public async Task Command_CategoryCountRendersPerSectionMap()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = ["@Decompiler"],
                Count = true,
            })));
        VocabularySection tiers = VocabularyCatalog.GetById("csharp.style-tiers");
        VocabularySection choices = VocabularyCatalog.GetById("csharp.style-choices");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            $"""
            | Section | Count |
            | ------- | ----- |
            | C# Style Tiers | {tiers.Values.Length} |
            | C# Style Choices | {choices.Values.Length} |
            """,
            result.Output.Trim());
    }

    private static string ValueId(VocabularyRow row) =>
        row.GetRequired("id").Text!;
}
