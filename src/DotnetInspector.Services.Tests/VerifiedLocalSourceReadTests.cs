using System.Security.Cryptography;
using System.Text;

using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class VerifiedLocalSourceReadTests
{
    const string Source = """
        class Sample
        {
            public int M()
            {
                return 1;
            }
        }
        """;

    static string WriteTemp(string extension, byte[] content)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-src-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void ReturnsBytes_WhenChecksumMatches()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string path = WriteTemp(".cs", content);
        try
        {
            byte[]? result = AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
                path, "SHA256", SHA256.HashData(content));

            Assert.NotNull(result);
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNull_WhenChecksumMismatches()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string path = WriteTemp(".cs", content);
        try
        {
            // The on-disk bytes do not match the recorded hash: the read must be refused so the
            // caller falls back to the remote URL rather than surfacing unverified content.
            byte[]? result = AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
                path, "SHA256", SHA256.HashData(Encoding.UTF8.GetBytes(Source + "tampered")));

            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNull_ForNonCompilerSourceExtension_EvenWhenChecksumMatches()
    {
        // A malicious PDB could name a non-source local file (e.g. a secret). The extension gate
        // plus the checksum gate keep the reader from disclosing arbitrary local files.
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string path = WriteTemp(".txt", content);
        try
        {
            byte[]? result = AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
                path, "SHA256", SHA256.HashData(content));

            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNull_WhenChecksumAbsent()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string path = WriteTemp(".cs", content);
        try
        {
            Assert.Null(AuthoredSourceAcquisition.TryReadVerifiedLocalSource(path, "SHA256", null));
            Assert.Null(AuthoredSourceAcquisition.TryReadVerifiedLocalSource(path, null, SHA256.HashData(content)));
            Assert.Null(AuthoredSourceAcquisition.TryReadVerifiedLocalSource(path, "SHA256", []));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNull_WhenFileAbsent()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.cs");

        Assert.Null(AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
            path, "SHA256", SHA256.HashData(content)));
    }

    [Fact]
    public void AcceptsCrlfNormalizedChecksum()
    {
        // The recorded hash is over LF content; the on-disk file is CRLF. Line-ending
        // normalization must still authenticate it.
        byte[] lf = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\n"));
        byte[] crlf = Encoding.UTF8.GetBytes(Source.ReplaceLineEndings("\r\n"));
        string path = WriteTemp(".cs", crlf);
        try
        {
            byte[]? result = AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
                path, "SHA256", SHA256.HashData(lf));

            Assert.NotNull(result);
            Assert.Equal(crlf, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ObservationOverload_ReadsOriginalPath_WhenVerified()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);
        string path = WriteTemp(".cs", content);
        try
        {
            var document = new SourceDocumentObservation(
                CanonicalPath: "Sample.cs",
                OriginalPath: path,
                DocumentRowId: 1,
                Storage: SourceDocumentStorage.SourceLink,
                ResolvedUrl: "https://example.test/Sample.cs",
                ChecksumAlgorithm: "SHA256",
                Checksum: Convert.ToHexString(SHA256.HashData(content)));

            byte[]? result = AuthoredSourceAcquisition.TryReadVerifiedLocalSource(document);

            Assert.NotNull(result);
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyChecksum_Overload_ReportsExactAndMismatch()
    {
        byte[] content = Encoding.UTF8.GetBytes(Source);

        Assert.Equal(
            SourceChecksumVerification.Exact,
            AuthoredSourceAcquisition.VerifyChecksum("SHA256", SHA256.HashData(content), content));
        Assert.Equal(
            SourceChecksumVerification.Mismatch,
            AuthoredSourceAcquisition.VerifyChecksum("SHA256", SHA256.HashData(content), Encoding.UTF8.GetBytes("other")));
        Assert.Equal(
            SourceChecksumVerification.Unavailable,
            AuthoredSourceAcquisition.VerifyChecksum(null, SHA256.HashData(content), content));
    }

    [Theory]
    [InlineData(@"\\attacker\share\Sample.cs")]
    [InlineData(@"\\?\C:\Sample.cs")]
    [InlineData(@"\\.\C:\Sample.cs")]
    [InlineData("relative/Sample.cs")]
    public void ReturnsNull_ForNonLocalOrRelativePath_WithoutIo(string path)
    {
        // The document path originates in an untrusted PDB. A UNC share or Win32 device path must
        // be rejected before any filesystem access so it cannot trigger outbound SMB/network I/O,
        // and a relative path is never honored — independent of the (here matching) checksum.
        byte[] content = Encoding.UTF8.GetBytes(Source);
        Assert.Null(AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
            path, "SHA256", SHA256.HashData(content)));
    }

    [Fact]
    public void DecodeSourceText_HonorsByteOrderMarkEncoding()
    {
        // Non-ASCII content makes the encoding observable. A checksum-verified local file may be
        // UTF-8 (with or without BOM) or UTF-16; each must decode correctly, not as raw UTF-8.
        const string text = "héllo wörld";
        Assert.Equal(text, AuthoredSourceAcquisition.DecodeSourceText(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, AuthoredSourceAcquisition.DecodeSourceText(
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]));
        Assert.Equal(text, AuthoredSourceAcquisition.DecodeSourceText(
            [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]));
        Assert.Equal(text, AuthoredSourceAcquisition.DecodeSourceText(
            [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)]));
    }

    [Fact]
    public void Utf16LocalFile_VerifiesAgainstRawBytes_AndDecodesToText()
    {
        // csc records the checksum over the raw file bytes regardless of encoding, so a UTF-16
        // source verifies against those bytes and must then decode as UTF-16 (Finding 2 regression).
        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Source)];
        string path = WriteTemp(".cs", utf16);
        try
        {
            byte[]? result = AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
                path, "SHA256", SHA256.HashData(utf16));

            Assert.NotNull(result);
            Assert.Equal(utf16, result);
            Assert.Equal(
                Source.ReplaceLineEndings("\n"),
                AuthoredSourceAcquisition.DecodeSourceText(result).ReplaceLineEndings("\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
