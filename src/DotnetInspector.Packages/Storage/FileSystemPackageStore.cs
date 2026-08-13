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
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();

        CachedPackage? cached;
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageLoad))
        {
            cached = NuGetCache.TryGetCachedPackageContent(
                normalizedName,
                normalizedVersion,
                allowedSourceKeys);
        }

        // Layout/archive admission is PackageContentAdmission's job. Returning
        // the slot even when the extracted tree is damaged lets offline errors
        // say the entry is unusable rather than "no cached package was found".
        if (cached == null)
            return null;

        log?.Invoke($"Using cached package: {cached.ExtractPath}");
        var cachedNupkg = FindNupkgInDirectory(
            cached.ExtractPath,
            normalizedName,
            normalizedVersion);
        return new FileSystemPackageContent(
            cached.ExtractPath,
            cachedNupkg,
            fromCache: true,
            cached.ProducerKey);
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
                fromCache: true,
                committed.ProducerKey);
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

    private static string? FindNupkgInDirectory(string cacheDir, string packageName, string version)
    {
        // Standard NuGet cache layout: {package}/{version}/{package}.{version}.nupkg
        // Only the expected retained archive name is admissible. Scanning for
        // any *.nupkg would let extracted package content (a decoy nupkg in the
        // tree) stand in for the archive PackageContentAdmission re-validates.
        var expectedPath = Path.Combine(cacheDir, $"{packageName}.{version}.nupkg");
        return File.Exists(expectedPath) ? expectedPath : null;
    }
}
