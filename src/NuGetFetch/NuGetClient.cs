using System.Collections.Concurrent;
using System.Net.Http.Headers;
using NuGet.Versioning;

namespace NuGetFetch;

/// <summary>
/// Lightweight NuGet V3 API client. Accepts an HttpClient from the caller.
/// </summary>
public class NuGetClient(HttpClient client)
{
    internal const string NuGetOrgFlatContainer = "https://api.nuget.org/v3-flatcontainer/";
    internal const string NuGetOrgServiceIndex = "https://api.nuget.org/v3/index.json";
    internal const string NuGetOrgSearchUrl = "https://azuresearch-usnc.nuget.org/query";

    private readonly ConcurrentDictionary<string, string> _baseAddressCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all available versions for a package from a NuGet source.
    /// Returns empty list if the package does not exist.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        string baseAddress = await ResolveBaseAddressAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
        string url = $"{baseAddress}{packageId.ToLowerInvariant()}/index.json";

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            ApplyCredential(request, credential);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            VersionIndex? index = await NuGetApi.GetVersionIndexAsync(stream, cancellationToken).ConfigureAwait(false);
            return (IReadOnlyList<string>?)index?.Versions ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    /// <summary>
    /// Gets the latest version for a package. Uses the search API for nuget.org (faster).
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        // For nuget.org, use the search API (faster than listing all versions)
        if (sourceUrl is null || IsNuGetOrg(sourceUrl))
        {
            return await GetLatestVersionFromSearchAsync(packageId, includePrerelease, cancellationToken).ConfigureAwait(false);
        }

        // For other sources, list all versions and pick the latest
        IReadOnlyList<string> versions = await GetVersionsAsync(packageId, sourceUrl, credential, cancellationToken).ConfigureAwait(false);
        return FindLatestVersion(versions, includePrerelease);
    }

    /// <summary>
    /// Gets the latest version across multiple sources. Returns the first match.
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, IEnumerable<PackageSource> sources, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        foreach (PackageSource source in sources)
        {
            try
            {
                string? version = await GetLatestVersionAsync(packageId, includePrerelease, source.Url, source.Credential, cancellationToken).ConfigureAwait(false);
                if (version is not null)
                {
                    return version;
                }
            }
            catch (HttpRequestException)
            {
                // Try next source
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads a package as a stream. The returned stream must be disposed by the caller,
    /// which will also dispose the underlying HTTP response.
    /// </summary>
    public async Task<Stream> DownloadAsync(string packageId, string version, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        string baseAddress = await ResolveBaseAddressAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
        string id = packageId.ToLowerInvariant();
        string ver = NormalizeVersion(version);
        string url = $"{baseAddress}{id}/{ver}/{id}.{ver}.nupkg";

        HttpRequestMessage request = new(HttpMethod.Get, url);
        ApplyCredential(request, credential);

        HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseStream(contentStream, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Downloads a package to a file.
    /// </summary>
    public async Task DownloadToFileAsync(string packageId, string version, string destinationPath, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using Stream source = await DownloadAsync(packageId, version, sourceUrl, credential, cancellationToken).ConfigureAwait(false);
        using FileStream dest = File.Create(destinationPath);
        await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the PackageBaseAddress endpoint from a V3 service index.
    /// </summary>
    public async Task<string?> GetPackageBaseAddressAsync(string serviceIndexUrl, CancellationToken cancellationToken = default)
    {
        using Stream stream = await client.GetStreamAsync(serviceIndexUrl, cancellationToken).ConfigureAwait(false);
        ServiceIndex? index = await NuGetApi.GetServiceIndexAsync(stream, cancellationToken).ConfigureAwait(false);

        string? baseAddress = index?.Resources
            .Where(r => r.Type.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .FirstOrDefault();

        if (baseAddress is null)
        {
            return null;
        }

        return baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/";
    }

    /// <summary>
    /// Resolves versions matching a wildcard pattern (e.g., "11.0.0-preview*").
    /// </summary>
    public async Task<string?> ResolveVersionPatternAsync(string packageId, string pattern, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> versions = await GetVersionsAsync(packageId, sourceUrl, credential, cancellationToken).ConfigureAwait(false);

        string prefix = pattern.TrimEnd('*');
        NuGetVersion? best = null;

        foreach (string v in versions)
        {
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                NuGetVersion.TryParse(v, out NuGetVersion? parsed))
            {
                if (best is null || parsed > best)
                {
                    best = parsed;
                }
            }
        }

        return best?.OriginalVersion;
    }

    private async Task<string?> GetLatestVersionFromSearchAsync(string packageId, bool includePrerelease, CancellationToken cancellationToken)
    {
        string prerelease = includePrerelease ? "true" : "false";
        string url = $"{NuGetOrgSearchUrl}?q=packageid:{Uri.EscapeDataString(packageId)}&take=1&prerelease={prerelease}";
        using Stream stream = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        SearchResponse? response = await NuGetApi.GetSearchResponseAsync(stream, cancellationToken).ConfigureAwait(false);
        return response?.Data.FirstOrDefault()?.Version;
    }

    private async Task<string> ResolveBaseAddressAsync(string? sourceUrl, CancellationToken cancellationToken)
    {
        if (sourceUrl is null || IsNuGetOrg(sourceUrl))
        {
            return NuGetOrgFlatContainer;
        }

        if (_baseAddressCache.TryGetValue(sourceUrl, out string? cached))
        {
            return cached;
        }

        string baseAddress = await GetPackageBaseAddressAsync(sourceUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not resolve PackageBaseAddress from {sourceUrl}");

        _baseAddressCache.TryAdd(sourceUrl, baseAddress);
        return baseAddress;
    }

    private static bool IsNuGetOrg(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string host = uri.Host;
            return host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase)
                || host.Equals("azuresearch-usnc.nuget.org", StringComparison.OrdinalIgnoreCase)
                || host.Equals("globalcdn.nuget.org", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void ApplyCredential(HttpRequestMessage request, PackageSourceCredential? credential)
    {
        if (credential is not null)
        {
            string encoded = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{credential.Username}:{credential.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    internal static string? FindLatestVersion(IReadOnlyList<string> versions, bool includePrerelease)
    {
        NuGetVersion? best = null;

        foreach (string v in versions)
        {
            if (NuGetVersion.TryParse(v, out NuGetVersion? parsed))
            {
                if (!includePrerelease && parsed.IsPrerelease)
                {
                    continue;
                }

                if (best is null || parsed > best)
                {
                    best = parsed;
                }
            }
        }

        return best?.OriginalVersion;
    }

    /// <summary>
    /// Normalizes a version string using NuGet versioning rules.
    /// Falls back to lowercasing if the version string can't be parsed.
    /// </summary>
    public static string NormalizeVersion(string version)
    {
        if (NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            return parsed.ToNormalizedString().ToLowerInvariant();
        }

        return version.ToLowerInvariant();
    }
}

/// <summary>
/// A stream wrapper that disposes the underlying HttpResponseMessage when the stream is disposed.
/// </summary>
internal sealed class HttpResponseStream(Stream inner, HttpResponseMessage response) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        response.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
