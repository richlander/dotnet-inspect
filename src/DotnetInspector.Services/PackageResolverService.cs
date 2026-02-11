using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Resolves NuGet packages: version discovery, cache lookup, download, and extraction.
/// </summary>
public static class PackageResolverService
{
    /// <summary>
    /// Result of resolving and extracting a NuGet package.
    /// </summary>
    public record PackageResolution(
        string ExtractPath,
        string? TempDir,
        string PackageName,
        string Version,
        string? NupkgPath,
        bool FromCache);

    /// <summary>
    /// Resolves a package from cache or downloads it from NuGet.
    /// Handles both local .nupkg files and NuGet package names (with optional @version).
    /// </summary>
    public static async Task<PackageResolution?> ResolvePackageAsync(
        string packageSource,
        string? version,
        Action<string>? log,
        HttpClient httpClient,
        NuGetSourceOptions? sourceOptions = null)
    {
        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            return ExtractLocalPackage(packageSource, log);
        }

        // Parse package@version format
        string packageName;
        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            packageName = packageSource[..atIndex].ToLowerInvariant();
            version = packageSource[(atIndex + 1)..].ToLowerInvariant();
            log?.Invoke($"Using specified version: {version}");
        }
        else
        {
            packageName = packageSource.ToLowerInvariant();
        }

        var sources = NuGetSourceResolver.ResolveSources(sourceOptions);
        bool isDefaultSource = sources.Count == 1 && sources[0].IsNuGetOrg();

        // Resolve wildcard version patterns (e.g., 11.0.0-preview*)
        if (version != null && version.Contains('*'))
        {
            version = await PackageExtractor.ResolveVersionPatternAsync(httpClient, packageName, version, sources, log);
            if (version == null)
            {
                return null;
            }
        }
        // Discover latest version if not specified
        else if (string.IsNullOrEmpty(version))
        {
            if (isDefaultSource)
            {
                version = await GetLatestVersionAsync(httpClient, packageName, log);
            }
            else
            {
                version = await PackageExtractor.GetLatestVersionAsync(httpClient, packageName, sources, log);
            }

            if (version == null)
            {
                return null;
            }
        }

        // Check NuGet cache first
        var cachedPath = NuGetCache.TryGetCachedPackage(packageName, version);
        if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
        {
            log?.Invoke($"Using cached package: {cachedPath}");
            var expectedNupkg = Path.Combine(cachedPath, $"{packageName}.{version}.nupkg");
            string? nupkgPath = File.Exists(expectedNupkg) ? expectedNupkg : null;
            return new PackageResolution(cachedPath, null, packageName, version, nupkgPath, FromCache: true);
        }

        // Download from NuGet
        string tempDir = Path.Combine(Path.GetTempPath(), $"inspect-{packageName}-{version}-{Guid.NewGuid():N}");
        string extractPath = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);

        byte[]? packageBytes = null;
        foreach (var source in sources)
        {
            var nupkgUrl = await PackageExtractor.GetPackageDownloadUrlAsync(httpClient, source, packageName, version, log);
            if (nupkgUrl == null) continue;

            log?.Invoke($"Downloading: {packageName} {version} from {source.Name}");
            try
            {
                packageBytes = await HttpRetryHelper.GetBytesWithRetryAsync(httpClient, nupkgUrl);
                if (packageBytes != null) break;
            }
            catch (HttpRequestException)
            {
                continue;
            }
        }
        if (packageBytes == null)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        string nupkgFilePath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
        await File.WriteAllBytesAsync(nupkgFilePath, packageBytes);
        ZipFile.ExtractToDirectory(nupkgFilePath, extractPath);
        log?.Invoke("Package downloaded successfully.");

        // Cache for future use
        var newCachePath = NuGetCache.CachePackage(extractPath, packageName, version);
        if (newCachePath != null)
        {
            log?.Invoke($"Cached to: {newCachePath}");
        }

        return new PackageResolution(extractPath, tempDir, packageName, version, nupkgFilePath, FromCache: false);
    }

    /// <summary>
    /// Gets the latest version of a package from NuGet. Prefers stable versions.
    /// Uses a 1-hour disk cache to avoid network calls on repeated invocations.
    /// </summary>
    public static async Task<string?> GetLatestVersionAsync(
        HttpClient client, string packageName, Action<string>? log)
    {
        string normalizedName = packageName.ToLowerInvariant();

        // Check version cache first (1-hour TTL)
        var cached = CoreCache.TryGet("versions", normalizedName, TimeSpan.FromHours(1), extension: "txt");
        if (cached != null)
        {
            log?.Invoke($"Using cached version: {cached}");
            return cached;
        }

        // Use the search API — returns latest stable version directly
        var version = await GetLatestVersionFromSearchAsync(client, normalizedName, log);

        // Fall back to flat-container index
        version ??= await GetLatestVersionFromIndexAsync(client, normalizedName, log);

        if (version != null)
        {
            CoreCache.Set("versions", normalizedName, version, extension: "txt");
            log?.Invoke($"Latest version: {version}");
        }

        return version;
    }

    private static async Task<string?> GetLatestVersionFromSearchAsync(
        HttpClient client, string packageName, Action<string>? log)
    {
        string searchUrl = $"https://azuresearch-usnc.nuget.org/query?q=packageid:{packageName}&take=1&prerelease=false";
        log?.Invoke($"Fetching latest version from: {searchUrl}");

        try
        {
            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, searchUrl);
            if (json == null)
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("version", out var version))
            {
                return version.GetString();
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Search API failed: {ex.Message}");
        }

        return null;
    }

    private static async Task<string?> GetLatestVersionFromIndexAsync(
        HttpClient client, string packageName, Action<string>? log)
    {
        var versions = await FetchVersionIndexAsync(client, packageName, log);
        if (versions == null || versions.Count == 0)
            return null;

        var stableVersions = versions.Where(v => !v.Contains('-')).ToList();
        return stableVersions.Count > 0 ? stableVersions[^1] : versions[^1];
    }

    /// <summary>
    /// Lists available versions of a package from NuGet, newest first.
    /// </summary>
    public static async Task<List<string>?> GetVersionsAsync(
        HttpClient client, string packageName, bool includePrerelease,
        int? limit, Action<string>? log)
    {
        var versions = await FetchVersionIndexAsync(client, packageName, log);
        if (versions == null)
            return null;

        var filtered = includePrerelease
            ? versions
            : versions.Where(v => !v.Contains('-')).ToList();

        // Newest first, with optional limit
        List<string> result = [];
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            result.Add(filtered[i]);
            if (limit.HasValue && result.Count >= limit.Value)
                break;
        }

        return result;
    }

    private static PackageResolution? ExtractLocalPackage(string path, Action<string>? log)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"inspect-local-{Guid.NewGuid():N}");
        string extractPath = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);

        log?.Invoke($"Extracting package: {Path.GetFileName(path)}");
        ZipFile.ExtractToDirectory(path, extractPath);

        // Derive package name from filename (e.g., Foo.Bar.1.0.0.nupkg)
        string fileName = Path.GetFileNameWithoutExtension(path);
        var (packageName, version) = ParsePackageFileName(fileName);

        return new PackageResolution(extractPath, tempDir, packageName ?? fileName, version ?? "", path, FromCache: false);
    }

    internal static (string? name, string? version) ParsePackageFileName(string fileName)
    {
        var parts = fileName.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
            {
                var name = string.Join(".", parts.Take(i));
                var version = string.Join(".", parts.Skip(i));
                return (name, version);
            }
        }
        return (fileName, null);
    }

    private static async Task<List<string>?> FetchVersionIndexAsync(
        HttpClient client, string packageName, Action<string>? log)
    {
        try
        {
            string indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/index.json";
            log?.Invoke($"Fetching versions from: {indexUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
            if (json == null)
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                return versions.EnumerateArray()
                    .Select(v => v.GetString())
                    .Where(v => v != null)
                    .Cast<string>()
                    .ToList();
            }
        }
        catch (HttpRequestException ex)
        {
            log?.Invoke($"Error fetching versions: {ex.Message}");
        }

        return null;
    }
}
