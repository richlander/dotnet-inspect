using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Ecosystems;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

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
            args = CommandLineBuilder.PreprocessArgs(args);
            var root = CommandLineBuilder.CreateRootCommand();
            return await root.Parse(args).InvokeAsync();
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
        Assert.Equal("System.Text.Json@10.0.0", type.PackagePath);
        Assert.Equal("net10.0", type.Tfm);
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.Methods },
            type.IncludeSections);
        Assert.Null(type.PlatformAssembly);
    }

    [Fact]
    public void Runner_LowersCallGraphToMemberSectionWithCallerPackages()
    {
        var resolved = ResolveDemo(ProductDemoIds.ExtensionsCallGraph);
        Assert.True(DemoScenarioRunner.TryCreateOptions(resolved, OutputFormat.Markdown, noHeader: false, out var options, out var error), error);
        var member = Assert.IsType<MemberOptions>(options);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
            member.TypeName);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions@10.0.0",
            member.PackagePath);
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
        Assert.Contains("Microsoft.Extensions.Logging@10.0.0", member.CallerScopePackages);
        Assert.Contains("Microsoft.Extensions.Http@10.0.0", member.CallerScopePackages);
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
        Assert.Equal("System.Text.Json@10.0.0", member.PackagePath);
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
        Assert.NotEmpty(member.CallerScopePackages);
    }

    [Fact]
    public void Runner_CallGraph_Json_FailsClosed()
    {
        var resolved = ResolveDemo(ProductDemoIds.ExtensionsCallGraph);
        Assert.False(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Json, noHeader: false, out _, out var error));
        Assert.Contains("--json cannot represent Call Graph", error, StringComparison.Ordinal);
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
    public async Task Cli_DemoCallGraph_Json_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductDemoIds.ExtensionsCallGraph,
            "--json");

        Assert.Equal(1, exitCode);
        Assert.Contains("--json cannot represent Call Graph", error, StringComparison.Ordinal);
        Assert.DoesNotContain("\"members\"", output, StringComparison.Ordinal);
    }

    private static string[] MarkdownSectionHeadings(string markdown) =>
        markdown
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToArray();

    private static IReadOnlyList<EcosystemDemoDescriptor> ProductDemos =>
        EcosystemPackCatalog.DiscoverDemos();

    private static ResolvedScenario ResolveDemo(string scenarioId) =>
        Assert.IsType<EcosystemDemoSelectionResult.Known>(
            EcosystemPackCatalog.SelectDemo(scenarioId)).Selection.Scenario;
}
