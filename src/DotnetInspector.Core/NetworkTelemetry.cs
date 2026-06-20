using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace DotnetInspector.Core;

public readonly record struct RequestCurrency(string? What, string? Why);

public static class RequestTelemetry
{
    private static readonly AsyncLocal<RequestCurrency?> CurrentValue = new();

    public static IDisposable Scope(string? what, string? why)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = new RequestCurrency(what, why);
        return new RequestCurrencyScope(previous);
    }

    internal static RequestCurrency Current => CurrentValue.Value ?? default;

    public static void Breadcrumb(string stage, string detail)
        => BreadcrumbTelemetry.Record(stage, detail);

    private sealed class RequestCurrencyScope(RequestCurrency? previous) : IDisposable
    {
        public void Dispose() => CurrentValue.Value = previous;
    }
}

public static class BreadcrumbTelemetry
{
    public const string BreadcrumbEventName = "dotnet-inspect.request.breadcrumb";

    private static readonly object Gate = new();
    private static ImmutableArray<IObserver<BreadcrumbObservation>> Subscribers = [];

    public static IDisposable Subscribe(IObserver<BreadcrumbObservation> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (Gate)
        {
            Subscribers = Subscribers.Add(observer);
        }

        return new Subscription(observer);
    }

    public static void Record(string stage, string detail)
    {
        var observation = new BreadcrumbObservation(
            stage,
            detail,
            RequestTelemetry.Current.What,
            RequestTelemetry.Current.Why);

        Activity.Current?.AddEvent(new ActivityEvent(
            BreadcrumbEventName,
            tags: observation.ToActivityTags()));

        ImmutableArray<IObserver<BreadcrumbObservation>> subscribers;
        lock (Gate)
        {
            subscribers = Subscribers;
        }

        foreach (var subscriber in subscribers)
            subscriber.OnNext(observation);
    }

    private sealed class Subscription(IObserver<BreadcrumbObservation> observer) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                Subscribers = Subscribers.Remove(observer);
            }
        }
    }
}

public sealed record BreadcrumbObservation(
    string Stage,
    string Detail,
    string? RequestWhat,
    string? RequestWhy)
{
    internal ActivityTagsCollection ToActivityTags()
    {
        var tags = new ActivityTagsCollection
        {
            ["dotnet_inspect.breadcrumb.stage"] = Stage,
            ["dotnet_inspect.breadcrumb.detail"] = Detail
        };

        if (RequestWhat != null)
            tags["dotnet_inspect.request.what"] = RequestWhat;
        if (RequestWhy != null)
            tags["dotnet_inspect.request.why"] = RequestWhy;

        return tags;
    }
}

/// <summary>
/// Zero-dependency network observations emitted by dotnet-inspect before managed HTTP requests.
/// </summary>
public static class NetworkTelemetry
{
    public const string RequestStartingEventName = "dotnet-inspect.network.request.start";

    private static readonly object Gate = new();
    private static readonly AsyncLocal<NetworkTrafficKind> CurrentKind = new();
    private static readonly AsyncLocal<ImmutableHashSet<NetworkTrafficKind>?> AllowedKinds = new();
    private static ImmutableArray<IObserver<NetworkRequestObservation>> Subscribers = [];

    public static IDisposable Scope(NetworkTrafficKind kind)
    {
        var previous = CurrentKind.Value;
        CurrentKind.Value = kind;
        return new TrafficKindScope(previous);
    }

    internal static NetworkTrafficKind CurrentTrafficKind => CurrentKind.Value;

    public static IDisposable Allow(NetworkTrafficKind kind)
    {
        var previous = AllowedKinds.Value;
        AllowedKinds.Value = (previous ?? ImmutableHashSet<NetworkTrafficKind>.Empty).Add(kind);
        return new AllowedKindScope(previous);
    }

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
        var trafficKind = CurrentKind.Value;
        var observation = NetworkRequestObservation.Create(
            request,
            clientKind,
            trafficKind,
            IsAllowedByPolicy(trafficKind));

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

    private sealed class AllowedKindScope(ImmutableHashSet<NetworkTrafficKind>? previous) : IDisposable
    {
        public void Dispose() => AllowedKinds.Value = previous;
    }

    private static bool IsAllowedByPolicy(NetworkTrafficKind kind) =>
        kind switch
        {
            // Package load/search traffic is always allowed; vulnerability traffic is view-gated.
            NetworkTrafficKind.VulnerabilityData => AllowedKinds.Value?.Contains(kind) == true,
            _ => true
        };
}

public sealed record NetworkRequestObservation(
    string Method,
    string? Url,
    string? Scheme,
    string? Host,
    string ClientKind,
    NetworkTrafficKind TrafficKind,
    bool IsAllowedByPolicy,
    string? RequestWhat,
    string? RequestWhy)
{
    internal static NetworkRequestObservation Create(
        HttpRequestMessage request,
        string clientKind,
        NetworkTrafficKind trafficKind,
        bool isAllowedByPolicy)
    {
        var uri = request.RequestUri;
        return new NetworkRequestObservation(
            request.Method.Method,
            RedactUrl(uri),
            uri?.IsAbsoluteUri == true ? uri.Scheme : null,
            uri?.IsAbsoluteUri == true ? uri.Host : null,
            clientKind,
            trafficKind,
            isAllowedByPolicy,
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
            ["dotnet_inspect.network.policy.allowed"] = IsAllowedByPolicy
        };

        if (Url != null)
            tags["url.full"] = Url;
        if (Scheme != null)
            tags["url.scheme"] = Scheme;
        if (Host != null)
            tags["server.address"] = Host;
        if (RequestWhat != null)
            tags["dotnet_inspect.request.what"] = RequestWhat;
        if (RequestWhy != null)
            tags["dotnet_inspect.request.why"] = RequestWhy;

        return tags;
    }

    internal static string? RedactUrl(Uri? uri)
    {
        if (uri == null)
            return null;

        if (!uri.IsAbsoluteUri)
            return RedactRelativeUrl(uri.ToString());

        var builder = new UriBuilder(uri)
        {
            UserName = "",
            Password = ""
        };

        builder.Query = RedactQuery(builder.Query);
        return builder.Uri.ToString();
    }

    internal static string RedactSensitiveUrlText(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return RedactUrl(absolute) ?? value;

        if (Uri.TryCreate(value, UriKind.Relative, out var relative))
            return RedactRelativeUrl(relative.ToString());

        return value;
    }

    private static string RedactRelativeUrl(string url)
    {
        var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
            return url;

        var path = url[..queryIndex];
        var query = url[queryIndex..];
        return $"{path}?{RedactQuery(query)}";
    }

    private static string RedactQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
            return "";

        var trimmed = query[0] == '?' ? query[1..] : query;
        if (trimmed.Length == 0)
            return "";

        var parts = trimmed.Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var name = separator >= 0 ? part[..separator] : part;
            if (IsSensitiveQueryName(Uri.UnescapeDataString(name)))
                parts[i] = separator >= 0 ? $"{name}=REDACTED" : $"{name}=REDACTED";
        }

        return string.Join('&', parts);
    }

    private static bool IsSensitiveQueryName(string name)
        => name.Equals("access_token", StringComparison.OrdinalIgnoreCase)
           || name.Equals("api_key", StringComparison.OrdinalIgnoreCase)
           || name.Equals("apikey", StringComparison.OrdinalIgnoreCase)
           || name.Equals("code", StringComparison.OrdinalIgnoreCase)
           || name.Equals("password", StringComparison.OrdinalIgnoreCase)
           || name.Equals("sig", StringComparison.OrdinalIgnoreCase)
           || name.Equals("signature", StringComparison.OrdinalIgnoreCase)
           || name.Equals("token", StringComparison.OrdinalIgnoreCase);
}

public static class CacheTelemetry
{
    public const string CacheAccessEventName = "dotnet-inspect.cache.access";

    private static readonly object Gate = new();
    private static ImmutableArray<IObserver<CacheObservation>> Subscribers = [];

    public static IDisposable Subscribe(IObserver<CacheObservation> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (Gate)
        {
           Subscribers = Subscribers.Add(observer);
        }

        return new Subscription(observer);
    }

    public static void Record(string category, string key, CacheAccessResult result)
    {
        var observation = CacheObservation.Create(category, key, result, NetworkTelemetry.CurrentTrafficKind);

        Activity.Current?.AddEvent(new ActivityEvent(
           CacheAccessEventName,
           tags: observation.ToActivityTags()));

        ImmutableArray<IObserver<CacheObservation>> subscribers;
        lock (Gate)
        {
           subscribers = Subscribers;
        }

        foreach (var subscriber in subscribers)
           subscriber.OnNext(observation);
    }

    private sealed class Subscription(IObserver<CacheObservation> observer) : IDisposable
    {
        public void Dispose()
        {
           lock (Gate)
           {
               Subscribers = Subscribers.Remove(observer);
           }
        }
    }
}

public sealed record CacheObservation(
    string Category,
    string Key,
    CacheAccessResult Result,
    NetworkTrafficKind TrafficKind,
    string? RequestWhat,
    string? RequestWhy)
{
    internal static CacheObservation Create(
        string category,
        string key,
        CacheAccessResult result,
        NetworkTrafficKind trafficKind)
        => new(
           category,
           NetworkRequestObservation.RedactSensitiveUrlText(key),
           result,
           trafficKind,
           RequestTelemetry.Current.What,
           RequestTelemetry.Current.Why);

    internal ActivityTagsCollection ToActivityTags()
    {
        var tags = new ActivityTagsCollection
        {
           ["dotnet_inspect.cache.category"] = Category,
           ["dotnet_inspect.cache.key"] = Key,
           ["dotnet_inspect.cache.result"] = Result.ToTelemetryName(),
           ["dotnet_inspect.network.kind"] = TrafficKind.ToTelemetryName()
        };

        if (RequestWhat != null)
           tags["dotnet_inspect.request.what"] = RequestWhat;
        if (RequestWhy != null)
           tags["dotnet_inspect.request.why"] = RequestWhy;

        return tags;
    }
}

public enum CacheAccessResult
{
    Hit,
    Miss,
    Store
}

public static class CacheAccessResultExtensions
{
    public static string ToTelemetryName(this CacheAccessResult result) => result switch
    {
        CacheAccessResult.Hit => "hit",
        CacheAccessResult.Miss => "miss",
        CacheAccessResult.Store => "store",
        _ => "unknown"
    };
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

// Writes traffic observations to a sink captured when logging is enabled — not to
// the ambient Console.Error at publish time. Publishing runs on the async HTTP
// pipeline, so a write that targeted the ambient Console.Error could land after a
// test had swapped/disposed it (issue #705); binding the sink up front removes that
// coupling, and the caller scopes the subscription's lifetime around the sink's.
internal sealed class NetworkTrafficLogConsumer(System.IO.TextWriter sink) : IObserver<NetworkRequestObservation>
{
    public void OnNext(NetworkRequestObservation observation)
    {
        sink.WriteLine(
            $"Network traffic [{observation.TrafficKind.ToTelemetryName()}]: {observation.Method} {observation.Url}");

        if (observation is { TrafficKind: NetworkTrafficKind.VulnerabilityData, IsAllowedByPolicy: false })
        {
            sink.WriteLine(
                $"Network policy error [vulnerability-data]: NuGet vulnerability service was accessed outside detailed view or an explicit network-using section: {observation.Method} {observation.Url}");
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }
}

public sealed class RequestMermaidDiagram :
    IObserver<NetworkRequestObservation>,
    IObserver<CacheObservation>,
    IObserver<BreadcrumbObservation>,
    IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _nodes = [];
    private readonly HashSet<string> _cacheHitsShown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cacheStoresShown = new(StringComparer.Ordinal);
    private readonly IDisposable _networkSubscription;
    private readonly IDisposable _cacheSubscription;
    private readonly IDisposable _breadcrumbSubscription;

    private RequestMermaidDiagram()
    {
        _networkSubscription = NetworkTelemetry.Subscribe((IObserver<NetworkRequestObservation>)this);
        _cacheSubscription = CacheTelemetry.Subscribe(this);
        _breadcrumbSubscription = BreadcrumbTelemetry.Subscribe(this);
    }

    public static RequestMermaidDiagram Start() => new();

    public void OnNext(NetworkRequestObservation observation)
    {
        lock (_gate)
        {
            _nodes.Add($"{observation.TrafficKind.ToTelemetryName()}<br/>{observation.Method} {observation.Url}");
        }
    }

    public void OnNext(CacheObservation observation)
    {
        lock (_gate)
        {
            var cacheKey = $"{observation.Category}\0{observation.Key}";
            if (observation.Result == CacheAccessResult.Store)
                _cacheStoresShown.Add(cacheKey);

            if (observation.Result == CacheAccessResult.Hit)
            {
                if (_cacheStoresShown.Contains(cacheKey) || !_cacheHitsShown.Add(cacheKey))
                    return;
            }

            _nodes.Add($"cache {observation.Result.ToTelemetryName()}<br/>{observation.Category} {observation.Key}");
        }
    }

    public void OnNext(BreadcrumbObservation observation)
    {
        lock (_gate)
        {
            _nodes.Add($"{observation.Stage}<br/>{observation.Detail}");
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        _networkSubscription.Dispose();
        _cacheSubscription.Dispose();
        _breadcrumbSubscription.Dispose();
    }

    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToMermaid());
    }

    public string ToMermaid()
    {
        List<string> nodes;
        lock (_gate)
        {
            nodes = [.. _nodes];
        }

        var builder = new StringBuilder();
        builder.AppendLine("flowchart TD");
        builder.AppendLine("  n0[\"dotnet-inspect\"]");

        if (nodes.Count == 0)
        {
            builder.AppendLine("  n0 --> n1[\"no managed HTTP/cache activity observed\"]");
            return builder.ToString();
        }

        var previous = "n0";
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = $"n{i + 1}";
            builder.AppendLine($"  {node}[\"{EscapeMermaidLabel(nodes[i])}\"]");
            builder.AppendLine($"  {previous} --> {node}");
            previous = node;
        }

        return builder.ToString();
    }

    private static string EscapeMermaidLabel(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
