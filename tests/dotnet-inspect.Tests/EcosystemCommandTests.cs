using System.Text.Json;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class EcosystemCommandTests
{
    [Fact]
    public void Command_IsReservedFromImplicitPackageRouting()
    {
        string[] arguments =
            CommandLineBuilder.PreprocessArgs(["ecosystem", "--help"]);

        Assert.Equal("ecosystem", arguments[0]);
        Assert.Contains("ecosystem", CommandLineBuilder.KnownCommands);
    }

    [Fact]
    public void Command_RegistersSharedCatalogOptions()
    {
        string[] arguments =
        [
            "ecosystem",
            "aspire",
            "-S",
            "Integrations",
            "--json",
            "--rows",
            "1",
        ];

        var result = CommandLineBuilder.CreateRootCommand().Parse(arguments);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CommandLine_ExecutesTheCatalogRoute()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] arguments = ["ecosystem", "aspire", "-S", "Integrations"];
        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(root.Parse(arguments), arguments));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("| Aspire | integration.aspire |", result.Output);
    }

    [Fact]
    public async Task CommandLine_OutOfRangeRowWindowDoesNotClaimConfiguredSectionIsEmpty()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] arguments =
        [
            "ecosystem",
            "aspire",
            "-S",
            "Integrations",
            "--rows",
            "2..2",
        ];
        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(root.Parse(arguments), arguments));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Known Integrations", result.Output);
        Assert.DoesNotContain("integration.aspire", result.Output);
        Assert.DoesNotContain(
            "No Integration concepts are explicitly bound",
            result.Output);
    }

    [Fact]
    public async Task Default_ListsTheShippedEcosystemCatalog()
    {
        var result = await ExecuteAsync(new EcosystemOptions());

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("# Ecosystem Catalog", result.Output);
        Assert.Contains("## Ecosystems", result.Output);
        Assert.Contains("ecosystem.platform", result.Output);
        Assert.Contains("ecosystem.microsoft-extensions", result.Output);
        Assert.Contains("ecosystem.aspnetcore", result.Output);
        Assert.Contains("ecosystem.aspire", result.Output);
        Assert.Contains(
            "| ecosystem.aspire | Aspire | Aspire package and demo content. | configured | 1 | 2 |",
            result.Output);
    }

    [Fact]
    public async Task ShortSelector_DefaultsToPackInformation()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("# Aspire", result.Output);
        Assert.Contains("## Ecosystem Info", result.Output);
        Assert.Contains("| ID | ecosystem.aspire |", result.Output);
        Assert.Contains("| Known Integration Bindings | 1 |", result.Output);
        Assert.DoesNotContain("## Known Integrations", result.Output);
    }

    [Fact]
    public async Task IntegrationsAlias_ReportsConfiguredKnowledgeNotLibraryObservation()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "ecosystem.aspire",
            Select = ["Integrations"],
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("# Aspire", result.Output);
        Assert.Contains("## Known Integrations", result.Output);
        Assert.Contains("| Aspire | integration.aspire |", result.Output);
        Assert.Contains(
            "these are not observations from a library",
            result.Output);
    }

    [Fact]
    public async Task EcosystemWithoutBinding_ReportsKnowledgeGapExplicitly()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "microsoft-extensions",
            Select = ["Integrations"],
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "No Integration concepts are explicitly bound to this ecosystem",
            result.Output);
        Assert.Contains(
            "This does not mean the external ecosystem has no integrations.",
            result.Output);
        Assert.DoesNotContain("Dependency Injection", result.Output);
    }

    [Fact]
    public async Task CatalogWideIntegrations_AttributesEveryBindingToItsEcosystem()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Select = ["Integrations"],
            Format = OutputFormat.Tsv,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        string[] lines = result.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "ecosystem\tintegration\tid\tevidence_relationships\tbinding\tknowledge_scope",
            lines[0]);
        Assert.StartsWith(
            "Aspire\tAspire\tintegration.aspire\tintegration.observed, integration.opportunity\tconfigured\t",
            lines[1]);
        Assert.EndsWith(
            "Configured product knowledge; not a library observation.",
            lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Theory]
    [InlineData(OutputFormat.Table)]
    [InlineData(OutputFormat.Tsv)]
    [InlineData(OutputFormat.Jsonl)]
    [InlineData(OutputFormat.Json)]
    public async Task UnboundIntegrations_PreserveKnowledgeScopeInMachineFormats(
        OutputFormat format)
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "microsoft-extensions",
            Select = ["Integrations"],
            Format = format,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("not configured", result.Output);
        Assert.Contains(
            "This does not mean the external ecosystem has no integrations.",
            result.Output);
    }

    [Fact]
    public async Task UnboundIntegrations_CountLogicalConceptsNotEmptyState()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "microsoft-extensions",
            Select = ["Integrations"],
            Count = true,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal("0", result.Output.Trim());
    }

    [Fact]
    public async Task UnboundIntegrations_RowWindowDoesNotRemoveEmptyState()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "microsoft-extensions",
            Select = ["Integrations"],
            Rows = RowWindow.Range(2, 2),
            Format = OutputFormat.Jsonl,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("\"binding\":\"not configured\"", result.Output);
        Assert.Contains(
            "This does not mean the external ecosystem has no integrations.",
            result.Output);
    }

    [Fact]
    public async Task FieldsProjectTableColumnsForEcosystemSections()
    {
        var jsonl = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            Select = ["Integrations"],
            Fields = ["Binding"],
            Format = OutputFormat.Jsonl,
        });
        var json = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            Select = ["Integrations"],
            Fields = ["Binding"],
            Format = OutputFormat.Json,
        });

        Assert.Equal(0, jsonl.ExitCode);
        Assert.Empty(jsonl.Error);
        Assert.Equal(
            """{"binding":"configured"}""",
            jsonl.Output.Trim());

        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Error);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        JsonElement row = Assert.Single(
            document.RootElement
                .GetProperty("known_integrations")
                .EnumerateArray());
        JsonProperty property = Assert.Single(row.EnumerateObject());
        Assert.Equal("binding", property.Name);
        Assert.Equal("configured", property.Value.GetString());
    }

    [Fact]
    public async Task RowWindowIsAppliedBeforeTableRendering()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspnetcore",
            Select = ["Core Packages"],
            Rows = RowWindow.Range(2, 2),
            Format = OutputFormat.Tsv,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            "package\nMicrosoft.AspNetCore.Authentication.JwtBearer",
            result.Output.Trim());
    }

    [Fact]
    public async Task MultiSectionCount_AssignsZeroToProjectedAwaySections()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            SelectDefault = true,
            Columns = ["Package"],
            Count = true,
            Format = OutputFormat.Json,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        var counts = document.RootElement
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("section").GetString()!,
                row => row.GetProperty("count").GetInt32());
        Assert.Equal(0, counts["Ecosystem Info"]);
        Assert.Equal(0, counts["Namespace Hints"]);
        Assert.Equal(1, counts["Core Packages"]);
        Assert.Equal(1, counts["Tool Packages"]);
        Assert.Equal(0, counts["Known Integrations"]);
        Assert.Equal(0, counts["Demos"]);
    }

    [Fact]
    public async Task BareSelect_RendersAllPackKnowledgeAndExplicitEmptySections()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspnetcore",
            SelectDefault = true,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Ecosystem Info", result.Output);
        Assert.Contains("## Namespace Hints", result.Output);
        Assert.Contains("## Core Packages", result.Output);
        Assert.Contains("## Tool Packages", result.Output);
        Assert.Contains("## Known Integrations", result.Output);
        Assert.Contains("## Demos", result.Output);
        Assert.Contains(
            "No Integration concepts are explicitly bound",
            result.Output);
    }

    [Fact]
    public async Task DiscoveryDescribesFocusedSectionsAndKnownIntegrationSchema()
    {
        var sections = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            Discover = [],
        });
        var schema = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            Discover = ["Integrations"],
        });

        Assert.Equal(0, sections.ExitCode);
        Assert.Contains("| Known Integrations | section |", sections.Output);
        Assert.Equal(0, schema.ExitCode);
        Assert.Contains("| Integration | column |", schema.Output);
        Assert.Contains("| Evidence Relationships | column |", schema.Output);
    }

    [Fact]
    public async Task JsonPreservesSectionAndStableColumnNames()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            Select = ["Core Packages"],
            Format = OutputFormat.Json,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement section =
            document.RootElement.GetProperty("core_packages");
        JsonElement row = Assert.Single(section.EnumerateArray());
        Assert.Equal("Aspire.Hosting", row.GetProperty("package").GetString());
    }

    [Fact]
    public async Task UnknownSelector_ListsShortAndCanonicalChoices()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "unknown",
        });

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Unknown ecosystem 'unknown'.", result.Error);
        Assert.Contains("aspire (ecosystem.aspire)", result.Error);
    }

    private static Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        EcosystemOptions options) =>
        ConsoleCapture.RunAsync(
            () => Task.FromResult(EcosystemCommand.Execute(options)));
}
