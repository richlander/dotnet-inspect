using System.Security.Cryptography;
using System.Text;

using DotnetInspector.Core;

using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Services.Tests;

[Collection(CoreCacheCollection.Name)]
public class AuthoredSourceAcquisitionTests
{
    static readonly FindingSubject Subject = new("M~source", "Sample.M");
    const string Source = """
        class Sample
        {
            public int M()
            {
                return 1;
            }
        }
        """;

    [Fact]
    public void FromContent_VerifiedSourceProducesCompleteLineCensus()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var result = AuthoredSourceAcquisition.FromContent(
            Mapping(),
            Document(content),
            content,
            "M",
            Subject);

        var complete = Assert.IsType<FindingInspection<string>.Complete>(
            result.Lines.Value);
        Assert.Equal(SourceChecksumVerification.Exact, result.ChecksumVerification);
        Assert.Contains(
            complete.Findings,
            finding => finding.Payload.Contains(
                "public int M()",
                StringComparison.Ordinal));
        Assert.Contains(
            complete.Findings,
            finding => finding.Payload.Contains(
                "return 1;",
                StringComparison.Ordinal));
    }

    [Fact]
    public void FromContent_MismatchedChecksumProducesFailedInspection()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var result = AuthoredSourceAcquisition.FromContent(
            Mapping(),
            Document(Encoding.UTF8.GetBytes(Source + "changed")),
            content,
            "M",
            Subject);

        var failed = Assert.IsType<FindingInspection<string>.Failed>(
            result.Lines.Value);
        Assert.Equal(SourceChecksumVerification.Mismatch, result.ChecksumVerification);
        Assert.Contains("does not match", failed.Error.Reason);
    }

    [Fact]
    public void VerifyChecksum_AcceptsLineEndingNormalization()
    {
        byte[] expected = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\n"));
        byte[] actual = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"));

        var verification = AuthoredSourceAcquisition.VerifyChecksum(
            Document(expected),
            actual);

        Assert.Equal(
            SourceChecksumVerification.LineEndingNormalized,
            verification);
    }

    [Fact]
    public void FromContent_MissingChecksumIsNotAuthoredEvidence()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var document = Document(content) with
        {
            ChecksumAlgorithm = null,
            Checksum = null,
        };

        var result = AuthoredSourceAcquisition.FromContent(
            Mapping(),
            document,
            content,
            "M",
            Subject);

        Assert.True(result.Lines.Value is FindingInspection<string>.Failed);
        Assert.Equal(
            SourceChecksumVerification.Unavailable,
            result.ChecksumVerification);
    }

    [Fact]
    public async Task FetchValidatedSourceBytes_InvalidCacheRetriesAndRepairsFromNetwork()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        CoreCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] invalid = Encoding.UTF8.GetBytes("invalid");
        byte[] expected = Encoding.UTF8.GetBytes(Source);
        var handler = new QueueHandler(invalid, expected);
        using var client = new HttpClient(handler);
        const string Url = "https://example.test/Sample.cs";

        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var firstFetcher = new SourceFetcher(client);
            Assert.Equal(
                invalid,
                await firstFetcher.FetchSourceBytesAsync(Url, cancellationToken));

            var secondFetcher = new SourceFetcher(client);
            var repaired = await secondFetcher.FetchValidatedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                cancellationToken);

            Assert.Equal(expected, repaired);
            Assert.Equal(2, handler.RequestCount);

            var thirdFetcher = new SourceFetcher(client);
            var cached = await thirdFetcher.FetchValidatedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                cancellationToken);

            Assert.Equal(expected, cached);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    static MemberSourceObservation Mapping()
        => new(
            new MemberAnchor(
                "M~1234567890",
                "M:Sample.M",
                "1234567890",
                "Sample",
                "M"),
            MetadataToken: 0x06000001,
            DocumentRowId: 1,
            CanonicalPath: "Sample.cs",
            OriginalPath: "/_/Sample.cs",
            ResolvedUrl: "https://example.test/Sample.cs",
            StartLine: 5,
            EndLine: 5,
            IsPrimaryDocument: true);

    static SourceDocumentObservation Document(byte[] content)
        => new(
            CanonicalPath: "Sample.cs",
            OriginalPath: "/_/Sample.cs",
            DocumentRowId: 1,
            Storage: SourceDocumentStorage.SourceLink,
            ResolvedUrl: "https://example.test/Sample.cs",
            ChecksumAlgorithm: "SHA256",
            Checksum: Convert.ToHexString(SHA256.HashData(content)));

    sealed class QueueHandler(params byte[][] responses) : HttpMessageHandler
    {
        readonly Queue<byte[]> _responses = new(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responses.Dequeue()),
            });
        }
    }
}
