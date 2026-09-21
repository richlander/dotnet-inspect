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
