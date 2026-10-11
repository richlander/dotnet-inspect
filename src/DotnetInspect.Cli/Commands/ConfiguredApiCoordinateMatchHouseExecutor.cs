using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal sealed class ConfiguredApiCoordinateMatchHouseExecutor
    : IApiCoordinateMatchHouseExecutor, IAsyncDisposable
{
    readonly DesktopPackageSourceComposition _composition;
    readonly NuGetSourceOptions? _sourceOptions;
    readonly Dictionary<ConfiguredPackageAuthority, IPackageStore> _stores =
        new(ReferenceEqualityComparer.Instance);
    readonly PackagePayloadAcquisitionPlan _payloadAcquisition;

    internal ConfiguredApiCoordinateMatchHouseExecutor(
        TimeSpan requestTimeout,
        NuGetSourceOptions? sourceOptions)
    {
        _composition =
            new DesktopPackageSourceComposition(requestTimeout);
        _sourceOptions = sourceOptions;
        _payloadAcquisition = new(
            GetStore,
            PackagePayloadLimits.Default,
            access: PackagePayloadAccess.Ranged);
        Execution = new(
            _composition.CreateHouseOperation(
                PackageHouseOperationProfile.Realize),
            this);
    }

    internal ApiCoordinateMatchHouseExecution Execution { get; }

    public async Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageProducerIdentity? requiredProducer,
        CancellationToken cancellationToken = default)
    {
        PackageHouse house = _composition.CreateRealizationHouse(
            _payloadAcquisition,
            _sourceOptions,
            requiredProducerKey: requiredProducer?.PortableKey);
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

    public ValueTask DisposeAsync() => _composition.DisposeAsync();

    IPackageStore GetStore(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer)
    {
        if (!_stores.TryGetValue(authority, out IPackageStore? store))
        {
            store = new InMemoryPackageStore();
            _stores.Add(authority, store);
        }
        return store;
    }
}
