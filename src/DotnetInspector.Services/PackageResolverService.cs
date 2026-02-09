using System.IO.Compression;
using System.Text.Json;
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
        HttpClient httpClient)
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

        // Discover latest version if not specified
        if (string.IsNullOrEmpty(version))
        {
            version = await GetLatestVersionAsync(httpClient, packageName, log);
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

        string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName}/{version}/{packageName}.{version}.nupkg";
        log?.Invoke($"Downloading: {nupkgUrl}");

        byte[]? packageBytes = await HttpRetryHelper.GetBytesWithRetryAsync(httpClient, nupkgUrl);
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
    /// </summary>
    public static async Task<string?> GetLatestVersionAsync(
        HttpClient client, string packageName, Action<string>? log)
    {
        var versions = await FetchVersionIndexAsync(client, packageName, log);
        if (versions == null || versions.Count == 0)
            return null;

        // Prefer stable versions (those without a hyphen)
        var stableVersions = versions.Where(v => !v.Contains('-')).ToList();
        string latest = stableVersions.Count > 0 ? stableVersions[^1] : versions[^1];
        log?.Invoke($"Latest version: {latest}");
        return latest;
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
        var result = new List<string>();
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
