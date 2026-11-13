using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal sealed class ConfiguredPackageRootHouseExecutor
    : IPackageRootHouseExecutor, IAsyncDisposable
{
    readonly DesktopPackageSourceComposition _composition;
    readonly NuGetSourceOptions? _sourceOptions;
    readonly Dictionary<ConfiguredPackageAuthority, IPackageStore> _stores =
        new(ReferenceEqualityComparer.Instance);
    readonly PackageStoreProvider? _getExternalStore;
    readonly PackagePayloadAcquisitionPlan _payloadAcquisition;
    string? _temporaryRoot;

    internal ConfiguredPackageRootHouseExecutor(
        TimeSpan requestTimeout,
        NuGetSourceOptions? sourceOptions,
        WorkspaceContextLoadOptions loadOptions)
        : this(
            new DesktopPackageSourceComposition(requestTimeout),
            sourceOptions,
            loadOptions)
    {
    }

    internal ConfiguredPackageRootHouseExecutor(
        DesktopPackageSourceComposition composition,
        NuGetSourceOptions? sourceOptions,
        WorkspaceContextLoadOptions loadOptions)
        : this(
            composition,
            sourceOptions,
            loadOptions,
            getStore: null)
    {
    }

    internal ConfiguredPackageRootHouseExecutor(
        DesktopPackageSourceComposition composition,
        NuGetSourceOptions? sourceOptions,
        WorkspaceContextLoadOptions loadOptions,
        PackageStoreProvider? getStore)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(loadOptions);
        _composition = composition;
        _sourceOptions = sourceOptions;
        _getExternalStore = getStore;
        _payloadAcquisition =
            new PackagePayloadAcquisitionPlan(
                GetStore,
                loadOptions.PayloadLimits,
                loadOptions.PackageTransferPolicy,
                loadOptions.Log,
                access: PackagePayloadAccess.Ranged);
        Execution = new(
            _composition.CreateHouseOperation(
                PackageHouseOperationProfile.Realize),
            this);
    }

    internal PackageRootHouseExecution Execution { get; }

    public async Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        string? requiredProducerKey,
        CancellationToken cancellationToken = default)
    {
        PackageHouse house = _composition.CreateRealizationHouse(
            _payloadAcquisition,
            _sourceOptions,
            requiredProducerKey: requiredProducerKey);
        PackageSourceOperationLease? operation =
            _composition.IssueSettlementOperation(cancellationToken);
        try
        {
            Task<PackageHouseSettlement> pending =
                house.ExecuteAsync(request, operation);
            operation = null;
            return await pending.ConfigureAwait(false);
        }
        finally
        {
            operation?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _composition.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            DotnetInspector.Packages.PackageExtractor.Cleanup(
                _temporaryRoot);
        }
    }

    IPackageStore GetStore(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer)
    {
        if (!_stores.TryGetValue(authority, out IPackageStore? store))
        {
            IPackageStore retained =
                _getExternalStore?.Invoke(authority, producer)
                ?? AuthorityScopedFileSystemPackageStore
                    .CreateWithLegacyApplicationCache(
                        authority,
                        producer,
                        () =>
                            _temporaryRoot ??=
                                Directory.CreateTempSubdirectory(
                                    "inspect-root").FullName);
            store = new EntryOpaquePackageStore(retained);
            _stores.Add(authority, store);
        }
        return store;
    }

    private sealed class EntryOpaquePackageStore(
        IPackageStore inner)
        : IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            inner.TryGetCached(
                packageName,
                version,
                allowedSourceKeys,
                log);

        public IEnumerable<IPackageContent> EnumerateCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            inner.EnumerateCached(
                packageName,
                version,
                allowedSourceKeys,
                log);

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            inner.CommitAsync(
                packageName,
                version,
                sourceKey,
                nupkg,
                cancellationToken);
    }
}
