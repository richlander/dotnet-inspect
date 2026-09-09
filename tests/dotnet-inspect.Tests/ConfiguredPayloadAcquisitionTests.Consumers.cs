using System.Collections.Concurrent;
using System.IO.Compression;

using DotnetInspector.Fixtures;
using DotnetInspector.Output;

using CoreHttpClientFactory = DotnetInspector.Core.HttpClientFactory;

namespace DotnetInspector.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    private const string RangeType = "DiffFixtureSample.BodyStateSample";

    [Theory]
    [InlineData("1.0.0..3.0.0", "last", "3.0.0", false)]
    [InlineData("3.0.0..1.0.0", "#2", "2.0.0", true)]
    [InlineData("1.0.0..3.0.0", "2.0.0", "2.0.0", false)]
    public async Task ApiRange_LocalFeedSelectsTheRequestedAddress(
        string endpoints, string selector, string expected, bool fileUri)
    {
        const string Id = "range.api.local";
        string source = Path.Combine(_root, "api-range");
        foreach (string version in new[] { "1.0.0", "2.0.0", "3.0.0" })
            WriteApiPackage(source, Id, version);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Local API range opened an HTTP transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", $"{Id}@{endpoints}", "--at", selector,
                "--source", fileUri ? new Uri(source).AbsoluteUri : source, "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains(RangeType, result.Output);
        Assert.Contains($"{Id} {expected}", result.Output);
    }

    [Fact]
    public async Task ApiRange_MissingAddressDoesNotDiscoverOrAcquire()
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("An unaddressed API range opened a transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", "range.api.noaddress@1.0.0..3.0.0",
                "--source", FirstFeed, "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("requires --at", result.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("timeline")]
    public async Task RangeConsumers_UnreadablePeerFailsBeforePayload(string command)
    {
        const string Id = "range.consumer.partial";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "3.0.0"],
                _ => throw new InvalidOperationException("Partial discovery reached a payload."),
                requests));
        string missing = Path.Combine(_root, "missing-peer");
        List<string> args = command == "type"
            ? ["type", RangeType]
            : ["timeline", "--type", RangeType, "--finding", "api.type"];
        args.AddRange(["--package", $"{Id}@1.0.0..3.0.0", "--at", "first",
            "--source", FirstFeed, "--source", missing, "--tips", "q"]);

        var result = await RunCommandAsync([.. args]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("complete discovery is required", result.Error);
        Assert.Contains(missing, result.Error);
        Assert.DoesNotContain(requests, request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("sparse", 2)]
    [InlineData("all", 3)]
    public async Task TimelineRange_OneDiscoveryAcquiresOnlyExplicitAddresses(string selection, int payloadCount)
    {
        const string Id = "range.timeline.selection";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            new SelectionFeedHandler(FirstFeed, Id, ["1.0.0", "2.0.0", "3.0.0"],
                version => CreateApiPackage(Id, version), requests));
        List<string> args = ["timeline", "--package", $"{Id}@1.0.0..3.0.0",
            "--type", RangeType, "--finding", "api.type", "--source", FirstFeed, "--tips", "q"];
        if (selection == "sparse")
            args.AddRange(["--at", "first", "--at", "last"]);
        else if (selection == "all")
            args.AddRange(["--at", "all"]);

        var result = await RunCommandAsync([.. args]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(1, requests.Count(request =>
            request.EndsWith($"/{Id}/index.json", StringComparison.Ordinal)));
        Assert.Equal(payloadCount, requests.Count(request =>
            request.EndsWith(".nupkg", StringComparison.Ordinal)));
        if (selection == "all")
        {
            Assert.DoesNotContain("Unevaluated", result.Output);
            Assert.DoesNotContain("Recommendation", result.Output);
        }
        else
        {
            Assert.Contains("Unevaluated", result.Output);
            Assert.Contains($"--source {ShellCommandText.Quote(FirstFeed)}", result.Output);
            Assert.Contains("--nugetconfig-directory", result.Output);
        }
        if (selection == "sparse")
            Assert.Contains("Gap (1)", result.Output);
    }

    [Fact]
    public async Task TimelineRange_ProbeReplayRetainsWorkingDirectoryAndSelectionPolicy()
    {
        const string Id = "range.timeline.replay";
        string source = Path.Combine(_root, "timeline-feed");
        foreach (string version in new[] { "1.0.0", "2.0.0-preview.1", "3.0.0" })
            WriteApiPackage(source, Id, version);
        string originalDirectory = Directory.GetCurrentDirectory();
        string replayDirectory = Directory.CreateDirectory(Path.Combine(_root, "replay")).FullName;

        var result = await RunCommandAsync(
            ["timeline", "--package", $"{Id}@1.0.0..3.0.0", "--type", RangeType,
                "--finding", "api.type", "--source", Path.GetRelativePath(originalDirectory, source),
                "--preview", "--all", "--tfm", "net10.0", "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        string recommendation = result.Output.Split('\n').Single(line => line.Contains("Probe #2", StringComparison.Ordinal));
        Assert.Contains("2.0.0-preview.1", recommendation);
        Assert.Contains($"--source {ShellCommandText.Quote(source)}", recommendation);
        Assert.Contains($"--nugetconfig-directory {ShellCommandText.Quote(originalDirectory)}", recommendation);
        Assert.Contains("--preview", recommendation);
        Assert.Contains("--all", recommendation);
        Assert.Contains("--tfm 'net10.0'", recommendation);

        try
        {
            Directory.SetCurrentDirectory(replayDirectory);
            var replay = await RunCommandAsync(
                ["timeline", "--package", $"{Id}@1.0.0..3.0.0", "--type", RangeType,
                    "--finding", "api.type", "--source", source,
                    "--nugetconfig-directory", originalDirectory,
                    "--preview", "--all", "--tfm", "net10.0", "--at", "#2", "--tips", "q"]);
            Assert.True(replay.Exit == 0, replay.Error);
            Assert.Contains("| #2 | 2.0.0-preview.1 | Present |", replay.Output);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public async Task ApiPin_LocalSourceSupportsExactReplayWithoutDiscovery()
    {
        const string Id = "range.api.pin";
        string source = Path.Combine(_root, "api-pin");
        WriteApiPackage(source, Id, Version);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Local API pin opened an HTTP transport."));

        var result = await RunCommandAsync(
            ["type", RangeType, "--package", $"{Id}@{Version}",
                "--source", source, "--source", Path.Combine(_root, "unreadable"),
                "--tips", "q"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains($"{Id} {Version}", result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimelineRange_ConfigDirectoryErrorsPrecedeDiscovery(bool conflictingConfig)
    {
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
            throw new InvalidOperationException("Invalid replay configuration reached discovery."));
        List<string> args =
        [
            "timeline", "--package", "range.config.invalid@1.0.0..2.0.0",
            "--type", RangeType, "--source", FirstFeed,
            "--nugetconfig-directory", conflictingConfig ? _root : Path.Combine(_root, "missing"),
        ];
        if (conflictingConfig)
        {
            string config = Path.Combine(_root, "NuGet.Config");
            File.WriteAllText(config, $"""
                <configuration><packageSources><clear />
                <add key="feed" value="{FirstFeed}" />
                </packageSources></configuration>
                """);
            args.AddRange(["--nugetconfig", config]);
        }

        var result = await RunCommandAsync([.. args]);

        Assert.Equal(1, result.Exit);
        Assert.Contains(conflictingConfig
            ? "--nugetconfig and --nugetconfig-directory cannot be combined"
            : "NuGet config discovery directory not found", result.Error);
    }

    [Fact]
    public async Task ApiRange_ProjectionFailureCleansTransferredTemporaryDirectory()
    {
        const string Id = "range.api.cleanup";
        string source = Path.Combine(_root, "api-cleanup");
        WriteApiPackage(source, Id, Version);
        string temporary = Directory.CreateDirectory(Path.Combine(_root, "owned-temporary")).FullName;

        var result = await RunIsolatedCommandAsync(temporary,
            ["type", "--package", $"{Id}@{Version}..{Version}", "--at", "first",
                "--library", "Missing.dll", "--source", source, "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("Library 'Missing.dll' not found", result.Error);
        Assert.Empty(Directory.EnumerateDirectories(temporary, "inspect-api*", SearchOption.TopDirectoryOnly));
    }

    private static void WriteApiPackage(string source, string id, string version)
    {
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, $"{id}.{version}.nupkg"), CreateApiPackage(id, version));
    }

    private static byte[] CreateApiPackage(string id, string version)
    {
        using var buffer = new MemoryStream();
        buffer.Write(CreatePackage(id, version, version: version));
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            archive.CreateEntryFromFile(FixtureCatalog.DiffV1.AssemblyPath(), "lib/net10.0/RangeFixture.dll");
        return buffer.ToArray();
    }
}
