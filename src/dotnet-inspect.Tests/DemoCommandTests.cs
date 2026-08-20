using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

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
        Assert.Equal(
            [SectionNames.CallGraph],
            ProductDemoSections.ExpandRunSections(ProductDemoSections.CallGraph, standaloneGraphFormat: true));
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
    public void Runner_LowersPlatformListToCoreLibMethods()
    {
        var resolved = ProductInspectionDemos.ResolveHomeScenario(ProductInspectionDemos.PlatformListScenarioId);
        Assert.True(DemoScenarioRunner.TryCreateOptions(resolved, OutputFormat.Markdown, noHeader: false, out var options, out var error), error);
        var type = Assert.IsType<TypeOptions>(options);
        Assert.Equal("System.Collections.Generic.List`1", type.TypeName);
        Assert.Equal("System.Private.CoreLib", type.PlatformAssembly);
        Assert.Equal("runtime@10.0.10", type.PlatformFramework);
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.Methods },
            type.IncludeSections);
        Assert.Null(type.PackagePath);
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
    public async Task ExecuteScenario_PlatformList_ReturnsMethodsSection()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            () => DemoCommand.ExecuteScenarioAsync(
                ProductInspectionDemos.PlatformListScenarioId,
                OutputFormat.Markdown));

        Assert.True(exitCode == 0, error + "\n" + output);
        Assert.Equal(["## Methods"], MarkdownSectionHeadings(output));
        Assert.Contains("List", output, StringComparison.Ordinal);
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
    public async Task Cli_DemoCallGraph_Mermaid_EmitsGraph()
    {
        var (exitCode, output, error) = await RunCliAsync(
            "demo",
            ProductInspectionDemos.ExtensionsCallGraphScenarioId,
            "--mermaid");

        Assert.True(exitCode == 0, error + "\n" + output);
        Assert.Empty(error);
        Assert.Contains("graph TD", output, StringComparison.Ordinal);
        Assert.Contains("TryAddEnumerable", output, StringComparison.Ordinal);
    }

    private static string[] MarkdownSectionHeadings(string markdown) =>
        markdown
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToArray();
}
