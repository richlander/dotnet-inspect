using System.Diagnostics;
using DotnetInspector.Cache;

namespace DotnetInspector.Cache.Tests;

[Collection(CacheStateCollection.Name)]
public sealed class CacheTelemetryTests
{
    private const string Secret = "s3cr3t-signature-value";

    [Fact]
    public void CacheObservationKey_CarriesNoQueryValue()
    {
        string category = $"url-redaction-{Guid.NewGuid():N}";
        var observer = new RecordingObserver(category);
        using var subscription = CacheTelemetry.Subscribe(observer);

        CacheTelemetry.Record(
            category,
            $"https://feed.test/flat/sample/index.json?x={Secret}",
            CacheAccessResult.Miss);

        CacheObservation observation = Assert.Single(observer.Observations);
        Assert.DoesNotContain(
            Secret,
            observation.Key.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://user:s3cr3t-signature-value@feed.test/a?x=s3cr3t-signature-value#s3cr3t-signature-value")]
    [InlineData("//user:s3cr3t-signature-value@feed.test/a?x=s3cr3t-signature-value")]
    [InlineData(@"\\user:s3cr3t-signature-value@feed.test\a?x=s3cr3t-signature-value")]
    [InlineData("https://user:s3cr3t-signature-value@bad[")]
    [InlineData("custom://user:s3cr3t-signature-value@feed.test/a?x=s3cr3t-signature-value")]
    public void CacheObservationKey_RedactsLocatorBoundaryVariants(string key)
    {
        string redacted = CacheObservation.RedactCacheKey(key).ToString();

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheObservationKey_HandlesEmptyKey()
    {
        Assert.Equal(string.Empty, CacheObservation.RedactCacheKey("").ToString());
    }

    [Fact]
    public void CacheObservationKey_PreservesPackageCoordinate()
    {
        string category = $"package-coordinate-{Guid.NewGuid():N}";
        var observer = new RecordingObserver(category);
        using var subscription = CacheTelemetry.Subscribe(observer);

        CacheTelemetry.Record(
            category,
            "newtonsoft.json@13.0.3",
            CacheAccessResult.Hit);
        CacheTelemetry.Record(
            category,
            "system.text.json@9.0.0",
            CacheAccessResult.Hit);

        Assert.Collection(
            observer.Observations,
            observation => Assert.Equal(
                "newtonsoft.json@13.0.3",
                observation.Key.ToString()),
            observation => Assert.Equal(
                "system.text.json@9.0.0",
                observation.Key.ToString()));
    }

    [Fact]
    public void Record_CapturesNestedRequestAndNetworkContexts()
    {
        string category = $"nested-context-{Guid.NewGuid():N}";
        var observer = new RecordingObserver(category);
        using var subscription = CacheTelemetry.Subscribe(observer);
        using (RequestTelemetry.Scope("outer what", "outer why"))
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch))
        {
            CacheTelemetry.Record(category, "outer-before", CacheAccessResult.Miss);
            using (RequestTelemetry.Scope("inner what", "inner why"))
            using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageDownload))
            {
                CacheTelemetry.Record(category, "inner", CacheAccessResult.Hit);
            }
            CacheTelemetry.Record(category, "outer-after", CacheAccessResult.Store);
        }

        Assert.Collection(
            observer.Observations,
            observation =>
            {
                Assert.Equal(NetworkTrafficKind.PackageSearch, observation.TrafficKind);
                Assert.Equal("outer what", observation.RequestWhat);
                Assert.Equal("outer why", observation.RequestWhy);
            },
            observation =>
            {
                Assert.Equal(NetworkTrafficKind.PackageDownload, observation.TrafficKind);
                Assert.Equal("inner what", observation.RequestWhat);
                Assert.Equal("inner why", observation.RequestWhy);
            },
            observation =>
            {
                Assert.Equal(NetworkTrafficKind.PackageSearch, observation.TrafficKind);
                Assert.Equal("outer what", observation.RequestWhat);
                Assert.Equal("outer why", observation.RequestWhy);
            });
    }

    [Fact]
    public void Subscription_DisposeStopsObservations()
    {
        string category = $"subscription-disposal-{Guid.NewGuid():N}";
        var observer = new RecordingObserver(category);
        IDisposable subscription = CacheTelemetry.Subscribe(observer);

        CacheTelemetry.Record(category, "before", CacheAccessResult.Hit);
        subscription.Dispose();
        subscription.Dispose();
        CacheTelemetry.Record(category, "after", CacheAccessResult.Miss);

        Assert.Equal(
            "before",
            Assert.Single(observer.Observations).Key.ToString());
    }

    [Fact]
    public void CacheTelemetry_AddsActivityEventWithRequestCurrency()
    {
        using var activitySource = new ActivitySource("cache-telemetry-test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "cache-telemetry-test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using Activity? activity = activitySource.StartActivity("command");
        using (RequestTelemetry.Scope("package Markout", "package versions"))
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageVersionList))
        {
            CacheTelemetry.Record(
                "versions",
                "https://api.nuget.org/v3/index.json?token=secret",
                CacheAccessResult.Hit);
        }

        ActivityEvent evt = Assert.Single(activity!.Events);
        Assert.Equal(CacheTelemetry.CacheAccessEventName, evt.Name);
        Dictionary<string, object?> tags =
            evt.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        Assert.Equal("versions", tags["dotnet_inspect.cache.category"]);
        Assert.Equal("hit", tags["dotnet_inspect.cache.result"]);
        Assert.Equal(
            "package-version-list",
            tags["dotnet_inspect.network.kind"]);
        Assert.Equal("package Markout", tags["dotnet_inspect.request.what"]);
        Assert.Equal("package versions", tags["dotnet_inspect.request.why"]);
        string key = Assert.IsType<string>(tags["dotnet_inspect.cache.key"]);
        Assert.Equal(
            "https://api.nuget.org/v3/index.json?REDACTED",
            key);
        Assert.DoesNotContain("secret", key);
        Assert.DoesNotContain("token=", key);
    }

    private sealed class RecordingObserver(string category)
        : IObserver<CacheObservation>
    {
        public List<CacheObservation> Observations { get; } = [];

        public void OnNext(CacheObservation value)
        {
            if (value.Category == category)
                Observations.Add(value);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
