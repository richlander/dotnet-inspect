using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Integration tests for the in-process SignatureVerifier.
/// Verifies committed signed packages without downloading test inputs.
/// </summary>
public class SignatureVerifierTests
{
    [Fact]
    public void Verify_AuthorSignedPackage_ExtractsPublisher()
    {
        var result = SignatureVerifier.Verify(PackageFixture("newtonsoft.json.13.0.3.nupkg"));

        Assert.NotNull(result);
        Assert.Equal("Json.NET (.NET Foundation)", result.Publisher);
        Assert.True(result.AuthorVerified);
        Assert.True(result.RepositoryVerified);
        Assert.Contains("NuGet.org", result.Repository, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.IsUnsigned);
        Assert.Null(result.StatusMessage);
    }

    [Fact]
    public void FromNuGetResult_AuthorOnlySignatureDoesNotClaimRepositoryVerification()
    {
        var source = new NuGetFetch.SignatureVerificationResult(
            SignatureStatus.Valid,
            Reason: null)
        {
            SignatureType = SignatureType.Author,
            Publisher = "Author",
            PackageContentVerified = true,
        };

        SignatureVerificationResult result = SignatureVerifier.FromNuGetResult(source);

        Assert.True(result.AuthorVerified);
        Assert.Equal("Author", result.Publisher);
        Assert.False(result.RepositoryVerified);
        Assert.Null(result.Repository);
    }

    [Fact]
    public void FromNuGetResult_AuthorAndRepositorySignaturesPreserveBothIdentities()
    {
        var source = new NuGetFetch.SignatureVerificationResult(
            SignatureStatus.Valid,
            Reason: null)
        {
            SignatureType = SignatureType.Author,
            Publisher = "Author",
            PackageContentVerified = true,
            CounterSignature = new NuGetFetch.SignatureVerificationResult(
                SignatureStatus.Valid,
                Reason: null)
            {
                SignatureType = SignatureType.Repository,
                Publisher = "Repository",
                PackageContentVerified = true,
            },
        };

        SignatureVerificationResult result = SignatureVerifier.FromNuGetResult(source);

        Assert.True(result.AuthorVerified);
        Assert.Equal("Author", result.Publisher);
        Assert.True(result.RepositoryVerified);
        Assert.Equal("Repository", result.Repository);
    }

    [Fact]
    public void FromNuGetResult_RepositorySignatureDoesNotClaimAuthorVerification()
    {
        var source = new NuGetFetch.SignatureVerificationResult(
            SignatureStatus.Valid,
            Reason: null)
        {
            SignatureType = SignatureType.Repository,
            Publisher = "Repository",
            PackageContentVerified = true,
        };

        SignatureVerificationResult result = SignatureVerifier.FromNuGetResult(source);

        Assert.False(result.AuthorVerified);
        Assert.Null(result.Publisher);
        Assert.True(result.RepositoryVerified);
        Assert.Equal("Repository", result.Repository);
    }

    [Fact]
    public void FromNuGetResult_UnboundSignatureDoesNotEstablishTrust()
    {
        var source = new NuGetFetch.SignatureVerificationResult(
            SignatureStatus.Valid,
            Reason: null)
        {
            SignatureType = SignatureType.Author,
            Publisher = "Author",
        };

        SignatureVerificationResult result = SignatureVerifier.FromNuGetResult(source);

        Assert.False(result.AuthorVerified);
        Assert.False(result.RepositoryVerified);
        Assert.Contains("content hash", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_MicrosoftPackage_ExtractsPublisher()
    {
        var result = SignatureVerifier.Verify(PackageFixture("system.text.json.9.0.4.nupkg"));

        Assert.NotNull(result);
        Assert.NotNull(result.Publisher);
        Assert.Contains("Microsoft", result.Publisher);
        Assert.True(result.AuthorVerified);
        Assert.True(result.RepositoryVerified);
    }

    [Fact]
    public void Verify_UnsignedPackage_ReturnsUnsigned()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("test.nuspec");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<package />");
            }

            var result = SignatureVerifier.Verify(tempPath);

            Assert.NotNull(result);
            Assert.True(result.IsUnsigned);
            Assert.Equal("Package is not signed", result.StatusMessage);
            Assert.Null(result.Publisher);
            Assert.False(result.AuthorVerified);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Verify_NonExistentFile_ReturnsNull()
    {
        var result = SignatureVerifier.Verify("/nonexistent/path.nupkg");
        Assert.Null(result);
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsFailure()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"tampered-{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspec = archive.CreateEntry("test.nuspec");
                using (var writer = new StreamWriter(nuspec.Open()))
                    writer.Write("<package />");

                var sig = archive.CreateEntry(".signature.p7s");
                using (var stream = sig.Open())
                    stream.Write(new byte[] { 0x30, 0x00 });
            }

            var result = SignatureVerifier.Verify(tempPath);

            Assert.NotNull(result);
            Assert.False(result.IsUnsigned);
            Assert.NotNull(result.StatusMessage);
            Assert.Contains("Verification failed", result.StatusMessage);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Verify_ModifiedSignedPackage_ReturnsFailure()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"modified-signed-{Guid.NewGuid():N}.nupkg");
        try
        {
            File.Copy(PackageFixture("newtonsoft.json.13.0.3.nupkg"), tempPath);
            using (var archive = System.IO.Compression.ZipFile.Open(tempPath, System.IO.Compression.ZipArchiveMode.Update))
            {
                var nuspec = archive.GetEntry("Newtonsoft.Json.nuspec");
                Assert.NotNull(nuspec);
                using var stream = nuspec.Open();
                stream.SetLength(0);
                using var writer = new StreamWriter(stream);
                writer.Write("<package />");
            }

            var result = SignatureVerifier.Verify(tempPath);

            Assert.NotNull(result);
            Assert.False(result.IsUnsigned);
            Assert.False(result.AuthorVerified);
            Assert.False(result.RepositoryVerified);
            Assert.Contains("content hash", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static string PackageFixture(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Signatures", fileName);
        Assert.True(File.Exists(path), $"Missing signed-package fixture '{path}'. Rebuild the test project.");
        return path;
    }
}
