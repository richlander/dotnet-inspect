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
    public void FromContent_MissingChecksumIsAbsentEvidence()
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

        Assert.IsType<FindingInspection<string>.Absent>(result.Lines.Value);
        Assert.Equal(
            SourceChecksumVerification.Unavailable,
            result.ChecksumVerification);
        Assert.NotNull(result.Mapping);
        Assert.NotNull(result.Document);
        Assert.Null(result.Text);
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

    [Fact]
    public async Task FetchSource_ConcurrentRequestsShareNetworkOperation()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        CoreCache.Initialize("dotnet-inspect-test", cachePath);
        var handler = new GatedStringHandler(Source);
        using var client = new HttpClient(handler);
        var fetcher = new SourceFetcher(client);
        const string Url = "https://example.test/Shared.cs";

        try
        {
            Task<string?>[] requests = Enumerable.Range(0, 32)
                .Select(_ => fetcher.FetchSourceAsync(Url))
                .ToArray();

            Assert.All(requests, request => Assert.Same(requests[0], request));
            await handler.RequestStarted;
            Assert.Equal(1, handler.RequestCount);

            handler.Release();
            string?[] results = await Task.WhenAll(requests);

            Assert.All(results, result => Assert.Equal(Source, result));
            Assert.Same(requests[0], fetcher.FetchSourceAsync(Url));
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            handler.Release();
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    [Fact]
    public void FromContent_FinalizerMapping_ExtractsDestructorNotPrecedingMember()
    {
        const string source = """
            class Sample
            {
                internal int Preceding;

                ~Sample()
                {
                    System.GC.KeepAlive(this);
                }
            }
            """;
        byte[] content = Encoding.UTF8.GetBytes(source);
        var result = AuthoredSourceAcquisition.FromContent(
            DestructorMapping(memberName: "Finalize", startLine: 6, endLine: 7, isFinalizer: true),
            Document(content),
            content,
            "Finalize",
            Subject);

        var complete = Assert.IsType<FindingInspection<string>.Complete>(result.Lines.Value);
        Assert.Contains(
            complete.Findings,
            finding => finding.Payload.Contains("~Sample()", StringComparison.Ordinal));
        Assert.DoesNotContain(
            complete.Findings,
            finding => finding.Payload.Contains("Preceding", StringComparison.Ordinal));
    }

    [Fact]
    public void FromContent_OrdinaryMethodNamedFinalize_NotTreatedAsDestructor()
    {
        // A non-destructor method may legally be named "Finalize". Its identity
        // (IsFinalizer == false) must govern the scan; a stray "~" continuation in
        // a multi-line default parameter must NOT truncate the signature.
        const string source = """
            class Sample
            {
                internal int Preceding;

                public int Finalize(int mask =
                    ~0)
                {
                    return mask;
                }
            }
            """;
        byte[] content = Encoding.UTF8.GetBytes(source);
        var result = AuthoredSourceAcquisition.FromContent(
            DestructorMapping(memberName: "Finalize", startLine: 7, endLine: 8, isFinalizer: false),
            Document(content),
            content,
            "Finalize",
            Subject);

        var complete = Assert.IsType<FindingInspection<string>.Complete>(result.Lines.Value);
        Assert.Contains(
            complete.Findings,
            finding => finding.Payload.Contains("public int Finalize(int mask =", StringComparison.Ordinal));
        Assert.DoesNotContain(
            complete.Findings,
            finding => finding.Payload.Contains("Preceding", StringComparison.Ordinal));
    }

    static MemberSourceObservation DestructorMapping(
        string memberName, int startLine, int endLine, bool isFinalizer)
        => new(
            new MemberAnchor(
                $"{memberName}~1234567890",
                $"M:Sample.{memberName}",
                "1234567890",
                "Sample",
                memberName),
            MetadataToken: 0x06000001,
            DocumentRowId: 1,
            CanonicalPath: "Sample.cs",
            OriginalPath: "/_/Sample.cs",
            ResolvedUrl: "https://example.test/Sample.cs",
            StartLine: startLine,
            EndLine: endLine,
            IsPrimaryDocument: true,
            IsFinalizer: isFinalizer);

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

    sealed class GatedStringHandler(string response) : HttpMessageHandler
    {
        readonly TaskCompletionSource<bool> _requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _requestCount;

        public Task RequestStarted => _requestStarted.Task;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public void Release() => _release.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(response),
            };
        }
    }
}
