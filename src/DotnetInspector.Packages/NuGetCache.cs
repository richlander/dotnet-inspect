using System.Security.Cryptography;
using System.Text;

namespace DotnetInspector.Packages;

/// <summary>
/// Utilities for working with package and content caches.
/// Uses cross-platform paths via Environment.SpecialFolder.
/// Never writes to ~/.nuget/packages (read-only).
/// </summary>
public static class NuGetCache
{
    /// <summary>
    /// Gets the path to the NuGet package cache (read-only).
    /// </summary>
    public static string GetNuGetCachePath()
    {
        // NEVER write to ~/.nuget/packages; OK to read
        
        // Check NUGET_PACKAGES environment variable first
        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(nugetPackages) && Directory.Exists(nugetPackages))
        {
            return nugetPackages;
        }

        // Default: ~/.nuget/packages on all platforms
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".nuget", "packages");
    }

    /// <summary>
    /// Gets the base path for dotnet-inspect caches (read-write).
    /// Windows: %LOCALAPPDATA%\dotnet-inspect
    /// macOS/Linux: ~/.local/share/dotnet-inspect
    /// </summary>
    public static string GetAppCacheBasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "dotnet-inspect");
    }

    /// <summary>
    /// Gets the path to the dotnet-inspect package cache (read-write).
    /// </summary>
    public static string GetAppCachePath()
    {
        return Path.Combine(GetAppCacheBasePath(), "packages");
    }

    /// <summary>
    /// Gets the path to the source content cache (read-write).
    /// </summary>
    public static string GetSourceCachePath()
    {
        return Path.Combine(GetAppCacheBasePath(), "sources");
    }

    /// <summary>
    /// Tries to find a cached package in either the NuGet cache or the app cache.
    /// </summary>
    /// <param name="packageName">The package name (case-insensitive)</param>
    /// <param name="version">The package version</param>
    /// <returns>The path to the cached package directory, or null if not found</returns>
    public static string? TryGetCachedPackage(string packageName, string version)
    {
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();

        // Check NuGet cache first (more likely to have packages)
        var nugetCachePath = GetNuGetCachePath();
        if (Directory.Exists(nugetCachePath))
        {
            var nugetPackageDir = Path.Combine(nugetCachePath, normalizedName, normalizedVersion);
            if (Directory.Exists(nugetPackageDir) && IsCachedPackageValid(nugetPackageDir, normalizedName))
            {
                return nugetPackageDir;
            }
        }

        // Check app cache
        var appCachePath = GetAppCachePath();
        if (Directory.Exists(appCachePath))
        {
            var appPackageDir = Path.Combine(appCachePath, normalizedName, normalizedVersion);
            if (Directory.Exists(appPackageDir) && IsCachedPackageValid(appPackageDir, normalizedName))
            {
                return appPackageDir;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the path where a package should be cached in the app cache.
    /// Creates the directory structure if it doesn't exist.
    /// </summary>
    public static string GetPackageCachePath(string packageName, string version)
    {
        var appCachePath = GetAppCachePath();
        var packageDir = Path.Combine(appCachePath, packageName.ToLowerInvariant(), version.ToLowerInvariant());
        return packageDir;
    }

    /// <summary>
    /// Caches an extracted package to the app cache.
    /// </summary>
    /// <param name="extractedPath">Path to the extracted package contents</param>
    /// <param name="packageName">The package name</param>
    /// <param name="version">The package version</param>
    /// <returns>The path to the cached package, or null if caching failed</returns>
    public static string? CachePackage(string extractedPath, string packageName, string version)
    {
        try
        {
            var targetPath = GetPackageCachePath(packageName, version);

            // If already exists and valid, return it
            if (Directory.Exists(targetPath) && IsCachedPackageValid(targetPath))
            {
                return targetPath;
            }

            // Create parent directories
            var parentDir = Path.GetDirectoryName(targetPath);
            if (parentDir != null && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // Remove incomplete cache if exists
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            // Copy to cache
            CopyDirectory(extractedPath, targetPath);

            return targetPath;
        }
        catch
        {
            // Caching is best-effort, don't fail the operation
            return null;
        }
    }

    /// <summary>
    /// Checks if a cached package has the expected structure.
    /// </summary>
    public static bool IsCachedPackageValid(string cachedPath) => IsCachedPackageValid(cachedPath, null);

    /// <summary>
    /// Checks if a cached package has the expected structure.
    /// When packageName is provided, uses direct file check instead of directory scan.
    /// </summary>
    public static bool IsCachedPackageValid(string cachedPath, string? packageName)
    {
        if (!Directory.Exists(cachedPath))
            return false;

        // Valid if it contains a .nuspec file (extracted) or lib/tools directory
        bool hasNuspec;
        if (packageName != null)
        {
            // Fast path: check for expected nuspec name directly
            hasNuspec = File.Exists(Path.Combine(cachedPath, $"{packageName}.nuspec"));
        }
        else
        {
            // Fallback: scan for any nuspec file
            hasNuspec = Directory.GetFiles(cachedPath, "*.nuspec").Length > 0;
        }
        var hasLib = Directory.Exists(Path.Combine(cachedPath, "lib"));
        var hasTools = Directory.Exists(Path.Combine(cachedPath, "tools"));

        return hasNuspec || hasLib || hasTools;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, targetSubDir);
        }
    }

    /// <summary>
    /// Gets cached source content for a URL, if available.
    /// </summary>
    /// <param name="url">The source URL (e.g., raw.githubusercontent.com)</param>
    /// <returns>The cached content, or null if not cached</returns>
    public static string? TryGetCachedSource(string url)
    {
        var cachePath = GetSourceFilePath(url);
        if (File.Exists(cachePath))
        {
            try
            {
                return File.ReadAllText(cachePath);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Caches source content for a URL.
    /// </summary>
    /// <param name="url">The source URL</param>
    /// <param name="content">The content to cache</param>
    public static void CacheSource(string url, string content)
    {
        try
        {
            var cachePath = GetSourceFilePath(url);
            var cacheDir = Path.GetDirectoryName(cachePath);
            if (cacheDir != null && !Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            File.WriteAllText(cachePath, content);
        }
        catch
        {
            // Caching is best-effort
        }
    }

    /// <summary>
    /// Gets the file path for cached source content.
    /// Uses SHA256 hash of URL to create a unique, filesystem-safe filename.
    /// </summary>
    private static string GetSourceFilePath(string url)
    {
        // Hash the URL to create a safe filename
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var hashString = Convert.ToHexString(hash).ToLowerInvariant();
        
        // Use first 2 chars as subdirectory to avoid too many files in one folder
        var subDir = hashString[..2];
        var fileName = hashString[2..] + ".txt";
        
        return Path.Combine(GetSourceCachePath(), subDir, fileName);
    }
}
