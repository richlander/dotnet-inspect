using ZipFetch;

namespace NuGetFetch;

internal sealed partial class NuGetGalleryPackageSourceClient : IPackageArchiveRangeSource
{
    private readonly PackageArchiveRangeMemory _archiveRange = new();

    /// <summary>
    /// Opens the archive of one exact coordinate by range from the gallery's
    /// package endpoint, with the same retry and per-request deadline as the
    /// full fetch; the gallery applies no per-request credential.
    /// </summary>
    public Task<PackageArchiveReadResult<PackageArchiveReader>> OpenArchiveAsync(
        string packageId,
        string version,
        ZipReadLimits limits,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        string url = PackageEndpoint
            + EscapeSegment($"{coordinate.PackageId}.{coordinate.Version}.nupkg");
        return PackageArchiveRangeAccess.OpenAsync(
            _results,
            coordinate,
            _archiveRange,
            _client,
            _client.Timeout,
            _options,
            _ => Task.FromResult<(string, PackageSourceCredential?)>((url, null)),
            limits,
            cancellationToken,
            operationContext);
    }
}
