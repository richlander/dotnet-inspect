using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;
using DotnetInspector.Networking;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
    [Fact]
    public async Task LatestVersions_LatestCompatibleLabelsTheHouseSelection()
    {
        string source = CreateTemporaryDirectory();
        foreach (string id in new[]
                 {
                     "Contoso.Keeps",
                     "Contoso.Drops",
                     "Contoso.EmptyRef",
                     "Contoso.Tool",
                 })
        {
            WriteLocalSourcePackage(source, id, "1.0.0", "");
        }
        WriteLocalAssetPackage(
            source,
            "Contoso.Keeps",
            "2.0.0",
            "lib/netstandard2.0/Contoso.Keeps.dll");
        // Latest moves to a framework the default target cannot consume.
        WriteLocalAssetPackage(
            source,
            "Contoso.Drops",
            "2.0.0",
            "lib/net472/Contoso.Drops.dll");
        WriteLocalAssetPackage(
            source,
            "Contoso.EmptyRef",
            "2.0.0",
            "ref/net8.0/_._",
            "lib/net8.0/Contoso.EmptyRef.dll");
        WriteLocalSourcePackage(source, "Contoso.Tool", "2.0.0", "");
        string manifest = WriteTemporaryFile(
            "compatible.nuspec",
            Manifest(
                "Contoso.Compatible",
                "1.0.0",
                """
                <group targetFramework="net11.0">
                  <dependency id="Contoso.Keeps" version="[1.0.0, )" />
                  <dependency id="Contoso.Drops" version="[1.0.0, )" />
                  <dependency id="Contoso.EmptyRef" version="[1.0.0, )" />
                  <dependency id="Contoso.Tool" version="[1.0.0, )" />
                </group>
                """));
        string[] select =
        [
            "depends",
            "--nuspec",
            manifest,
            "--source",
            source,
            "-S",
            "Latest Versions",
        ];

        // An omitted --tfm evaluates Latest against the product default.
        JsonElement[] rows = await LatestRowsAsync([.. select]);
        Assert.All(
            rows,
            static row => Assert.Equal(
                "net11.0",
                row.GetProperty("compatibility_target").GetString()));
        AssertCompatible(rows, "contoso.keeps", "Yes");
        AssertCompatible(rows, "contoso.drops", "No");
        AssertCompatible(rows, "contoso.emptyref", "Yes");
        AssertCompatible(rows, "contoso.tool", "NoAssets");
        // No is a fact about Latest, not a different version or a failure.
        JsonElement drops = Row(rows, 1, "contoso.drops");
        Assert.Equal("2.0.0", drops.GetProperty("latest").GetString());
        Assert.Equal("Compared", drops.GetProperty("state").GetString());

        // --tfm moves the target; the House answers again for it.
        JsonElement[] net472 = await LatestRowsAsync(
            [.. select, "--tfm", "net472"]);
        Assert.All(
            net472,
            static row => Assert.Equal(
                "net472",
                row.GetProperty("compatibility_target").GetString()));
        AssertCompatible(net472, "contoso.keeps", "Yes");
        AssertCompatible(net472, "contoso.drops", "Yes");
        AssertCompatible(net472, "contoso.emptyref", "No");
        AssertCompatible(net472, "contoso.tool", "NoAssets");

        (int countExitCode, string countOutput, string countError) =
            await RunCapturedAsync([.. select, "--count"]);
        Assert.True(countExitCode == 0, countError);
        Assert.Equal("4", countOutput.Trim());

        static void AssertCompatible(
            JsonElement[] rows,
            string package,
            string expected) =>
            Assert.Equal(
                expected,
                Row(rows, 1, package)
                    .GetProperty("latest_compatible")
                    .GetString());
    }

    [Fact]
    public async Task LatestVersions_AsksTheHouseOncePerQuestionAndReadsOnlyTheDirectory()
    {
        string id = $"Contoso.Ranged.{Guid.NewGuid():N}";
        string rootId = $"Contoso.RangedRoot.{Guid.NewGuid():N}";
        byte[] filler = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        var feed = new CountingPackageFeed(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{id}/1.0.0"] = AssetPackage(id, "1.0.0", []),
                [$"{id}/2.0.0"] = AssetPackage(
                    id,
                    "2.0.0",
                    [
                        ($"lib/netstandard2.0/{id}.dll", [0x4d, 0x5a]),
                        ("content/filler.bin", filler),
                    ]),
                [$"{rootId}/1.0.0"] = PackageArchive(
                    Manifest(
                        rootId,
                        "1.0.0",
                        Dependency(id, "[1.0.0, )")),
                    rootId,
                    []),
            });
        string first = WriteTemporaryFile(
            "first.nuspec",
            Manifest("Contoso.First", "1.0.0", Dependency(id, "[1.0.0, )")));
        string second = WriteTemporaryFile(
            "second.nuspec",
            Manifest("Contoso.Second", "1.0.0", Dependency(id, "[1.0.0, )")));

        // Dependencies alone performs no version discovery.
        (int dependenciesExit, _, string dependenciesError) =
            await RunCapturedWithFeedAsync(
                feed,
                [
                    "depends",
                    "--package",
                    $"{rootId}@1.0.0",
                    "--source",
                    CountingPackageFeed.Source,
                    "-S",
                    "Dependencies",
                    "--json",
                ]);
        Assert.True(dependenciesExit == 0, dependenciesError);
        Assert.Equal(0, feed.VersionIndexRequests(id));

        (int exitCode, string output, string error) =
            await RunCapturedWithFeedAsync(
                feed,
                [
                    "depends",
                    "--nuspec",
                    first,
                    "--nuspec",
                    second,
                    "--source",
                    CountingPackageFeed.Source,
                    "-S",
                    "Latest Versions",
                    "--json",
                    "--compact",
                ]);

        Assert.True(exitCode == 0, error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray(),
        ];
        Assert.Equal(2, rows.Length);
        Assert.All(
            rows,
            static row => Assert.Equal(
                "Yes",
                row.GetProperty("latest_compatible").GetString()));
        // Two declarations share one version discovery and one compatibility
        // question about the one Latest coordinate.
        Assert.Equal(1, feed.VersionIndexRequests(id));
        Assert.Equal(1, feed.ArchiveReads($"{id}/2.0.0"));
        Assert.Equal(0, feed.ArchiveReads($"{id}/1.0.0"));
        // The directory is read; the large entry beside it never is.
        Assert.True(
            feed.ArchiveBytesServed($"{id}/2.0.0") < filler.Length / 2,
            $"served {feed.ArchiveBytesServed($"{id}/2.0.0")} archive bytes");
    }

    private async Task<JsonElement[]> LatestRowsAsync(string[] args)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
            [.. args, "--json", "--compact"]);
        Assert.True(
            exitCode == 0,
            $"Expected success.{Environment.NewLine}{error}{Environment.NewLine}{output}");
        using JsonDocument document = JsonDocument.Parse(output);
        return
        [
            .. document.RootElement.GetProperty("latest_versions")
                .EnumerateArray()
                .Select(static row => row.Clone()),
        ];
    }

    private static void WriteLocalAssetPackage(
        string folder,
        string packageId,
        string version,
        params string[] entries) =>
        File.WriteAllBytes(
            Path.Combine(folder, $"{packageId}.{version}.nupkg"),
            AssetPackage(
                packageId,
                version,
                [.. entries.Select(static entry => (entry, new byte[] { 0x4d, 0x5a }))]));

    private static byte[] AssetPackage(
        string packageId,
        string version,
        (string Path, byte[] Content)[] entries) =>
        PackageArchive(Manifest(packageId, version, ""), packageId, entries);

    private static byte[] PackageArchive(
        byte[] manifest,
        string packageId,
        (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
                   buffer,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream stream = archive
                    .CreateEntry(path, CompressionLevel.NoCompression)
                    .Open();
                stream.Write(content);
            }
            using Stream nuspec = archive.CreateEntry($"{packageId}.nuspec").Open();
            nuspec.Write(manifest);
        }
        return buffer.ToArray();
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunCapturedWithFeedAsync(HttpMessageHandler feed, string[] args)
    {
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ => feed);
        try
        {
            return await ConsoleCapture.RunAsync(() => RunAsync(args));
        }
        finally
        {
            CoreHttpClientFactory.SetPackageSourceHandlerForTesting(null);
            CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
            CoreHttpClientFactory.ResetSharedForTesting();
        }
    }

    /// <summary>
    /// A v3 flat-container feed that honors single <c>Range</c> requests and
    /// counts version-index requests, archive reads, and archive bytes served.
    /// An archive read is one complete response, which the House's size probe
    /// abandons before its body when it can read the directory by range.
    /// </summary>
    private sealed class CountingPackageFeed(
        IReadOnlyDictionary<string, byte[]> archives) : HttpMessageHandler
    {
        public const string Source = "https://counted.feed.test/v3/index.json";

        private readonly ConcurrentDictionary<string, int> _indexRequests =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _archiveReads =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _bytesServed =
            new(StringComparer.OrdinalIgnoreCase);

        public int VersionIndexRequests(string id) =>
            _indexRequests.GetValueOrDefault(id.ToLowerInvariant());

        public int ArchiveReads(string key) =>
            _archiveReads.GetValueOrDefault(key.ToLowerInvariant());

        public long ArchiveBytesServed(string key) =>
            _bytesServed.GetValueOrDefault(key.ToLowerInvariant());

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/v3/index.json")
            {
                return Respond(
                    request,
                    HttpStatusCode.OK,
                    new StringContent(
                        """
                        {"version":"3.0.0","resources":[
                          {"@id":"https://counted.feed.test/flat/","@type":"PackageBaseAddress/3.0.0"}
                        ]}
                        """));
            }

            string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments is ["flat", string id, "index.json"])
            {
                _indexRequests.AddOrUpdate(id, 1, static (_, count) => count + 1);
                string[] versions =
                [
                    .. archives.Keys
                        .Where(key => key.StartsWith(
                            $"{id}/",
                            StringComparison.OrdinalIgnoreCase))
                        .Select(static key => key[(key.IndexOf('/') + 1)..]),
                ];
                return versions.Length == 0
                    ? Respond(request, HttpStatusCode.NotFound, new StringContent(""))
                    : Respond(
                        request,
                        HttpStatusCode.OK,
                        new StringContent(JsonSerializer.Serialize(
                            new Dictionary<string, string[]>
                            {
                                ["versions"] = versions,
                            })));
            }

            if (segments is ["flat", string packageId, string version, string file]
                && archives.TryGetValue(
                    $"{packageId}/{version}",
                    out byte[]? archive))
            {
                string key = $"{packageId}/{version}".ToLowerInvariant();
                if (file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = new ZipArchive(new MemoryStream(archive));
                    using var nuspec = new MemoryStream();
                    zip.Entries
                        .Single(entry => entry.FullName.EndsWith(
                            ".nuspec",
                            StringComparison.OrdinalIgnoreCase))
                        .Open()
                        .CopyTo(nuspec);
                    return Respond(
                        request,
                        HttpStatusCode.OK,
                        new ByteArrayContent(nuspec.ToArray()));
                }

                return ServeArchive(request, key, archive);
            }

            return Respond(request, HttpStatusCode.NotFound, new StringContent(""));
        }

        private Task<HttpResponseMessage> ServeArchive(
            HttpRequestMessage request,
            string key,
            byte[] archive)
        {
            RangeItemHeaderValue? range =
                request.Headers.Range?.Ranges.SingleOrDefault();
            if (range is null)
            {
                _archiveReads.AddOrUpdate(key, 1, static (_, count) => count + 1);
                var full = new StreamContent(
                    new CountingArchiveStream(archive, count =>
                        _bytesServed.AddOrUpdate(
                            key,
                            count,
                            (_, total) => total + count)));
                full.Headers.ContentLength = archive.Length;
                return Respond(request, HttpStatusCode.OK, full, etag: true);
            }

            long start = range.From
                ?? archive.Length - Math.Min(range.To!.Value, archive.Length);
            long end = range.From is null
                ? archive.Length - 1
                : Math.Min(range.To ?? archive.Length - 1, archive.Length - 1);
            int length = checked((int)(end - start + 1));
            _bytesServed.AddOrUpdate(key, length, (_, total) => total + length);
            var content = new ByteArrayContent(archive, (int)start, length);
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(start, end, archive.Length);
            return Respond(
                request,
                HttpStatusCode.PartialContent,
                content,
                etag: true);
        }

        private static Task<HttpResponseMessage> Respond(
            HttpRequestMessage request,
            HttpStatusCode status,
            HttpContent content,
            bool etag = false)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = content,
                RequestMessage = request,
            };
            if (etag)
            {
                response.Headers.ETag = new EntityTagHeaderValue("\"counted\"");
                response.Headers.AcceptRanges.Add("bytes");
            }
            return Task.FromResult(response);
        }

        /// <summary>Counts each archive byte a client actually reads.</summary>
        private sealed class CountingArchiveStream(
            byte[] bytes,
            Action<int> served) : Stream
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
                served(read);
                return read;
            }

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
    }
}
