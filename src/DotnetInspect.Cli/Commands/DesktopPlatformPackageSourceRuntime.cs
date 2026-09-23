using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.Platforms.Packages;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal sealed class DesktopPlatformPackageSourceRuntime :
    IPackageSourceAuthorization,
    IAsyncDisposable
{
    private readonly Func<DesktopPackageSourceComposition>
        _createComposition;
    private readonly NuGetSourceOptions? _sourceOptions;
    private readonly DesktopPackageStoreScope _stores;
    private DesktopPackageSourceComposition? _composition;

    internal DesktopPlatformPackageSourceRuntime(
        Func<DesktopPackageSourceComposition> createComposition,
        NuGetSourceOptions? sourceOptions,
        string temporaryDirectoryPrefix)
    {
        ArgumentNullException.ThrowIfNull(createComposition);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            temporaryDirectoryPrefix);
        _createComposition = createComposition;
        _sourceOptions = sourceOptions;
        _stores = new DesktopPackageStoreScope(
            temporaryDirectoryPrefix);
    }

    internal PackagePlatformHouseAdapter CreateAdapter(
        string capabilityName) =>
        new(
            new PackagePlatformSource(
                this,
                new PackagePayloadAcquisitionPlan(_stores.Get)),
            capabilityName);

    internal PackageSourceOperationLease IssueOperation(
        PlatformHouseRequest request,
        PlatformHouseWorkBudget remainingWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(remainingWork);
        return IssueOperation(request.CancellationToken);
    }

    internal PackageSourceOperationLease IssueOperation(
        CancellationToken cancellationToken) =>
        Composition.IssueSettlementOperation(cancellationToken);

    public PackageSourceAuthorization AuthorizeSourcesFor(
        string packageId) =>
        Composition.AuthorizeSourcesFor(
            packageId,
            _sourceOptions);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_composition is not null)
            {
                await _composition.DisposeAsync().ConfigureAwait(false);
                _composition = null;
            }
        }
        finally
        {
            _stores.Dispose();
        }
    }

    private DesktopPackageSourceComposition Composition =>
        _composition ??= _createComposition();
}

internal sealed class DesktopPackageStoreScope(
    string temporaryDirectoryPrefix) : IDisposable
{
    private readonly Dictionary<ConfiguredPackageAuthority, IPackageStore>
        _stores = [];
    private string? _temporaryRoot;

    internal IPackageStore Get(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer)
    {
        if (!_stores.TryGetValue(authority, out IPackageStore? store))
        {
            store = new AuthorityScopedFileSystemPackageStore(
                authority,
                producer,
                () => _temporaryRoot ??=
                    Directory.CreateTempSubdirectory(
                        temporaryDirectoryPrefix).FullName);
            _stores.Add(authority, store);
        }
        return store;
    }

    public void Dispose()
    {
        _stores.Clear();
        DotnetInspector.Packages.PackageExtractor.Cleanup(
            _temporaryRoot);
        _temporaryRoot = null;
    }
}
