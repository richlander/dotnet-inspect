using System.Net;
using System.Security.Cryptography;
using DotnetInspector.Services;

namespace DotnetInspector.Services.Tests;

public class SourceLinkQueryServiceTests
{
    [Fact]
    public async Task Availability_AccountsForEmbeddedReachableAndMissingDocuments()
    {
        SourceDocumentObservation[] documents =
        [
            Document("/src/Embedded.cs", SourceDocumentStorage.Embedded),
            Document("/src/Reachable.cs", url: "https://example.test/reachable.cs"),
            Document("/src/Missing.cs", url: "https://example.test/missing.cs"),
            Document(
                "/repo/artifacts/obj/Generated.g.cs",
                url: "https://example.test/generated.cs"),
            Document("/src/Notes.txt", url: "https://example.test/notes.txt"),
        ];
        using var client = new HttpClient(new StubHandler(request =>
            new HttpResponseMessage(
                request.RequestUri!.AbsolutePath.Contains("reachable", StringComparison.Ordinal)
                    ? HttpStatusCode.OK
                    : HttpStatusCode.NotFound)));

        SourceAvailabilitySummary result = await SourceAvailabilityService.InspectAsync(
            documents,
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, result.TotalSourceFiles);
        Assert.Equal(2, result.AccessibleSourceFiles);
        Assert.Equal(1, result.EmbeddedSourceFiles);
        Assert.Equal(
            ["/repo/artifacts/obj/Generated.g.cs", "/src/Missing.cs"],
            result.MissingSourceFiles);
        Assert.False(result.AllSourcesAccessible);
    }

    [Fact]
    public async Task Integrity_DistinguishesVerifiedMismatchedAndUnverifiableDocuments()
    {
        byte[] exactBody = "exact source"u8.ToArray();
        byte[] changedBody = "changed source"u8.ToArray();
        SourceDocumentObservation[] documents =
        [
            Document(
                "/src/Exact.cs",
                url: "https://example.test/exact.cs",
                checksum: Convert.ToHexString(SHA256.HashData(exactBody))),
            Document(
                "/src/Mismatch.cs",
                url: "https://example.test/mismatch.cs",
                checksum: Convert.ToHexString(SHA256.HashData("expected source"u8))),
            Document("/src/NoChecksum.cs", url: "https://example.test/no-checksum.cs"),
            Document("/src/Embedded.cs", SourceDocumentStorage.Embedded),
        ];
        using var client = new HttpClient(new StubHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                    request.RequestUri!.AbsolutePath.Contains("exact", StringComparison.Ordinal)
                        ? exactBody
                        : changedBody),
            }));
        List<string> logs = [];

        SourceIntegritySummary result = await SourceIntegrityService.InspectAsync(
            documents,
            client,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Verified);
        Assert.Equal(1, result.Mismatched);
        Assert.Equal(0, result.LineEndingNormalized);
        Assert.Equal(1, result.Unverifiable);
        Assert.Equal(["/src/Mismatch.cs"], result.MismatchedFiles);
        Assert.Contains("Source integrity checksum mismatch.", logs);
        Assert.DoesNotContain(
            logs,
            message => message.Contains("/src/Mismatch.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Availability_DoesNotCountCrossOriginRedirectAsReachable()
    {
        const string Url =
            "https://dev.azure.com/org/project/_apis/git/repositories/repo/items"
            + "?api-version=7.1&versionType=commit"
            + "&version=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&path=/A.cs";
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage((HttpStatusCode)203)
            {
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Head,
                    "https://spsprodeus27.vssps.visualstudio.com/_signin"),
            };
        }));
        List<string> logs = [];
        RecordingSourceLinkQueryCache cache = new();

        SourceAvailabilitySummary result = await SourceAvailabilityService.InspectAsync(
            [Document("/src/A.cs", url: Url)],
            client,
            cache,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);
        SourceAvailabilitySummary repeated = await SourceAvailabilityService.InspectAsync(
            [Document("/src/A.cs", url: Url)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.AccessibleSourceFiles);
        Assert.Equal(0, repeated.AccessibleSourceFiles);
        Assert.Equal(["/src/A.cs"], result.MissingSourceFiles);
        Assert.Equal(2, requests);
        Assert.Empty(cache.Writes);
        Assert.DoesNotContain(logs, message => message.Contains(Url, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("spsprodeus27", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, message => message.Contains("https://", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "https://raw.githubusercontent.com/example/repo/0123456789abcdef0123456789abcdef01234567/src/a.cs",
        true)]
    [InlineData("https://example.test/src/a.cs", false)]
    public async Task Availability_ReusesPositiveEvidenceWithProvenanceLifetime(
        string url,
        bool immutable)
    {
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        RecordingSourceLinkQueryCache cache = new();

        SourceAvailabilitySummary initial = await SourceAvailabilityService.InspectAsync(
            [Document("/src/A.cs", url: url)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);
        SourceAvailabilitySummary repeated = await SourceAvailabilityService.InspectAsync(
            [Document("/src/A.cs", url: url)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(initial.AllSourcesAccessible);
        Assert.True(repeated.AllSourcesAccessible);
        Assert.Equal(1, requests);
        Assert.Equal(2, cache.Lookups.Count);
        Assert.All(
            cache.Lookups,
            lookup =>
            {
                Assert.Equal("source-audit-v2", lookup.Category);
                Assert.Equal(url, lookup.Key);
                Assert.Equal("ok", lookup.Extension);
                Assert.Equal(immutable ? null : TimeSpan.FromDays(1), lookup.MaxAge);
            });
        Assert.Equal(
            new CacheWrite("source-audit-v2", url, "1", "ok"),
            Assert.Single(cache.Writes));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Availability_NonSuccessRemainsOperationLocal(HttpStatusCode statusCode)
    {
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(statusCode);
        }));
        RecordingSourceLinkQueryCache cache = new();
        cache.Seed(
            "source-audit-v2",
            "https://example.test/src/a.cs",
            "1",
            "miss");
        SourceDocumentObservation[] documents =
            [Document("/src/A.cs", url: "https://example.test/src/a.cs")];

        SourceAvailabilitySummary initial = await SourceAvailabilityService.InspectAsync(
            documents,
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);
        SourceAvailabilitySummary repeated = await SourceAvailabilityService.InspectAsync(
            documents,
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(initial.AllSourcesAccessible);
        Assert.False(repeated.AllSourcesAccessible);
        Assert.Equal(2, requests);
        Assert.All(cache.Lookups, lookup => Assert.Equal("ok", lookup.Extension));
        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Availability_LocalClassificationsNeedNoCacheOrNetwork()
    {
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        RecordingSourceLinkQueryCache cache = new();
        SourceDocumentObservation[] documents =
        [
            Document("/src/Embedded.cs", SourceDocumentStorage.Embedded),
            Document("/src/Unresolved.cs"),
            Document("/src/Unsupported.cs", url: "ftp://example.test/unsupported.cs"),
            new(
                "src/Generated.g.cs",
                "/repo/artifacts/obj/Generated.g.cs",
                DocumentRowId: 4,
                SourceDocumentStorage.SourceLink,
                "https://example.test/generated.cs",
                ChecksumAlgorithm: null,
                Checksum: null),
        ];

        SourceAvailabilitySummary result = await SourceAvailabilityService.InspectAsync(
            documents,
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.EmbeddedSourceFiles);
        Assert.Equal(3, result.MissingSourceFiles.Length);
        Assert.Equal(0, requests);
        Assert.Empty(cache.Lookups);
        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Availability_EmptyCensusIsNotAccessible()
    {
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        RecordingSourceLinkQueryCache cache = new();

        SourceAvailabilitySummary result = await SourceAvailabilityService.InspectAsync(
            [],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.TotalSourceFiles);
        Assert.Equal(0, result.AccessibleSourceFiles);
        Assert.False(result.AllSourcesAccessible);
        Assert.Equal(0, requests);
        Assert.Empty(cache.Lookups);
        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Availability_DuplicateUrlRetainsEachDocumentInTheDenominator()
    {
        const string Url = "https://example.test/src/a.cs";
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        RecordingSourceLinkQueryCache cache = new();
        cache.Seed("source-audit-v2", Url, "1", "ok");
        SourceDocumentObservation[] documents =
        [
            new(
                "src/A.cs",
                "/_/src/A.cs",
                DocumentRowId: 1,
                SourceDocumentStorage.SourceLink,
                Url,
                ChecksumAlgorithm: null,
                Checksum: null),
            new(
                "src/A.cs",
                "/_2/src/A.cs",
                DocumentRowId: 2,
                SourceDocumentStorage.SourceLink,
                Url,
                ChecksumAlgorithm: null,
                Checksum: null),
        ];

        SourceAvailabilitySummary result = await SourceAvailabilityService.InspectAsync(
            documents,
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalSourceFiles);
        Assert.Equal(2, result.AccessibleSourceFiles);
        Assert.True(result.AllSourcesAccessible);
        Assert.Equal(0, requests);
        Assert.Equal(2, cache.Lookups.Count);
        Assert.All(cache.Lookups, lookup => Assert.Equal(Url, lookup.Key));
    }

    [Fact]
    public async Task Integrity_DoesNotAcceptMatchingBytesFromCrossOriginRedirect()
    {
        byte[] body = "exact source"u8.ToArray();
        const string Url =
            "https://dev.azure.com/org/project/_apis/git/repositories/repo/items"
            + "?api-version=7.1&versionType=commit"
            + "&version=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&path=/A.cs";
        var content = new TrackingContent(body);
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://spsprodeus27.vssps.visualstudio.com/_signin"),
            }));
        List<string> logs = [];

        SourceIntegritySummary result = await SourceIntegrityService.InspectAsync(
            [
                Document(
                    "/src/A.cs",
                    url: Url,
                    checksum: Convert.ToHexString(SHA256.HashData(body)))
            ],
            client,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Verified);
        Assert.Equal(0, result.Mismatched);
        Assert.Equal(1, result.Unverifiable);
        Assert.Equal(0, content.ReadCount);
        Assert.DoesNotContain(logs, message => message.Contains(Url, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("spsprodeus27", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, message => message.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Integrity_FailureDiagnosticsDoNotEchoArtifactUrls()
    {
        const string Url = "https://example.test/secret.cs";
        List<string> logs = [];
        using var client = new HttpClient(
            new ThrowingHandler($"transport exposed {Url}"));

        SourceIntegritySummary result = await SourceIntegrityService.InspectAsync(
            [
                Document(
                    "/src/Secret.cs",
                    url: Url,
                    checksum: Convert.ToHexString(SHA256.HashData("expected"u8)))
            ],
            client,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Unverifiable);
        Assert.Contains("Source integrity fetch failed.", logs);
        Assert.DoesNotContain(logs, message => message.Contains(Url, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("/src/Secret.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreliableFinalUrl_FailsClosedOnlyForAttributedSourceAudits()
    {
        const string Attributed =
            "https://raw.githubusercontent.com/dotnet/runtime/"
            + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/src/A.cs";
        const string Unattributed = "https://example.test/source.cs";

        ILInspector.SourceLink.SourceLinkFetchOriginResult attributed =
            SourceFetchOriginValidator.Validate(
                Attributed,
                Attributed,
                finalUrlReliable: false);
        ILInspector.SourceLink.SourceLinkFetchOriginResult unattributed =
            SourceFetchOriginValidator.Validate(
                Unattributed,
                Unattributed,
                finalUrlReliable: false);

        Assert.Equal(
            ILInspector.SourceLink.SourceLinkFetchOriginStatus.Changed,
            attributed.Status);
        Assert.Equal(
            ILInspector.SourceLink.SourceLinkFetchOriginStatus.Unattributed,
            unattributed.Status);
        Assert.True(unattributed.IsAllowed);
    }

    private static SourceDocumentObservation Document(
        string path,
        SourceDocumentStorage storage = SourceDocumentStorage.SourceLink,
        string? url = null,
        string? checksum = null)
        => new(
            path,
            path,
            DocumentRowId: 1,
            storage,
            url,
            checksum == null ? null : "SHA256",
            checksum);

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(string message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(
                new InvalidOperationException(message));
    }

    private sealed class RecordingSourceLinkQueryCache : ISourceLinkQueryCache
    {
        private readonly Dictionary<(string Category, string Key, string Extension), string>
            _entries = [];

        public List<CacheLookup> Lookups { get; } = [];

        public List<CacheWrite> Writes { get; } = [];

        public void Seed(string category, string key, string content, string extension)
            => _entries[(category, key, extension)] = content;

        public string? TryGet(
            string category,
            string key,
            TimeSpan? maxAge,
            string extension)
        {
            Lookups.Add(new CacheLookup(category, key, maxAge, extension));
            return _entries.TryGetValue((category, key, extension), out string? value)
                ? value
                : null;
        }

        public void Set(string category, string key, string content, string extension)
        {
            Writes.Add(new CacheWrite(category, key, content, extension));
            _entries[(category, key, extension)] = content;
        }
    }

    private sealed record CacheLookup(
        string Category,
        string Key,
        TimeSpan? MaxAge,
        string Extension);

    private sealed record CacheWrite(
        string Category,
        string Key,
        string Content,
        string Extension);

    private sealed class TrackingContent(byte[] content) : HttpContent
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            Interlocked.Increment(ref _readCount);
            await stream.WriteAsync(content);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }
    }
}
