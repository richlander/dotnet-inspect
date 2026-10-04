using System.Collections.Immutable;
using System.Diagnostics;
using InertText;

namespace DotnetInspector.Networking;

/// <summary>
/// Zero-dependency network observations emitted by dotnet-inspect before managed HTTP requests.
/// </summary>
public static class NetworkTelemetry
{
    public const string RequestStartingEventName = "dotnet-inspect.network.request.start";

    private static readonly object Gate = new();
    private static readonly AsyncLocal<NetworkTrafficKind> CurrentKind = new();
    private static ImmutableArray<IObserver<NetworkRequestObservation>> Subscribers = [];

    public static IDisposable Scope(NetworkTrafficKind kind)
    {
        var previous = CurrentKind.Value;
        CurrentKind.Value = kind;
        return new TrafficKindScope(previous);
    }

    internal static NetworkTrafficKind CurrentTrafficKind => CurrentKind.Value;

    public static IDisposable Subscribe(IObserver<NetworkRequestObservation> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (Gate)
        {
            Subscribers = Subscribers.Add(observer);
        }

        return new Subscription(observer);
    }

    internal static void RecordRequestStarting(
        HttpRequestMessage request,
        string clientKind)
    {
        var observation = NetworkRequestObservation.Create(
            request,
            clientKind,
            CurrentKind.Value);

        Activity.Current?.AddEvent(new ActivityEvent(
            RequestStartingEventName,
            tags: observation.ToActivityTags()));

        ImmutableArray<IObserver<NetworkRequestObservation>> subscribers;
        lock (Gate)
        {
            subscribers = Subscribers;
        }

        foreach (var subscriber in subscribers)
            subscriber.OnNext(observation);
    }

    private sealed class Subscription(IObserver<NetworkRequestObservation> observer) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                Subscribers = Subscribers.Remove(observer);
            }
        }
    }

    private sealed class TrafficKindScope(NetworkTrafficKind previous) : IDisposable
    {
        public void Dispose() => CurrentKind.Value = previous;
    }
}

public sealed record NetworkRequestObservation(
    string Method,
    InertString? Url,
    string? Scheme,
    InertString? Host,
    string ClientKind,
    NetworkTrafficKind TrafficKind,
    string? RequestWhat,
    string? RequestWhy)
{
    internal static NetworkRequestObservation Create(
        HttpRequestMessage request,
        string clientKind,
        NetworkTrafficKind trafficKind)
    {
        var uri = request.RequestUri;
        return new NetworkRequestObservation(
            request.Method.Method,
            RedactUrl(uri),
            uri?.IsAbsoluteUri == true ? uri.Scheme : null,
            uri?.IsAbsoluteUri == true
                ? new InertString(TextPolicy.Field, uri.Host)
                : null,
            clientKind,
            trafficKind,
            RequestTelemetry.Current.What,
            RequestTelemetry.Current.Why);
    }

    internal ActivityTagsCollection ToActivityTags()
    {
        var tags = new ActivityTagsCollection
        {
            ["http.request.method"] = Method,
            ["dotnet_inspect.http.client_kind"] = ClientKind,
            ["dotnet_inspect.network.kind"] = TrafficKind.ToTelemetryName(),
        };

        if (Url is { } url)
            tags["url.full"] = url.ToString();
        if (Scheme != null)
            tags["url.scheme"] = Scheme;
        if (Host is { } host)
            tags["server.address"] = host.ToString();
        if (RequestWhat != null)
            tags["dotnet_inspect.request.what"] = RequestWhat;
        if (RequestWhy != null)
            tags["dotnet_inspect.request.why"] = RequestWhy;

        return tags;
    }

    internal static InertString? RedactUrl(Uri? uri)
        => UrlRedaction.ForDiagnostics(uri);

    /// <summary>Returns inert display text with credential-bearing URL components redacted.</summary>
    public static InertString RedactSensitiveUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return UrlRedaction.ForDiagnostics(uri) ?? throw new UnreachableException();
    }

    /// <summary>Returns inert display text with credential-bearing URL components redacted.</summary>
    public static InertString RedactSensitiveUrlText(string value)
        => UrlRedaction.ForDiagnostics(value);
}

public enum NetworkTrafficKind
{
    Unknown = 0,
    AdvisoryData,
    PackageDownload,
    PackageManifest,
    PackageMetadata,
    PackageSearch,
    PackageLoad,
    PackageSizeProbe,
    PackageSourceDiscovery,
    PackageVersionList,
    PlatformResolution,
    RidPackageProbe,
    SourceAudit,
    SourceFetch,
    SourceIntegrity,
    SymbolDownload,
    VulnerabilityData
}

public static class NetworkTrafficKindExtensions
{
    public static string ToTelemetryName(this NetworkTrafficKind kind) => kind switch
    {
        NetworkTrafficKind.AdvisoryData => "advisory-data",
        NetworkTrafficKind.PackageDownload => "package-download",
        NetworkTrafficKind.PackageManifest => "package-manifest",
        NetworkTrafficKind.PackageMetadata => "package-metadata",
        NetworkTrafficKind.PackageSearch => "package-search",
        NetworkTrafficKind.PackageLoad => "package-load",
        NetworkTrafficKind.PackageSizeProbe => "package-size-probe",
        NetworkTrafficKind.PackageSourceDiscovery => "package-source-discovery",
        NetworkTrafficKind.PackageVersionList => "package-version-list",
        NetworkTrafficKind.PlatformResolution => "platform-resolution",
        NetworkTrafficKind.RidPackageProbe => "rid-package-probe",
        NetworkTrafficKind.SourceAudit => "source-audit",
        NetworkTrafficKind.SourceFetch => "source-fetch",
        NetworkTrafficKind.SourceIntegrity => "source-integrity",
        NetworkTrafficKind.SymbolDownload => "symbol-download",
        NetworkTrafficKind.VulnerabilityData => "vulnerability-data",
        _ => "unknown"
    };
}

internal static class NetworkClientKinds
{
    public const string Shared = "shared";
    public const string UntrustedFetch = "untrusted-source-fetch";
}

internal sealed class NetworkTrafficLogConsumer(TextWriter sink, Func<string, string> contain)
    : IObserver<NetworkRequestObservation>
{
    public void OnNext(NetworkRequestObservation observation)
    {
        if (HttpClientFactory.IsNetworkTrafficLoggingSuppressed)
            return;

        sink.WriteLine(contain(
            $"Network traffic [{observation.TrafficKind.ToTelemetryName()}]: {observation.Method} {observation.Url}"));
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }
}
