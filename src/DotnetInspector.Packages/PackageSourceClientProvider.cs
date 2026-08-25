using DotnetInspector.Core;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Adapts desktop host transports to typed package-source clients.
/// </summary>
/// <remarks>
/// <c>PackageSourceClientProvider_SelectsHostTransportOnlyForSharedClient</c>
/// gates production per-origin selection and injected-client preservation.
/// </remarks>
internal static class PackageSourceClientProvider
{
    internal static IPackageSourceClient Create(
        PackageSource source,
        HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(client);

        if (source is RoutedPackageSource route)
        {
            return new FailoverPackageSourceClient(
                [
                    .. route.Transports.Select(transport =>
                        PackageSourceClientFactory.Create(
                            transport,
                            SelectTransport(transport, client))),
                ]);
        }

        return PackageSourceClientFactory.Create(
            source,
            SelectTransport(source, client));
    }

    internal static HttpClient SelectTransport(
        PackageSource source,
        HttpClient client) =>
        ReferenceEquals(client, HttpClientFactory.Shared)
            ? HttpClientFactory.GetPackageSourceClient(source.Url)
            : client;

    internal static string ProducerKey(PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ProducerKey(source.Url);
    }

    internal static string ProducerKey(string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        string identity =
            Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? endpoint)
                && endpoint.Scheme is "http" or "https"
                    ? PackageSourceIdentity
                        .ForProducerEndpoint(endpoint)
                        .Value
                    : sourceUrl;
        return NuGetCache.GetSourceKey(identity);
    }

    internal static void RecordFailure(
        PackageSource source,
        PackageSourceFailure failure,
        NetworkTrafficKind trafficKind)
    {
        using var trafficScope = NetworkTelemetry.Scope(trafficKind);
        FeedFailureTelemetry.Record(
            source.Url,
            failure.StatusCode);
    }
}

internal sealed record RoutedPackageSource : PackageSource
{
    internal RoutedPackageSource(IReadOnlyList<PackageSource> transports)
        : this(First(transports), transports)
    {
    }

    private RoutedPackageSource(
        PackageSource first,
        IReadOnlyList<PackageSource> transports)
        : base(
            first.Name,
            first.Url,
            first.Credential)
    {
        Transports = [.. transports];
    }

    internal IReadOnlyList<PackageSource> Transports { get; }

    private static PackageSource First(
        IReadOnlyList<PackageSource> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        return transports.Count > 0
            ? transports[0]
            : throw new ArgumentException(
                "A routed package source requires a transport.",
                nameof(transports));
    }
}

internal sealed class FailoverPackageSourceClient
    : IPackageSourceClient
{
    private readonly IReadOnlyList<IPackageSourceClient> _transports;

    internal FailoverPackageSourceClient(
        IReadOnlyList<IPackageSourceClient> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        if (transports.Count == 0)
        {
            throw new ArgumentException(
                "A failover package source requires a transport.",
                nameof(transports));
        }

        Identity = transports[0].Identity;
        if (transports.Any(transport => transport.Identity != Identity))
        {
            throw new ArgumentException(
                "Every failover transport must represent the same producer.",
                nameof(transports));
        }

        _transports = [.. transports];
    }

    public PackageSourceIdentity Identity { get; }

    public PackageSourceKind Kind => _transports[0].Kind;

    public PackageSourceCapabilities Capabilities =>
        _transports.Aggregate(
            PackageSourceCapabilities.None,
            static (capabilities, transport) =>
                capabilities | transport.Capabilities);

    public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            transport => transport.SearchAsync(
                query,
                take,
                prerelease,
                cancellationToken));

    public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            transport => transport.GetVersionsAsync(
                packageId,
                cancellationToken));

    public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            transport => transport.GetPackageAsync(
                packageId,
                version,
                cancellationToken),
            stopOnNotFound: true);

    public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            transport => transport.TryGetSymbolsAsync(
                packageId,
                version,
                cancellationToken),
            stopOnNotFound: true);

    public void Dispose()
    {
        foreach (IPackageSourceClient transport in _transports)
            transport.Dispose();
    }

    private async Task<PackageSourceOperationResult<T>> ExecuteAsync<T>(
        Func<IPackageSourceClient,
            Task<PackageSourceOperationResult<T>>> operation,
        bool stopOnNotFound = false)
    {
        PackageSourceOperationResult<T>.Failed? lastFailure = null;
        foreach (IPackageSourceClient transport in _transports)
        {
            PackageSourceOperationResult<T> result =
                await operation(transport).ConfigureAwait(false);
            if (result
                is PackageSourceOperationResult<T>.Succeeded)
            {
                return result;
            }

            lastFailure =
                (PackageSourceOperationResult<T>.Failed)result;
            if (stopOnNotFound
                && lastFailure.Failure.Kind
                    == PackageSourceFailureKind.NotFound)
            {
                return lastFailure;
            }
        }

        return lastFailure!;
    }
}
