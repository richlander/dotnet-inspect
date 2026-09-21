using System.Net;
using System.Text.Json;
using DotnetInspector.Networking;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ResourceExplanationCommandTests : IDisposable
{
    public void Dispose()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(null);
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
    }

    [Fact]
    public async Task ExactSection_RendersResourceAndRelatedPaths()
    {
        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarchy");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "# Explain library/sections/reference-hierarchy",
            result.Output);
        Assert.Contains(
            "library/sections/reference-hierarchy/items/column/target",
            result.Output);
        Assert.Contains("Structural section", result.Output);
    }

    [Fact]
    public async Task DiscoveryPath_CanBePassedUnchangedToExplain()
    {
        var human = await RunAsync(
            "library",
            "-D",
            "@Dependencies");

        Assert.Equal(0, human.ExitCode);
        Assert.Empty(human.Error);
        Assert.Contains("| Name | Kind | Path |", human.Output);
        Assert.Contains(
            "| Reference Hierarchy | section "
            + "| library/sections/reference-hierarchy |",
            human.Output);

        var tree = await RunAsync(
            "library",
            "-D",
            "@Dependencies",
            "--tree");

        Assert.Equal(0, tree.ExitCode);
        Assert.Empty(tree.Error);
        Assert.Contains(
            "Reference Hierarchy "
            + "[library/sections/reference-hierarchy]",
            tree.Output);

        var discovery = await RunAsync(
            "library",
            "-D",
            "@Dependencies",
            "--json");

        Assert.Equal(0, discovery.ExitCode);
        Assert.Empty(discovery.Error);
        using JsonDocument discoveryDocument =
            JsonDocument.Parse(discovery.Output);
        JsonElement row = discoveryDocument.RootElement
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "Reference Hierarchy");
        string path = row.GetProperty("path").GetString()!;
        Assert.Equal(
            "library/sections/reference-hierarchy",
            path);

        var effective = await RunAsync(
            "library",
            "System.Text.Json",
            "-D",
            "@Dependencies",
            "--json");

        Assert.Equal(0, effective.ExitCode);
        Assert.Empty(effective.Error);
        using JsonDocument effectiveDocument =
            JsonDocument.Parse(effective.Output);
        JsonElement effectiveRow = effectiveDocument.RootElement
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "Reference Hierarchy");
        Assert.Equal(
            path,
            effectiveRow.GetProperty("path").GetString());

        var projected = await RunAsync(
            "library",
            "-D",
            "@Dependencies",
            "--json",
            "--fields",
            "Name,Path");

        Assert.Equal(0, projected.ExitCode);
        Assert.Empty(projected.Error);
        using JsonDocument projectedDocument =
            JsonDocument.Parse(projected.Output);
        JsonElement projectedRow = projectedDocument.RootElement
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("name").GetString()
                == "Reference Hierarchy");
        Assert.Equal(
            path,
            projectedRow.GetProperty("path").GetString());

        var detailed = await RunAsync(
            "library",
            "-D",
            "Reference Hierarchy",
            "--details",
            "--json");

        Assert.Equal(0, detailed.ExitCode);
        Assert.Empty(detailed.Error);
        using JsonDocument detailedDocument =
            JsonDocument.Parse(detailed.Output);
        Assert.Equal(
            path,
            detailedDocument.RootElement[0]
                .GetProperty("path")
                .GetString());

        var explanation = await RunAsync("explain", path, "--json");

        Assert.Equal(0, explanation.ExitCode);
        Assert.Empty(explanation.Error);
        using JsonDocument explanationDocument =
            JsonDocument.Parse(explanation.Output);
        Assert.Equal(
            path,
            explanationDocument.RootElement
                .GetProperty("requested_path")
                .GetString());

        var pathless = await RunAsync("vocabulary", "-D");

        Assert.Equal(0, pathless.ExitCode);
        Assert.Empty(pathless.Error);
        Assert.Contains("| Name | Kind |", pathless.Output);
        Assert.DoesNotContain("| Name | Kind | Path |", pathless.Output);
    }

    [Fact]
    public async Task Json_UsesTheHostNeutralContentShape()
    {
        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarchy",
            "--depth",
            "1",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "library/sections/reference-hierarchy",
            document.RootElement
                .GetProperty("requested_path")
                .GetString());
        Assert.Equal(
            "StructuralSection",
            document.RootElement
                .GetProperty("resources")[0]
                .GetProperty("resource_kind")
                .GetString());
        Assert.True(
            document.RootElement
                .GetProperty("resources")
                .GetArrayLength()
            > 1);
    }

    [Fact]
    public async Task UnknownPath_FailsWithBoundedSuggestions()
    {
        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarch");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("was not found", result.Error);
        Assert.Contains(
            "library/sections/reference-hierarchy",
            result.Error);
        Assert.InRange(
            result.Error.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Count(static line => line.StartsWith("  ")),
            1,
            5);
    }

    [Fact]
    public async Task Explain_DoesNotAttemptPackageAcquisition()
    {
        int requests = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new RecordingFailureHandler(
                () => requests++));

        var result = await RunAsync(
            "explain",
            "library/sections/reference-hierarchy");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, requests);
    }

    private static Task<(
        int ExitCode,
        string Output,
        string Error)> RunAsync(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });

    private sealed class RecordingFailureHandler(Action record) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            record();
            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.InternalServerError));
        }
    }
}
