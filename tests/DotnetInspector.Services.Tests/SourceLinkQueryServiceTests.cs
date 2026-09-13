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
    public async Task Integrity_ImmutableExactEvidenceIsReused()
    {
        const string Url =
            "https://raw.githubusercontent.com/example/repo/"
            + "0123456789abcdef0123456789abcdef01234567/src/a.cs";
        byte[] body = "exact source"u8.ToArray();
        string checksum = Convert.ToHexString(SHA256.HashData(body));
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
        }));
        RecordingSourceLinkQueryCache cache = new();

        SourceIntegritySummary initial = await SourceIntegrityService.InspectAsync(
            [Document("/src/A.cs", url: Url, checksum: checksum)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);
        SourceIntegritySummary repeated = await SourceIntegrityService.InspectAsync(
            [Document("/src/A.cs", url: Url, checksum: checksum)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, initial.Verified);
        Assert.Equal(1, repeated.Verified);
        Assert.Equal(1, requests);
        Assert.Equal(["verified", "normalized", "verified"],
            cache.Lookups.Select(static lookup => lookup.Extension));
        Assert.All(cache.Lookups, lookup =>
        {
            Assert.Equal("source-integrity-v2", lookup.Category);
            Assert.Equal($"{Url}|SHA256|{checksum}", lookup.Key);
            Assert.Null(lookup.MaxAge);
        });
        Assert.Equal(
            new CacheWrite(
                "source-integrity-v2",
                $"{Url}|SHA256|{checksum}",
                "1",
                "verified"),
            Assert.Single(cache.Writes));
    }

    [Fact]
    public async Task Integrity_ImmutableNormalizedEvidenceIsReused()
    {
        const string Url =
            "https://raw.githubusercontent.com/example/repo/"
            + "0123456789abcdef0123456789abcdef01234567/src/a.cs";
        byte[] expected = "first\nsecond\n"u8.ToArray();
        byte[] served = "first\r\nsecond\r\n"u8.ToArray();
        string checksum = Convert.ToHexString(SHA256.HashData(expected));
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(served),
            };
        }));
        RecordingSourceLinkQueryCache cache = new();

        SourceIntegritySummary initial = await SourceIntegrityService.InspectAsync(
            [Document("/src/A.cs", url: Url, checksum: checksum)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);
        SourceIntegritySummary repeated = await SourceIntegrityService.InspectAsync(
            [Document("/src/A.cs", url: Url, checksum: checksum)],
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, initial.Verified);
        Assert.Equal(1, initial.LineEndingNormalized);
        Assert.Equal(1, repeated.Verified);
        Assert.Equal(1, repeated.LineEndingNormalized);
        Assert.Equal(1, requests);
        Assert.Equal(
            ["verified", "normalized", "verified", "normalized"],
            cache.Lookups.Select(static lookup => lookup.Extension));
        Assert.Equal(
            new CacheWrite(
                "source-integrity-v2",
                $"{Url}|SHA256|{checksum}",
                "1",
                "normalized"),
            Assert.Single(cache.Writes));
    }

    [Fact]
    public async Task Integrity_MutablePositiveIsVerifiedOnEveryAudit()
    {
        const string Url = "https://example.test/src/a.cs";
        byte[] body = "exact source"u8.ToArray();
        string checksum = Convert.ToHexString(SHA256.HashData(body));
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
        }));
        RecordingSourceLinkQueryCache cache = new();
        SourceDocumentObservation[] documents =
            [Document("/src/A.cs", url: Url, checksum: checksum)];

        SourceIntegritySummary initial = await SourceIntegrityService.InspectAsync(
            documents,
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);
        SourceIntegritySummary repeated = await SourceIntegrityService.InspectAsync(
            documents,
            client,
            cache,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, initial.Verified);
        Assert.Equal(1, repeated.Verified);
        Assert.Equal(2, requests);
        Assert.Empty(cache.Lookups);
        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Integrity_DuplicateObservationsRemainInTheDenominator()
    {
        const string Url = "https://example.test/src/a.cs";
        byte[] body = "exact source"u8.ToArray();
        string checksum = Convert.ToHexString(SHA256.HashData(body));
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            Interlocked.Increment(ref requests);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
        }));
        SourceDocumentObservation observation =
            Document("/src/A.cs", url: Url, checksum: checksum);

        SourceIntegritySummary result = await SourceIntegrityService.InspectAsync(
            [observation, observation],
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Verified);
        Assert.Equal(0, result.Mismatched);
        Assert.Equal(0, result.Unverifiable);
        Assert.Equal(2, requests);
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
    public async Task Availability_CancellationAfterHeadDoesNotPublishPositiveEvidence()
    {
        using CancellationTokenSource cancellation = new();
        using var client = new HttpClient(new StubHandler(_ =>
        {
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        RecordingSourceLinkQueryCache cache = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SourceAvailabilityService.InspectAsync(
                [Document("/src/A.cs", url: "https://example.test/src/a.cs")],
                client,
                cache,
                cancellationToken: cancellation.Token));

        Assert.Empty(cache.Writes);
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
        RecordingSourceLinkQueryCache cache = new();
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
            cache,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Verified);
        Assert.Equal(0, result.Mismatched);
        Assert.Equal(1, result.Unverifiable);
        Assert.Equal(0, content.ReadCount);
        Assert.Empty(cache.Writes);
        Assert.DoesNotContain(logs, message => message.Contains(Url, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("spsprodeus27", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, message => message.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Integrity_FailureDiagnosticsDoNotEchoArtifactUrls()
    {
        const string Url =
            "https://raw.githubusercontent.com/example/secret/"
            + "0123456789abcdef0123456789abcdef01234567/src/secret.cs";
        List<string> logs = [];
        using var client = new HttpClient(
            new ThrowingHandler($"transport exposed {Url}"));
        RecordingSourceLinkQueryCache cache = new();

        SourceIntegritySummary result = await SourceIntegrityService.InspectAsync(
            [
                Document(
                    "/src/Secret.cs",
                    url: Url,
                    checksum: Convert.ToHexString(SHA256.HashData("expected"u8)))
            ],
            client,
            cache,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Unverifiable);
        Assert.Contains("Source integrity fetch failed.", logs);
        Assert.Empty(cache.Writes);
        Assert.DoesNotContain(logs, message => message.Contains(Url, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("/src/Secret.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Integrity_CancellationAfterBodyDoesNotPublishEvidence()
    {
        const string Url =
            "https://raw.githubusercontent.com/example/repo/"
            + "0123456789abcdef0123456789abcdef01234567/src/a.cs";
        byte[] body = "exact source"u8.ToArray();
        using CancellationTokenSource cancellation = new();
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new CancellationAtEofContent(body, cancellation),
            }));
        RecordingSourceLinkQueryCache cache = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SourceIntegrityService.InspectAsync(
                [
                    Document(
                        "/src/A.cs",
                        url: Url,
                        checksum: Convert.ToHexString(SHA256.HashData(body)))
                ],
                client,
                cache,
                cancellationToken: cancellation.Token));

        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Integrity_UnexpectedFailureEscapesTheAudit()
    {
        const string Url = "https://example.test/source.cs";
        using var client = new HttpClient(
            new UnexpectedThrowingHandler(new InvalidOperationException("unexpected")));
        RecordingSourceLinkQueryCache cache = new();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SourceIntegrityService.InspectAsync(
                [
                    Document(
                        "/src/A.cs",
                        url: Url,
                        checksum: Convert.ToHexString(SHA256.HashData("expected"u8)))
                ],
                client,
                cache,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(cache.Writes);
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
                new HttpRequestException(message));
    }

    private sealed class UnexpectedThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
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

    private sealed class CancellationAtEofContent(
        byte[] content,
        CancellationTokenSource cancellation)
        : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => stream.WriteAsync(content).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(
                new CancellationAtEofStream(content, cancellation));

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }
    }

    private sealed class CancellationAtEofStream(
        byte[] content,
        CancellationTokenSource cancellation)
        : MemoryStream(content, writable: false)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = Read(buffer.Span);
            if (read == 0)
                cancellation.Cancel();
            return ValueTask.FromResult(read);
        }
    }

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
