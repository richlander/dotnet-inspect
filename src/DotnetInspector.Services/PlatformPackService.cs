using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Downloads and caches .NET platform ref/runtime packs from NuGet.
/// Packs are cached in the app cache directory mirroring the SDK packs layout:
/// ~/.local/share/dotnet-inspect/packs/{PackName}/{Version}/ref/net{TFM}/*.dll
/// </summary>
public static class PlatformPackService
{
    /// <summary>
    /// Gets the app cache packs directory, or null if CoreCache is not initialized.
    /// </summary>
    public static string? GetPacksCachePath()
    {
        try
        {
            return Path.Combine(CoreCache.GetBasePath(), "packs");
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
        Action<string>? log = null)
    {
        if (!PlatformResolver.FrameworkMappings.TryGetValue(frameworkShortName, out var packName))
        {
            return null;
        }

        return await EnsurePackAsync(packName, version, client, log).ConfigureAwait(false);
    }

    /// <summary>
    /// Prefix-to-pack bias: determines which pack most likely contains an assembly.
    /// Most-specific prefixes first.
    ///
    /// Aspnetcore-only Microsoft.* prefixes: AspNetCore, Extensions, JSInterop, Net.
    /// Everything else Microsoft.* (CSharp, VisualBasic, Win32) is in runtime.
    /// System.* is overwhelmingly runtime (4 exceptions live in aspnetcore but
    /// those are rare enough that biasing to runtime is correct).
    /// </summary>
    private static readonly (string Prefix, string ShortName)[] PackBias =
    [
        ("Microsoft.AspNetCore.", "aspnetcore"),
        ("Microsoft.Extensions.", "aspnetcore"),
        ("Microsoft.JSInterop.", "aspnetcore"),
        ("Microsoft.Net.", "aspnetcore"),
        ("Microsoft.", "runtime"),
        ("System.", "runtime"),
    ];

    /// <summary>
    /// Returns the framework short name most likely to contain the assembly, or null.
    /// </summary>
    internal static string? GetBiasedPack(string assemblyName)
    {
        foreach (var (prefix, shortName) in PackBias)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return shortName;
        }
        return null;
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
    /// Downloads multiple packs with staggered starts. The biased pack (first in
    /// the request list) starts immediately; others start after a short delay.
    /// If the biased pack completes before the delay, the caller gets the result
    /// without waiting. Remaining downloads continue for cache warming.
    /// </summary>
    public static IAsyncEnumerable<PackResult> EnsurePacksAsync(
        IEnumerable<PackRequest> requests,
        HttpClient client,
        Action<string>? log = null,
        bool forceLatest = false)
    {
        var downloader = new Downloader<PackResult>();
        foreach (var req in requests)
        {
            var packName = req.PackName;
            var version = req.Version;
            downloader.Add(() => ResolveAndDownloadAsync(packName, version, client, log, forceLatest));
        }
        return downloader;
    }

    /// <summary>
    /// Builds a prioritized list of pack requests for a platform assembly.
    /// The biased pack comes first and gets the explicit version; others get latest.
    /// </summary>
    public static List<PackRequest> BuildPackRequests(string assemblyName, string? explicitVersion)
    {
        var biased = GetBiasedPack(assemblyName);
        List<PackRequest> requests = [];

        // Biased pack first
        if (biased != null && PlatformResolver.FrameworkMappings.TryGetValue(biased, out var biasedPackName))
        {
            requests.Add(new PackRequest(biasedPackName, explicitVersion));
        }

        // Remaining packs at latest
        foreach (var (shortName, packName) in PlatformResolver.FrameworkMappings)
        {
            if (shortName != biased)
            {
                requests.Add(new PackRequest(packName));
            }
        }

        return requests;
    }

    private static async Task<PackResult?> ResolveAndDownloadAsync(
        string packName,
        string? version,
        HttpClient client,
        Action<string>? log,
        bool forceLatest = false)
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
            version = await GetLatestPackVersionAsync(packName, client, log).ConfigureAwait(false);
        }
        if (version == null) return null;

        var packDir = await EnsurePackAsync(packName, version, client, log).ConfigureAwait(false);
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
        if (!Directory.Exists(packRoot)) return null;

        // Find the newest valid cached version by semver
        NuGet.Versioning.NuGetVersion? best = null;
        string? bestDir = null;

        foreach (var dir in Directory.GetDirectories(packRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (NuGet.Versioning.NuGetVersion.TryParse(dirName, out var parsed)
                && IsPackValid(dir)
                && (best == null || parsed > best))
            {
                best = parsed;
                bestDir = dir;
            }
        }

        return best != null ? (best.ToNormalizedString(), bestDir!) : null;
    }

    /// <summary>
    /// Gets the latest version of a pack from NuGet.
    /// </summary>
    public static async Task<string?> GetLatestPackVersionAsync(
        string packName,
        HttpClient client,
        Action<string>? log = null)
    {
        var sources = NuGetSourceResolver.ResolveSources(null);
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
        Action<string>? log = null)
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
            tempDirPrefix: "inspect-pack").ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            log?.Invoke(outcome.ErrorMessage!);
            return null;
        }
        var result = outcome.Result!;

        try
        {
            Directory.CreateDirectory(packDir);
            MoveContents(result.ExtractPath, packDir);

            log?.Invoke($"Cached pack: {packDir}");
            return packDir;
        }
        catch (Exception)
        {
            try { if (Directory.Exists(packDir)) Directory.Delete(packDir, recursive: true); } catch { }
            return null;
        }
        finally
        {
            PackageExtractor.Cleanup(result.TempDir);
        }
    }

    /// <summary>
    /// Returns true if a cached pack directory is valid (contains ref/ or data/ subdirectory).
    /// </summary>
    internal static bool IsPackValid(string packDir)
    {
        return Directory.Exists(packDir)
            && (Directory.Exists(Path.Combine(packDir, "ref"))
                || Directory.Exists(Path.Combine(packDir, "data")));
    }

    /// <summary>
    /// Moves directory contents from source to destination, preserving structure.
    /// Skips NuGet metadata files (.nuspec, [Content_Types].xml, _rels/, package/).
    /// </summary>
    private static void MoveContents(string source, string destination)
    {
        foreach (var dir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(dir);

            // Skip NuGet packaging metadata
            if (dirName is "_rels" or "package")
                continue;

            var destDir = Path.Combine(destination, dirName);
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);
            Directory.Move(dir, destDir);
        }

        foreach (var file in Directory.GetFiles(source))
        {
            var fileName = Path.GetFileName(file);

            // Skip NuGet metadata files
            if (fileName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                continue;

            var destFile = Path.Combine(destination, fileName);
            File.Move(file, destFile, overwrite: true);
        }
    }
}
