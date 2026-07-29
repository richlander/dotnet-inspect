using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Integration tests that hit the real NuGet V3 API.
/// These validate the full client pipeline against nuget.org.
/// </summary>
[Collection("NuGet Integration")]
public class NuGetClientIntegrationTests : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly NuGetClient _client;

    public NuGetClientIntegrationTests()
    {
        _client = new NuGetClient(_http);
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task GetVersionsAsync_ReturnsVersions()
    {
        var versions = await _client.GetVersionsAsync("Newtonsoft.Json", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(versions);
        Assert.Contains("13.0.3", versions);
    }

    [Fact]
    public async Task GetVersionsAsync_NonExistentPackage_ReturnsEmpty()
    {
        var versions = await _client.GetVersionsAsync("this-package-does-not-exist-xyz-12345", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetLatestVersionAsync_ReturnsVersion()
    {
        string? version = await _client.GetLatestVersionAsync("Newtonsoft.Json", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(version);
        // Should be 13.x or higher
        Assert.StartsWith("13.", version);
    }

    [Fact]
    public async Task GetLatestVersionAsync_NonExistentPackage_ReturnsNull()
    {
        string? version = await _client.GetLatestVersionAsync("this-package-does-not-exist-xyz-12345", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(version);
    }

    [Fact]
    public async Task ResolveVersionPatternAsync_WildcardMajor()
    {
        string? version = await _client.ResolveVersionPatternAsync("Newtonsoft.Json", "13.0.*", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(version);
        Assert.StartsWith("13.0.", version);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsStream()
    {
        using Stream stream = await _client.DownloadAsync("Humanizer.Core", "2.14.1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(stream.CanRead);

        // Verify it's a zip (PK header)
        byte[] header = new byte[4];
        int read = await stream.ReadAsync(header, TestContext.Current.CancellationToken);
        Assert.Equal(4, read);
        Assert.Equal((byte)'P', header[0]);
        Assert.Equal((byte)'K', header[1]);
    }

    [Fact]
    public async Task DownloadAndExtract_EndToEnd()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"nf-e2e-{Guid.NewGuid():N}");
        try
        {
            using Stream stream = await _client.DownloadAsync("Humanizer.Core", "2.14.1", cancellationToken: TestContext.Current.CancellationToken);
            await PackageExtractor.ExtractAsync(stream, tempDir, TestContext.Current.CancellationToken);

            Assert.True(PackageExtractor.IsValidPackage(tempDir));
            Assert.True(Directory.Exists(Path.Combine(tempDir, "lib")));

            string? tfmPath = TfmResolver.ResolvePackagePath(tempDir);
            Assert.NotNull(tfmPath);

            var dlls = TfmResolver.GetPackageDlls(tempDir);
            Assert.NotEmpty(dlls);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetLatestVersionAsync_MultipleSources()
    {
        var sources = new[]
        {
            PackageSource.NuGetOrg,
            new PackageSource("nonexistent", "https://nonexistent.example.com/v3/index.json"),
        };

        string? version = await _client.GetLatestVersionAsync("Newtonsoft.Json", sources, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(version);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("01.00.00", "1.0.0")]
    [InlineData("13.0.3", "13.0.3")]
    [InlineData("9.0.0-preview.1", "9.0.0-preview.1")]
    [InlineData("not-a-version", "not-a-version")]
    public void NormalizeVersion_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, NuGetClient.NormalizeVersion(input));
    }
}
