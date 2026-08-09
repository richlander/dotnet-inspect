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

        SourceIntegritySummary result = await SourceIntegrityService.InspectAsync(
            documents,
            client,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Verified);
        Assert.Equal(1, result.Mismatched);
        Assert.Equal(0, result.LineEndingNormalized);
        Assert.Equal(1, result.Unverifiable);
        Assert.Equal(["/src/Mismatch.cs"], result.MismatchedFiles);
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
            return Task.FromResult(respond(request));
        }
    }
}
