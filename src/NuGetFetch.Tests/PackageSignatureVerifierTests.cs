using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Integration tests for PackageSignatureVerifier.
/// Downloads real packages from nuget.org to verify signatures.
/// </summary>
[Collection("NuGet Integration")]
public class PackageSignatureVerifierTests : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly NuGetClient _client;

    public PackageSignatureVerifierTests()
    {
        _client = new NuGetClient(_http);
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task VerifyPackage_AuthorSignedPackage_ReturnsValidWithPublisher()
    {
        // Newtonsoft.Json is author-signed by James Newton-King
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.Equal(SignatureStatus.Valid, result.Status);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Json.NET", result.Publisher);
            Assert.NotNull(result.Thumbprint);
            Assert.NotEmpty(result.Thumbprint);
            Assert.Equal(SignatureType.Author, result.SignatureType);
            Assert.Null(result.Reason);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_MicrosoftPackage_ReturnsValidWithPublisher()
    {
        // Microsoft packages are author-signed
        string nupkgPath = await DownloadPackageAsync("System.Text.Json", "9.0.4");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Microsoft", result.Publisher);
            Assert.Equal(SignatureType.Author, result.SignatureType);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_DotNetFoundationPackage_ReturnsValid()
    {
        // Humanizer.Core is signed by the .NET Foundation
        string nupkgPath = await DownloadPackageAsync("Humanizer.Core", "2.14.1");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Humanizer", result.Publisher);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_SignedPackage_HasTimestamp()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            // Most nuget.org packages have timestamps
            Assert.NotNull(result.Timestamp);
            // Timestamp should be in the past
            Assert.True(result.Timestamp < DateTimeOffset.UtcNow);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_SignedPackage_HasContentHash()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.NotNull(result.ContentHash);
            Assert.NotEmpty(result.ContentHash);
            // Should be valid base64
            byte[] decoded = Convert.FromBase64String(result.ContentHash);
            Assert.Equal(32, decoded.Length); // SHA-256 = 32 bytes
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public void VerifyPackage_UnsignedPackage_ReturnsUnsigned()
    {
        // Create a minimal zip with no .signature.p7s
        string tempPath = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("test.nuspec");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<package />");
            }

            var result = PackageSignatureVerifier.VerifyPackage(tempPath);

            Assert.True(result.IsUnsigned);
            Assert.Equal(SignatureStatus.Unsigned, result.Status);
            Assert.Null(result.Publisher);
            Assert.Null(result.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void VerifyPackage_TamperedSignature_ReturnsInvalid()
    {
        // Create a zip with a bogus .signature.p7s
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
                    stream.Write(new byte[] { 0x30, 0x00 }); // minimal invalid DER
            }

            var result = PackageSignatureVerifier.VerifyPackage(tempPath);

            Assert.False(result.IsValid);
            Assert.Equal(SignatureStatus.Invalid, result.Status);
            Assert.NotNull(result.Reason);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_Stream_WorksLikePath()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            using FileStream stream = File.OpenRead(nupkgPath);
            var result = PackageSignatureVerifier.VerifyPackage(stream);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Publisher);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public void TrustedRoots_CodeSigningRoots_LoadsSuccessfully()
    {
        var roots = TrustedRoots.CodeSigningRoots;
        Assert.NotEmpty(roots);
    }

    [Fact]
    public void TrustedRoots_TimestampingRoots_LoadsSuccessfully()
    {
        var roots = TrustedRoots.TimestampingRoots;
        Assert.NotEmpty(roots);
    }

    [Fact]
    public async Task VerifyPackage_AuthorSignedPackage_HasRepositoryCounterSignature()
    {
        // Newtonsoft.Json is author-signed; nuget.org adds a repository counter-signature
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.Equal(SignatureType.Author, result.SignatureType);
            Assert.NotNull(result.CounterSignature);
            Assert.True(result.CounterSignature.IsValid);
            Assert.Equal(SignatureType.Repository, result.CounterSignature.SignatureType);
            Assert.NotNull(result.CounterSignature.Publisher);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifySignatureFile_MatchesPackageVerification()
    {
        // Verify that VerifySignatureFile on an extracted .signature.p7s
        // produces the same result as VerifyPackage on the .nupkg
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var packageResult = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            // Extract the .signature.p7s from the nupkg
            string tempDir = Path.Combine(Path.GetTempPath(), $"sig-test-{Guid.NewGuid():N}");
            System.IO.Compression.ZipFile.ExtractToDirectory(nupkgPath, tempDir);
            try
            {
                string sigPath = Path.Combine(tempDir, ".signature.p7s");
                Assert.True(File.Exists(sigPath));

                var fileResult = PackageSignatureVerifier.VerifySignatureFile(sigPath);

                Assert.Equal(packageResult.Status, fileResult.Status);
                Assert.Equal(packageResult.Publisher, fileResult.Publisher);
                Assert.Equal(packageResult.SignatureType, fileResult.SignatureType);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_RepositorySignedPackage_HasNoCounterSignature()
    {
        // dotnet-install is repository-signed only (no author signature)
        string nupkgPath = await DownloadPackageAsync("dotnet-install", "0.1.1");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.Equal(SignatureType.Repository, result.SignatureType);
            Assert.Null(result.CounterSignature);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    private async Task<string> DownloadPackageAsync(string id, string version)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{id}.{version}.nupkg");
        await _client.DownloadToFileAsync(id, version, path);
        return path;
    }
}
