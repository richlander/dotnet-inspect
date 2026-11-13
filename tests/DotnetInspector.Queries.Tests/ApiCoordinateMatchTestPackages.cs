using DotnetInspector.Packages;
using NuGetFetch;
using InertText;

namespace DotnetInspector.Queries.Tests;

/// <summary>Pinned NuGet archives admitted by the real typed payload acquisition path.</summary>
internal sealed class ApiCoordinateMatchTestPackages :
    IPackageRootPayloadProvider,
    IApiCoordinateMatchHouseExecutor,
    IDisposable
{
    readonly InMemoryPackageStore _store = new();
    readonly IPackageSourceClient _source = PackageSourceClientFactory.Create(
        PackageSource.NuGetOrg, PackageSourceAssociation.Create(), new NoNetworkHandler());
    readonly DesktopPackageSourceComposition _composition =
        new(TimeSpan.FromSeconds(5));
    readonly PackagePayloadAcquisitionPlan _payloadAcquisition;

    internal ApiCoordinateMatchTestPackages()
    {
        _payloadAcquisition = new(
            (_, _) => _store,
            PackagePayloadLimits.Default,
            access: PackagePayloadAccess.Ranged);
        HouseExecution = new(
            _composition.CreateHouseOperation(
                PackageHouseOperationProfile.Realize),
            this);
    }

    public List<PackageSourceCoordinate> Requests { get; } = [];

    public List<(PackageHouseRequest Request, PackageProducerIdentity? RequiredProducer)>
        HouseRequests { get; } = [];

    public ApiCoordinateMatchHouseExecution HouseExecution { get; }

    internal string? RequiredProducerKeyOverride { get; set; }

    public async Task AddAsync(string packageId, string version)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "ApiMatching",
            $"{packageId.ToLowerInvariant()}.{version}.nupkg");
        await using FileStream input = File.OpenRead(path);
        await _store.CommitAsync(
            packageId, version, _source.Source.Producer.Key,
            input, TestContext.Current.CancellationToken);
    }

    public async ValueTask<PackageRootPayloadResult> GetPayloadAsync(
        PackageSourceCoordinate coordinate,
        string? requiredProducerKey,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        Requests.Add(coordinate);
        if (requiredProducerKey is not null
            && requiredProducerKey != _source.Source.Producer.Key
            && requiredProducerKey != _source.Source.Producer.PortableKey)
        {
            return new PackageRootPayloadResult.Unavailable(
                new InertString(TextPolicy.Field, "nuget.org"),
                "The requested producer is not authorized by this test host.",
                PackageRootAcquisitionFailureKind.ProducerNotAuthorized);
        }
        AcquiredPackageSourcePayload? payload = await PackagePayloadAcquisition.TryGetCachedAsync(
            coordinate, _source.Source.Producer, _source.Source.Producer.Key, _store,
            limits, log: null, cancellationToken);
        return new PackageRootPayloadResult.Available(
            Assert.IsType<AcquiredPackageSourcePayload>(payload));
    }

    public async Task<PackageRootBinding> BindingAsync(
        string packageId, string version, string? framework = null)
    {
        await AddAsync(packageId, version);
        var result = Assert.IsType<PackageRootPayloadResult.Available>(
            await GetPayloadAsync(PackageSourceCoordinate.Create(packageId, version), null,
                PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
        return PackageRootBinding.CreateFromSource(result.Payload, framework);
    }

    public async Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageProducerIdentity? requiredProducer,
        CancellationToken cancellationToken = default)
    {
        HouseRequests.Add((request, requiredProducer));
        PackageHouse house = _composition.CreateRealizationHouse(
            _payloadAcquisition,
            requiredProducerKey: requiredProducer is null
                ? null
                : RequiredProducerKeyOverride
                    ?? requiredProducer.PortableKey);
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

    public void Dispose()
    {
        _source.Dispose();
        _composition.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"A pinned API correspondence fixture unexpectedly requested network access: {request.RequestUri}");
    }
}
