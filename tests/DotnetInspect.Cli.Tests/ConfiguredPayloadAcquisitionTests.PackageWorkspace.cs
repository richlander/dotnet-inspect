using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Theory]
    [InlineData(null, "Measured")]
    [InlineData("all", "Measured")]
    [InlineData("net9.0", "NoApplicableSlice")]
    public async Task PackageCommand_OnlineFactsQueryContentOnlyRoot(
        string? framework,
        string expectedStatus)
    {
        string id = $"Workspace.PackageFacts.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "package facts do not require a loadable Library",
            library: new byte[17]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));
        List<string> args =
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "-S", "Package Info", "--json", "--verbose",
        ];
        if (framework is not null)
            args.AddRange(["--tfm", framework]);

        var result = await RunCommandAsync([.. args]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement measurements = document.RootElement.GetProperty(
            "package_info_measurements");
        Assert.Equal(
            expectedStatus,
            measurements.GetProperty("status").GetString());
        Assert.Equal(
            archive.LongLength,
            measurements.GetProperty("compressed_package_bytes").GetInt64());
        if (expectedStatus == "Measured")
        {
            Assert.Equal(
                17,
                measurements.GetProperty(
                    "selected_library_payload_bytes").GetInt64());
        }
        else
        {
            Assert.False(measurements.TryGetProperty(
                "selected_library_payload_bytes", out _));
        }
        Assert.Single(requests, request =>
            request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PackageCommand_OnlineChildrenRetainExplicitTarget()
    {
        string id = $"Workspace.PackageChildren.{Guid.NewGuid():N}";
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] archive = CreatePackage(
            id,
            "the requested framework chooses the Library population",
            library: library,
            libraryName: "Net11Only.dll",
            extraEntries:
            [
                ("lib/net8.0/Net8Only.dll", library),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "--tfm", "net8.0", "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains("Net8Only", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Net11Only", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageCommand_OnlineFactsRetainSelectedReportingSource()
    {
        string id = $"Workspace.SelectedFacts.{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new SelectionFeedHandler(
                source,
                id,
                source == FirstFeed ? ["1.0.0", "2.0.0"] : ["1.5.0"],
                version => CreatePackage(id, source, version: version),
                requests));

        var result = await RunCommandAsync(
        [
            "package", $"{id}@1.*", "--source", FirstFeed,
            "--source", SecondFeed, "-S", "Package Info",
            "--json", "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "1.5.0",
            document.RootElement.GetProperty("version").GetString());
        string payloadRequest = Assert.Single(requests, request =>
            request.EndsWith(".nupkg", StringComparison.Ordinal));
        Assert.StartsWith(
            "https://second-payload.invalid/",
            payloadRequest,
            StringComparison.Ordinal);
    }
}
