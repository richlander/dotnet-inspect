using ZipFetch;

namespace NuGetFetch;

internal sealed partial class NuGetV3PackageSourceClient : IPackageArchiveRangeSource
{
    private readonly PackageArchiveRangeMemory _archiveRange = new();

    /// <summary>
    /// Opens the archive of one exact coordinate by range through the flat
    /// container the service index names, with the same credential, browser
    /// options, and per-request deadline as the full fetch, and the retry
    /// mechanics the gallery full fetch uses.
    /// </summary>
    public Task<PackageArchiveReadResult<PackageArchiveReader>> OpenArchiveAsync(
        string packageId,
        string version,
        ZipReadLimits limits,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null,
        PackageArchiveRequestLog? requestLog = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return PackageArchiveRangeAccess.OpenAsync(
            _results,
            coordinate,
            _archiveRange,
            _client,
            _clientTimeout,
            _options,
            deadline => _packageResources.ResolvePackageUrlAsync(
                coordinate.PackageId,
                coordinate.Version,
                NuGetSourceRequest.EndpointUrl(_endpoint),
                _credential,
                _options,
                deadline,
                useNuGetOrgShortcut: false),
            limits,
            cancellationToken,
            operationContext,
            requestLog);
    }
}
