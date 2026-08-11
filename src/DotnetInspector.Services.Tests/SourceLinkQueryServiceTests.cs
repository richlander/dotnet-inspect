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
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)203)
            {
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Head,
                    "https://spsprodeus27.vssps.visualstudio.com/_signin"),
            }));
        List<string> logs = [];

        SourceAvailabilitySummary result = await SourceAvailabilityService.InspectAsync(
            [Document("/src/A.cs", url: Url)],
            client,
            log: logs.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.AccessibleSourceFiles);
        Assert.Equal(["/src/A.cs"], result.MissingSourceFiles);
        Assert.DoesNotContain(logs, message => message.Contains(Url, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs,
            message => message.Contains("spsprodeus27", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, message => message.Contains("https://", StringComparison.Ordinal));
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
