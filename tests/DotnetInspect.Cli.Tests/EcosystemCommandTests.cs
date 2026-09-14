using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using NuGet.Versioning;

namespace DotnetInspect.Cli.Tests;

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
            "1..1",
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
        string[] arguments =
        [
            "ecosystem",
            "aspire",
            "-S",
            "Integrations",
            "--rows",
            "2..2",
        ];
        var result = await ExecuteCommandLineAsync(arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Known Integrations", result.Output);
        Assert.DoesNotContain("integration.aspire", result.Output);
        Assert.DoesNotContain(
            "No Integration concepts are explicitly bound",
            result.Output);
    }

    [Fact]
    public async Task CommandLine_SemanticLimitsApplyToRowsAndCounts()
    {
        var head = await ExecuteCommandLineAsync(
            "ecosystem",
            "microsoft-extensions",
            "-S",
            "Core Packages",
            "-n",
            "1",
            "--tsv");
        var tail = await ExecuteCommandLineAsync(
            "ecosystem",
            "microsoft-extensions",
            "-S",
            "Core Packages",
            "-n",
            "1",
            "--tail",
            "--tsv");
        var count = await ExecuteCommandLineAsync(
            "ecosystem",
            "microsoft-extensions",
            "-S",
            "Core Packages",
            "-n",
            "1",
            "--count");

        Assert.Equal(0, head.ExitCode);
        Assert.Empty(head.Error);
        Assert.Equal(
            "package\nMicrosoft.Extensions.DependencyInjection.Abstractions",
            head.Output.Trim());
        Assert.Equal(0, tail.ExitCode);
        Assert.Empty(tail.Error);
        Assert.Equal(
            "package\nMicrosoft.Extensions.Logging.Abstractions",
            tail.Output.Trim());
        Assert.Equal(0, count.ExitCode);
        Assert.Empty(count.Error);
        Assert.Equal("1", count.Output.Trim());
    }

    [Fact]
    public async Task CommandLine_ExplicitMarkdownIsAvailable()
    {
        var result = await ExecuteCommandLineAsync(
            "ecosystem",
            "aspire",
            "--markdown");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("# Aspire", result.Output);
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

    [Theory]
    [InlineData("--jsonl")]
    [InlineData("--json")]
    public async Task UnboundIntegrations_RejectProjectionThatDropsDisclosure(
        string format)
    {
        var result = await ExecuteCommandLineAsync(
            "ecosystem",
            "microsoft-extensions",
            "-S",
            "Integrations",
            "--columns",
            "ID",
            format);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "must include both 'Binding' and 'Knowledge Scope'",
            result.Error);
    }

    [Fact]
    public async Task UnboundIntegrations_AllowsCompleteDisclosureProjection()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "microsoft-extensions",
            Select = ["Integrations"],
            Columns = ["Binding", "Knowledge Scope"],
            Format = OutputFormat.Jsonl,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("\"binding\":\"not configured\"", result.Output);
        Assert.Contains(
            "\"knowledge_scope\":\"No Integration concepts are explicitly bound",
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
    public async Task JsonPreservesEmptyTableAsArray()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspnetcore",
            Select = ["Tool Packages"],
            Format = OutputFormat.Json,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement section =
            document.RootElement.GetProperty("tool_packages");
        Assert.Equal(JsonValueKind.Array, section.ValueKind);
        Assert.Empty(section.EnumerateArray());
    }

    [Fact]
    public async Task JsonOmitsSectionsProjectedAwayByColumns()
    {
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "microsoft-extensions",
            SelectDefault = true,
            Columns = ["Package"],
            Format = OutputFormat.Json,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonProperty[] sections =
            [.. document.RootElement.EnumerateObject()];
        Assert.Equal(
            ["core_packages", "tool_packages"],
            sections.Select(section => section.Name));
        Assert.Equal(JsonValueKind.Array, sections[0].Value.ValueKind);
        Assert.Equal(JsonValueKind.Array, sections[1].Value.ValueKind);
        Assert.Empty(sections[1].Value.EnumerateArray());
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CommandLine_ExplicitBlankSelectorFailsVisibly(
        string selector)
    {
        var result = await ExecuteCommandLineAsync(
            "ecosystem",
            selector,
            "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Unknown ecosystem", result.Error);
        Assert.Contains("Available ecosystems:", result.Error);
    }

    private static Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        EcosystemOptions options) =>
        ConsoleCapture.RunAsync(
            () => Task.FromResult(EcosystemCommand.Execute(options)));

    private static async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        EcosystemOptions options,
        Func<InstalledPlatformPruneSource.Result> pruneSource,
        string? expectedError = null)
    {
        var result = await ConsoleCapture.RunAsync(
            () => Task.FromResult(EcosystemCommand.Execute(options, pruneSource)));
        if (expectedError is not null)
            Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
        return result;
    }

    private static Task<(int ExitCode, string Output, string Error)>
        ExecuteCommandLineAsync(params string[] arguments)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        return ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(arguments),
                arguments));
    }

    [Fact]
    public async Task Pruning_ListsWhatTheInstalledPlatformTargetSupplies()
    {
        // The real reference pack installed on this machine, which is the scenario the section
        // exists for. Membership moves with the SDK, so this pins the shape of the answer and
        // leaves the live/frozen contract to the deterministic case below.
        var result = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "platform",
            Select = ["Pruning"],
            Format = OutputFormat.Json,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);

        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement[] rows =
            [.. document.RootElement.GetProperty("pruning").EnumerateArray()];

        // System.Text.Json has been a subsumed identity for every supported target, and the
        // installed runtime pack is the family that supplies it.
        JsonElement json = Assert.Single(
            rows,
            row => row.GetProperty("package").GetString() == "System.Text.Json");
        Assert.Equal("Microsoft.NETCore.App", json.GetProperty("supplied_by").GetString());
        Assert.True(
            NuGetVersion.TryParse(json.GetProperty("supplied").GetString(), out _),
            "the supplied version is the pack's literal, so it must parse");
    }

    [Fact]
    public async Task Pruning_SeparatesTheVersionThatMovesWithTheFrameworkFromThePinnedOne()
    {
        // The distinction is what explains a result rather than restating it: a supplied version
        // that is the pack's own version moves with the framework, while a lower one is pinned to
        // a release the framework has passed.
        var result = await ExecuteAsync(
            new EcosystemOptions
            {
                Ecosystem = "platform",
                Select = ["Pruning"],
                Format = OutputFormat.Json,
            },
            Supplying("Microsoft.CSharp|4.7.0", "System.Text.Json|11.0.0"));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);

        using JsonDocument document = JsonDocument.Parse(result.Output);
        Dictionary<string, (string Supplied, string Kind, string SuppliedBy)> rows =
            document.RootElement
                .GetProperty("pruning")
                .EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("package").GetString()!,
                    row => (
                        row.GetProperty("supplied").GetString()!,
                        row.GetProperty("kind").GetString()!,
                        row.GetProperty("supplied_by").GetString()!));

        Assert.Equal(("4.7.0", "frozen", "Microsoft.NETCore.App"), rows["Microsoft.CSharp"]);
        Assert.Equal(("11.0.0", "live", "Microsoft.NETCore.App"), rows["System.Text.Json"]);
    }

    [Fact]
    public async Task Pruning_BelongsToThePlatformEcosystemAlone()
    {
        // Only the platform ecosystem can answer which identities a target subsumes, so the
        // section is not selectable from the catalog-wide view or from another pack.
        var catalogWide = await ExecuteAsync(new EcosystemOptions { Select = ["Pruning"] });
        Assert.Equal(1, catalogWide.ExitCode);

        var otherPack = await ExecuteAsync(new EcosystemOptions
        {
            Ecosystem = "aspire",
            Select = ["Pruning"],
        });
        Assert.Equal(1, otherPack.ExitCode);
    }

    [Fact]
    public async Task Pruning_ReadsTheInstalledPackOnlyWhenItIsSelected()
    {
        // It is the one section backed by an installed reference pack rather than a compiled-in
        // descriptor, so the count is the property that matters: routes that do not select it
        // read nothing, and a route that does reads once.
        int reads = 0;
        InstalledPlatformPruneSource.Result Counting()
        {
            reads++;
            return Supplying("System.Text.Json|11.0.0")();
        }

        var catalog = await ExecuteAsync(new EcosystemOptions(), Counting);
        Assert.Equal(0, catalog.ExitCode);
        Assert.DoesNotContain("## Pruning", catalog.Output);
        Assert.Equal(0, reads);

        var platformInfo = await ExecuteAsync(
            new EcosystemOptions { Ecosystem = "platform" },
            Counting);
        Assert.Equal(0, platformInfo.ExitCode);
        Assert.DoesNotContain("## Pruning", platformInfo.Output);
        Assert.Empty(platformInfo.Error);
        Assert.Equal(0, reads);

        var pruning = await ExecuteAsync(
            new EcosystemOptions { Ecosystem = "platform", Select = ["Pruning"] },
            Counting);
        Assert.Equal(0, pruning.ExitCode);
        Assert.Contains("## Pruning", pruning.Output);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Pruning_ReportsAnUnreadablePackRatherThanAnEmptyInventory()
    {
        // A pack that cannot be read is not a platform that subsumes nothing. The failure reaches
        // stderr and no row claims otherwise.
        var result = await ExecuteAsync(
            new EcosystemOptions { Ecosystem = "platform", Select = ["Pruning"] },
            static () => new InstalledPlatformPruneSource.Result(
                null,
                "Could not read '/packs/Microsoft.NETCore.App.Ref/11.0.0/data/PackageOverrides.txt'."),
            expectedError: "Could not read");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Pruning", result.Output);

        // The empty text stands in for the table, so reaching it is the assertion that no row
        // rendered — and it reports what was read rather than what the platform contains.
        Assert.Contains(
            "No platform prune inventory was read for this target.",
            result.Output);
    }

    [Fact]
    public async Task Pruning_StructuredOutputCarriesNoEmptyRowWhenNothingIsSubsumed()
    {
        // A pack that publishes no entries subsumes nothing. Structured output says so with no
        // rows rather than with one row of empty strings, which a consumer would read as an
        // identity with a blank name.
        var result = await ExecuteAsync(
            new EcosystemOptions
            {
                Ecosystem = "platform",
                Select = ["Pruning"],
                Format = OutputFormat.Json,
            },
            Supplying());

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);

        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Empty(
            document.RootElement.GetProperty("pruning").EnumerateArray());
    }

    /// <summary>
    /// A prune source that supplies exactly these <c>PackageOverrides.txt</c> lines for a fixed
    /// target, so a case can assert the rendered answer rather than the installed SDK.
    /// </summary>
    private static Func<InstalledPlatformPruneSource.Result> Supplying(params string[] lines) =>
        () => new InstalledPlatformPruneSource.Result(
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net11.0",
                    NuGetVersion.Parse("11.0.0")),
                lines),
            null);
}
