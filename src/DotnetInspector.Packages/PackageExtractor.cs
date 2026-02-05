// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;

namespace DotnetInspector.Packages;

/// <summary>
/// Result of a package extraction operation.
/// </summary>
/// <param name="ExtractPath">Path to the extracted package contents</param>
/// <param name="TempDir">Temporary directory to clean up (null if using cache)</param>
/// <param name="PackageName">Package name</param>
/// <param name="Version">Package version (may be null for local files)</param>
/// <param name="NupkgPath">Path to the .nupkg file for signature verification (null if not available)</param>
public record PackageExtractionResult(
    string ExtractPath,
    string? TempDir,
    string? PackageName,
    string? Version,
    string? NupkgPath = null);

/// <summary>
/// Shared utility for extracting NuGet packages from local files or NuGet feeds.
/// </summary>
public static class PackageExtractor
{
    /// <summary>
    /// Extracts a package from a local .nupkg file or downloads from NuGet sources.
    /// </summary>
    /// <param name="client">HTTP client for downloading packages</param>
    /// <param name="packageSource">Local .nupkg path or package reference (name or name@version)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="tempDirPrefix">Prefix for temporary directory name (e.g., "inspect-api")</param>
    /// <param name="sourceOptions">NuGet source configuration (defaults to nuget.org)</param>
    /// <returns>Extraction result or null if failed</returns>
    public static async Task<PackageExtractionResult?> ExtractPackageAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log = null,
        string tempDirPrefix = "inspect-pkg",
        NuGetSourceOptions? sourceOptions = null)
    {
        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            return ExtractLocalPackage(packageSource, log, tempDirPrefix);
        }

        return await DownloadAndExtractPackageAsync(client, packageSource, log, tempDirPrefix, sourceOptions);
    }

    private static PackageExtractionResult? ExtractLocalPackage(
        string packageSource,
        Action<string>? log,
        string tempDirPrefix)
    {
        if (!File.Exists(packageSource))
        {
            return null;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"{tempDirPrefix}-{Guid.NewGuid():N}");
        string extractPath = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);

        log?.Invoke($"Extracting package: {Path.GetFileName(packageSource)}");
        ZipFile.ExtractToDirectory(packageSource, extractPath);

        var (pkgName, pkgVersion) = ParsePackageReference(packageSource);
        return new PackageExtractionResult(extractPath, tempDir, pkgName, pkgVersion, packageSource);
    }

    private static async Task<PackageExtractionResult?> DownloadAndExtractPackageAsync(
        HttpClient client,
        string packageSource,
        Action<string>? log,
        string tempDirPrefix,
        NuGetSourceOptions? sourceOptions)
    {
        var (packageName, version) = ParsePackageReference(packageSource);

        // Resolve NuGet sources
        var sources = NuGetSourceResolver.ResolveSources(sourceOptions);

        // Get version if not specified
        if (version == null)
        {
            version = await GetLatestVersionAsync(client, packageName, sources, log);
            if (version == null)
            {
                return null;
            }
        }

        // Normalize to lowercase for NuGet API
        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();

        // Check NuGet cache first
        var cachedPath = NuGetCache.TryGetCachedPackage(normalizedName, normalizedVersion);
        if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
        {
            log?.Invoke($"Using cached package: {cachedPath}");
            // Try to find .nupkg in cache directory
            var cachedNupkg = FindNupkgInDirectory(cachedPath, normalizedName, normalizedVersion);
            return new PackageExtractionResult(cachedPath, null, packageName, version, cachedNupkg);
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"{tempDirPrefix}-{Guid.NewGuid():N}");
        string extractPath = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(tempDir);

        // Try each source in order
        byte[]? packageBytes = null;
        string? successfulSource = null;

        foreach (var source in sources)
        {
            var nupkgUrl = await GetPackageDownloadUrlAsync(client, source, normalizedName, normalizedVersion, log);
            if (nupkgUrl == null)
                continue;

            log?.Invoke($"Downloading: {packageName} {version} from {source.Name}");

            try
            {
                packageBytes = await HttpRetryHelper.GetBytesWithRetryAsync(client, nupkgUrl);
                if (packageBytes != null)
                {
                    successfulSource = source.Name;
                    break;
                }
            }
            catch (HttpRequestException)
            {
                // Try next source
                continue;
            }
        }

        if (packageBytes == null)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        string? nupkgPath = null;
        try
        {
            nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
            await File.WriteAllBytesAsync(nupkgPath, packageBytes);
            ZipFile.ExtractToDirectory(nupkgPath, extractPath);
            log?.Invoke($"Package downloaded successfully from {successfulSource}.");

            // Cache the package for future use
            var newCachePath = NuGetCache.CachePackage(extractPath, packageName, version);
            if (newCachePath != null)
            {
                log?.Invoke($"Cached to: {newCachePath}");
            }
        }
        catch (Exception)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return null;
        }

        return new PackageExtractionResult(extractPath, tempDir, packageName, version, nupkgPath);
    }

    /// <summary>
    /// Gets the download URL for a package from a specific source.
    /// </summary>
    private static async Task<string?> GetPackageDownloadUrlAsync(
        HttpClient client,
        NuGetSource source,
        string packageName,
        string version,
        Action<string>? log)
    {
        // Check for well-known flat-container URL (nuget.org optimization)
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            return $"{flatContainerUrl}/{packageName}/{version}/{packageName}.{version}.nupkg";
        }

        // Query V3 service index to discover PackageBaseAddress (flat-container) endpoint
        var baseAddress = await GetPackageBaseAddressAsync(client, source, log);
        if (baseAddress != null)
        {
            // Ensure trailing slash
            if (!baseAddress.EndsWith('/'))
                baseAddress += "/";

            return $"{baseAddress}{packageName}/{version}/{packageName}.{version}.nupkg";
        }

        return null;
    }

    /// <summary>
    /// Discovers the PackageBaseAddress (flat-container) endpoint from a V3 service index.
    /// </summary>
    private static async Task<string?> GetPackageBaseAddressAsync(
        HttpClient client,
        NuGetSource source,
        Action<string>? log)
    {
        // The source URL should be the V3 index.json
        var indexUrl = source.Url;
        if (!indexUrl.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            // Try appending /v3/index.json for common feed patterns
            if (indexUrl.EndsWith('/'))
                indexUrl += "v3/index.json";
            else
                indexUrl += "/v3/index.json";
        }

        log?.Invoke($"Querying service index: {indexUrl}");

        string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
        if (json == null)
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var resources = doc.RootElement.GetProperty("resources");

            foreach (var resource in resources.EnumerateArray())
            {
                var type = resource.GetProperty("@type").GetString();
                if (type != null && type.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase))
                {
                    return resource.GetProperty("@id").GetString();
                }
            }
        }
        catch
        {
            // Invalid service index
        }

        return null;
    }

    /// <summary>
    /// Finds the .nupkg file in a cache directory.
    /// </summary>
    private static string? FindNupkgInDirectory(string cacheDir, string packageName, string version)
    {
        // Standard NuGet cache layout: {package}/{version}/{package}.{version}.nupkg
        var expectedPath = Path.Combine(cacheDir, $"{packageName}.{version}.nupkg");
        if (File.Exists(expectedPath))
            return expectedPath;

        // Try to find any .nupkg file
        try
        {
            var nupkgFiles = Directory.GetFiles(cacheDir, "*.nupkg");
            return nupkgFiles.Length > 0 ? nupkgFiles[0] : null;
        }
        catch
        {
            return null;
        }
    }
    /// Parses a package reference string into name and optional version.
    /// Handles formats: "PackageName", "PackageName@1.0.0", "Package.Name.1.0.0.nupkg"
    /// </summary>
    public static (string name, string? version) ParsePackageReference(string packageSource)
    {
        // Handle local .nupkg files
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileNameWithoutExtension(packageSource);
            // Try to parse name.version pattern (e.g., "System.Text.Json.8.0.0")
            int lastDotIndex = fileName.LastIndexOf('.');
            while (lastDotIndex > 0)
            {
                string potentialVersion = fileName[(lastDotIndex + 1)..];
                if (char.IsDigit(potentialVersion.FirstOrDefault()))
                {
                    // Found version start
                    return (fileName[..lastDotIndex], fileName[(lastDotIndex + 1)..]);
                }
                lastDotIndex = fileName.LastIndexOf('.', lastDotIndex - 1);
            }
            return (fileName, null);
        }

        // Handle package@version format
        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);
        }

        return (packageSource, null);
    }

    private static async Task<string?> GetLatestVersionAsync(
        HttpClient client,
        string packageName,
        List<NuGetSource> sources,
        Action<string>? log)
    {
        string normalizedName = packageName.ToLowerInvariant();

        foreach (var source in sources)
        {
            var version = await GetLatestVersionFromSourceAsync(client, normalizedName, source, log);
            if (version != null)
                return version;
        }

        return null;
    }

    private static async Task<string?> GetLatestVersionFromSourceAsync(
        HttpClient client,
        string packageName,
        NuGetSource source,
        Action<string>? log)
    {
        // Try flat-container index first (faster)
        var flatContainerUrl = source.GetFlatContainerUrl();
        if (flatContainerUrl != null)
        {
            string indexUrl = $"{flatContainerUrl}/{packageName}/index.json";
            log?.Invoke($"Fetching versions from: {indexUrl}");

            var version = await ParseVersionIndexAsync(client, indexUrl);
            if (version != null)
                return version;
        }

        // Fall back to V3 service index discovery
        var baseAddress = await GetPackageBaseAddressAsync(client, source, log);
        if (baseAddress != null)
        {
            if (!baseAddress.EndsWith('/'))
                baseAddress += "/";

            string indexUrl = $"{baseAddress}{packageName}/index.json";
            log?.Invoke($"Fetching versions from: {indexUrl}");

            var version = await ParseVersionIndexAsync(client, indexUrl);
            if (version != null)
                return version;
        }

        return null;
    }

    private static async Task<string?> ParseVersionIndexAsync(HttpClient client, string indexUrl)
    {
        string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
        if (json == null)
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var versions = doc.RootElement.GetProperty("versions");
            if (versions.GetArrayLength() > 0)
            {
                // Get latest non-prerelease version, or latest overall
                string? latestStable = null;
                string? latestAny = null;
                foreach (var v in versions.EnumerateArray())
                {
                    var ver = v.GetString();
                    if (ver != null)
                    {
                        latestAny = ver;
                        if (!ver.Contains('-'))
                            latestStable = ver;
                    }
                }
                return latestStable ?? latestAny;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Cleans up temporary directory if it exists.
    /// </summary>
    public static void Cleanup(string? tempDir)
    {
        if (tempDir != null)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
