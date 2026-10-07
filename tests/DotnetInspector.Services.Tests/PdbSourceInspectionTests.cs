using DotnetInspector.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;


using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;
using DotnetInspector.SourceHouse;

namespace DotnetInspector.Services.Tests;

[Collection(PersistentCacheCollection.Name)]
public class PdbSourceInspectionTests
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
        var result = PdbSourceInspectionProjection.FromMemberContent(
            Mapping(),
            Document(content),
            content,
            "M",
            Subject);

        var complete = Assert.IsType<FindingInspection<string>.Complete>(
            result.Lines.Value);
        Assert.Equal(
            PdbMemberSourceOutcome.Complete,
            result.Outcome);
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

    [Theory]
    [InlineData(
        "https://raw.githubusercontent.com/example/repository/0123456789abcdef0123456789abcdef01234567/src/Widget.cs",
        "https://github.com/example/repository/blob/0123456789abcdef0123456789abcdef01234567/src/Widget.cs")]
    [InlineData(
        "https://raw.githubusercontent.com/example/repository/v1/src/Widget.cs",
        null)]
    [InlineData("https://example.test/src/Widget.cs", null)]
    public void Inspection_ProjectsDetachedSourceLocation(
        string resolvedUrl,
        string? expectedBrowseUrl)
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        SourceDocumentObservation document =
            Document(content) with { ResolvedUrl = resolvedUrl };

        PdbMemberSourceInspection result =
            PdbSourceInspectionProjection.FromMemberContent(
                Mapping(),
                document,
                content,
                "M",
                Subject);

        Assert.Equal(resolvedUrl, result.ResolvedUrl);
        Assert.Equal(expectedBrowseUrl, result.VerifiedBrowseUrl);
    }

    [Fact]
    public void Inspection_BrowseUrlRequiresVerifiedChecksum()
    {
        const string resolvedUrl =
            "https://raw.githubusercontent.com/example/repository/"
            + "0123456789abcdef0123456789abcdef01234567/src/Widget.cs";
        byte[] content = Encoding.UTF8.GetBytes(Source);
        SourceDocumentObservation document =
            Document(content) with { ResolvedUrl = resolvedUrl };
        PdbMemberSourceInspection result =
            PdbSourceInspectionProjection.FromMemberContent(
                Mapping(),
                document,
                content,
                "M",
                Subject);

        Assert.NotNull(result.VerifiedBrowseUrl);
        Assert.NotNull(
            (result with
            {
                ChecksumVerification =
                    SourceChecksumVerification.LineEndingNormalized,
            }).VerifiedBrowseUrl);
        Assert.Null(
            (result with
            {
                ChecksumVerification = SourceChecksumVerification.Mismatch,
            }).VerifiedBrowseUrl);
    }

    [Fact]
    public void FromContent_UsesSequencePointEvidenceToSelectAConditionalMember()
    {
        const string source = """
            class Sample
            {
            #if FIRST
                public int Dead() => 1;
            #else
                public int Live() => 2;
            #endif
            }
            """;
        byte[] content = Encoding.UTF8.GetBytes(source);
        var mapping = Mapping() with
        {
            StartLine = 6,
            EndLine = 6,
            SequencePointStartLines = [6],
        };

        var result = PdbSourceInspectionProjection.FromMemberContent(
            mapping,
            Document(content),
            content,
            "Live",
            Subject);

        Assert.Equal("public int Live() => 2;", result.Text);
        Assert.IsType<FindingInspection<string>.Complete>(result.Lines.Value);
    }

    [Fact]
    public void FromContent_MismatchedChecksumProducesFailedInspection()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var result = PdbSourceInspectionProjection.FromMemberContent(
            Mapping(),
            Document(Encoding.UTF8.GetBytes(Source + "changed")),
            content,
            "M",
            Subject);

        var failed = Assert.IsType<FindingInspection<string>.Failed>(
            result.Lines.Value);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumMismatch,
            result.Outcome);
        Assert.Equal(SourceChecksumVerification.Mismatch, result.ChecksumVerification);
        Assert.Contains("does not match", failed.Error.Reason);
    }

    [Fact]
    public void FromContent_UnsupportedChecksumPreservesTypedOutcome()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        SourceDocumentObservation document =
            Document(content) with
            {
                ChecksumAlgorithm = "MD5",
            };

        PdbMemberSourceInspection result =
            PdbSourceInspectionProjection.FromMemberContent(
                Mapping(),
                document,
                content,
                "M",
                Subject);

        Assert.IsType<FindingInspection<string>.Failed>(
            result.Lines.Value);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumUnsupported,
            result.Outcome);
        Assert.Equal(
            SourceChecksumVerification.Unsupported,
            result.ChecksumVerification);
    }

    [Fact]
    public void FromContent_TokenDenseSourceProducesVisibleFailedEvidence()
    {
        string source = "class Sample { public void M() { "
            + new string(';', 500_001)
            + " } }";
        byte[] content = Encoding.UTF8.GetBytes(source);

        var result = PdbSourceInspectionProjection.FromMemberContent(
            Mapping(),
            Document(content),
            content,
            "M",
            Subject);

        var failed = Assert.IsType<FindingInspection<string>.Failed>(result.Lines.Value);
        Assert.Contains("lexical complexity limit", failed.Error.Reason, StringComparison.Ordinal);
        Assert.Equal(
            PdbMemberSourceOutcome.SourceTooComplex,
            result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void FromContent_InvalidCoordinatesPreserveTypedOutcome()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        MemberSourceObservation mapping =
            Mapping() with
            {
                SequencePointStartLines = [100],
            };

        PdbMemberSourceInspection result =
            PdbSourceInspectionProjection.FromMemberContent(
                mapping,
                Document(content),
                content,
                "M",
                Subject);

        Assert.IsType<FindingInspection<string>.Failed>(
            result.Lines.Value);
        Assert.Equal(
            PdbMemberSourceOutcome.InvalidSequencePointCoordinates,
            result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void FromContent_NonDeclarationRangePreservesTypedOutcome()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        MemberSourceObservation mapping =
            Mapping() with
            {
                StartLine = 1,
                EndLine = 1,
                SequencePointStartLines = [1],
            };

        PdbMemberSourceInspection result =
            PdbSourceInspectionProjection.FromMemberContent(
                mapping,
                Document(content),
                content,
                "M",
                Subject);

        Assert.IsType<FindingInspection<string>.Absent>(
            result.Lines.Value);
        Assert.Equal(
            PdbMemberSourceOutcome.NoVouchedDeclaration,
            result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void FromTypeContent_NewlineDenseSourceProducesVisibleFailedEvidence()
    {
        byte[] content = Encoding.UTF8.GetBytes(
            new string(
                '\n',
                PdbSourceInspectionProjection
                    .MaxPdbSourceLineCount));
        var mapping = TypeMapping();

        PdbTypeSourceInspection result =
            PdbSourceInspectionProjection.FromTypeContent(
                mapping,
                Document(content),
                content,
                Subject);

        var failed =
            Assert.IsType<FindingInspection<string>.Failed>(
                result.Lines.Value);
        Assert.Contains(
            "finding complexity limit",
            failed.Error.Reason,
            StringComparison.Ordinal);
        Assert.Null(result.Text);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            result.ChecksumVerification);
        Assert.Equal(
            PdbTypeSourceOutcome.SourceTooComplex,
            result.Outcome);
    }

    [Fact]
    public void MemberAcquisitionFailed_PreservesTypedFailure()
    {
        PdbMemberSourceInspection result =
            PdbSourceInspectionProjection.MemberAcquisitionFailed(
                Subject,
                new IOException("source fetch failed"));

        Assert.Equal(
            PdbMemberSourceOutcome.PortablePdbAcquisitionFailed,
            result.Outcome);
        var failure =
            Assert.IsType<FindingInspection<string>.Failed>(
                result.Lines.Value);
        Assert.Contains(
            "Portable PDB acquisition failed: source fetch failed",
            failure.Error.Reason);
    }

    [Fact]
    public void FromVerifiedTypeContent_PreservesAcceptedVerification()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        PdbTypeSourceInspection result =
            PdbSourceInspectionProjection.FromVerifiedTypeContent(
                TypeMapping(),
                Document(content),
                Source,
                SourceChecksumVerification.LineEndingNormalized,
                Subject);

        Assert.Equal(PdbTypeSourceOutcome.Complete, result.Outcome);
        Assert.Equal(Source, result.Text);
        Assert.Equal(
            SourceChecksumVerification.LineEndingNormalized,
            result.ChecksumVerification);
    }

    [Theory]
    [InlineData(SourceChecksumVerification.Unavailable)]
    [InlineData(SourceChecksumVerification.Unsupported)]
    [InlineData(SourceChecksumVerification.Mismatch)]
    public void FromVerifiedTypeContent_RejectsUnacceptedVerification(
        SourceChecksumVerification verification)
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdbSourceInspectionProjection.FromVerifiedTypeContent(
                TypeMapping(),
                Document(content),
                Source,
                verification,
                Subject));
    }

    [Fact]
    public void FromTypeContent_ChecksumFailurePreservesTypedOutcome()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var mapping = TypeMapping();

        PdbTypeSourceInspection mismatch =
            PdbSourceInspectionProjection.FromTypeContent(
                mapping,
                Document(Encoding.UTF8.GetBytes(Source + "changed")),
                content,
                Subject);
        PdbTypeSourceInspection unsupported =
            PdbSourceInspectionProjection.FromTypeContent(
                mapping,
                Document(content) with { ChecksumAlgorithm = "MD5" },
                content,
                Subject);

        Assert.Equal(PdbTypeSourceOutcome.ChecksumMismatch, mismatch.Outcome);
        Assert.Equal(PdbTypeSourceOutcome.ChecksumUnsupported, unsupported.Outcome);
    }

    [Fact]
    public void NonCompleteTypeInspection_DefaultsToUnspecifiedOutcome()
    {
        var result = new PdbTypeSourceInspection(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                "Synthetic legacy result."),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);

        Assert.Equal(PdbTypeSourceOutcome.Unspecified, result.Outcome);
    }

    [Fact]
    public void VerifyChecksum_AcceptsLineEndingNormalization()
    {
        byte[] expected = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\n"));
        byte[] actual = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"));

        var verification = SourceLinkService.VerifyChecksum(
            Document(expected),
            actual);

        Assert.Equal(
            SourceChecksumVerification.LineEndingNormalized,
            verification);
    }

    [Fact]
    public async Task VerifiedSourceTextFetch_PreservesLineEndingNormalizationEvidence()
    {
        byte[] expected = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\n"));
        byte[] actual = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"));
        var handler = new QueueHandler(actual);
        using var client = new HttpClient(handler);
        var fetcher = new SourceFetch(
            client,
            new InMemorySourceContentStore());

        VerifiedSourceTextResult result =
            await VerifiedSourceTextFetch.FetchAsync(
                fetcher,
                $"https://example.test/{Guid.NewGuid():N}/Sample.cs",
                "SHA256",
                SHA256.HashData(expected),
                TestContext.Current.CancellationToken);

        Assert.NotNull(result.Text);
        Assert.Null(result.Failure);
        Assert.Equal(
            SourceChecksumVerification.LineEndingNormalized,
            result.ChecksumVerification);
    }

    [Fact]
    public async Task VerifiedSourceTextFetch_MissingChecksumDoesNotDispatch()
    {
        var handler = new QueueHandler(Encoding.UTF8.GetBytes(Source));
        using var client = new HttpClient(handler);
        var fetcher = new SourceFetch(
            client,
            new InMemorySourceContentStore());

        VerifiedSourceTextResult result =
            await VerifiedSourceTextFetch.FetchAsync(
                fetcher,
                $"https://example.test/{Guid.NewGuid():N}/Sample.cs",
                checksumAlgorithm: null,
                checksum: null,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Text);
        Assert.Equal(
            "The portable PDB does not provide a usable source checksum.",
            result.Failure);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task VerifiedSourceTextFetch_MapsRejectedDestination()
    {
        var handler = new QueueHandler(Encoding.UTF8.GetBytes(Source));
        using var client = new HttpClient(handler);
        var policy = new RejectingSourceFetchPolicy();
        var fetcher = new SourceFetch(
            client,
            new InMemorySourceContentStore(),
            policy);

        VerifiedSourceTextResult result =
            await VerifiedSourceTextFetch.FetchAsync(
                fetcher,
                "https://localhost/Sample.cs",
                "SHA256",
                SHA256.HashData(Encoding.UTF8.GetBytes(Source)),
                TestContext.Current.CancellationToken);

        Assert.Null(result.Text);
        Assert.Equal(
            "The host does not authorize this SourceLink destination.",
            result.Failure);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(0, policy.ConfiguredRequests);
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

        var result = PdbSourceInspectionProjection.FromMemberContent(
            Mapping(),
            document,
            content,
            "M",
            Subject);

        Assert.IsType<FindingInspection<string>.Absent>(result.Lines.Value);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumUnavailable,
            result.Outcome);
        Assert.Equal(
            SourceChecksumVerification.Unavailable,
            result.ChecksumVerification);
        Assert.NotNull(result.Mapping);
        Assert.NotNull(result.Document);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task FetchVerifiedSourceBytes_InvalidCacheRetriesAndRepairsFromNetwork()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        PersistentCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] invalid = Encoding.UTF8.GetBytes("invalid");
        byte[] expected = Encoding.UTF8.GetBytes(Source);
        PersistentCache.Set(
            "source-bytes-v2",
            "https://example.test/Sample.cs",
            Convert.ToBase64String(invalid),
            extension: "base64");
        var handler = new QueueHandler(expected);
        using var client = new HttpClient(handler);
        const string Url = "https://example.test/Sample.cs";

        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var fetcher = new SourceFetch(client);
            FetchSourceResult repaired =
                await fetcher.FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                cancellationToken);

            Assert.Equal(
                expected,
                Assert.IsType<FetchSourceResult.Success>(repaired).Content);
            Assert.Equal(1, handler.RequestCount);

            FetchSourceResult cached =
                await new SourceFetch(client).FetchVerifiedSourceBytesAsync(
                    Url,
                    bytes => bytes.Span.SequenceEqual(expected),
                    cancellationToken);

            Assert.Equal(
                expected,
                Assert.IsType<FetchSourceResult.Success>(cached).Content);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    [Fact]
    public async Task FetchSourceBytes_AcceptsChecksumVerifiedBodyAfterRedirect()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        PersistentCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] source = Encoding.UTF8.GetBytes(Source);
        var content = new TrackingContent(source);
        var handler = new RedirectHandler(
            content,
            "https://spsprodeus27.vssps.visualstudio.com/_signin?realm=dev.azure.com");
        using var client = new HttpClient(handler);
        var fetcher = new SourceFetch(client);
        const string Url =
            "https://dev.azure.com/org/project/_apis/git/repositories/repo/items"
            + "?api-version=7.1&versionType=commit"
            + "&version=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&path=/A.cs";

        try
        {
            FetchSourceResult result =
                await fetcher.FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(source),
                TestContext.Current.CancellationToken);

            Assert.Equal(
                source,
                Assert.IsType<FetchSourceResult.Success>(result).Content);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(1, content.ReadCount);
        }
        finally
        {
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    [Fact]
    public async Task FetchSourceBytes_PolicyRejectsDestinationBeforeDispatch()
    {
        var handler = new QueueHandler(Encoding.UTF8.GetBytes(Source));
        using var client = new HttpClient(handler);
        var policy = new RejectingSourceFetchPolicy();
        var fetcher = new SourceFetch(
            client,
            new InMemorySourceContentStore(),
            policy);

        FetchSourceResult result = await fetcher.FetchVerifiedSourceBytesAsync(
            "https://localhost/Sample.cs",
            static _ => true,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            SourceError.RequestNotAuthorized,
            Assert.IsType<FetchSourceResult.Failure>(result).Error);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(0, policy.ConfiguredRequests);
    }

    [Fact]
    public async Task FetchSourceBytes_IgnoresPreOriginValidationCache()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        PersistentCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] stale = "stale redirected body"u8.ToArray();
        byte[] expected = Encoding.UTF8.GetBytes(Source);
        const string Url = "https://example.test/A.cs";
        PersistentCache.Set(
            "source-bytes-v1",
            Url,
            Convert.ToBase64String(stale),
            extension: "base64");
        var handler = new QueueHandler(expected);
        using var client = new HttpClient(handler);

        try
        {
            var fetcher = new SourceFetch(client);
            FetchSourceResult result =
                await fetcher.FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                TestContext.Current.CancellationToken);

            Assert.Equal(
                expected,
                Assert.IsType<FetchSourceResult.Success>(result).Content);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
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
        var result = PdbSourceInspectionProjection.FromMemberContent(
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
        var result = PdbSourceInspectionProjection.FromMemberContent(
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

    static SourceLinkResolver.TypeSourceInfo TypeMapping() =>
        new(
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("Example", ["Sample"]))
                .Name,
            [
                new(
                    "/_/Sample.cs",
                    "https://example.test/Sample.cs",
                    "https://example.test/browse/Sample.cs",
                    SourceLinkResolver.SourceResolutionMethod.SourceLink,
                    Checksum: SHA256.HashData(Encoding.UTF8.GetBytes(Source)),
                    ChecksumAlgorithm: "SHA256"),
            ]);

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the dotnet-inspect repository root.");
    }

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
                RequestMessage = request,
            });
        }
    }

    sealed class RedirectHandler(HttpContent response, string finalUrl) : HttpMessageHandler
    {
        int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = response,
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl),
            });
        }
    }

    sealed class TrackingContent(byte[] content) : HttpContent
    {
        int _readCount;

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

    sealed class RejectingSourceFetchPolicy : ISourceFetchPolicy
    {
        public int ConfiguredRequests { get; private set; }
        public bool IsRequestAllowed(Uri requestUri) => false;
        public void ConfigureRequest(HttpRequestMessage request) =>
            ConfiguredRequests++;
    }
}
