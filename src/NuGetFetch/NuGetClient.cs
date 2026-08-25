using System.Text.Json;
using NuGet.Versioning;

namespace NuGetFetch;

/// <summary>
/// Lightweight NuGet V3 API client. Accepts an HttpClient from the caller.
/// </summary>
public class NuGetClient(HttpClient client)
{
    internal const string NuGetOrgFlatContainer =
        NuGetV3PackageResourceClient.NuGetOrgFlatContainer;
    internal const string NuGetOrgServiceIndex =
        NuGetV3PackageResourceClient.NuGetOrgServiceIndex;
    internal const string NuGetOrgSearchUrl = "https://azuresearch-usnc.nuget.org/query";

    private readonly NuGetFetchOptions _options = new();
    private readonly NuGetV3PackageResourceClient _packageResources =
        new(client);

    /// <summary>
    /// Creates a NuGet client with configured resource limits and deadlines.
    /// </summary>
    public NuGetClient(HttpClient client, NuGetFetchOptions options)
        : this(client)
    {
        _options = NuGetFetchOptions.Validate(options);
    }

    /// <summary>
    /// Gets all available versions for a package from a NuGet source.
    /// Returns empty list if the package does not exist.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        return await GetVersionsAsync(
            packageId,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        string? sourceUrl,
        PackageSourceCredential? credential,
        NuGetOperationDeadline operation) =>
        await _packageResources.GetVersionsAsync(
            packageId,
            sourceUrl ?? NuGetOrgServiceIndex,
            credential,
            _options,
            operation,
            useNuGetOrgShortcut: true).ConfigureAwait(false);

    /// <summary>
    /// Gets the latest version for a package. Uses the search API for nuget.org (faster).
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        return await GetLatestVersionAsync(
            packageId,
            includePrerelease,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
    }

    private async Task<string?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        string? sourceUrl,
        PackageSourceCredential? credential,
        NuGetOperationDeadline operation)
    {
        // For nuget.org, use the search API (faster than listing all versions)
        if (sourceUrl is null
            || PackageSource.IsNuGetOrgServiceIndex(sourceUrl))
        {
            return await GetLatestVersionFromSearchAsync(
                packageId,
                includePrerelease,
                operation).ConfigureAwait(false);
        }

        // For other sources, list all versions and pick the latest
        IReadOnlyList<string> versions = await GetVersionsAsync(
            packageId,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
        return FindLatestVersion(versions, includePrerelease);
    }

    /// <summary>
    /// Gets the latest version across multiple sources. Returns the first match.
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, IEnumerable<PackageSource> sources, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        foreach (PackageSource source in sources)
        {
            try
            {
                string? version = await GetLatestVersionAsync(
                    packageId,
                    includePrerelease,
                    source.Url,
                    source.Credential,
                    operation).ConfigureAwait(false);
                if (version is not null)
                {
                    return version;
                }
            }
            catch (Exception ex) when (ex is
                HttpRequestException
                or JsonException
                or NuGetSourceResponseException)
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
        var operation = CreateOperation(cancellationToken);
        try
        {
            (Stream content, _) =
                await _packageResources.GetPackageAsync(
                    packageId,
                    version,
                    sourceUrl ?? NuGetOrgServiceIndex,
                    credential,
                    _options,
                    operation,
                    useNuGetOrgShortcut: true).ConfigureAwait(false);
            return content;
        }
        catch
        {
            operation.Dispose();
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
        using var operation = CreateOperation(cancellationToken);
        return await _packageResources.GetPackageBaseAddressAsync(
            serviceIndexUrl,
            credential: null,
            _options,
            operation).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves versions matching a wildcard pattern (e.g., "11.0.0-preview*").
    /// </summary>
    public async Task<string?> ResolveVersionPatternAsync(string packageId, string pattern, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        IReadOnlyList<string> versions = await GetVersionsAsync(
            packageId,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);

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

    private async Task<string?> GetLatestVersionFromSearchAsync(
        string packageId,
        bool includePrerelease,
        NuGetOperationDeadline operation)
    {
        var search = new SearchService(
            client,
            NuGetOrgSearchUrl,
            _options);
        IReadOnlyList<SearchResult> results = await search.SearchAsync(
            $"packageid:{packageId}",
            take: 1,
            prerelease: includePrerelease,
            auth: null,
            operation: operation).ConfigureAwait(false);
        return results.FirstOrDefault()?.Version;
    }

    private NuGetOperationDeadline CreateOperation(
        CancellationToken cancellationToken) =>
        new(_options, client.Timeout, cancellationToken);

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
        => NuGetV3PackageResourceClient.NormalizeVersion(version);
}
