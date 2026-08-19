using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class DemoCommandTests
{
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
    public async Task ExecuteScenario_ListAlias_ListsDemos()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteScenario("list")));

        Assert.Equal(0, exitCode);
        Assert.Contains("stj-serializer", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteScenario_UnknownId_FailsWithCatalog()
    {
        var (exitCode, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteScenario("missing-demo")));

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown home demo 'missing-demo'", error, StringComparison.Ordinal);
        Assert.Contains("stj-serializer", error, StringComparison.Ordinal);
        Assert.Contains("demo list", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteScenario_CallGraph_PrintsResolveOnlyPlan()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteScenario("extensions-callgraph")));

        Assert.Equal(0, exitCode);
        Assert.Contains("extensions-callgraph", output, StringComparison.Ordinal);
        Assert.Contains("74b6b4b321", output, StringComparison.Ordinal);
        Assert.Contains("call-graph", output, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.Abstractions", output, StringComparison.Ordinal);
        Assert.Contains("resolve-only", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteScenario_Json_EmitsStructuredPlan()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteScenario("stj-serializer", OutputFormat.Json)));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("stj-serializer", root.GetProperty("id").GetString());
        Assert.Equal("resolve-only", root.GetProperty("activation").GetString());
        Assert.True(root.GetProperty("createsAssemblyContextGroup").GetBoolean());
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            root.GetProperty("view").GetProperty("type").GetString());
        Assert.Equal(
            "System.Text.Json",
            root.GetProperty("members")[0].GetProperty("identity").GetString());
    }

    [Fact]
    public async Task ExecuteScenario_PlatformList_IncludesPlatformMember()
    {
        var (exitCode, output, _) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(DemoCommand.ExecuteScenario("platform-list", OutputFormat.Json)));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var members = document.RootElement.GetProperty("members");
        Assert.Contains(
            members.EnumerateArray(),
            member => member.GetProperty("kind").GetString() == "platform"
                && member.GetProperty("identity").GetString() == "runtime");
        Assert.Equal(
            "System.Collections.Generic.List`1",
            document.RootElement.GetProperty("view").GetProperty("type").GetString());
    }

    [Fact]
    public void KnownCommands_ReservesDemo()
    {
        Assert.Contains(DemoCommand.Name, ArgumentPreprocessor.KnownCommands);
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
    public async Task Cli_DemoScenario_DispatchesThroughPreprocessor()
    {
        var (exitCode, output, error) = await RunCliAsync("demo", "extensions-callgraph", "--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("extensions-callgraph", document.RootElement.GetProperty("id").GetString());
        Assert.Equal("resolve-only", document.RootElement.GetProperty("activation").GetString());
        Assert.Contains("74b6b4b321", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DemoBare_ListsCatalog()
    {
        var (exitCode, output, _) = await RunCliAsync("demo");

        Assert.Equal(0, exitCode);
        Assert.Contains("stj-serializer", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown command 'demo'", output, StringComparison.Ordinal);
    }
}
