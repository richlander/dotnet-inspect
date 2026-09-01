using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
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
        foreach (var entry in ProductInspectionDemos.Entries)
        {
            Assert.Contains(entry.Id, output, StringComparison.Ordinal);
            Assert.Contains(entry.Title, output, StringComparison.Ordinal);
            Assert.Contains(entry.Summary, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ExecuteList_Json_EmitsCatalogRows()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteList(OutputFormat.Json)));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(ProductInspectionDemos.Entries.Count, document.RootElement.GetArrayLength());

        string[] ids = document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("id").GetString()!)
            .ToArray();
        Assert.Equal(ProductInspectionDemos.HomeScenarioIds, ids);
    }

    [Fact]
    public async Task ExecuteList_AppliesItemWindow()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(
                DemoCommand.ExecuteList(
                    OutputFormat.Json,
                    rows: RowWindow.Head(1))));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Equal(
            ProductInspectionDemos.Entries[0].Id,
            document.RootElement[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ListCommand_AppliesItemWindow()
    {
        var (exitCode, output, error) =
            await RunCliAsync("demo", "list", "-n", "1", "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListCommand_AppliesAbsoluteRange()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo", "list", "--rows", "2..3", "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal(
            ProductInspectionDemos.Entries[1].Id,
            document.RootElement[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ListCommand_AppliesParentAbsoluteRange()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo", "--rows", "2..3", "list", "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("--lines")]
    [InlineData("--tail-lines")]
    public async Task ListCommand_AppliesRenderedLineWindow(string lineOption)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            string[] args = CommandLineBuilder.PreprocessArgs(
                ["demo", "list", "-n", "1", lineOption]);
            var result = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(result);
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Single(output.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public async Task RootCommand_AppliesItemWindowWhenListing()
    {
        var (exitCode, output, error) =
            await RunCliAsync("demo", "-n", "1", "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ScenarioCommand_RejectsItemWindow()
    {
        var (exitCode, output, error) =
            await RunCliAsync("demo", "stj-serializer", "-n", "1");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "-n item windows apply to 'demo list'",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScenarioCommand_RejectsAbsoluteRangeTruthfully()
    {
        var (exitCode, output, error) =
            await RunCliAsync(
                "demo", "stj-serializer", "--rows", "2..3");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "--rows absolute row ranges apply to 'demo list'",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("-n item windows", error, StringComparison.Ordinal);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.StjSerializerScenarioId);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.ExtensionsCallGraphScenarioId);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(
            ProductInspectionDemos.StjSerializeCallGraphScenarioId);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(
            ProductInspectionDemos.StjSerializeCallGraphScenarioId);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.ExtensionsCallGraphScenarioId);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.StjSerializerScenarioId);
        Assert.False(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Mermaid, noHeader: false, out _, out var error));
        Assert.Contains("--mermaid requires a Call Graph home demo", error, StringComparison.Ordinal);
        Assert.Contains("Methods", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_CallGraph_Table_UsesCallersSingleSection()
    {
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.ExtensionsCallGraphScenarioId);
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
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.ExtensionsCallGraphScenarioId);
        Assert.False(
            DemoScenarioRunner.TryCreateOptions(
                resolved, OutputFormat.Json, noHeader: false, out _, out var error));
        Assert.Contains("--json cannot represent Call Graph", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_MethodsDemo_SetsSelectForSectionQuery()
    {
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.StjSerializerScenarioId);
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
        Assert.Equal(ProductInspectionDemos.Entries.Count, document.RootElement.GetArrayLength());
        Assert.Contains(
            document.RootElement.EnumerateArray(),
            element => element.GetProperty("id").GetString() == "stj-serializer");
    }

    [Fact]
    public async Task ExecuteScenario_Stj_ReturnsMethodsSection()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync(
                ProductInspectionDemos.StjSerializerScenarioId,
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
                ProductInspectionDemos.ExtensionsCallGraphScenarioId,
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
            ProductInspectionDemos.ExtensionsCallGraphScenarioId,
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
            ProductInspectionDemos.StjSerializerScenarioId,
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
            ProductInspectionDemos.StjSerializerScenarioId,
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
            ProductInspectionDemos.StjSerializerScenarioId,
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
            ProductInspectionDemos.ExtensionsCallGraphScenarioId,
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
            ProductInspectionDemos.ExtensionsCallGraphScenarioId,
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
        foreach (var entry in ProductInspectionDemos.Entries)
        {
            var resolved = ProductInspectionDemos.ResolveHomeScenario(entry.Id);
            if (!string.Equals(
                    resolved.View?.Section,
                    ProductDemoSections.CallGraph,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var (exitCode, output, error) = await RunCliAsync("demo", entry.Id, "--mermaid");
            Assert.True(exitCode == 0, $"{entry.Id}: {error}\n{output}");
            Assert.Contains("graph TD", output, StringComparison.Ordinal);
            Assert.True(
                output.Length > 80,
                $"{entry.Id}: mermaid output too short ({output.Length} bytes).");
        }
    }

    [Fact]
    public async Task Cli_EveryCallGraphDemo_Table_EmitsNonEmptyRows()
    {
        foreach (var entry in ProductInspectionDemos.Entries)
        {
            var resolved = ProductInspectionDemos.ResolveHomeScenario(entry.Id);
            if (!string.Equals(
                    resolved.View?.Section,
                    ProductDemoSections.CallGraph,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var (exitCode, output, error) = await RunCliAsync("demo", entry.Id, "--table");
            Assert.True(exitCode == 0, $"{entry.Id}: {error}\n{output}");
            Assert.False(
                string.IsNullOrWhiteSpace(output),
                $"{entry.Id}: tabular Call Graph demo produced empty stdout.");
            // Must not fall through to the member inventory Kind/Name table.
            Assert.DoesNotContain("Return Type", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Cli_DemoCallGraph_Json_FailsClosed()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductInspectionDemos.ExtensionsCallGraphScenarioId,
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
}
