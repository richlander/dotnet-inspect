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
