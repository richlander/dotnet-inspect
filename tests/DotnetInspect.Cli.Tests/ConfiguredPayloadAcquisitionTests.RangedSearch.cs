using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    /// <summary>
    /// The exact-package search Root is realized from a ranged read: the
    /// archive directory and the selected compile assembly only, never the
    /// large entry beside it, and nothing is committed to a cache.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangedRead_TransfersOnlyTheSelectedAssembly()
    {
        string id = $"Workspace.Ranged.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] filler = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);
        byte[] package = CreatePackage(
            id,
            "ranged search package",
            library: assembly,
            libraryName: "Workspace.Ranged.dll",
            extraEntries: [("content/filler.bin", filler)]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        CoreHttpClientFactory.SetAuthenticationDecorator(_ => feed);
        CoreHttpClientFactory.ResetSharedForTesting();

        var result = await RunCommandAsync(
            [
                "find",
                typeof(WorkspaceImplementation).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            nameof(WorkspaceImplementation),
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        Assert.Contains("nothing was cached", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.True(feed.RangedResponses >= 2, $"ranged responses: {feed.RangedResponses}");
        Assert.True(
            feed.PackageBytesServed < assembly.Length + (1024 * 1024),
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    /// <summary>
    /// A source that answers a ranged request with the whole archive is
    /// refused as <c>RangeIgnored</c> and the same authority serves the
    /// complete download, so the search still completes.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangeIgnored_FallsBackToTheCompleteDownload()
    {
        string id = $"Workspace.RangeIgnored.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "range-ignoring package",
            library: assembly,
            libraryName: "Workspace.RangeIgnored.dll");
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, id, package, ignoreRange: true);
        CoreHttpClientFactory.SetAuthenticationDecorator(_ => feed);
        CoreHttpClientFactory.ResetSharedForTesting();

        var result = await RunCommandAsync(
            [
                "find",
                typeof(WorkspaceImplementation).FullName!,
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            nameof(WorkspaceImplementation),
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("RangeIgnored", result.Error, StringComparison.Ordinal);
        Assert.Contains("payload Download", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A v3 feed whose flat container honors single <c>Range</c> requests
    /// (suffix and absolute) with <c>206</c>, <c>Content-Range</c>, and an
    /// <c>ETag</c>, counting what it transfers.
    /// </summary>
    private sealed class RangeHonoringFeedHandler(
        string source,
        string id,
        byte[] package,
        bool ignoreRange = false) : HttpMessageHandler
    {
        private const string ETag = "\"ranged-fixture\"";
        private long _packageBytesServed;
        private int _rangedResponses;
        private int _fullPackageResponses;

        public long PackageBytesServed => Interlocked.Read(ref _packageBytesServed);

        public int RangedResponses => Volatile.Read(ref _rangedResponses);

        public int FullPackageResponses => Volatile.Read(ref _fullPackageResponses);

        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue(url);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string lower = id.ToLowerInvariant();
            string packageUrl = $"{flat}{lower}/{Version}/{lower}.{Version}.nupkg";
            if (url == source)
            {
                return Task.FromResult(Respond(
                    request,
                    HttpStatusCode.OK,
                    new StringContent($$"""
                        {"version":"3.0.0","resources":[
                          {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                        ]}
                        """)));
            }
            if (url != packageUrl)
                throw new InvalidOperationException($"Unexpected ranged-feed request: {url}");

            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (ignoreRange || range is null)
            {
                Interlocked.Increment(ref _fullPackageResponses);
                Interlocked.Add(ref _packageBytesServed, package.Length);
                HttpResponseMessage full = Respond(
                    request, HttpStatusCode.OK, new ByteArrayContent(package));
                full.Headers.ETag = new EntityTagHeaderValue(ETag);
                return Task.FromResult(full);
            }

            long start;
            long end;
            if (range.From is null)
            {
                long suffix = Math.Min(range.To!.Value, package.Length);
                start = package.Length - suffix;
                end = package.Length - 1;
            }
            else
            {
                start = range.From.Value;
                end = Math.Min(range.To ?? package.Length - 1, package.Length - 1);
            }

            int length = checked((int)(end - start + 1));
            Interlocked.Increment(ref _rangedResponses);
            Interlocked.Add(ref _packageBytesServed, length);
            var content = new ByteArrayContent(package, (int)start, length);
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(start, end, package.Length);
            HttpResponseMessage partial = Respond(
                request, HttpStatusCode.PartialContent, content);
            partial.Headers.ETag = new EntityTagHeaderValue(ETag);
            partial.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(partial);
        }

        private static HttpResponseMessage Respond(
            HttpRequestMessage request,
            HttpStatusCode status,
            HttpContent content) =>
            new(status)
            {
                Content = content,
                RequestMessage = request,
            };
    }
}
