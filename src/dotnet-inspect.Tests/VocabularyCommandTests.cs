using System.Text.Json;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Vocabulary;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class VocabularyCommandTests
{
    [Fact]
    public void JsonSerialization_PreservesWireShapeAcrossIndentationModes()
    {
        string indented = VocabularyJson.Serialize(VocabularyCatalog.Document);
        string compact = VocabularyJson.Serialize(
            VocabularyCatalog.Document,
            indented: false);
        using JsonDocument indentedDocument = JsonDocument.Parse(indented);
        using JsonDocument compactDocument = JsonDocument.Parse(compact);

        Assert.True(JsonElement.DeepEquals(
            indentedDocument.RootElement,
            compactDocument.RootElement));
        Assert.True(compactDocument.RootElement.TryGetProperty("sections", out _));
        Assert.False(compactDocument.RootElement.TryGetProperty("Sections", out _));
    }

    [Fact]
    public void Catalog_ProjectsOwnerValuesWithoutChangingIdentityOrOrder()
    {
        VocabularySection index =
            VocabularyCatalog.GetById("vocabulary.sections");
        Assert.Equal(
            VocabularyCatalog.Document.Sections.Select(section => section.Id),
            index.Values.Select(ValueId));
        Assert.Equal(
            VocabularyCatalog.Document.Sections.Length,
            index.Values[0].GetRequired("values").Integer);

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

        VocabularySection bodyKinds =
            VocabularyCatalog.GetById("csharp.body-kinds");
        Assert.Equal(
            BodyShapeSearch.SupportedKinds,
            bodyKinds.Values.Select(ValueId));
        Assert.Equal(
            BodyShapeSearch.SupportedKinds.Select(
                AnnotatedSourceNodeKinds.GetDisplayLabel),
            bodyKinds.Values.Select(row => row.GetRequired("label").Text));
        var bodyKindId = Assert.Single(
            bodyKinds.Fields,
            field => field.Id == "id");
        Assert.Equal([VocabularyOperator.Equals], bodyKindId.Operators);
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
        VocabularySection bodyKinds = VocabularyCatalog.GetById("csharp.body-kinds");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            $"""
            | Section | Count |
            | ------- | ----- |
            | C# Style Tiers | {tiers.Values.Length} |
            | C# Style Choices | {choices.Values.Length} |
            | C# Body Kinds | {bodyKinds.Values.Length} |
            """,
            result.Output.Trim());
    }

    [Fact]
    public async Task Command_StableSectionIdsRoundTripThroughSelectionAndDiscovery()
    {
        var selected = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = ["api.accessibility"],
                JsonOutput = true,
            })));
        var discovered = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Discover = ["csharp.style-choices"],
            })));

        Assert.Equal(0, selected.ExitCode);
        Assert.Empty(selected.Error);
        using JsonDocument document = JsonDocument.Parse(selected.Output);
        Assert.Equal(
            "api.accessibility",
            Assert.Single(document.RootElement.GetProperty("sections").EnumerateArray())
                .GetProperty("id")
                .GetString());

        Assert.Equal(0, discovered.ExitCode);
        Assert.Empty(discovered.Error);
        Assert.Contains("| ID | column |", discovered.Output);
        Assert.Contains("| Conflict Group | column |", discovered.Output);
    }

    [Fact]
    public async Task Command_AllCountIncludesEveryVocabularySection()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = [SelectResolver.AllSelector],
                Count = true,
            })));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        foreach (VocabularySection section in VocabularyCatalog.Document.Sections)
            Assert.Contains($"| {section.Name} | {section.Values.Length} |", result.Output);
    }

    [Fact]
    public async Task Command_PartialMachineKeyProjectionKeepsSectionIdentityAcrossFormats()
    {
        var json = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select =
                [
                    VocabularyCatalog.AccessibilitySection,
                    VocabularyCatalog.StyleTiersSection,
                ],
                JsonOutput = true,
                Columns = ["byte_divergent"],
            })));
        var markdown = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select =
                [
                    VocabularyCatalog.AccessibilitySection,
                    VocabularyCatalog.StyleTiersSection,
                ],
                Columns = ["byte_divergent"],
            })));

        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Error);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        Assert.False(document.RootElement.TryGetProperty("accessibility", out _));
        Assert.True(document.RootElement.TryGetProperty("c#style_tiers", out JsonElement rows));
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(4, rows.GetArrayLength());

        Assert.Equal(0, markdown.ExitCode);
        Assert.Empty(markdown.Error);
        Assert.DoesNotContain("## Accessibility", markdown.Output);
        Assert.Contains("## C# Style Tiers", markdown.Output);
        Assert.Contains("| Byte Divergent |", markdown.Output);
    }

    [Fact]
    public async Task Command_PlainTextUsesThePlainTextFormatter()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = [VocabularyCatalog.AccessibilitySection],
                PlainText = true,
            })));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Vocabulary", result.Output);
        Assert.Contains("Accessibility", result.Output);
        Assert.DoesNotContain("# Vocabulary", result.Output);
        Assert.DoesNotContain("| ID |", result.Output);
    }

    [Fact]
    public async Task CommandLine_PlainTextEnvironmentOverrideUsesThePlainTextFormatter()
    {
        string? original = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        var result = await ConsoleCapture.RunAsync(async () =>
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "plaintext");
            try
            {
                return await CommandLineBuilder.CreateRootCommand()
                    .Parse(["vocabulary", "-S", "Accessibility"])
                    .InvokeAsync();
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", original);
            }
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Accessibility", result.Output);
        Assert.DoesNotContain("# Vocabulary", result.Output);
        Assert.DoesNotContain("| ID |", result.Output);
    }

    [Fact]
    public async Task Command_PlainTextMultiSectionCountUsesPlainTextTable()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select = ["@Decompiler"],
                Count = true,
                PlainText = true,
            })));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Section", result.Output);
        Assert.Contains("C# Style Tiers", result.Output);
        Assert.Contains("C# Style Choices", result.Output);
        Assert.DoesNotContain("| Section |", result.Output);
    }

    [Fact]
    public async Task Command_CountUsesRowCardinalityBeforeColumnProjection()
    {
        var result = await ConsoleCapture.RunAsync(() => Task.FromResult(
            VocabularyCommand.Execute(new VocabularyOptions
            {
                Select =
                [
                    VocabularyCatalog.AccessibilitySection,
                    VocabularyCatalog.StyleTiersSection,
                ],
                Count = true,
                Columns = ["byte_divergent"],
            })));
        VocabularySection accessibility =
            VocabularyCatalog.GetById("api.accessibility");
        VocabularySection tiers =
            VocabularyCatalog.GetById("csharp.style-tiers");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"| Accessibility | {accessibility.Values.Length} |",
            result.Output);
        Assert.Contains(
            $"| C# Style Tiers | {tiers.Values.Length} |",
            result.Output);
    }

    private static string ValueId(VocabularyRow row) =>
        row.GetRequired("id").Text!;
}
