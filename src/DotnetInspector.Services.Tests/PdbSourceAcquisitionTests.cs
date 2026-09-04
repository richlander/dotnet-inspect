using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Core;

using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.Services.Tests;

[Collection(CoreCacheCollection.Name)]
public class PdbSourceAcquisitionTests
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
    public async Task UnresolvedPortablePdbFailsMemberAndTypeInspections()
    {
        using SourceLinkService source = OpenSourceNeedingPdb();
        using var client = new HttpClient(new QueueHandler());
        var fetcher = new SourceFetcher(
            client,
            new InMemorySourceContentStore());
        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "DotnetInspector.Services.Tests",
                [nameof(PdbSourceAcquisitionTests)]));

        PdbMemberSourceInspection member =
            await PdbSourceAcquisition.AcquireMemberAsync(
                source,
                0x06000001,
                "M",
                Subject,
                fetcher,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        PdbTypeSourceInspection typeInspection =
            await PdbSourceAcquisition.AcquireTypeAsync(
                source,
                type.Name,
                Subject,
                fetcher,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Contains(
            "remains unresolved",
            Assert.IsType<FindingInspection<string>.Failed>(
                member.Lines.Value).Error.Reason);
        Assert.Equal(
            PdbMemberSourceOutcome.PortablePdbUnavailable,
            member.Outcome);
        Assert.Contains(
            "remains unresolved",
            Assert.IsType<FindingInspection<string>.Failed>(
                typeInspection.Lines.Value).Error.Reason);
    }

    [Fact]
    public async Task WindowsPdbIsNoApplicableInputForMemberAndType()
    {
        using SourceLinkService source = OpenSourceNeedingPdb();
        source.Context.LoadPdbFromStream(new MemoryStream(
            Encoding.ASCII.GetBytes(
                "Microsoft C/C++ MSF 7.00\r\n\u001ADS\0\0\0"),
            writable: false));
        Assert.True(source.Context.WindowsPdbDetected);
        using var client = new HttpClient(new QueueHandler());
        var fetcher = new SourceFetcher(
            client,
            new InMemorySourceContentStore());
        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "DotnetInspector.Services.Tests",
                [nameof(PdbSourceAcquisitionTests)]));

        PdbMemberSourceInspection member =
            await PdbSourceAcquisition.AcquireMemberAsync(
                source,
                0x06000001,
                "M",
                Subject,
                fetcher,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        PdbTypeSourceInspection typeInspection =
            await PdbSourceAcquisition.AcquireTypeAsync(
                source,
                type.Name,
                Subject,
                fetcher,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var memberAbsent =
            Assert.IsType<FindingInspection<string>.Absent>(
                member.Lines.Value);
        var typeAbsent =
            Assert.IsType<FindingInspection<string>.Absent>(
                typeInspection.Lines.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            memberAbsent.Kind);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            typeAbsent.Kind);
    }

    [Fact]
    public void FromContent_VerifiedSourceProducesCompleteLineCensus()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var result = PdbSourceAcquisition.FromContent(
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

        var result = PdbSourceAcquisition.FromContent(
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
        var result = PdbSourceAcquisition.FromContent(
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
            PdbSourceAcquisition.FromContent(
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

        var result = PdbSourceAcquisition.FromContent(
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
            PdbSourceAcquisition.FromContent(
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
            PdbSourceAcquisition.FromContent(
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
                PdbSourceAcquisition
                    .MaxPdbSourceLineCount));
        var mapping =
            new ILInspector.SourceLink.SourceLinkResolver
                .TypeSourceInfo(
                    "/_/Sample.cs",
                    "https://example.test/Sample.cs",
                    LineNumber: null,
                    GitHubBrowseUrl: null);

        PdbTypeSourceInspection result =
            PdbSourceAcquisition.FromTypeContent(
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
    }

    [Fact]
    public void VerifyChecksum_AcceptsLineEndingNormalization()
    {
        byte[] expected = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\n"));
        byte[] actual = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"));

        var verification = PdbSourceAcquisition.VerifyChecksum(
            Document(expected),
            actual);

        Assert.Equal(
            SourceChecksumVerification.LineEndingNormalized,
            verification);
    }

    [Fact]
    public async Task FetchVerifiedSourceText_PreservesLineEndingNormalizationEvidence()
    {
        byte[] expected = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\n"));
        byte[] actual = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"));
        var handler = new QueueHandler(actual);
        using var client = new HttpClient(handler);
        var fetcher = new SourceFetcher(
            client,
            new InMemorySourceContentStore());

        VerifiedSourceTextResult result =
            await PdbSourceAcquisition.FetchVerifiedSourceTextAsync(
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
    public void FromContent_MissingChecksumIsAbsentEvidence()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        var document = Document(content) with
        {
            ChecksumAlgorithm = null,
            Checksum = null,
        };

        var result = PdbSourceAcquisition.FromContent(
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
        CoreCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] invalid = Encoding.UTF8.GetBytes("invalid");
        byte[] expected = Encoding.UTF8.GetBytes(Source);
        CoreCache.Set(
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
            var fetcher = new SourceFetcher(client);
            var repaired = await fetcher.FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                cancellationToken);

            Assert.Equal(expected, repaired);
            Assert.Equal(1, handler.RequestCount);

            var cached = await new SourceFetcher(client).FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                cancellationToken);

            Assert.Equal(expected, cached);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, recursive: true);
        }
    }

    [Fact]
    public async Task FetchSourceBytes_RejectsRedirectOutsideAttributedOrigin()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        CoreCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] source = Encoding.UTF8.GetBytes(Source);
        var content = new TrackingContent(source);
        var handler = new RedirectHandler(
            content,
            "https://spsprodeus27.vssps.visualstudio.com/_signin?realm=dev.azure.com");
        using var client = new HttpClient(handler);
        var fetcher = new SourceFetcher(client);
        const string Url =
            "https://dev.azure.com/org/project/_apis/git/repositories/repo/items"
            + "?api-version=7.1&versionType=commit"
            + "&version=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&path=/A.cs";

        try
        {
            byte[]? result = await fetcher.FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(source),
                TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(0, content.ReadCount);
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
        var fetcher = new SourceFetcher(
            client,
            new InMemorySourceContentStore(),
            policy);

        byte[]? result = await fetcher.FetchVerifiedSourceBytesAsync(
            "https://localhost/Sample.cs",
            static _ => true,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(0, policy.ConfiguredRequests);
    }

    [Fact]
    public async Task FetchSourceBytes_IgnoresPreOriginValidationCache()
    {
        string cachePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-cache-{Guid.NewGuid():N}");
        CoreCache.Initialize("dotnet-inspect-test", cachePath);
        byte[] stale = "stale redirected body"u8.ToArray();
        byte[] expected = Encoding.UTF8.GetBytes(Source);
        const string Url = "https://example.test/A.cs";
        CoreCache.Set(
            "source-bytes-v1",
            Url,
            Convert.ToBase64String(stale),
            extension: "base64");
        var handler = new QueueHandler(expected);
        using var client = new HttpClient(handler);

        try
        {
            var fetcher = new SourceFetcher(client);
            byte[]? result = await fetcher.FetchVerifiedSourceBytesAsync(
                Url,
                bytes => bytes.Span.SequenceEqual(expected),
                TestContext.Current.CancellationToken);

            Assert.Equal(expected, result);
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
        var result = PdbSourceAcquisition.FromContent(
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
        var result = PdbSourceAcquisition.FromContent(
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

    [Fact]
    public void SelectMappedDocument_UsesDocumentRowWhenPathsAreDuplicated()
    {
        byte[] firstContent = "first"u8.ToArray();
        byte[] secondContent = "second"u8.ToArray();
        var first = Document(firstContent);
        var second = Document(secondContent) with { DocumentRowId = 2 };
        var mapping = Mapping() with { DocumentRowId = 2 };

        SourceDocumentObservation? selected =
            PdbSourceAcquisition.SelectMappedDocument(
                mapping,
                [first, second]);

        Assert.Same(second, selected);
    }

    [Fact]
    public void SelectMappedDocument_RejectsAMismatchedRowPathPair()
    {
        var mapping = Mapping() with { DocumentRowId = 2 };
        var document = Document("content"u8.ToArray()) with
        {
            DocumentRowId = 2,
            OriginalPath = "/_/Other.cs",
        };

        Assert.Null(PdbSourceAcquisition.SelectMappedDocument(
            mapping,
            [document]));
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

    static SourceLinkService OpenSourceNeedingPdb()
    {
        byte[] assemblyBytes = File.ReadAllBytes(
            typeof(PdbSourceAcquisitionTests).Assembly.Location);
        AssemblyReferenceIdentity identity;
        using (var stream = new MemoryStream(
                   assemblyBytes,
                   writable: false))
        using (var reader = new PEReader(stream))
        {
            identity = AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        }

        var assembly = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(
                assemblyBytes,
                writable: false),
            AssemblyResolutionProvenance.Local(
                "unresolved portable PDB test"));
        SourceLinkService source = SourceLinkService.Open(assembly);
        Assert.True(source.Context.NeedsPdb);
        return source;
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
        public bool FinalResponseUriIsReliable => true;
        public bool IsRequestAllowed(Uri requestUri) => false;
        public void ConfigureRequest(HttpRequestMessage request) =>
            ConfiguredRequests++;
    }
}
