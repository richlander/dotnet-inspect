using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

// PR-fast: bounded documents and one exact member from this repository's built assembly.
public class SourceLinkContentProducerTests
{
    [Fact]
    public void SuppliedPdbRefreshesPathlessMappingAndProducesVerifiedRepositorySource()
    {
        string assemblyPath = typeof(SourceLinkIndexCacheTests).Assembly.Location;
        byte[] image = File.ReadAllBytes(assemblyPath);
        var assemblyStream = new MemoryStream(image, writable: false);
        var pdbStream = new MemoryStream(
            File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb")), writable: false);
        using (SourceLinkService source = SourceLinkService.OpenMetadataOnly(
            Descriptor(image, () => assemblyStream),
            log: null,
            cache: null,
            readLimits: new SourceLinkReadLimits(1_000_000, 1_000_000, 100)))
        {
            Assert.False(source.HasPdb);
            Assert.Empty(source.GetTrackedFiles());
            source.LoadPdbFromStream(pdbStream, throwOnReadFailure: true);
            Assert.False(pdbStream.CanRead);
            Assert.True(source.HasPdb);

            SourceDocument document = Assert.Single(source.GetTrackedFiles(),
                value => value.FilePath.EndsWith(
                    "/SourceLinkIndexCacheTests.cs", StringComparison.Ordinal)
                    || value.FilePath.EndsWith(
                        "\\SourceLinkIndexCacheTests.cs", StringComparison.Ordinal));
            var method = typeof(SourceLinkIndexCacheTests).GetMethod(
                nameof(SourceLinkIndexCacheTests.PdbLoadedThroughContext_InvalidatesServiceState))!;
            var mapping = source.ResolveMethodSource(
                typeof(SourceLinkIndexCacheTests).FullName!,
                method.Name, 0, metadataToken: method.MetadataToken);
            Assert.NotNull(mapping);

            byte[] content = File.ReadAllBytes(Path.Combine(
                RepositoryRoot(), "tests", "ILInspector.Metadata.Tests", "SourceLinkIndexCacheTests.cs"));
            VerifiedSourceTextResult result = SourceLinkService.VerifySourceContent(
                document.ChecksumAlgorithm, document.Checksum, content);
            Assert.True(result.IsVerified);
            Assert.Null(result.Failure);
            Assert.Equal(SourceChecksumVerification.Exact, result.ChecksumVerification);
            Assert.Contains(method.Name, result.Text, StringComparison.Ordinal);
        }
        Assert.False(assemblyStream.CanRead);
    }

    [Fact]
    public void SuppliedPdbRetainsReadLimits()
    {
        string assemblyPath = typeof(SourceLinkIndexCacheTests).Assembly.Location;
        byte[] image = File.ReadAllBytes(assemblyPath);
        using SourceLinkService source = SourceLinkService.OpenMetadataOnly(
            Descriptor(image, () => new MemoryStream(image, writable: false)),
            log: null,
            cache: null,
            readLimits: new SourceLinkReadLimits(1_000_000, 1, 100));
        source.LoadPdbFromStream(new MemoryStream(
            File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb")), writable: false));

        Assert.True(source.HasPdb);
        SourceLinkMapAudit map = source.InspectSourceLinkMap();
        Assert.Equal(SourceLinkMapLimitKind.EncodedBytes, map.LimitKind);
        Assert.True(map.EncodedBytes > 1);
        Assert.Empty(map.Entries);
    }

    [Fact]
    public void SuppliedMismatchedPdbCannotProduceSourceDocuments()
    {
        byte[] image = File.ReadAllBytes(typeof(SourceLinkIndexCacheTests).Assembly.Location);
        using SourceLinkService source = SourceLinkService.OpenMetadataOnly(
            Descriptor(image, () => new MemoryStream(image, writable: false)));
        var pdb = new MemoryStream(File.ReadAllBytes(
            Path.ChangeExtension(typeof(PdbContext).Assembly.Location, ".pdb")), writable: false);
        source.LoadPdbFromStream(pdb);

        Assert.False(pdb.CanRead);
        Assert.False(source.HasPdb);
        Assert.IsType<FindingInspection<SourceDocumentObservation>.Failed>(
            SourceLinkFindings.InspectSourceDocuments(source, new("content", "content")).Value);
    }

    [Fact]
    public void SuppliedPdbReadFailureRemainsVisibleAndReleasesInput()
    {
        byte[] image = File.ReadAllBytes(typeof(SourceLinkIndexCacheTests).Assembly.Location);
        using SourceLinkService source = SourceLinkService.OpenMetadataOnly(
            Descriptor(image, () => new MemoryStream(image, writable: false)));
        var pdb = new MemoryStream([1, 2], writable: false);

        Assert.Throws<EndOfStreamException>(
            () => source.LoadPdbFromStream(pdb, throwOnReadFailure: true));
        Assert.False(pdb.CanRead);
        Assert.False(source.HasPdb);
    }

    [Theory]
    [InlineData("SHA256", SourceChecksumVerification.Exact)]
    [InlineData("SHA1", SourceChecksumVerification.Exact)]
    [InlineData(null, SourceChecksumVerification.Unavailable)]
    [InlineData("unsupported", SourceChecksumVerification.Unsupported)]
    public void SourceContentRetainsChecksumVerdict(
        string? algorithm,
        SourceChecksumVerification expected)
    {
        byte[] content = Encoding.UTF8.GetBytes("class Example {}\n");
        byte[] checksum = algorithm == "SHA1" ? SHA1.HashData(content) : SHA256.HashData(content);

        VerifiedSourceTextResult result =
            SourceLinkService.VerifySourceContent(algorithm, checksum, content);

        Assert.Equal(expected, result.ChecksumVerification);
        Assert.Equal(expected == SourceChecksumVerification.Exact, result.IsVerified);
        if (result.IsVerified)
        {
            Assert.Equal(Encoding.UTF8.GetString(content), result.Text);
            Assert.Null(result.Failure);
        }
        else
        {
            Assert.Null(result.Text);
            Assert.NotEmpty(result.Failure!);
        }
    }

    [Fact]
    public void SourceContentRetainsNormalizationAndRefusesMismatch()
    {
        byte[] expected = Encoding.UTF8.GetBytes("first\nsecond\n");
        byte[] checksum = SHA256.HashData(expected);
        VerifiedSourceTextResult normalized = SourceLinkService.VerifySourceContent(
            "SHA256", checksum, Encoding.UTF8.GetBytes("first\r\nsecond\r\n"));
        Assert.True(normalized.IsVerified);
        Assert.Equal(SourceChecksumVerification.LineEndingNormalized, normalized.ChecksumVerification);
        Assert.Equal("first\r\nsecond\r\n", normalized.Text);

        VerifiedSourceTextResult mismatch = SourceLinkService.VerifySourceContent(
            "SHA256", checksum, Encoding.UTF8.GetBytes("different"));
        Assert.Equal(SourceChecksumVerification.Mismatch, mismatch.ChecksumVerification);
        Assert.False(mismatch.IsVerified);
        Assert.Null(mismatch.Text);
        Assert.NotEmpty(mismatch.Failure!);
    }

    [Fact]
    public void VerifiedSourceRetainsBomDecodingAndEmptyContent()
    {
        const string Text = "class Example { const string Name = \"caf\u00e9\"; }";
        foreach (Encoding encoding in new Encoding[]
            { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 })
        {
            byte[] content = [.. encoding.GetPreamble(), .. encoding.GetBytes(Text)];
            VerifiedSourceTextResult result = SourceLinkService.VerifySourceContent(
                "SHA256", SHA256.HashData(content), content);
            Assert.Equal(SourceChecksumVerification.Exact, result.ChecksumVerification);
            Assert.Equal(Text, result.Text);
        }
        Assert.Equal("", SourceLinkService.VerifySourceContent("SHA256", SHA256.HashData([]), []).Text);
    }

    static ResolvedAssemblyReference Descriptor(byte[] image, Func<Stream> openRead)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        return ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
            path: null, openRead, AssemblyResolutionProvenance.Local("content-producer"));
    }

    static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate repository source.");
    }
}
