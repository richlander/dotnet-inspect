using DotnetInspector.Cache;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Downloads and caches .NET platform ref/runtime packs from NuGet.
/// Packs are cached in the app cache directory mirroring the SDK packs layout under a
/// versioned category directory:
/// {cache}/dotnet-inspect/packs-v2/{PackName}/{Version}/ref/net{TFM}/*.dll
/// </summary>
public static class PlatformPackService
{
    /// <summary>
    /// The current app cache category directory name for platform packs.
    /// Unlike SDK-installed packs, this is versioned and does not equal <c>packs</c>.
    /// </summary>
    public const string PacksCategory = "packs-v2";

    /// <summary>
    /// The prefix shared by every app cache packs category directory name.
    /// </summary>
    public const string PacksCategoryPrefix = "packs-v";

    private const string CommitMarkerFileName = ".dotnet-inspect.pack.complete";

    static PlatformPackService()
    {
        PersistentCache.RegisterVersionedCategory(
            PacksCategoryPrefix,
            PacksCategory);
    }

    /// <summary>
    /// Gets the app cache packs directory, or null if PersistentCache is not initialized.
    /// </summary>
    public static string? GetPacksCachePath()
    {
        try
        {
            return Path.Combine(PersistentCache.GetBasePath(), PacksCategory);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ensures a ref pack is available locally, downloading from NuGet if needed.
    /// </summary>
    public static async Task<string?> EnsureRefPackAsync(
        string frameworkShortName,
        string version,
        HttpClient client,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        if (!PlatformResolver.FrameworkMappings.TryGetValue(frameworkShortName, out var packName))
        {
            return null;
        }

        return await EnsurePackAsync(
            packName,
            version,
            client,
            log,
            sourceOptions).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns true if the assembly exists in the given pack directory.
    /// </summary>
    public static bool ContainsAssembly(string packDir, string assemblyName)
    {
        return Directory.EnumerateFiles(packDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Any();
    }

    // ── Multi-pack download ──────────────────────────────────────────

    /// <summary>
    /// A pack download request: pack name and optional pinned version.
    /// </summary>
    public record PackRequest(string PackName, string? Version = null);

    /// <summary>
    /// A completed pack download result.
    /// </summary>
    public record PackResult(string PackName, string Version, string PackDir);

    /// <summary>
    /// Filters framework requests to only include those not locally available.
    /// Use this before calling EnsurePacksAsync to implement "local-only" policy.
    /// </summary>
    /// <param name="frameworks">Framework specs (e.g., "runtime", "aspnetcore@9.0")</param>
    /// <returns>Pack requests for frameworks that need to be downloaded</returns>
    public static List<PackRequest> GetMissingPackRequests(IEnumerable<string> frameworks)
    {
        List<PackRequest> requests = [];
        foreach (var fw in frameworks)
        {
            var fwName = fw.Contains('@') ? fw[..fw.IndexOf('@')] : fw;
            var fwVersion = fw.Contains('@') ? fw[(fw.IndexOf('@') + 1)..] : null;

            // Check if already available locally
            var (refPath, _, _) = PlatformResolver.ResolveFramework(fw);
            if (refPath == null && PlatformResolver.FrameworkMappings.TryGetValue(fwName, out var packName))
            {
                requests.Add(new PackRequest(packName, fwVersion));
            }
        }
        return requests;
    }

    /// <summary>
    /// Ensures each requested pack is available locally, downloading the ones
    /// that are not, and yields each pack as it completes.
    /// </summary>
    public static IAsyncEnumerable<PackResult> EnsurePacksAsync(
        IEnumerable<PackRequest> requests,
        HttpClient client,
        Action<string>? log = null,
        bool forceLatest = false,
        NuGetSourceOptions? sourceOptions = null)
    {
        var downloader = new Downloader<PackResult>();
        foreach (var req in requests)
        {
            var packName = req.PackName;
            var version = req.Version;
            downloader.Add(() => ResolveAndDownloadAsync(
                packName,
                version,
                client,
                log,
                forceLatest,
                sourceOptions));
        }
        return downloader;
    }

    /// <summary>
    /// The reference-pack request for one named framework, such as
    /// <c>runtime</c>, <c>aspnetcore@10.0.1</c>, or <c>netstandard</c>, at the
    /// spec's <c>@</c> version or latest. Returns null for a framework with no
    /// reference pack.
    /// </summary>
    internal static PackRequest? PackRequestFor(string frameworkSpec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameworkSpec);
        string name = frameworkSpec;
        string? version = null;
        int at = frameworkSpec.LastIndexOf('@');
        if (at > 0)
        {
            name = frameworkSpec[..at];
            version = frameworkSpec[(at + 1)..];
        }
        return PlatformResolver.FrameworkMappings.TryGetValue(name, out string? packName)
            ? new PackRequest(packName, string.IsNullOrWhiteSpace(version) ? null : version)
            : null;
    }

    private static async Task<PackResult?> ResolveAndDownloadAsync(
        string packName,
        string? version,
        HttpClient client,
        Action<string>? log,
        bool forceLatest,
        NuGetSourceOptions? sourceOptions)
    {
        if (version == null)
        {
            if (!forceLatest)
            {
                // Cache-first: use already-downloaded pack version if available
                var cached = TryGetCachedPackVersion(packName);
                if (cached != null)
                {
                    log?.Invoke($"Using cached pack version: {packName} {cached.Value.Version}");
                    return new PackResult(packName, cached.Value.Version, cached.Value.PackDir);
                }
            }

            // No cached pack (or @latest forced) — resolve from network
            version = await GetLatestPackVersionAsync(
                packName,
                client,
                log,
                sourceOptions).ConfigureAwait(false);
        }
        if (version == null) return null;

        var packDir = await EnsurePackAsync(
            packName,
            version,
            client,
            log,
            sourceOptions).ConfigureAwait(false);
        return packDir != null ? new PackResult(packName, version, packDir) : null;
    }

    /// <summary>
    /// Returns the newest cached pack version on disk, or null if none exists.
    /// Pure disk I/O — never hits the network.
    /// </summary>
    internal static (string Version, string PackDir)? TryGetCachedPackVersion(string packName)
    {
        var cachePath = GetPacksCachePath();
        if (cachePath == null) return null;

        var packRoot = Path.Combine(cachePath, packName);

        // Newest valid cached version by semver (prerelease packs are allowed).
        var best = Packages.VersionDirectory.SelectBest(packRoot, includePrerelease: true, IsPackValid);
        return best is { } b ? (b.Version.ToNormalizedString(), b.DirPath) : null;
    }

    /// <summary>
    /// Gets the latest version of a pack from NuGet.
    /// </summary>
    public static async Task<string?> GetLatestPackVersionAsync(
        string packName,
        HttpClient client,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        var sources = NuGetSourceResolver.ResolveSourcesForPackage(
            sourceOptions,
            packName);
        return await PackageExtractor.GetLatestVersionAsync(client, packName, sources, log).ConfigureAwait(false);
    }

    // ── Core download ────────────────────────────────────────────────

    /// <summary>
    /// Ensures a pack is available locally, downloading from NuGet if needed.
    /// </summary>
    public static async Task<string?> EnsurePackAsync(
        string packName,
        string version,
        HttpClient client,
        Action<string>? log = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        var cachePath = GetPacksCachePath();
        if (cachePath == null)
        {
            return null;
        }

        var packDir = Path.Combine(cachePath, packName, version);

        if (IsPackValid(packDir))
        {
            log?.Invoke($"Using cached pack: {packName} {version}");
            return packDir;
        }

        log?.Invoke($"Downloading pack: {packName} {version}");

        var outcome = await PackageExtractor.ExtractPackageAsync(
            client,
            $"{packName}@{version}",
            log,
            tempDirPrefix: "inspect-pack",
            sourceOptions: sourceOptions).ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            log?.Invoke(outcome.ErrorMessage!);
            return null;
        }
        var result = outcome.Result!;

        try
        {
            string committedPath = CommitPack(
                result.ExtractPath,
                packDir,
                packName,
                version);
            log?.Invoke($"Cached pack: {committedPath}");
            return committedPath;
        }
        catch (IOException ex)
        {
            log?.Invoke($"Failed to cache pack '{packName}@{version}': {ex.Message}");
            return null;
        }
        catch (InvalidDataException ex)
        {
            log?.Invoke($"Failed to cache pack '{packName}@{version}': {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            log?.Invoke($"Failed to cache pack '{packName}@{version}': {ex.Message}");
            return null;
        }
        finally
        {
            PackageExtractor.Cleanup(result.TempDir);
        }
    }

    /// <summary>
    /// Returns true if a cached pack directory is complete and contains a
    /// ref/ or data/ subdirectory.
    /// </summary>
    internal static bool IsPackValid(string packDir)
    {
        try
        {
            if (!Directory.Exists(packDir)
                || (!Directory.Exists(Path.Combine(packDir, "ref"))
                    && !Directory.Exists(Path.Combine(packDir, "data"))))
            {
                return false;
            }

            return File.Exists(Path.Combine(packDir, CommitMarkerFileName));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string CommitPack(
        string source,
        string targetPath,
        string packName,
        string version)
    {
        if (IsPackValid(targetPath))
            return targetPath;
        if (Directory.Exists(targetPath))
        {
            // A concurrent winner may have just published an already-valid
            // tree via Directory.Move between the check above and this
            // Exists probe. Re-check before treating the slot as corrupt.
            if (IsPackValid(targetPath))
                return targetPath;

            throw new InvalidDataException(
                $"Pack cache entry '{targetPath}' is incomplete or corrupt. Clear the cache before retrying.");
        }

        string? parentDir = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                $"Pack cache path has no parent: {targetPath}");
        PersistentCache.EnsurePathInCacheContext(targetPath);
        Directory.CreateDirectory(parentDir);
        string stagingPath = Path.Combine(
            parentDir,
            $".{version}.tmp-{Guid.NewGuid():N}");
        PersistentCache.EnsurePathInCacheContext(stagingPath);

        try
        {
            CopyContents(source, stagingPath);
            if (!Directory.Exists(Path.Combine(stagingPath, "ref"))
                && !Directory.Exists(Path.Combine(stagingPath, "data")))
            {
                throw new InvalidDataException(
                    $"Pack '{packName}@{version}' has no ref or data directory.");
            }

            using (var marker = new FileStream(
                Path.Combine(stagingPath, CommitMarkerFileName),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(marker))
            {
                writer.Write($"{PacksCategory}:{packName}@{version}");
            }

            try
            {
                Directory.Move(stagingPath, targetPath);
            }
            catch (IOException) when (IsPackValid(targetPath))
            {
                return targetPath;
            }

            return targetPath;
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                try
                {
                    Directory.Delete(stagingPath, recursive: true);
                }
                catch (IOException)
                {
                    // The committed destination is authoritative.
                }
                catch (UnauthorizedAccessException)
                {
                    // The committed destination is authoritative.
                }
            }
        }
    }

    /// <summary>
    /// Copies directory contents from source to destination, preserving structure.
    /// Package extraction paths may be shared cache entries and must remain immutable.
    /// Skips NuGet metadata files (.nuspec, [Content_Types].xml, _rels/, package/).
    /// </summary>
    private static void CopyContents(string source, string destination)
    {
        foreach (var dir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(dir);

            // Skip NuGet packaging metadata
            if (dirName is "_rels" or "package")
                continue;

            var destDir = Path.Combine(destination, dirName);
            if (Directory.Exists(destDir))
            {
                PersistentCache.EnsurePathInCacheContext(destDir);
                Directory.Delete(destDir, recursive: true);
            }
            CopyDirectory(dir, destDir);
        }

        foreach (var file in Directory.GetFiles(source))
        {
            var fileName = Path.GetFileName(file);

            // Skip NuGet metadata files
            if (fileName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals(
                    NuGetCache.CommitMarkerFileName,
                    StringComparison.Ordinal)
                || fileName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                continue;

            var destFile = Path.Combine(destination, fileName);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
