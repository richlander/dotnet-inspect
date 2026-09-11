using System.Collections.Immutable;
using System.Diagnostics;
using DotnetInspector.Networking;
using InertText;

namespace DotnetInspector.Cache;

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
        var observation = CacheObservation.Create(
            category,
            key,
            result,
            NetworkTelemetry.CurrentTrafficKind);

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
    InertString Key,
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
           RedactCacheKey(key),
           result,
           trafficKind,
           RequestTelemetry.Current.What,
           RequestTelemetry.Current.Why);

    internal static InertString RedactCacheKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return InertString.Empty;

        if (key.Contains("://", StringComparison.Ordinal)
            || key.StartsWith("//", StringComparison.Ordinal)
            || key.StartsWith("\\\\", StringComparison.Ordinal)
            || (Uri.TryCreate(key, UriKind.Absolute, out Uri? absolute)
                && (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.Ordinal)
                    || absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal))))
        {
            return UrlRedaction.ForDiagnostics(key);
        }

        return new InertString(TextPolicy.Field, key);
    }

    internal ActivityTagsCollection ToActivityTags()
    {
        var tags = new ActivityTagsCollection
        {
           ["dotnet_inspect.cache.category"] = Category,
           ["dotnet_inspect.cache.key"] = Key.ToString(),
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
