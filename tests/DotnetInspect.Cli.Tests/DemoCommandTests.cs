using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Ecosystems;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class DemoCommandTests
{
    public DemoCommandTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        return await ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(
                root.Parse(args),
                args);
        });
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliWithLineWindowAsync(
        params string[] args)
    {
        return await ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeWithLineWindowAsync(
                root.Parse(args),
                args);
        });
    }

    [Fact]
    public async Task ExecuteList_IncludesEveryHomeDemo()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteList()));

        Assert.Equal(0, exitCode);
        Assert.Contains("Home demos", output, StringComparison.Ordinal);
        foreach (var entry in ProductDemos)
        {
            Assert.Contains(entry.ScenarioId, output, StringComparison.Ordinal);
            Assert.Contains(entry.Title, output, StringComparison.Ordinal);
            Assert.Contains(entry.Summary, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ListUsesCatalogDescriptorMetadata()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteList(OutputFormat.Json)));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(ProductDemos.Count, document.RootElement.GetArrayLength());

        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        for (int index = 0; index < ProductDemos.Count; index++)
        {
            EcosystemDemoDescriptor descriptor = ProductDemos[index];
            Assert.Equal(
                descriptor.ScenarioId,
                rows[index].GetProperty("id").GetString());
            Assert.Equal(
                descriptor.Title,
                rows[index].GetProperty("title").GetString());
            Assert.Equal(
                descriptor.Summary,
                rows[index].GetProperty("summary").GetString());
        }

        EcosystemDemoSelection aspire = Assert.IsType<EcosystemDemoSelectionResult.Known>(
            EcosystemPackCatalog.SelectDemo(
                ProductDemoIds.AspirePostgresCallGraph)).Selection;
        Assert.NotEqual(aspire.Descriptor.Title, aspire.Scenario.Title);
        Assert.NotEqual(aspire.Descriptor.Summary, aspire.Scenario.Description);
    }

    [Fact]
    public async Task ExecuteScenario_UnknownId_FailsWithCatalog()
    {
        var (exitCode, _, error) = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync("missing-demo"));

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown home demo 'missing-demo'", error, StringComparison.Ordinal);
        Assert.Contains("stj-serializer", error, StringComparison.Ordinal);
        Assert.Contains("demo list", error, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownCommands_ReservesDemo()
    {
        Assert.Contains(DemoCommand.Name, ArgumentPreprocessor.KnownCommands);
    }

    [Fact]
    public void ProductDemoSections_AreProductSectionNames()
    {
        // Gate: home-demo allow list stays inside the CLI SectionNames token space.
        var sectionNameConstants = typeof(SectionNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(ProductDemoSections.Known, id => Assert.Contains(id, sectionNameConstants));
        Assert.Equal(SectionNames.Methods, ProductDemoSections.Methods);
        Assert.Equal(SectionNames.CallGraph, ProductDemoSections.CallGraph);
        Assert.Equal(SectionNames.Callers, ProductDemoSections.Callers);
        Assert.Equal(
            [SectionNames.CallGraph, SectionNames.Callers],
            ProductDemoSections.ExpandRunSections(ProductDemoSections.CallGraph));
        // Tabular + caller scope: Callers so MemberCommand's re-add stays one section.
        Assert.Equal(
            [SectionNames.Callers],
            ProductDemoSections.ExpandRunSections(
                ProductDemoSections.CallGraph,
                singleSectionFormat: true,
                hasCallerScope: true));
        // Tabular without caller scope: Call Graph (no re-add; empty Callers would ship silence).
        Assert.Equal(
            [SectionNames.CallGraph],
            ProductDemoSections.ExpandRunSections(
                ProductDemoSections.CallGraph,
                singleSectionFormat: true,
                hasCallerScope: false));
        Assert.Equal(
            [SectionNames.Methods],
            ProductDemoSections.ExpandRunSections(ProductDemoSections.Methods));
    }

    [Fact]
    public void Runner_LowersStjToTypeMethodsSection()
    {
        var resolved = ResolveDemo(ProductDemoIds.StjSerializer);
        Assert.True(DemoScenarioRunner.TryCreateOptions(resolved, OutputFormat.Markdown, noHeader: false, out var options, out var error), error);
        var type = Assert.IsType<TypeOptions>(options);
        Assert.Equal("System.Text.Json.JsonSerializer", type.TypeName);
        Assert.Null(type.PackagePath);
        Assert.Equal("System.Text.Json", type.PlatformAssembly);
        Assert.Equal("runtime@10.0.12", type.PlatformFramework);
        Assert.Equal("net10.0", type.Tfm);
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.Methods },
            type.IncludeSections);
    }

    [Fact]
    public void Runner_LowersMultiPlatformCallGraphWithCallerScopeSections()
    {
        var resolved = ResolveDemo(ProductDemoIds.ExtensionsCallGraph);
        Assert.True(DemoScenarioRunner.TryCreateOptions(resolved, OutputFormat.Markdown, noHeader: false, out var options, out var error), error);
        var member = Assert.IsType<MemberOptions>(options);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            member.TypeName);
        Assert.Null(member.PackagePath);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            member.PlatformAssembly);
        Assert.Equal("aspnetcore@10.0.12", member.PlatformFramework);
        Assert.Equal("net10.0", member.Tfm);
        Assert.Equal("74b6b4b321", member.MemberDigest);
        Assert.Contains("TryAddEnumerable", member.MemberFilter);
        Assert.Contains("method", member.KindFilter);
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.CallGraph,
                SectionNames.Callers,
            },
            member.IncludeSections);
        Assert.Empty(member.CallerScopePackages);
    }

    [Fact]
    public void Runner_LowersSinglePackageCallGraphWithoutCallerPackages()
    {
        var resolved = ResolveDemo(ProductDemoIds.StjSerializeCallGraph);
        Assert.True(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Mermaid, noHeader: false, out var options, out var error),
            error);
        var member = Assert.IsType<MemberOptions>(options);
        Assert.Equal("System.Text.Json.JsonSerializer", member.TypeName);
        Assert.Null(member.PackagePath);
        Assert.Equal("System.Text.Json", member.PlatformAssembly);
        Assert.Equal("runtime@10.0.12", member.PlatformFramework);
        Assert.Equal("1dc14dd1fb", member.MemberDigest);
        Assert.Contains("Serialize", member.MemberFilter);
        Assert.True(member.MermaidOutput);
        Assert.Empty(member.CallerScopePackages);
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.CallGraph },
            member.IncludeSections);
    }

    [Fact]
    public void Runner_SinglePackageCallGraph_Table_UsesCallGraphSection()
    {
        var resolved = ResolveDemo(ProductDemoIds.StjSerializeCallGraph);
        Assert.True(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Table, noHeader: false, out var options, out var error),
            error);
        var member = Assert.IsType<MemberOptions>(options);
        Assert.Empty(member.CallerScopePackages);
        // Call Graph alone: no caller-scope re-add, so tabular keeps the graph.
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.CallGraph },
            member.IncludeSections);
        Assert.Equal([SectionNames.CallGraph], Assert.IsType<string[]>(member.Select));
        Assert.True(member.Tabular);
    }

    [Fact]
    public void Runner_Mermaid_SetsMermaidOutputAndSingleGraphSection()
    {
        var resolved = ResolveDemo(ProductDemoIds.ExtensionsCallGraph);
        Assert.True(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Mermaid, noHeader: false, out var options, out var error),
            error);
        var member = Assert.IsType<MemberOptions>(options);
        Assert.True(member.MermaidOutput);
        Assert.False(member.EmbeddedMermaid);
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.CallGraph },
            member.IncludeSections);
        Assert.Equal([SectionNames.CallGraph], Assert.IsType<string[]>(member.Select));
    }

    [Fact]
    public void Runner_MethodsDemo_RejectsStandaloneMermaid()
    {
        var resolved = ResolveDemo(ProductDemoIds.StjSerializer);
        Assert.False(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Mermaid, noHeader: false, out _, out var error));
        Assert.Contains("--mermaid requires a Call Graph home demo", error, StringComparison.Ordinal);
        Assert.Contains("Methods", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_CallGraph_Table_UsesCallersSingleSection()
    {
        var resolved = ResolveDemo(ProductDemoIds.ExtensionsCallGraph);
        Assert.True(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Table, noHeader: false, out var options, out var error),
            error);
        var member = Assert.IsType<MemberOptions>(options);
        // Callers alone: survives MemberCommand IncludeCallersSection re-add.
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.Callers },
            member.IncludeSections);
        Assert.Equal([SectionNames.Callers], Assert.IsType<string[]>(member.Select));
        Assert.True(member.Tabular);
        Assert.Empty(member.CallerScopePackages);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            member.PlatformAssembly);
    }

    [Fact]
    public void Runner_CallGraph_Json_SelectsCompleteGraphDocument()
    {
        var resolved = ResolveDemo(ProductDemoIds.ExtensionsCallGraph);
        Assert.True(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Json, noHeader: false, out var options, out var error),
            error);
        var member = Assert.IsType<MemberOptions>(options);
        Assert.Equal(
            [SectionNames.CallGraph],
            Assert.IsType<string[]>(member.Select));
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.CallGraph,
            },
            member.IncludeSections);
        Assert.True(member.JsonOutput);
    }

    [Fact]
    public void Runner_MethodsDemo_SetsSelectForSectionQuery()
    {
        var resolved = ResolveDemo(ProductDemoIds.StjSerializer);
        Assert.True(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Markdown, noHeader: false, out var options, out var error),
            error);
        var type = Assert.IsType<TypeOptions>(options);
        Assert.Equal([SectionNames.Methods], Assert.IsType<string[]>(type.Select));
        Assert.True(type.HasSectionQuery);
    }

    [Fact]
    public async Task Cli_DemoList_DispatchesThroughPreprocessor()
    {
        var (exitCode, output, error) = await RunCliAsync("demo", "list", "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(ProductDemos.Count, document.RootElement.GetArrayLength());
        Assert.Contains(
            document.RootElement.EnumerateArray(),
            element => element.GetProperty("id").GetString() == "stj-serializer");
    }

    [Theory]
    [InlineData("list")]
    [InlineData(null)]
    public async Task Cli_DemoList_LimitSelectsCompleteJsonRow(
        string? subcommand)
    {
        string[] args =
            subcommand is null
                ? ["demo", "-n", "1", "--json"]
                : ["demo", subcommand, "-n", "1", "--json"];
        var (exitCode, output, error) =
            await RunCliAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ProductDemos[0].ScenarioId,
            row.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("list")]
    [InlineData(null)]
    public async Task Cli_DemoList_CountObservesSelectedDescriptors(
        string? subcommand)
    {
        string[] args =
            subcommand is null
                ? ["demo", "-n", "2", "--count", "--json"]
                : ["demo", subcommand, "-n", "2", "--count", "--json"];
        var (exitCode, output, error) =
            await RunCliAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task Cli_DemoList_LineSelectionAppliesToCountPayload()
    {
        var (exitCode, output, error) =
            await RunCliWithLineWindowAsync(
                "demo",
                "list",
                "-n",
                "1",
                "--lines",
                "--count");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            ProductDemos.Count,
            int.Parse(
                output.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("list")]
    [InlineData(null)]
    public async Task Cli_DemoList_JsonLineSelectionAppliesToCountPayload(
        string? subcommand)
    {
        string[] args =
            subcommand is null
                ? ["demo", "-n", "1", "--lines", "--count", "--json"]
                : ["demo", subcommand, "-n", "1", "--lines", "--count", "--json"];
        var (exitCode, output, error) =
            await RunCliWithLineWindowAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            ProductDemos.Count,
            int.Parse(
                output.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Cli_DemoScenario_RejectsCount()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                ProductDemoIds.StjSerializer,
                "--count");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "--count is available only when listing demos.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoList_ParentBoundLimitSelectsCompleteJsonRow()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "-n",
                "1",
                "list",
                "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ProductDemos[0].ScenarioId,
            row.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Cli_DemoList_BareShorthandSelectsCompleteJsonRow()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "list",
                "-1",
                "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ProductDemos[0].ScenarioId,
            row.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Cli_DemoBareList_OverflowShorthandUsesCountDiagnostic()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "-2147483648",
                "--json");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(
            "Error: -n requires a positive whole number.",
            error.Trim());
    }

    [Theory]
    [InlineData("LIST", "-n", "1", "--json")]
    [InlineData("-n", "1", "--json", "--", "list")]
    public async Task Cli_DemoListAliasesUseSemanticRows(
        params string[] arguments)
    {
        var (exitCode, output, error) =
            await RunCliAsync(["demo", .. arguments]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ProductDemos[0].ScenarioId,
            row.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Cli_DemoList_TailSelectsLastCompleteJsonRow()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "list",
                "-n",
                "1",
                "--tail",
                "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ProductDemos[^1].ScenarioId,
            row.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Cli_DemoList_WindowComposesBeforeJsonRendering()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "list",
                "-n",
                "2",
                "--rows",
                "2..3",
                "--json");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(
            "Error: Demo row selection stage 2 requires row 3, but only "
                + "2 demo rows are available.",
            error.Trim());
    }

    [Fact]
    public async Task Cli_DemoList_WindowThenLimitPreservesArgumentOrder()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "list",
                "--rows",
                "2..3",
                "-n",
                "1",
                "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ProductDemos[1].ScenarioId,
            row.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(
        "Error: -n requires a positive whole number.",
        "-n",
        "0",
        "-n",
        "1")]
    [InlineData(
        "Error: --rows requires N..M, N.., or ..M with positive positions.",
        "--rows",
        "invalid",
        "-n",
        "bad")]
    public async Task Cli_DemoList_RepeatedValuesPreserveFailureOrder(
        string expectedError,
        params string[] rowArguments)
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                [
                    "demo",
                    "list",
                    .. rowArguments,
                    "--json",
                ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(expectedError, error.Trim());
    }

    [Theory]
    [InlineData("")]
    [InlineData("--plaintext")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task Cli_DemoList_LimitSelectsSameMarkoutRow(
        string formatOption)
    {
        string[] args =
            string.IsNullOrEmpty(formatOption)
                ? ["demo", "list", "-n", "1"]
                : ["demo", "list", "-n", "1", formatOption];
        var (exitCode, output, error) =
            await RunCliAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            ProductDemos[0].ScenarioId,
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ProductDemos[1].ScenarioId,
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoList_LinesClipsRenderedOutput()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "list",
                "-n",
                "2",
                "--lines");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            "# Home demos\n\n",
            output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Cli_DemoList_LinesRejectsJson()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                "list",
                "-n",
                "2",
                "--lines",
                "--json");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(
            "Error: Rendered-line selection cannot be combined with JSON output.",
            error.Trim());
    }

    [Theory]
    [InlineData("list", "-n", "2")]
    [InlineData("-n", "2", "list")]
    [InlineData("list", "--rows", "1..2")]
    [InlineData("list", "--tail")]
    [InlineData("list", "--lines")]
    public async Task Cli_DemoList_RowSelectionCannotBypassInvocationLowering(
        params string[] arguments)
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => root.Parse(["demo", .. arguments]).InvokeAsync());

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(
            "Error: Demo row selection was not lowered before execution.",
            error.Trim());
    }

    [Fact]
    public async Task Cli_DemoList_NoSelectionCanInvokeWithoutLowering()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => root.Parse(["demo", "list", "--json"]).InvokeAsync());

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            ProductDemos.Count,
            document.RootElement.GetArrayLength());
    }

    [Fact]
    public void DemoScenario_RowSelectionRequiresInvocationPreparation()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var parseResult =
            root.Parse(
                [
                    "demo",
                    ProductDemoIds.StjSerializer,
                    "-n",
                    "1",
                ]);

        Assert.False(
            CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Demo",
                out RowSelectionIntent<string>? intent,
                out string? error));
        Assert.Null(intent);
        Assert.Equal(
            "Demo row selection was not lowered before execution.",
            error);
    }

    [Fact]
    public void DemoScenarioAdoptsExplicitLineSelectionFallback()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var parseResult =
            root.Parse(
                [
                    "demo",
                    ProductDemoIds.StjSerializer,
                    "-n",
                    "1",
                ]);

        Assert.True(
            CliRowSelectionCommandRegistry.TryGetActiveAdoption(
                parseResult,
                out _));
    }

    [Fact]
    public async Task Cli_DemoScenario_PositionalOwnedShorthandDoesNotClipLines()
    {
        var (exitCode, output, error) =
            await RunCliWithLineWindowAsync(
                "demo",
                "-1",
                ProductDemoIds.StjSerializer);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.True(
            output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries).Length > 1);
    }

    [Fact]
    public async Task Cli_DemoScenario_ShorthandWithLinesClipsOutput()
    {
        var (exitCode, output, error) =
            await RunCliWithLineWindowAsync(
                "demo",
                ProductDemoIds.StjSerializer,
                "-1",
                "--lines");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("2147483648")]
    public async Task Cli_DemoScenario_InvalidLimitRetainsIntegerDiagnostic(
        string value)
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                ProductDemoIds.StjSerializer,
                "-n",
                value);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(
            $"Error: Cannot parse value '{value}' for option '-n' as an integer.",
            error.Trim());
    }

    [Fact]
    public async Task Cli_DemoScenarioRejectsListOnlyRowOptions()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo",
                ProductDemoIds.StjSerializer,
                "--rows",
                "1..1");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "available only when listing demos",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteScenario_Stj_ReturnsMethodsSection()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync(
                ProductDemoIds.StjSerializer,
                OutputFormat.Markdown));

        Assert.True(exitCode == 0, error + "\n" + output);
        Assert.Equal(["## Methods"], MarkdownSectionHeadings(output));
        Assert.Contains("JsonSerializer", output, StringComparison.Ordinal);
        Assert.DoesNotContain("resolve-only", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteScenario_StjDefinitionAndProgrammaticPlanReturnSameMethods()
    {
        ResolvedScenario scenario = CreateStjDefinitionScenario();
        WorkspacePlan documentPlan = Assert.IsType<WorkspacePlan>(scenario.WorkspacePlan);
        Assert.Same(documentPlan.Contexts[1], scenario.SelectedContext!.Input);
        Assert.Empty(documentPlan.Registrations);

        var document = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync(scenario));
        WorkspacePlan programmaticPlan = CreateStjPlan();
        Assert.Empty(programmaticPlan.Registrations);
        Assert.True(DemoScenarioRunner.TryCreateOptions(
            scenario, OutputFormat.Markdown, noHeader: false, out var options, out var error), error);
        var programmatic = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecutePlatformScenarioAsync(
                "programmatic-stj", programmaticPlan, programmaticPlan.Contexts[1], options));
        var shipped = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync(ProductDemoIds.StjSerializer));

        Assert.True(document.ExitCode == 0, document.Error);
        Assert.True(programmatic.ExitCode == 0, programmatic.Error);
        Assert.True(shipped.ExitCode == 0, shipped.Error);
        Assert.Equal(["## Methods"], MarkdownSectionHeadings(document.Output));
        Assert.Contains("JsonSerializer", document.Output, StringComparison.Ordinal);
        Assert.Equal(shipped.Output, document.Output);
        Assert.Equal(document.Output, programmatic.Output);
    }

    [Theory]
    [InlineData("net9.0", null, WorkspaceContextLoadFailureKind.ConflictingAcquisitionTarget)]
    [InlineData("net10.0", "LINUX-X64", WorkspaceContextLoadFailureKind.InvalidCoordinate)]
    public async Task ExecutePlatformScenario_PreservesPlanTargetFailures(
        string framework,
        string? runtimeIdentifier,
        WorkspaceContextLoadFailureKind failureKind)
    {
        ResolvedScenario scenario = CreateStjDefinitionScenario();
        Assert.True(DemoScenarioRunner.TryCreateOptions(
            scenario, OutputFormat.Markdown, noHeader: false, out var options, out var error), error);
        WorkspacePlan plan = CreateStjPlan(framework, runtimeIdentifier);

        var result = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecutePlatformScenarioAsync(
                "programmatic-stj", plan, plan.Contexts[1], options));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("could not load its exact Platform implementation", result.Error, StringComparison.Ordinal);
        Assert.Contains(failureKind.ToString(), result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteScenario_CallGraph_ReturnsDeclaredSectionSet()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync(
                ProductDemoIds.ExtensionsCallGraph,
                OutputFormat.Markdown));

        Assert.True(exitCode == 0, error + "\n" + output);
        // Closed preset: Call Graph + Callers (companion under multi-package caller-scope encoding).
        Assert.Equal(["## Callers", "## Call Graph"], MarkdownSectionHeadings(output));
        Assert.Contains("TryAddEnumerable", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoCallGraph_Mermaid_ReportsIncompleteWorkspaceBinding()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.ExtensionsCallGraph,
            "--mermaid");

        Assert.True(exitCode == 0, error + "\n" + output);
        Assert.Contains(
            "Warning: Call graph results are incomplete because",
            error,
            StringComparison.Ordinal);
        Assert.Contains("graph TD", output, StringComparison.Ordinal);
        Assert.Contains("TryAddEnumerable", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoMethods_Mermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.StjSerializer,
            "--mermaid");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("├─", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid requires a Call Graph home demo", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoMethods_EmbeddedMermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.StjSerializer,
            "--markdown",
            "--mermaid");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("graph TD", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Methods", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid requires a Call Graph home demo", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoList_Mermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync("demo", "--mermaid");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("stj-serializer", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid is not supported for demo list", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoList_EmbeddedMermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync("demo", "--markdown", "--mermaid");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("stj-serializer", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid is not supported for demo list", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoListSubcommand_ParentEmbeddedMermaid_FailsClosed()
    {
        // Parent-bound flags before the list subcommand token.
        var (exitCode, output, error) = await RunCliAsync(
            "demo", "--markdown", "--mermaid", "list");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("stj-serializer", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid is not supported for demo list", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoListSubcommand_ParentJsonMermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo", "--json", "--mermaid", "list");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("\"id\"", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid cannot be combined with --json", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoMethods_JsonMermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.StjSerializer,
            "--json",
            "--mermaid");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("\"members\"", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid cannot be combined with --json", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoCallGraph_PlaintextMermaid_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.ExtensionsCallGraph,
            "--plaintext",
            "--mermaid");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("graph TD", output, StringComparison.Ordinal);
        Assert.Contains("--mermaid cannot be combined with", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoCallGraph_Table_EmitsCallersRows()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.ExtensionsCallGraph,
            "--table");

        Assert.True(exitCode == 0, error + "\n" + output);
        Assert.DoesNotContain("Selection matches 2 sections", error, StringComparison.Ordinal);
        // Must be Callers section rows, not the Kind/Name member inventory fallback.
        Assert.Contains("Caller", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Return Type", output, StringComparison.Ordinal);
        Assert.Contains("TryAddEnumerable", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Cli_EveryCallGraphDemo_Mermaid_EmitsNonEmptyGraph()
    {
        foreach (var entry in ProductDemos)
        {
            var resolved = ResolveDemo(entry.ScenarioId);
            if (!string.Equals(
                    resolved.View?.Section,
                    ProductDemoSections.CallGraph,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var (exitCode, output, error) = await RunCliAsync("demo", entry.ScenarioId, "--mermaid");
            Assert.True(exitCode == 0, $"{entry.ScenarioId}: {error}\n{output}");
            Assert.Contains("graph TD", output, StringComparison.Ordinal);
            Assert.True(
                output.Length > 80,
                $"{entry.ScenarioId}: mermaid output too short ({output.Length} bytes).");
        }
    }

    [Fact]
    public async Task Cli_EveryCallGraphDemo_Table_EmitsNonEmptyRows()
    {
        foreach (var entry in ProductDemos)
        {
            var resolved = ResolveDemo(entry.ScenarioId);
            if (!string.Equals(
                    resolved.View?.Section,
                    ProductDemoSections.CallGraph,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var (exitCode, output, error) = await RunCliAsync("demo", entry.ScenarioId, "--table");
            Assert.True(exitCode == 0, $"{entry.ScenarioId}: {error}\n{output}");
            Assert.False(
                string.IsNullOrWhiteSpace(output),
                $"{entry.ScenarioId}: tabular Call Graph demo produced empty stdout.");
            // Must not fall through to the member inventory Kind/Name table.
            Assert.DoesNotContain("Return Type", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Cli_DemoCallGraph_JsonEmitsCompleteGraphDocument()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.ExtensionsCallGraph,
            "--json");

        Assert.True(exitCode == 0, error + "\n" + output);
        using var document = System.Text.Json.JsonDocument.Parse(output);
        Assert.NotEmpty(
            document.RootElement.GetProperty("nodes").EnumerateArray());
        Assert.NotEmpty(
            document.RootElement.GetProperty("edges").EnumerateArray());
        Assert.NotEmpty(
            document.RootElement.GetProperty("occurrences").EnumerateArray());
        Assert.DoesNotContain("\"members\"", output, StringComparison.Ordinal);
    }

    private static string[] MarkdownSectionHeadings(string markdown) =>
        markdown
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToArray();

    private static ResolvedScenario CreateStjDefinitionScenario()
    {
        var registry = new InspectionDefinitionRegistry();
        registry.AddJson("""
            {
              "schemaVersion": 1,
              "kind": "workspace",
              "id": "stj-workspace",
              "contexts": [
                {
                  "name": "unused",
                  "framework": "net8.0",
                  "members": [
                    {
                      "kind": "platform", "family": "runtime",
                      "assembly": "System.Text.Json", "version": "10.0.12",
                      "framework": "net10.0"
                    }
                  ]
                },
                {
                  "name": "stj",
                  "framework": "net10.0",
                  "members": [
                    {
                      "kind": "platform", "family": "runtime",
                      "assembly": "System.Text.Json", "version": "10.0.12",
                      "framework": "net10.0"
                    }
                  ]
                }
              ]
            }
            """);
        registry.AddJson("""
            {
              "schemaVersion": 1, "kind": "view", "id": "stj-view",
              "type": "System.Text.Json.JsonSerializer", "section": "Methods"
            }
            """);
        registry.AddJson("""
            {
              "schemaVersion": 1, "kind": "scenario", "id": "stj-scenario",
              "workspace": "stj-workspace", "context": "stj", "view": "stj-view"
            }
            """);
        return registry.ResolveScenario("stj-scenario");
    }

    private static WorkspacePlan CreateStjPlan(
        string framework = "net10.0",
        string? runtimeIdentifier = null) =>
        new(
            [],
            [
                new WorkspaceContextInput
                {
                    Framework = "net8.0",
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "runtime", "System.Text.Json", "10.0.12", "net10.0")],
                },
                new WorkspaceContextInput
                {
                    Framework = framework,
                    RuntimeIdentifier = runtimeIdentifier,
                    Members = [WorkspaceMemberCoordinate.Platform(
                        "runtime", "System.Text.Json", "10.0.12", "net10.0")],
                },
            ]);

    private static IReadOnlyList<EcosystemDemoDescriptor> ProductDemos =>
        EcosystemPackCatalog.DiscoverDemos();

    private static ResolvedScenario ResolveDemo(string scenarioId) =>
        Assert.IsType<EcosystemDemoSelectionResult.Known>(
            EcosystemPackCatalog.SelectDemo(scenarioId)).Selection.Scenario;
}
