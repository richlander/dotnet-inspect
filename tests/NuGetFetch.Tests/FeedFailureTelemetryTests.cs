using System.Net;

namespace NuGetFetch.Tests;

public sealed class FeedFailureTelemetryTests
{
    [Fact]
    public void RecordWithoutScopeDoesNothing()
    {
        FeedFailureTelemetry.Record(
            "https://example.test/v3/index.json",
            HttpStatusCode.Unauthorized,
            FeedFailurePhase.PackageSourceDiscovery);

        Assert.Null(FeedFailureTelemetry.Current);
    }

    [Fact]
    public void RecordStoresRedactedUrlClassificationAndPhase()
    {
        using IDisposable scope = FeedFailureTelemetry.Scope();

        FeedFailureTelemetry.Record(
            "https://user:password@example.test/v3/index.json?token=secret",
            HttpStatusCode.Unauthorized,
            FeedFailurePhase.PackageSourceDiscovery);

        FeedFailure failure = Assert.Single(
            FeedFailureTelemetry.Current!.Failures);
        Assert.Equal(
            "https://example.test/v3/index.json?REDACTED",
            failure.Url.ToString());
        Assert.Equal(FeedFailureKind.Authentication, failure.Kind);
        Assert.Equal(FeedFailurePhase.PackageSourceDiscovery, failure.Phase);
        Assert.Equal("reading the service index", failure.PhaseText);
    }

    [Fact]
    public void RelativeUriIsRedactedBeforeStorage()
    {
        using IDisposable scope = FeedFailureTelemetry.Scope();
        var uri = new Uri(
            "//user:secret@example.test/F/auth/secret/api?access_token=secret#fragment",
            UriKind.Relative);

        FeedFailureTelemetry.Record(
            uri,
            HttpStatusCode.Forbidden,
            FeedFailurePhase.PackageMetadata);

        FeedFailure failure = Assert.Single(
            FeedFailureTelemetry.Current!.Failures);
        Assert.Equal(
            "//example.test/F/auth/REDACTED/api?REDACTED",
            failure.Url.ToString());
        Assert.Equal(FeedFailureKind.Authorization, failure.Kind);
    }

    [Fact]
    public void NestedScopeIsolatedThenMergedIntoParent()
    {
        using IDisposable outer = FeedFailureTelemetry.Scope();
        FeedFailureTelemetry.Record(
            "https://outer.example/v3/index.json",
            HttpStatusCode.Forbidden,
            FeedFailurePhase.PackageSourceDiscovery);

        using (FeedFailureTelemetry.Scope())
        {
            FeedFailureTelemetry.Record(
                "https://inner.example/query",
                HttpStatusCode.Unauthorized,
                FeedFailurePhase.PackageSearch);

            FeedFailure inner = Assert.Single(
                FeedFailureTelemetry.Current!.Failures);
            Assert.Contains(
                "inner.example",
                inner.Url.ToString(),
                StringComparison.Ordinal);
        }

        Assert.Equal(2, FeedFailureTelemetry.Current!.Failures.Count);
    }

    [Fact]
    public void NestedScopeCanDiscardFailures()
    {
        using IDisposable outer = FeedFailureTelemetry.Scope();

        using (FeedFailureTelemetry.Scope(mergeIntoParent: false))
        {
            FeedFailureTelemetry.Record(
                "https://example.test/query",
                HttpStatusCode.InternalServerError,
                FeedFailurePhase.PackageSearch);
        }

        Assert.Empty(FeedFailureTelemetry.Current!.Failures);
    }
}
