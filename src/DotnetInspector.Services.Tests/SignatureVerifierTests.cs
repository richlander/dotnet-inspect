
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Integration tests for the in-process SignatureVerifier.
/// Downloads real packages from nuget.org to verify signatures.
/// </summary>
public class SignatureVerifierTests : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly NuGetClient _client;

    public SignatureVerifierTests()
    {
        _client = new NuGetClient(_http);
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Verify_AuthorSignedPackage_ExtractsPublisher()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = SignatureVerifier.Verify(nupkgPath);

            Assert.NotNull(result);
            Assert.Equal("Json.NET (.NET Foundation)", result.Publisher);
            Assert.True(result.AuthorVerified);
            Assert.True(result.RepositoryVerified);
            Assert.Equal("nuget.org", result.Repository);
            Assert.False(result.IsUnsigned);
            Assert.Null(result.StatusMessage);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task Verify_MicrosoftPackage_ExtractsPublisher()
    {
        string nupkgPath = await DownloadPackageAsync("System.Text.Json", "9.0.4");
        try
        {
            var result = SignatureVerifier.Verify(nupkgPath);

            Assert.NotNull(result);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Microsoft", result.Publisher);
            Assert.True(result.AuthorVerified);
            Assert.True(result.RepositoryVerified);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
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

    private async Task<string> DownloadPackageAsync(string id, string version)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{id}.{version}.nupkg");
        await _client.DownloadToFileAsync(id, version, path);
        return path;
    }
}
