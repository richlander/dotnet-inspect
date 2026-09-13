using System.Collections.Immutable;
using System.Diagnostics;

namespace DotnetInspector.Networking;

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
