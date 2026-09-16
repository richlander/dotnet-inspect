using DotnetInspector.Packages;
using NuGetFetch;
using InertText;

namespace DotnetInspector.Queries.Tests;

/// <summary>Pinned NuGet archives admitted by the real typed payload acquisition path.</summary>
internal sealed class ApiCoordinateMatchTestPackages : IPackageRootPayloadProvider, IDisposable
{
    readonly InMemoryPackageStore _store = new();
    readonly IPackageSourceClient _source = PackageSourceClientFactory.Create(
        PackageSource.NuGetOrg, PackageSourceAssociation.Create(), new NoNetworkHandler());

    public List<PackageSourceCoordinate> Requests { get; } = [];

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

    public void Dispose() => _source.Dispose();

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"A pinned API correspondence fixture unexpectedly requested network access: {request.RequestUri}");
    }
}
