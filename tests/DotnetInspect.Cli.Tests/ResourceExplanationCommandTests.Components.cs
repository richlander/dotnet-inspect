using System.Text.Json;
using DotnetInspect.Cli.Commands;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ResourceExplanationCommandTests
{
    [Fact]
    public async Task ComponentContract_IsReachableFromTheProductionRoot()
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(() => requests++));
        var composition = ResourceExplanationCommand.CreateRootComposition();
        Assert.NotNull(composition);
        Assert.True(composition.IsPublishable);
        var root = await RunAsync("explain", ".hal", "--json");
        Assert.Equal(0, root.ExitCode);
        using JsonDocument document = JsonDocument.Parse(root.Output);
        Assert.Equal("_links", document.RootElement.EnumerateObject().First().Name);
        Assert.DoesNotContain("?projection=", root.Output);
        Assert.DoesNotContain("maximum_values", root.Output);
        Assert.Contains("component-contracts", root.Output);

        var collection = await RunAsync("explain", "component-contracts", ".hal", "--json");
        Assert.Equal(0, collection.ExitCode);
        using JsonDocument contracts = JsonDocument.Parse(collection.Output);
        string href = contracts.RootElement.GetProperty("_links")
            .GetProperty("collection-component-contract")[0].GetProperty("href").GetString()!;
        Assert.Equal("inspect-resource:/workspace/components/package", href);
        var leaf = await RunAsync("explain", href, ".hal", "--json");
        Assert.Equal(0, leaf.ExitCode);
        using JsonDocument contract = JsonDocument.Parse(leaf.Output);
        Assert.Equal(8, contract.RootElement.GetProperty("_embedded").GetProperty("data")
            .GetProperty("facts").GetProperty("fields").GetArrayLength());
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(".data")]
    [InlineData(".hal")]
    public async Task ComponentContract_RejectsQueryValueNarrowing(string projection)
    {
        var result = await RunAsync("explain", "workspace/components/package", projection,
            "--json", "-Q", "version");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
    }
}
