using System.IO.Compression;
using DotnetInspector.Core;

namespace DotnetInspector.Packages;

/// <summary>
/// Filesystem-backed <see cref="IPackageStore"/> that delegates to
/// <see cref="NuGetCache"/>. Reproduces the exact cache lookup, transactional
/// commit, and returned paths the desktop CLI has always used, so behavior is
/// unchanged; only the seam through which <see cref="PackageExtractor"/> reaches
/// persistence is new.
/// </summary>
public sealed class FileSystemPackageStore : IPackageStore
{
    /// <inheritdoc />
    public IPackageContent? TryGetCached(
        string packageName,
        string version,
        IReadOnlyCollection<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();

        string? cachedPath;
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageLoad))
        {
            cachedPath = NuGetCache.TryGetCachedPackage(
                normalizedName,
                normalizedVersion,
                allowedSourceKeys);
        }

        if (cachedPath == null || !NuGetCache.IsCachedPackageValid(cachedPath))
            return null;

        log?.Invoke($"Using cached package: {cachedPath}");
        var cachedNupkg = FindNupkgInDirectory(cachedPath, normalizedName, normalizedVersion);
        return new FileSystemPackageContent(cachedPath, cachedNupkg, fromCache: true);
    }

    /// <inheritdoc />
    public async ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        string sourceKey,
        Stream nupkg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nupkg);

        // Validate coordinates before building any path from them, so an
        // absolute or traversal-containing name cannot direct the temp write
        // outside the workspace (NuGetCache.CommitPackage validates again).
        NuGetCache.ValidatePathComponent(packageName, "package name");
        NuGetCache.ValidatePathComponent(version, "version");

        string tempDir = Directory.CreateTempSubdirectory("inspect-pkg-commit").FullName;
        try
        {
            // Fixed temp file name; the committed nupkg name is derived by
            // NuGetCache.CommitPackage from the validated coordinates.
            string nupkgPath = Path.Combine(tempDir, "package.nupkg");
            await using (var file = File.Create(nupkgPath))
            {
                await nupkg.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            string extractPath = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(nupkgPath, extractPath);

            CommittedPackage committed = NuGetCache.CommitPackage(
                extractPath,
                nupkgPath,
                packageName,
                version,
                sourceKey);

            return new FileSystemPackageContent(
                committed.ExtractPath,
                committed.NupkgPath,
                fromCache: true);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
                // The committed cache entry is independent of this temporary
                // download workspace.
            }
            catch (UnauthorizedAccessException)
            {
                // The committed cache entry is independent of this temporary
                // download workspace.
            }
        }
    }

    /// <inheritdoc />
    public string? TryGetLatestCachedVersion(
        string packageName,
        IReadOnlyCollection<string>? allowedSourceKeys)
        => NuGetCache.TryGetLatestCachedVersion(packageName, allowedSourceKeys);

    private static string? FindNupkgInDirectory(string cacheDir, string packageName, string version)
    {
        // Standard NuGet cache layout: {package}/{version}/{package}.{version}.nupkg
        var expectedPath = Path.Combine(cacheDir, $"{packageName}.{version}.nupkg");
        if (File.Exists(expectedPath))
            return expectedPath;

        try
        {
            var nupkgFiles = Directory.GetFiles(cacheDir, "*.nupkg");
            return nupkgFiles.Length > 0 ? nupkgFiles[0] : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
