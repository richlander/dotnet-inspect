using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task PackageInfo_RangedRead_UsesSelectedLibrariesWithoutUnrelatedBody()
    {
        string id = $"PackageInfo.Ranged.{Guid.NewGuid():N}";
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] filler =
            RandomNumberGenerator.GetBytes(
                4 * 1024 * 1024);
        byte[] package = CreatePackage(
            id,
            "ranged Package Info package",
            library: library,
            libraryName: $"{id}.dll",
            extraEntries:
            [
                ("content/filler.bin", filler),
                ("tools/net11.0/helper.dll", library),
            ]);
        var feed =
            new RangeHonoringFeedHandler(
                FirstFeed,
                id,
                package);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "package",
                $"{id}@{Version}",
                "--source",
                FirstFeed,
                "-S",
                "Package Info",
                "--markdown",
            ]);

        Assert.True(
            result.Exit == 0,
            result.Error);
        Assert.Contains(
            "| Built |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Content | lib, tools |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Selected TFM | net11.0 |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Ecosystem Dependencies |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Type | Library |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Equal(
            0,
            feed.FullPackageResponses);
        Assert.True(
            feed.RangedResponses >= 2,
            $"ranged responses: {feed.RangedResponses}");
        Assert.True(
            feed.PackageBytesServed
                < library.Length + (1024 * 1024),
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    [Theory]
    [InlineData(false, "net9.0", "NoApplicableTarget", 1)]
    [InlineData(true, "net11.0", "SelectedEmpty", 0)]
    public async Task PackageChildren_RangedRead_PreservesUnavailableAndEmptySelection(
        bool emptyCompile,
        string framework,
        string expectedStatus,
        int expectedExit)
    {
        string id = $"PackageChildren.Selection.{Guid.NewGuid():N}";
        List<(string, byte[])> entries =
        [
            ("content/unrelated.bin",
                RandomNumberGenerator.GetBytes(4 * 1024 * 1024)),
        ];
        if (emptyCompile)
            entries.Add(("ref/net11.0/_._", []));
        byte[] package = CreatePackage(
            id,
            "selection outcomes do not require Library bodies",
            library: RandomNumberGenerator.GetBytes(512 * 1024),
            extraEntries: [.. entries]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "--tfm", framework, "--json",
        ]);

        Assert.Equal(expectedExit, result.Exit);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(id, document.RootElement.GetProperty("package_id").GetString());
        Assert.Equal(
            expectedStatus,
            document.RootElement.GetProperty("status").GetString());
        Assert.Empty(document.RootElement.GetProperty("children").EnumerateArray());
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.True(
            feed.PackageBytesServed < 128 * 1024,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--json")]
    public async Task PackageChildren_RangedRead_PreservesUndeclaredToolFailure(
        string terminal)
    {
        string id = $"PackageChildren.InferredTool.{Guid.NewGuid():N}";
        byte[] package = CreateToolPackage(
            id,
            packageType: null,
            ("tools/net10.0/any/Shape.dll", new byte[11]));
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            terminal,
        ]);

        Assert.Equal(1, result.Exit);
        if (terminal == "--count")
        {
            Assert.Empty(result.Output);
            Assert.Contains("DotnetToolSettings.xml", result.Error);
        }
        else
        {
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(
                "Unavailable",
                document.RootElement.GetProperty("status").GetString());
            Assert.False(
                document.RootElement.GetProperty("is_complete").GetBoolean());
            Assert.Empty(
                document.RootElement.GetProperty("children").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--json")]
    public async Task PackageChildren_RangedRead_DoesNotAcquireLibraryBodies(
        string terminal)
    {
        string id = $"PackageChildren.Directory.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id,
            "Library children require selection, not loadable DLLs",
            library: RandomNumberGenerator.GetBytes(512 * 1024),
            libraryName: "Selected.dll",
            extraEntries:
            [
                ("content/unrelated.bin",
                    RandomNumberGenerator.GetBytes(4 * 1024 * 1024)),
                ("tools/net11.0/any/Helper.dll",
                    RandomNumberGenerator.GetBytes(512 * 1024)),
            ]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "--tfm", "net11.0", terminal, "--verbose",
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "Using committed content-only package Root for inspection:",
            result.Error,
            StringComparison.Ordinal);
        if (terminal == "--count")
        {
            Assert.Equal("1", result.Output.Trim());
        }
        else
        {
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(id, document.RootElement.GetProperty("package_id").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("total_count").GetInt32());
            Assert.Equal(
                "lib/net11.0/Selected.dll",
                document.RootElement.GetProperty("children")[0]
                    .GetProperty("asset").GetString());
        }
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.True(
            feed.PackageBytesServed < 128 * 1024,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--jsonl")]
    public async Task PackageFiles_RangedRead_RequestsDirectoryOnly(
        string terminal)
    {
        string id = $"PackageFiles.Directory.{Guid.NewGuid():N}";
        byte[] package = CreatePackage(
            id,
            "entry facts do not require payload content",
            library: RandomNumberGenerator.GetBytes(512 * 1024),
            extraEntries:
            [
                ("content/unrelated.bin",
                    RandomNumberGenerator.GetBytes(4 * 1024 * 1024)),
            ]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        var result = await RunCommandAsync(
        [
            "package", $"{id}@{Version}", "--source", FirstFeed,
            "-S", "Files", "--where", "Directory=content",
            "--rows", "1..1", terminal,
        ]);

        Assert.True(result.Exit == 0, result.Error);
        if (terminal == "--count")
        {
            Assert.Equal("1", result.Output.Trim());
        }
        else
        {
            using JsonDocument document = JsonDocument.Parse(result.Output);
            Assert.Equal(
                "content/unrelated.bin",
                document.RootElement.GetProperty("path").GetString());
            Assert.Equal(
                4 * 1024 * 1024,
                document.RootElement.GetProperty("size").GetInt64());
        }
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.True(
            feed.PackageBytesServed < 128 * 1024,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    [Fact]
    public async Task PackageInfo_RangedRead_PreservesExplicitDirectoryBuildDate()
    {
        string id =
            $"PackageInfo.DirectoryTimestamp.{Guid.NewGuid():N}";
        DateTimeOffset directoryTimestamp =
            new(2040, 1, 2, 3, 4, 6, TimeSpan.Zero);
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "directory timestamp package",
            library: library,
            libraryName: $"{id}.dll",
            extraEntries:
            [
                ("content/filler.bin",
                    RandomNumberGenerator.GetBytes(4 * 1024 * 1024)),
            ],
            explicitDirectories:
            [
                ("lib/", directoryTimestamp),
            ]);
        var feed =
            new RangeHonoringFeedHandler(
                FirstFeed,
                id,
                package);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "package",
                $"{id}@{Version}",
                "--source",
                FirstFeed,
                "-S",
                "Package Info",
                "--markdown",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "| Built | 2040-01-02 |",
            result.Output,
            StringComparison.Ordinal);
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.True(
            feed.RangedResponses >= 2,
            $"ranged responses: {feed.RangedResponses}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PackageInfo_TypedJson_RetainsBinarySignals(
        int packageCount)
    {
        string id =
            $"PackageInfo.TypedJson.{Guid.NewGuid():N}";
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "typed JSON Package Info package",
            library: library,
            libraryName: $"{id}.dll",
            extraEntries:
            [
                ("content/filler.bin",
                    RandomNumberGenerator.GetBytes(4 * 1024 * 1024)),
            ]);
        var feed =
            new RangeHonoringFeedHandler(
                FirstFeed,
                id,
                package);
        UseFeed(feed);

        string[] packages =
            Enumerable.Repeat(
                $"{id}@{Version}",
                packageCount)
            .ToArray();
        var result = await RunCommandAsync(
            [
                "package",
                .. packages,
                "--source",
                FirstFeed,
                "-S",
                "Package Info",
                "--json",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument json =
            JsonDocument.Parse(result.Output);
        JsonElement[] documents =
            json.RootElement.ValueKind == JsonValueKind.Array
                ? [.. json.RootElement.EnumerateArray()]
                : [json.RootElement];
        Assert.Equal(packageCount, documents.Length);
        Assert.All(
            documents,
            document => Assert.Equal(
                JsonValueKind.Object,
                document
                    .GetProperty("binary_signals")
                    .ValueKind));
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.Equal(0, feed.RangedResponses);
        Assert.True(
            feed.PackageBytesServed >= package.Length,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    /// <summary>
    /// The exact-package search Root is realized from a ranged read: the
    /// archive directory and the selected compile folder only, never the
    /// large entry beside it. The entries are kept in the entry cache, so a
    /// second search makes no package request.
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
        UseFeed(feed);

        string[] find =
        [
            "find",
            $".{MemberSearchServiceTests.SearchTargetMemberName}",
            "--package", $"{id}@{Version}",
            "--tfm", "net11.0",
            "--source", FirstFeed,
            "--all",
            "--json",
            "--verbose",
        ];
        var result = await RunCommandAsync(find);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            MemberSearchServiceTests.SearchTargetMemberName,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        // The feed is a credential-free HTTP authority, so the ranged entries
        // are durable (docs/design/package-cache-policy.md).
        Assert.Contains("kept in the entry cache", result.Error, StringComparison.Ordinal);
        // Directory-first acquisition opens the suffix directly.
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.True(feed.RangedResponses >= 2, $"ranged responses: {feed.RangedResponses}");
        Assert.True(
            feed.PackageBytesServed < assembly.Length + (1024 * 1024),
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");

        // The same search again answers from the entry cache with no package
        // request of either kind.
        int ranged = feed.RangedResponses;
        long served = feed.PackageBytesServed;
        var warm = await RunCommandAsync(find);

        Assert.True(warm.Exit == 0, warm.Error);
        Assert.Equal(result.Output, warm.Output);
        Assert.Contains("from the entry cache", warm.Error, StringComparison.Ordinal);
        Assert.Contains("no request", warm.Error, StringComparison.Ordinal);
        Assert.Equal(0, feed.FullPackageResponses);
        Assert.Equal(ranged, feed.RangedResponses);
        Assert.Equal(served, feed.PackageBytesServed);
    }

    /// <summary>
    /// The real Avalonia 12.1.2 archive (10.1 MB: net8.0 and net10.0 reference
    /// and implementation assemblies, XML docs, analyzers, designer tools)
    /// searched for net10.0 transfers the directory and the ref/net10.0
    /// folder only: about 2.2 MB, in the tail request and three spans.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangedRead_RealAvaloniaArchive()
    {
        const string Id = "Avalonia";
        const string AvaloniaVersion = "12.1.2";
        string path = Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "ApiMatching", "avalonia.12.1.2.nupkg");
        byte[] package = await File.ReadAllBytesAsync(
            path, TestContext.Current.CancellationToken);
        Assert.Equal(
            "99987414c63ac3993346a84a852006df963ff839f96d140557ee31f8850db08f",
            Convert.ToHexStringLower(SHA256.HashData(package)));
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, Id, package, version: AvaloniaVersion);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "find",
                ".InvalidateMeasure",
                "--package", $"{Id}@{AvaloniaVersion}",
                "--tfm", "net10.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("InvalidateMeasure", result.Output, StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        // Directory-first acquisition opens the suffix directly.
        Assert.Equal(0, feed.FullPackageResponses);
        // Search reads the surface only: the whole ref/net10.0 folder (11
        // assemblies and their 11 documentation files) and none of lib/. The
        // archive interleaves other folders' documentation into that folder,
        // so it is three spans, sent together after the tail.
        Assert.Contains("22 of 121 entries", result.Error, StringComparison.Ordinal);
        Assert.True(4 == feed.RangedResponses, string.Join("; ", feed.Ranges));
        const long RefFolderStart = 5_688_854;
        const long RefFolderEnd = 8_330_000;
        Assert.All(
            feed.Ranges.Skip(1),
            range => Assert.InRange(long.Parse(range.Split('-')[0]), RefFolderStart, RefFolderEnd));
        Assert.True(
            feed.PackageBytesServed < 2_500_000,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    /// <summary>
    /// A package whose only library targets netstandard2.0 is searched for
    /// net11.0: the ranged read materializes the compatible slice the Root
    /// binds, so no complete download follows.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangedRead_CompatibleSliceNeedsNoCompleteDownload()
    {
        string id = $"Workspace.Compatible.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "compatible slice package",
            library: assembly,
            libraryName: "Workspace.Compatible.dll",
            libraryDirectory: "lib/netstandard2.0");
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "find",
                $".{MemberSearchServiceTests.SearchTargetMemberName}",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            MemberSearchServiceTests.SearchTargetMemberName,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        // Directory-first acquisition opens the suffix directly.
        Assert.Equal(0, feed.FullPackageResponses);
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
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "find",
                $".{MemberSearchServiceTests.SearchTargetMemberName}",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            MemberSearchServiceTests.SearchTargetMemberName,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("RangeIgnored", result.Error, StringComparison.Ordinal);
        Assert.Contains("payload Download", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// What an abandoned size probe may read of a body that has already
    /// arrived before it finds a read that must wait for the network. On a
    /// socket those bytes were already received; an in-memory feed serves
    /// them on demand, so its served-byte gates allow for them.
    /// </summary>
    private const long AbandonedProbeReadBound = 64 * 1024;

    private static void UseFeed(HttpMessageHandler feed)
    {
        CoreHttpClientFactory.SetAuthenticationDecorator(_ => feed);
        CoreHttpClientFactory.ResetSharedForTesting();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ => feed);
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
        bool ignoreRange = false,
        string version = Version) : HttpMessageHandler
    {
        private const string ETag = "\"ranged-fixture\"";
        private long _packageBytesServed;
        private int _rangedResponses;
        private int _fullPackageResponses;

        public long PackageBytesServed => Interlocked.Read(ref _packageBytesServed);

        public int RangedResponses => Volatile.Read(ref _rangedResponses);

        public int FullPackageResponses => Volatile.Read(ref _fullPackageResponses);

        public ConcurrentQueue<string> Requests { get; } = new();

        public ConcurrentQueue<string> Ranges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue(url);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string lower = id.ToLowerInvariant();
            string packageUrl = $"{flat}{lower}/{version}/{lower}.{version}.nupkg";
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
                // Count the bytes actually read: size first abandons a complete
                // response before its body.
                Interlocked.Increment(ref _fullPackageResponses);
                HttpResponseMessage full = Respond(
                    request,
                    HttpStatusCode.OK,
                    new StreamContent(new CountingStream(package, this)));
                full.Content.Headers.ContentLength = package.Length;
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
            Ranges.Enqueue($"{start}-{end}");
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

        internal void AddServed(int count) =>
            Interlocked.Add(ref _packageBytesServed, count);

        /// <summary>
        /// A read-only archive body that counts each byte a client reads
        /// exactly once, whichever read overload the client uses.
        /// </summary>
        private sealed class CountingStream(byte[] bytes, RangeHonoringFeedHandler owner)
            : Stream
        {
            private int _position;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => bytes.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                int read = Math.Min(buffer.Length, bytes.Length - _position);
                bytes.AsSpan(_position, read).CopyTo(buffer);
                _position += read;
                owner.AddServed(read);
                return read;
            }

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(Read(buffer.Span));

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                Task.FromResult(Read(buffer.AsSpan(offset, count)));

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) =>
                throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
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
